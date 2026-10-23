using System.Net;

namespace Glance.Service.Tests;

public class StaticFilesTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    public async Task Html_is_revalidated_on_every_load(string path)
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var r = await c.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-cache", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Hashed_assets_are_cached_for_a_year()
    {
        await using var f = new GlanceFactory();
        var assets = Path.Combine(f.DataDir, "wwwroot", "assets");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "index-abc123.js"), "console.log(1)");
        var c = f.CreateClient();
        var r = await c.GetAsync("/assets/index-abc123.js");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cc = r.Headers.CacheControl!;
        Assert.True(cc.Public);
        Assert.Equal(TimeSpan.FromSeconds(31536000), cc.MaxAge);
        Assert.Contains(cc.Extensions, e => e.Name == "immutable");
    }
}
