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

if (Cli.TryRun(args) is { } cliExit) return cliExit;

var builder = WebApplication.CreateBuilder(args);

// Before anything is read from or written to it (settings, keys, logs): only SYSTEM and Administrators may use it.
// Nothing is created inside it until then: the subfolders follow the enforcement. The takeover marker sits next to the
// data directory (%ProgramData%), so it can be written before anything changes. A quarantine moved the subfolders aside
// too; they are created afterwards, empty, so Glance starts from defaults.
var root = GlancePaths.DefaultRoot();
var markerWarnings = new List<string>();
var (paths, dataDirAcl) = DataDirectoryStartup.Open(root,
    DataDirectoryAcl.ShouldEnforce(GlancePaths.IsOverridden, DataDirectoryAcl.IsElevated()),
    new FileTakeoverMarker(Directory.GetParent(root)?.FullName ?? root, markerWarnings.Add),
    DataDirectoryAcl.CreateHardenedIfMissing, DataDirectoryAcl.Enforce);
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
    sp.GetRequiredService<SettingsStore>(), n => sp.GetRequiredService<LayoutStore>().Get(n) is not null,
    sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger<DeviceManager>()));
s.AddHostedService(sp => sp.GetRequiredService<DeviceManager>());
s.AddHostedService<CoreRunner>();
s.AddHostedService(sp => new ShortcutSync(
    sp.GetRequiredService<SettingsStore>(), sp.GetRequiredService<WebBindingState>(),
    [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Glance.url"),
     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Glance.url")],
    sp.GetRequiredService<ILogger<ShortcutSync>>()));
s.AddSingleton<IFirewall, NetshFirewall>();
s.AddHostedService(sp => new FirewallSync(
    sp.GetRequiredService<SettingsStore>(), sp.GetRequiredService<WebBindingState>(),
    sp.GetRequiredService<IFirewall>(), DataDirectoryAcl.IsElevated,
    sp.GetRequiredService<ILogger<FirewallSync>>()));
s.AddGlanceSecurity(paths);

var app = builder.Build();
// Hosted services (so DeviceManager) start at app.Run, after this; a theme problem never stops startup.
var seeded = ThemeSeeder.Seed(Path.Combine(AppContext.BaseDirectory, "themes"),
    app.Services.GetRequiredService<LayoutStore>(), settings, m => app.Logger.LogWarning("{Message}", m));
if (seeded.Count > 0) app.Logger.LogInformation("Copied shipped themes: {Themes}", string.Join(", ", seeded));
if (settings.LoadError is not null)
    app.Services.GetRequiredService<NoticeBoard>().Add(new Notice(
        $"settings-repaired-{DateTime.UtcNow:yyyyMMddHHmmss}", "warning",
        settings.Repairs.Count > 0
            ? $"Some settings were invalid and were reset: {settings.LoadError}. The original file is settings.json.corrupt."
            : $"settings.json could not be read ({settings.LoadError}), so all settings were reset to defaults. The original file is settings.json.corrupt."));
DataDirectoryStartup.AddQuarantineNotice(app.Services.GetRequiredService<NoticeBoard>(), dataDirAcl);
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
return 0;

public partial class Program;
