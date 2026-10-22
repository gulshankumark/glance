using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Glance.Testing;
using SkiaSharp;

namespace Glance.Core.Tests;

public sealed class NoAssets : IAssetStore
{
    public SKBitmap? GetImage(string assetId) => null;
    public Animation? GetAnimation(string assetId) => null;
}

public static class RenderHelpers
{
    /// <summary>Renders items on a 1280x480 black colour background with a fake hub holding <paramref name="values"/>.</summary>
    public static SKBitmap Render(string itemsJson, Dictionary<string, double?> values, DateTimeOffset now,
        AppSettings? settings = null, IAssetStore? assets = null)
    {
        var layout = GlanceJson.Deserialize<Layout>(
            $$"""{"name":"t","width":1280,"height":480,"background":{"type":"color","color":"#000000"},"items":{{itemsJson}}}""")!;
        var src = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature), ("mb/fan", "motherboard", SensorKind.Fan));
        foreach (var (k, v) in values) src.Values[k] = v;
        var hub = HubFactory.NewHub(src, new FixedRoleMap("cpu.fan", "mb/fan"));
        var renderer = new OverlayRenderer();
        hub.SetInterest(renderer.HistoryNeeds(layout, hub).ToDictionary(kv => kv.Key, kv => kv.Value));
        hub.Tick();
        var ctx = new RenderContext(hub, settings ?? new AppSettings(), now, assets ?? new NoAssets(), new FontProvider());
        var overlay = renderer.Render(layout, ctx);
        var bmp = new SKBitmap(layout.Width, layout.Height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        canvas.DrawBitmap(overlay, 0, 0);
        overlay.Dispose();
        return bmp;
    }

    public static SKBitmap Solid(int w, int h, SKColor color)
    {
        var bmp = new SKBitmap(w, h);
        bmp.Erase(color);
        return bmp;
    }

    public static SKBitmap Noise(int w, int h, int seed)
    {
        var rnd = new Random(seed);
        var bmp = new SKBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, new SKColor((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256)));
        return bmp;
    }

    public static SKColor C(SKBitmap bmp, int x, int y) => bmp.GetPixel(x, y);

    public static SKColor C(SKBitmap bmp, (int X, int Y) p) => bmp.GetPixel(p.X, p.Y);

    /// <summary>Pixel on the circle (Skia convention: 0 degrees = 3 o'clock, clockwise).</summary>
    public static (int X, int Y) PointOnArc(double cx, double cy, double r, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return ((int)Math.Round(cx + r * Math.Cos(rad)), (int)Math.Round(cy + r * Math.Sin(rad)));
    }

    /// <summary>Transparent 400x400 overlay holding one graph item; sensor "s" (kind Load) has exactly <paramref name="history"/>, latest = current.</summary>
    public static SKBitmap Graph(string itemJson, IEnumerable<double?> history)
    {
        var layout = GlanceJson.Deserialize<Layout>(
            $$"""{"name":"t","width":400,"height":400,"items":[{{itemJson}}]}""")!;
        var src = new FakeSensorSource(("s", "cpu", SensorKind.Load));
        var hub = HubFactory.NewHub(src);
        var renderer = new OverlayRenderer();
        hub.SetInterest(renderer.HistoryNeeds(layout, hub).ToDictionary(kv => kv.Key, kv => kv.Value));
        foreach (var v in history)
        {
            src.Values["s"] = v;
            hub.Tick();
        }
        var ctx = new RenderContext(hub, new AppSettings(), DateTimeOffset.UnixEpoch, new NoAssets(), new FontProvider());
        return renderer.Render(layout, ctx);
    }

    /// <summary>Transparent 400x400 overlay holding one gauge item; sensor "s" (kind Load) has <paramref name="value"/>.</summary>
    public static SKBitmap Gauge(string itemJson, double? value)
    {
        var layout = GlanceJson.Deserialize<Layout>(
            $$"""{"name":"t","width":400,"height":400,"items":[{{itemJson}}]}""")!;
        var src = new FakeSensorSource(("s", "cpu", SensorKind.Load));
        src.Values["s"] = value;
        var hub = HubFactory.NewHub(src);
        var renderer = new OverlayRenderer();
        hub.SetInterest(renderer.HistoryNeeds(layout, hub).ToDictionary(kv => kv.Key, kv => kv.Value));
        hub.Tick();
        var ctx = new RenderContext(hub, new AppSettings(), DateTimeOffset.UnixEpoch, new NoAssets(), new FontProvider());
        return renderer.Render(layout, ctx);
    }
}
