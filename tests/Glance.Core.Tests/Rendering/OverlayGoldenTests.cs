using static Glance.Core.Tests.RenderHelpers;

namespace Glance.Core.Tests.Rendering;

public class OverlayGoldenTests
{
    [Fact] public void Text_sensor_clock_golden_and_missing_value_dash()
    {
        var bmp = Render("""
            [{"type":"sensor","id":"a","x":40,"y":40,"sensor":"cpu/t","label":"CPU","font":{"size":48}},
                             {"type":"sensor","id":"b","x":40,"y":140,"sensor":"role:cpu.fan","label":"FAN","font":{"size":48}},
                             {"type":"clock","id":"c","x":900,"y":40,"timeZone":"Asia/Tokyo","label":"Tokyo","font":{"size":48}},
                             {"type":"image","id":"d","x":10,"y":10,"assetId":"missing","width":10,"height":10}]
            """,
                         values: new() { ["cpu/t"] = 51.6 }, now: new DateTimeOffset(2026, 10, 21, 14, 5, 9, TimeSpan.Zero));
        GoldenAssert.Match(bmp, "text-sensor-clock");
    }

    [Fact] public void Text_item_draws_pixels_at_position()
    {
        using var bmp = Render("""[{"type":"text","id":"t","x":100,"y":100,"text":"HELLO","font":{"size":60,"weight":700}}]""",
            new(), DateTimeOffset.UnixEpoch);
        bool any = false;
        for (int y = 100; y < 170 && !any; y++)
            for (int x = 100; x < 300 && !any; x++) any = bmp.GetPixel(x, y) != SkiaSharp.SKColors.Black;
        Assert.True(any);
        Assert.Equal(SkiaSharp.SKColors.Black, bmp.GetPixel(50, 50));
    }

    private sealed class ThrowingAssets : Glance.Core.Media.IAssetStore
    {
        public SkiaSharp.SKBitmap? GetImage(string assetId) => throw new InvalidOperationException("boom");
        public Glance.Core.Media.Animation? GetAnimation(string assetId) => null;
    }

    [Fact] public void Throwing_item_is_skipped_and_others_still_draw()
    {
        using var bmp = Render("""
            [{"type":"image","id":"i","x":0,"y":0,"assetId":"x","width":10,"height":10},
             {"type":"text","id":"t","x":100,"y":100,"text":"HELLO","font":{"size":60,"weight":700}}]
            """, new(), DateTimeOffset.UnixEpoch, assets: new ThrowingAssets());
        bool any = false;
        for (int y = 100; y < 170 && !any; y++)
            for (int x = 100; x < 300 && !any; x++) any = bmp.GetPixel(x, y) != SkiaSharp.SKColors.Black;
        Assert.True(any);
    }

    [Fact] public void Running_message_points_at_the_configured_web_address()
    {
        Assert.Equal("Glance is running — open http://localhost:8735",
            Glance.Core.Rendering.FallbackScreen.RunningMessage(new Glance.Core.Layouts.AppSettings()));
        Assert.Equal("Glance is running — open http://localhost:9001",
            Glance.Core.Rendering.FallbackScreen.RunningMessage(new Glance.Core.Layouts.AppSettings { Port = 9001, Https = true }));
        Assert.Equal("Glance is running — open https://localhost:9002",
            Glance.Core.Rendering.FallbackScreen.RunningMessage(new Glance.Core.Layouts.AppSettings { Port = 9002, Https = true, NetworkAccess = true }));
    }

    [Fact] public void Fallback_screen_has_text_on_black()
    {
        using var bmp = Glance.Core.Rendering.FallbackScreen.Render(1280, 480, "Glance is running — open http://localhost:8735", new Glance.Core.Rendering.FontProvider());
        Assert.Equal((1280, 480), (bmp.Width, bmp.Height));
        Assert.Equal(SkiaSharp.SKColors.Black, bmp.GetPixel(2, 2));
        bool any = false;
        for (int y = 200; y < 280 && !any; y++)
            for (int x = 0; x < 1280 && !any; x++) any = bmp.GetPixel(x, y) != SkiaSharp.SKColors.Black;
        Assert.True(any);
    }
}
