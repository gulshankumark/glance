using Microsoft.AspNetCore.Http;
using System.Net;
using Glance.Core.Devices;
using Glance.Core.Layouts;
using Glance.Core.Sensors;
using Glance.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Glance.Service.Tests;

/// <summary>The service with fake sensors, drivers and USB; never touches LHM, PawnIO or real HID.</summary>
public sealed class GlanceFactory : WebApplicationFactory<Program>
{
    private readonly TempDir _dir = new();
    private readonly string? _oldEnv = Environment.GetEnvironmentVariable("GLANCE_DATA");

    public GlanceFactory(bool withDevice = false)
    {
        Environment.SetEnvironmentVariable("GLANCE_DATA", _dir.Path);
        Connection = new FakeConnection();
        Driver = new FakeDriver { Always = () => new ConnectResult.Connected(Connection) };
        Usb = new FakeEnumerator();
        if (withDevice) Usb.Add(new UsbDeviceInfo(0x1234, 0x0001, "fake-path", 37, 513, "Fake"));
    }

    public string DataDir => _dir.Path;
    public FakeConnection Connection { get; }
    public FakeDriver Driver { get; }
    public FakeEnumerator Usb { get; }
    public SettingsStore Settings => Services.GetRequiredService<SettingsStore>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Glance:MaxUploadBytes", "65536");   // keeps the oversize-upload test small
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<ISensorSource>();
            s.RemoveAll<IRoleMap>();
            s.RemoveAll<IDisplayDriver>();
            s.RemoveAll<IUsbEnumerator>();
            s.RemoveAll<IHidPortFactory>();
            var source = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature), ("/amdcpu/0/temperature/2", "cpu", SensorKind.Temperature));
            source.Values["cpu/t"] = 50;
            source.Values["/amdcpu/0/temperature/2"] = 61;
            s.AddSingleton<ISensorSource>(source);
            s.AddSingleton<IRoleMap>(new FixedRoleMap("cpu.temperature", "cpu/t"));
            s.AddSingleton<IDisplayDriver>(Driver);
            s.AddSingleton<IUsbEnumerator>(Usb);
            s.AddSingleton<IHidPortFactory, NoPorts>();
            s.AddTransient<IStartupFilter, RemoteIpFilter>();
            // The test process may share the machine with a running Glance on the default port: start as if bound.
            s.RemoveAll<Glance.Service.Security.WebBindingState>();
            s.AddSingleton(new Glance.Service.Security.WebBindingState(Glance.Service.Security.StartupBinding.Configured));
        });
    }

    /// <summary>A client of the one shared host with its own cookie jar; <paramref name="remoteIp"/> becomes the request's RemoteIpAddress.</summary>
    public HttpClient CreateClient(string? remoteIp = null) =>
        CreateDefaultClient(new CookieContainerHandler(), new IpHandler(remoteIp ?? "127.0.0.1"));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        Environment.SetEnvironmentVariable("GLANCE_DATA", _oldEnv);
        _dir.Dispose();
    }

    private sealed class NoPorts : IHidPortFactory
    {
        public IHidPort Open(UsbDeviceInfo dev) => throw new NotSupportedException();
    }

    internal const string IpHeader = "X-Test-Remote-Ip";

    private sealed class IpHandler(string ip) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.Remove(IpHeader);
            request.Headers.Add(IpHeader, ip);
            return base.SendAsync(request, ct);
        }
    }

    private sealed class RemoteIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, n) =>
            {
                if (ctx.Request.Headers.TryGetValue(IpHeader, out var v))
                {
                    ctx.Connection.RemoteIpAddress = IPAddress.Parse(v.ToString());
                    ctx.Request.Headers.Remove(IpHeader);
                }
                if (ctx.Request.Path == "/__test/info")
                {
                    var settings = ctx.RequestServices.GetRequiredService<SettingsStore>();
                    await ctx.Response.WriteAsync($"{ctx.Connection.RemoteIpAddress}|{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(settings)}");
                    return;
                }
                await n(ctx);
            });
            next(app);
        };
    }
}
