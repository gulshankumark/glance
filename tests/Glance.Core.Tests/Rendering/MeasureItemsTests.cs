using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Glance.Testing;
using SkiaSharp;

namespace Glance.Core.Tests.Rendering;

public class MeasureItemsTests
{
    private static (Layout Layout, RenderContext Ctx) Setup(string itemsJson)
    {
        var layout = GlanceJson.Deserialize<Layout>(
            $$"""{"name":"t","width":640,"height":240,"items":{{itemsJson}}}""")!;
        var src = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature));
        src.Values["cpu/t"] = 57;
        var hub = HubFactory.NewHub(src);
        hub.SetInterest(new OverlayRenderer().HistoryNeeds(layout, hub).ToDictionary(kv => kv.Key, kv => kv.Value));
        hub.Tick();
        return (layout, new RenderContext(hub, new AppSettings(), DateTimeOffset.UnixEpoch, new NoAssets(), new FontProvider()));
    }

    /// <summary>Smallest rectangle holding every non-transparent pixel.</summary>
    private static SKRectI PixelBounds(SKBitmap bmp)
    {
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).Alpha != 0)
                {
                    l = Math.Min(l, x); t = Math.Min(t, y);
                    r = Math.Max(r, x + 1); b = Math.Max(b, y + 1);
                }
        return new SKRectI(l, t, r, b);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    public void MeasureItems_text_bounds_match_drawn_pixels(string align)
    {
        var (layout, ctx) = Setup($$$"""[{"type":"sensor","id":"s1","x":300,"y":60,"sensor":"cpu/t","label":"CPU","align":"{{{align}}}","font":{"family":"Inter","size":48,"weight":700}}]""");
        var renderer = new OverlayRenderer();
        using var bmp = renderer.Render(layout, ctx);
        var pixels = PixelBounds(bmp);
        var measured = renderer.MeasureItems(layout, ctx)["s1"];

        var inflated = SKRect.Inflate(measured, 1, 1);
        Assert.True(inflated.Left <= pixels.Left && inflated.Top <= pixels.Top
            && inflated.Right >= pixels.Right && inflated.Bottom >= pixels.Bottom, $"pixels {pixels} outside {measured}");
        Assert.True(pixels.Left - measured.Left <= 4 && pixels.Top - measured.Top <= 4
            && measured.Right - pixels.Right <= 4 && measured.Bottom - pixels.Bottom <= 4, $"{measured} too large for {pixels}");
    }

    [Fact]
    public void MeasureItems_gauge_uses_declared_size()
    {
        var (layout, ctx) = Setup("""
            [{"type":"arcGauge","id":"a","x":10,"y":20,"sensor":"cpu/t","width":150,"height":120,"showValue":true,"label":"CPU"},
             {"type":"bar","id":"b","x":200,"y":30,"sensor":"cpu/t","width":300,"height":24,"showValue":true}]
            """);
        var bounds = new OverlayRenderer().MeasureItems(layout, ctx);
        Assert.Equal(new SKRect(10, 20, 160, 140), bounds["a"]);
        Assert.Equal(new SKRect(200, 30, 500, 54), bounds["b"]);
    }

    [Fact]
    public void MeasureItems_image_uses_declared_size_and_covers_every_item()
    {
        var (layout, ctx) = Setup("""
            [{"type":"image","id":"i","x":5,"y":6,"assetId":"none","width":40,"height":30},
             {"type":"clock","id":"c","x":100,"y":100,"format":"HH:mm"},
             {"type":"text","id":"t","x":300,"y":100,"text":"Hi"}]
            """);
        var bounds = new OverlayRenderer().MeasureItems(layout, ctx);
        Assert.Equal(new SKRect(5, 6, 45, 36), bounds["i"]);
        Assert.Equal(["c", "i", "t"], bounds.Keys.Order());
        Assert.True(bounds["c"].Width > 0 && bounds["t"].Width > 0);
    }
}
