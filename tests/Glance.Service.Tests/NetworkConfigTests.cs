using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Glance.Core.Layouts;
using Glance.Service.Security;
using Glance.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Glance.Service.Tests;

public class NetworkConfigTests
{
    private static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    /// <summary>A loopback port held open by a listener for the test's duration.</summary>
    private static TcpListener Occupy()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return l;
    }

    private static int FreePort()
    {
        using var l = Occupy();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    [Fact]
    public void Probe_reports_an_endpoint_another_program_holds()
    {
        using var held = Occupy();
        var port = ((IPEndPoint)held.LocalEndpoint).Port;
        Assert.False(NetworkConfig.CanBind(new IPEndPoint(IPAddress.Loopback, port)));
        Assert.False(NetworkConfig.CanBindAll(new AppSettings { Port = port }));
        held.Stop();
        Assert.True(NetworkConfig.CanBind(new IPEndPoint(IPAddress.Loopback, port)));
    }

    [Fact]
    public void Startup_binding_falls_back_to_default_loopback_then_to_no_web_endpoint()
    {
        var s = new AppSettings { Port = 9100, NetworkAccess = true };
        Assert.Equal(StartupBinding.Configured, NetworkConfig.ChooseStartupBinding(s, configuredUsable: true, _ => true));
        Assert.Equal(StartupBinding.LoopbackFallback, NetworkConfig.ChooseStartupBinding(s, configuredUsable: true, ep => ep.Port != 9100));
        Assert.Equal(StartupBinding.LoopbackFallback, NetworkConfig.ChooseStartupBinding(s, configuredUsable: false, _ => true));
        // only one of the two default loopback endpoints is free: not good enough
        Assert.Equal(StartupBinding.None, NetworkConfig.ChooseStartupBinding(s, configuredUsable: true,
            ep => ep.Port != 9100 && !ep.Address.Equals(IPAddress.IPv6Loopback)));
        Assert.Equal(StartupBinding.None, NetworkConfig.ChooseStartupBinding(s, configuredUsable: true, _ => false));
    }

    [Fact]
    public void Startup_rebuilds_kestrel_json_from_saved_settings_under_every_binding_outcome()
    {
        using var dir = new TempDir();
        var paths = new GlancePaths(dir.Path);
        var settings = new SettingsStore(paths);
        settings.Update(s => { s.Port = 9100; s.NetworkAccess = true; s.PasswordHash = "x"; });
        File.WriteAllText(paths.KestrelConfig, """{"Kestrel":{"Endpoints":{"Stale":{"Url":"http://127.0.0.1:1234"}}}}""");

        Assert.Equal(StartupBinding.Configured, NetworkConfig.ApplyAtStartup(paths, settings, _ => true));
        var text = File.ReadAllText(paths.KestrelConfig);
        Assert.Contains("http://0.0.0.0:9100", text);
        Assert.DoesNotContain("1234", text);

        Assert.Equal(StartupBinding.LoopbackFallback, NetworkConfig.ApplyAtStartup(paths, settings, ep => ep.Port == AppSettings.DefaultPort));
        text = File.ReadAllText(paths.KestrelConfig);
        Assert.Contains("http://127.0.0.1:8735", text);
        Assert.Contains("http://[::1]:8735", text);
        Assert.DoesNotContain("0.0.0.0", text);
        Assert.Equal(9100, settings.Current.Port);           // settings stay the source of truth
        Assert.True(settings.Current.NetworkAccess);

        Assert.Equal(StartupBinding.None, NetworkConfig.ApplyAtStartup(paths, settings, _ => false));
        Assert.Contains("http://0.0.0.0:9100", File.ReadAllText(paths.KestrelConfig));
    }

    [Fact]
    public async Task Service_start_rewrites_a_stale_kestrel_json_from_settings()
    {
        await using var f = new GlanceFactory();
        var port = FreePort();
        File.WriteAllText(Path.Combine(f.DataDir, "settings.json"), $$"""{"port":{{port}}}""");
        File.WriteAllText(Path.Combine(f.DataDir, "kestrel.json"), """{"Kestrel":{"Endpoints":{"Stale":{"Url":"http://127.0.0.1:1234"}}}}""");
        f.CreateClient();
        var text = File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json"));
        Assert.Contains($"http://127.0.0.1:{port}", text);
        Assert.DoesNotContain("1234", text);
    }

    [Fact]
    public async Task Network_change_to_a_port_in_use_is_refused_and_changes_nothing()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        using var held = Occupy();
        var port = ((IPEndPoint)held.LocalEndpoint).Port;
        var kestrelBefore = File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json"));

        var r = await c.PutAsync("/api/settings/network", Json(new { enabled = false, port }));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains(port.ToString(), (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(AppSettings.DefaultPort, f.Settings.Current.Port);
        Assert.Equal(kestrelBefore, File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json")));

        held.Stop();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync("/api/settings/network", Json(new { enabled = false, port }))).StatusCode);
        Assert.Equal(port, f.Settings.Current.Port);
        Assert.Contains($"http://127.0.0.1:{port}", File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json")));
    }

    [Fact]
    public void Fallback_running_message_names_bound_port()
    {
        var s = new AppSettings { Port = 9100, NetworkAccess = true, Https = true };

        var message = NetworkConfig.StartupMessage(StartupBinding.LoopbackFallback, s);

        Assert.Contains("port 9100 on the network", message);   // what could not be used
        Assert.Contains($"http://localhost:{AppSettings.DefaultPort} ", message);   // what is bound: loopback, http
        Assert.Contains($"port 9100 nor the default port {AppSettings.DefaultPort}", NetworkConfig.StartupMessage(StartupBinding.None, s));
        Assert.Null(NetworkConfig.StartupMessage(StartupBinding.Configured, s));
        Assert.Equal(AppSettings.DefaultPort, NetworkConfig.HeldPort(StartupBinding.LoopbackFallback, 9100));
        Assert.Equal(9100, NetworkConfig.HeldPort(StartupBinding.Configured, 9100));
        Assert.Null(NetworkConfig.HeldPort(StartupBinding.None, 9100));
    }

    [Fact]
    public async Task Port_probe_of_current_fallback_port_is_not_conflict()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        f.Settings.Update(s => s.Port = FreePort());   // the configured port the fallback stands in for
        var binding = f.Services.GetRequiredService<WebBindingState>();
        binding.Mode = StartupBinding.LoopbackFallback;
        // Stands in for our own fallback listener; when this fails, a running Glance holds the port, which is as good.
        TcpListener? ours = null;
        try { ours = new TcpListener(IPAddress.Loopback, AppSettings.DefaultPort); ours.Start(); }
        catch (SocketException) { ours = null; }
        try
        {
            var r = await c.PutAsync("/api/settings/network", Json(new { enabled = false, port = AppSettings.DefaultPort }));

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(AppSettings.DefaultPort, f.Settings.Current.Port);
            Assert.Equal(StartupBinding.Configured, binding.Mode);
        }
        finally { ours?.Stop(); }
    }
}
