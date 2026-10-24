using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Glance.Core.Layouts;
using Glance.Core.Notices;
using Microsoft.Extensions.DependencyInjection;

namespace Glance.Service.Tests;

public class NoticesApiTests
{
    private static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Get_returns_added_notice()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        f.Services.GetRequiredService<NoticeBoard>().Add(new Notice("n1", "warning", "Something happened"));
        var list = await c.GetFromJsonAsync<JsonElement>("/api/notices");
        var n = Assert.Single(list.EnumerateArray());
        Assert.Equal("n1", n.GetProperty("id").GetString());
        Assert.Equal("warning", n.GetProperty("severity").GetString());
        Assert.Equal("Something happened", n.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Dismiss_hides_notice_and_persists()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        f.Services.GetRequiredService<NoticeBoard>().Add(new Notice("n1", "error", "Boom"));
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/notices/n1/dismiss", null)).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<JsonElement>("/api/notices")).EnumerateArray());

        // A fresh board over the same data directory (a restart) still hides it.
        var board = new NoticeBoard(new SettingsStore(new GlancePaths(f.DataDir)));
        board.Add(new Notice("n1", "error", "Boom"));
        Assert.Empty(board.Active);
    }

    [Fact]
    public async Task Notices_require_auth_when_network_access_on()
    {
        await using var f = new GlanceFactory(); var local = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await local.PutAsync("/api/settings/password", Json(new { @new = "correct horse" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await local.PutAsync("/api/settings/network", Json(new { enabled = true }))).StatusCode);
        var remote = f.CreateClient(remoteIp: "192.168.1.50");
        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.GetAsync("/api/notices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.PostAsync("/api/notices/x/dismiss", null)).StatusCode);
    }

    [Fact]
    public async Task Repaired_settings_raise_a_notice_at_startup()
    {
        await using var f = new GlanceFactory();
        File.WriteAllText(Path.Combine(f.DataDir, "settings.json"), """{"devices":{"0416:5302":{"gamma":9}}}""");
        var c = f.CreateClient();
        var list = await c.GetFromJsonAsync<JsonElement>("/api/notices");
        var n = Assert.Single(list.EnumerateArray());
        Assert.StartsWith("settings-repaired-", n.GetProperty("id").GetString());
        Assert.Equal("warning", n.GetProperty("severity").GetString());
        Assert.Contains("devices.0416:5302.gamma", n.GetProperty("text").GetString());
        Assert.Contains("settings.json.corrupt", n.GetProperty("text").GetString());
    }
}
