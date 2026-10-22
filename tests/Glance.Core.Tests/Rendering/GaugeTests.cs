using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using SkiaSharp;
using static Glance.Core.Tests.RenderHelpers;

namespace Glance.Core.Tests.Rendering;

public class GaugeTests
{
    const string BarJson = """{"type":"bar","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":20,"min":0,"max":100,"rounded":false, "color":"#00FF00","trackColor":"#333333","thresholds":[{"from":70,"color":"#FF0000"}]}""";

    [Fact] public void Horizontal_bar_fills_fraction_with_threshold_colour()
    {
        var b = Gauge(BarJson, value: 50);
        Assert.Equal(new SKColor(0, 255, 0), C(b, 90, 10));
        Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, 150, 10));
        Assert.Equal(new SKColor(255, 0, 0), C(Gauge(BarJson, value: 75), 90, 10));
    }

    [Fact] public void Vertical_bar_fills_from_bottom()
    {
        var b = Gauge("""{"type":"bar","id":"g","x":0,"y":0,"sensor":"s","width":20,"height":200,"vertical":true,"rounded":false,"min":0,"max":100,"color":"#00FF00","trackColor":"#333333"}""", value: 25);
        Assert.Equal(new SKColor(0, 255, 0), C(b, 10, 190));
        Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, 10, 20));
    }

    [Fact] public void Segmented_bar_colours_each_block_by_its_own_value()
    {
        var b = Gauge("""{"type":"segmentedBar","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":20,"segments":10,"gap":0,"min":0,"max":100, "trackColor":"#333333","thresholds":[{"from":0,"color":"#00FF00"},{"from":60,"color":"#FFFF00"},{"from":80,"color":"#FF0000"}]}""", value: 75);
        Assert.Equal(new SKColor(0, 255, 0), C(b, 10, 10));
        Assert.Equal(new SKColor(255, 255, 0), C(b, 130, 10));
        Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, 170, 10));
    }

    [Fact] public void Arc_gauge_sweeps_clockwise_from_start()
    {
        var b = Gauge("""{"type":"arcGauge","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":200,"min":0,"max":100, "startAngle":135,"sweepAngle":270,"thickness":20,"color":"#00FF00","trackColor":"#333333"}""", value: 50);
        Assert.Equal(new SKColor(0, 255, 0), C(b, PointOnArc(100, 100, 90, 250)));
        Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, PointOnArc(100, 100, 90, 300)));
    }

    [Fact] public void Dial_needle_points_to_value()
    {
        const string Dial = """{"type":"dial","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":200,"min":0,"max":100, "startAngle":135,"sweepAngle":270,"needleColor":"#FF0000"}""";
        Assert.Equal(new SKColor(255, 0, 0), C(Gauge(Dial, value: 0), PointOnArc(100, 100, 60, 135)));
        Assert.Equal(new SKColor(255, 0, 0), C(Gauge(Dial, value: 100), PointOnArc(100, 100, 60, 45)));
    }

    [Fact] public void Missing_value_draws_empty_gauge_and_dash()
    {
        var b = Gauge("""{"type":"bar","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":20,"min":0,"max":100,"rounded":false,"trackColor":"#333333","showValue":true}""", value: null);
        Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, 5, 10));
    }

    [Fact] public void Degenerate_gauge_does_not_disturb_later_items()
    {
        var layout = GlanceJson.Deserialize<Layout>("""
            {"name":"t","width":100,"height":100,"items":[
              {"type":"dial","id":"a","x":50,"y":50,"sensor":"s","width":0,"height":0},
              {"type":"bar","id":"b","x":0,"y":0,"sensor":"s","width":20,"height":20,"rounded":false,"min":0,"max":100,"color":"#00FF00"}]}
            """)!;
        var src = new Glance.Testing.FakeSensorSource(("s", "cpu", SensorKind.Load));
        src.Values["s"] = 100;
        var hub = Glance.Testing.HubFactory.NewHub(src);
        hub.SetInterest(new Dictionary<string, int> { ["s"] = 0 });
        hub.Tick();
        using var bmp = new OverlayRenderer().Render(layout,
            new RenderContext(hub, new AppSettings(), DateTimeOffset.UnixEpoch, new NoAssets(), new FontProvider()));
        Assert.Equal(new SKColor(0, 255, 0), C(bmp, 10, 10));
    }

    [Fact] public async Task Hand_edited_huge_numbers_are_clamped_so_rendering_finishes()
    {
        // Bypasses the validator, as a renderer may see a value no API call would let through.
        const string Items = """
            {"type":"dial","id":"d","x":0,"y":0,"sensor":"s","width":200,"height":200,"ticks":2147483647,"sweepAngle":3.4E38,"font":{"size":3.4E38,"weight":2147483647},"showValue":true},
            {"type":"segmentedBar","id":"b","x":0,"y":210,"sensor":"s","width":200,"height":20,"segments":2147483647,"gap":2147483647},
            {"type":"arcGauge","id":"a","x":210,"y":0,"sensor":"s","width":100,"height":100,"thickness":3.4E38,"startAngle":-3.4E38},
            {"type":"text","id":"t","x":0,"y":300,"text":"hi","font":{"size":3.4E38}}
            """;
        var render = Task.Run(() => Gauge(Items, value: 50).Dispose());
        Assert.Same(render, await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(10))));
        await render;
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"family":null,"size":20}""")]
    public void Gauge_without_font_family_draws_its_value_in_the_default_font(string font)
    {
        using var b = Gauge($$"""{"type":"bar","id":"g","x":0,"y":0,"sensor":"s","width":100,"height":40,"min":0,"max":100,"rounded":false,"showValue":true,"font":{{font}}}""", value: 50);
        var any = false;
        for (var y = 0; y < 60 && !any; y++)
            for (var x = 108; x < 250 && !any; x++) any = b.GetPixel(x, y).Alpha > 0;
        Assert.True(any);
    }

    [Theory] [InlineData(SensorKind.Load, 0, 100)] [InlineData(SensorKind.Temperature, 0, 100)]
    public void Auto_range_fixed_kinds(SensorKind k, double min, double max) =>
        Assert.Equal((min, max), GaugeMath.Range(new BarItem(), k, Array.Empty<double?>(), 42));

    [Fact] public void Auto_range_from_history() =>
        Assert.Equal((0, 2200), GaugeMath.Range(new BarItem(), SensorKind.Fan, new double?[] { 1000, 2000, null }, 1500));

    [Fact] public void Explicit_min_max_win_and_span_is_at_least_one()
    {
        Assert.Equal((10, 20), GaugeMath.Range(new BarItem { Min = 10, Max = 20 }, SensorKind.Fan, Array.Empty<double?>(), 5));
        Assert.Equal((0, 1), GaugeMath.Range(new BarItem(), SensorKind.Fan, Array.Empty<double?>(), null));
    }

    [Fact] public void Fraction_clamps_and_null_is_zero()
    {
        Assert.Equal(0, GaugeMath.Fraction(null, 0, 100));
        Assert.Equal(1, GaugeMath.Fraction(150, 0, 100));
        Assert.Equal(0, GaugeMath.Fraction(-5, 0, 100));
        Assert.Equal(0.25, GaugeMath.Fraction(25, 0, 100));
    }

    [Fact] public void ColorFor_picks_last_threshold_reached()
    {
        var t = new List<Threshold> { new(0, "#0F0"), new(60, "#FF0"), new(80, "#F00") };
        Assert.Equal("#FF0", GaugeMath.ColorFor(65, t, "#FFF"));
        Assert.Equal("#F00", GaugeMath.ColorFor(80, t, "#FFF"));
        Assert.Equal("#FFF", GaugeMath.ColorFor(-1, t, "#FFF"));
        Assert.Equal("#FFF", GaugeMath.ColorFor(null, t, "#FFF"));
    }
}
