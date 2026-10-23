using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Glance.Service.Tests;

public class UiSupportApiTests
{
    private const string BoundsLayout = """
        {"name":"Draft","width":640,"height":240,"items":[
          {"type":"bar","id":"bar","x":10,"y":20,"width":200,"height":30,"sensor":"role:cpu.temperature"},
          {"type":"sensor","id":"temp","x":300,"y":100,"sensor":"cpu/t","font":{"size":40}}]}
        """;

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Render_bounds_returns_rect_per_item()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var r = await c.PostAsync("/api/render/bounds", Json(BoundsLayout));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var bar = body.GetProperty("bar");
        Assert.Equal((10f, 20f, 200f, 30f), (bar.GetProperty("x").GetSingle(), bar.GetProperty("y").GetSingle(),
            bar.GetProperty("width").GetSingle(), bar.GetProperty("height").GetSingle()));
        var temp = body.GetProperty("temp");
        // Ink bounds of the drawn string (a value or the missing-value dash) below the item's top-left corner.
        var (x, y) = (temp.GetProperty("x").GetSingle(), temp.GetProperty("y").GetSingle());
        Assert.InRange(x, 299, 310);
        Assert.InRange(y, 100, 150);
        Assert.InRange(temp.GetProperty("width").GetSingle(), 5, 300);
        Assert.InRange(y + temp.GetProperty("height").GetSingle(), y + 1, 150);
        Assert.False(File.Exists(Path.Combine(f.DataDir, "layouts", "Draft.json")));
    }

    [Fact]
    public async Task Render_bounds_validates_like_render_and_holds_the_ui_lease()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var ui = f.Services.GetRequiredService<UiInterest>();
        ui.Lease = TimeSpan.FromMilliseconds(300);
        var bad = await c.PostAsync("/api/render/bounds", Json("""{"name":"x","width":0,"height":480,"items":[]}"""));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True((await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/render/bounds", Json("{not json"))).StatusCode);
        Assert.True(ui.Active);
        for (var i = 0; i < 100 && ui.Active; i++) await Task.Delay(50);
        Assert.False(ui.Active);
    }

    [Fact]
    public async Task Fonts_include_Inter_default()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var body = await c.GetFromJsonAsync<JsonElement>("/api/fonts");
        Assert.Equal("Inter", body.GetProperty("default").GetString());
        var families = body.GetProperty("families").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal("Inter", families[0]);
        Assert.Equal(families.Count, families.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(families.Skip(1).Order(StringComparer.OrdinalIgnoreCase), families.Skip(1));
    }

    [Fact]
    public async Task Timezones_start_with_local_and_contain_Asia_Tokyo()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var zones = (await c.GetFromJsonAsync<JsonElement>("/api/timezones")).EnumerateArray().ToList();
        Assert.Equal("local", zones[0].GetProperty("id").GetString());
        Assert.Equal("Local time", zones[0].GetProperty("label").GetString());
        var tokyo = zones.Single(z => z.GetProperty("id").GetString() == "Asia/Tokyo");
        Assert.StartsWith("(UTC+09:00)", tokyo.GetProperty("label").GetString());
        var ids = zones.Select(z => z.GetProperty("id").GetString()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain("Tokyo Standard Time", ids);   // IANA ids, never Windows ids
        var offsets = ids.Skip(1).Select(id => TimeZoneInfo.FindSystemTimeZoneById(id!).BaseUtcOffset).ToList();   // clocks can use every id
        Assert.Equal(offsets.Order(), offsets);
    }
}
