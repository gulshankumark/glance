using System.Net.Http.Json;
using System.Text.Json;

// GlanceFactory sets the process-wide GLANCE_DATA variable, so tests must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Glance.Service.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_ok()
    {
        await using var f = new GlanceFactory();
        var r = await f.CreateClient().GetFromJsonAsync<JsonElement>("/api/health");
        Assert.Equal("ok", r.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(r.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task First_start_writes_kestrel_config_in_data_dir()
    {
        await using var f = new GlanceFactory();
        f.CreateClient();
        var text = File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json"));
        Assert.Contains("http://127.0.0.1:8735", text);
        Assert.Contains("http://[::1]:8735", text);
    }

    [Fact]
    public async Task Connected_device_gets_frames()
    {
        await using var f = new GlanceFactory(withDevice: true);
        f.CreateClient();
        for (var i = 0; i < 100 && f.Connection.Frames.Count == 0; i++) await Task.Delay(100);
        Assert.NotEmpty(f.Connection.Frames);
    }

    [Fact]
    public async Task Index_page_is_served()
    {
        await using var f = new GlanceFactory();
        var html = await f.CreateClient().GetStringAsync("/");
        Assert.Contains("<title>Glance</title>", html);
    }

    [Fact]
    public async Task Clients_and_factory_share_one_host()
    {
        await using var f = new GlanceFactory();
        var a = (await f.CreateClient().GetStringAsync("/__test/info")).Split('|');
        var b = (await f.CreateClient("192.168.1.9").GetStringAsync("/__test/info")).Split('|');
        Assert.Equal("127.0.0.1", a[0]);
        Assert.Equal("192.168.1.9", b[0]);
        Assert.Equal(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(f.Settings).ToString(), a[1]);
        Assert.Equal(a[1], b[1]);
    }
}
