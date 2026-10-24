using Glance.Core.Devices;
using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Notices;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Glance.Core.Sessions;
using Glance.Drivers.Thermalright;
using Glance.Platform.Windows;
using Glance.Sensors.Windows;
using Glance.Service;
using Glance.Service.Api;
using Glance.Service.Security;
using Serilog;
using Microsoft.AspNetCore.Hosting.Server;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

var paths = GlancePaths.Default();
// Before anything is read from or written to it (settings, keys, logs): only SYSTEM and Administrators may use it.
// The takeover marker sits next to the data directory (%ProgramData%), so it can be written before anything changes.
var markerWarnings = new List<string>();
var dataDirAcl = DataDirectoryStartup.Enforce(paths,
    DataDirectoryAcl.ShouldEnforce(GlancePaths.IsOverridden, DataDirectoryAcl.IsElevated()),
    new FileTakeoverMarker(Directory.GetParent(paths.Root)?.FullName ?? paths.Root, markerWarnings.Add), DataDirectoryAcl.Enforce);
// A quarantine moved the subfolders aside too: recreate them, empty, so Glance starts from defaults.
if (dataDirAcl?.Outcome == DataDirectoryAcl.Outcome.Quarantined) paths = new GlancePaths(paths.Root);
var settings = new SettingsStore(paths);
// kestrel.json is rebuilt from settings at every start; a port that cannot be bound must not take the screens down.
var binding = NetworkConfig.ApplyAtStartup(paths, settings, NetworkConfig.CanBind);
builder.Configuration.AddJsonFile(paths.KestrelConfig, optional: false, reloadOnChange: true);
if (binding == StartupBinding.None) builder.Services.AddSingleton<IServer, NoWebServer>();

builder.Host.UseWindowsService(o => o.ServiceName = "Glance");
builder.Host.UseSerilog((_, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.File(Path.Combine(paths.Logs, "glance-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7));

var s = builder.Services;
s.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions));
s.AddSingleton(paths);
s.AddSingleton(TimeProvider.System);
s.AddSingleton(settings);
s.AddSingleton<NoticeBoard>();
s.AddSingleton(new WebBindingState(binding));
s.AddSingleton<LayoutStore>();
s.AddSingleton<AssetStore>();
s.AddSingleton<IAssetStore>(sp => sp.GetRequiredService<AssetStore>());
s.AddSingleton<FontProvider>();
s.AddSingleton<OverlayRenderer>();
s.AddSingleton<IJpegCodec>(sp => WicJpegCodec.CreateOrFallback(sp.GetRequiredService<ILoggerFactory>().CreateLogger<WicJpegCodec>()));
s.AddSingleton<PawnIo>();
s.AddSingleton<ISensorSource, LhmSensorSource>();
s.AddSingleton<IRoleMap, LhmRoleMap>();
s.AddSingleton<SensorHub>();
s.AddSingleton<UiInterest>();
s.AddOptions<Microsoft.AspNetCore.Http.Features.FormOptions>().Configure<IConfiguration>((o, c) =>
    o.MultipartBodyLengthLimit = AssetsApi.MaxUploadBytes(c) + AssetsApi.MultipartOverhead);
s.AddSingleton<IUsbEnumerator, HidSharpUsbEnumerator>();
s.AddSingleton<IHidPortFactory, HidSharpPortFactory>();
s.AddSingleton<IDisplayDriver, ThermalrightHid5302Driver>();
s.AddSingleton(sp => new DeviceManager(
    sp.GetServices<IDisplayDriver>(), sp.GetRequiredService<IUsbEnumerator>(), sp.GetRequiredService<IHidPortFactory>(),
    conn => new DisplaySession(conn, sp.GetRequiredService<LayoutStore>(), sp.GetRequiredService<SettingsStore>(),
        sp.GetRequiredService<SensorHub>(), sp.GetRequiredService<IAssetStore>(), sp.GetRequiredService<OverlayRenderer>(),
        sp.GetRequiredService<FontProvider>(), sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<DisplaySession>(), sp.GetRequiredService<IJpegCodec>()),
    sp.GetRequiredService<SettingsStore>(), sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<DeviceManager>()));
s.AddHostedService(sp => sp.GetRequiredService<DeviceManager>());
s.AddHostedService<CoreRunner>();
s.AddGlanceSecurity(paths);

var app = builder.Build();
if (settings.LoadError is not null)
    app.Services.GetRequiredService<NoticeBoard>().Add(new Notice(
        $"settings-repaired-{DateTime.UtcNow:yyyyMMddHHmmss}", "warning",
        settings.Repairs.Count > 0
            ? $"Some settings were invalid and were reset: {settings.LoadError}. The original file is settings.json.corrupt."
            : $"settings.json could not be read ({settings.LoadError}), so all settings were reset to defaults. The original file is settings.json.corrupt."));
foreach (var warning in markerWarnings)
    app.Logger.LogWarning("{Warning}", warning);
foreach (var action in dataDirAcl?.Actions ?? [])
    app.Logger.LogInformation("Data directory: {Action}", action);
if (dataDirAcl?.Outcome == DataDirectoryAcl.Outcome.Quarantined)
    app.Logger.LogWarning("The data directory {Root} held files Glance could not trust; they were moved to {Quarantine} and Glance started from defaults", paths.Root, dataDirAcl.QuarantineDir);
else if (dataDirAcl?.Outcome == DataDirectoryAcl.Outcome.Tightened)
    app.Logger.LogInformation("The data directory {Root} was restricted to SYSTEM and Administrators", paths.Root);
if (NetworkConfig.StartupMessage(binding, settings.Current) is { } bindingMessage)
    app.Logger.LogError("{Message}", bindingMessage);
app.UseAuthentication();
app.UseMiddleware<AccessGate>();   // before static files and endpoints
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // index.html must be revalidated so an upgrade never leaves a browser asking for removed hashed bundles;
    // the bundles under /assets carry a content hash in their names and never change.
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    },
});
app.UseRouting();
app.UseRateLimiter();
app.MapAuthApi();
app.MapHealthApi();
app.MapSensorsApi();
app.MapLayoutsApi();
app.MapDevicesApi();
app.MapSettingsApi();
app.MapAssetsApi();
app.MapPreviewApi();
app.MapUiSupportApi();
app.MapNoticesApi();
app.MapEventsApi();
app.MapLogsApi();
app.Run();

public partial class Program;
