using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Glance.Testing;
using SkiaSharp;
using static Glance.Core.Tests.RenderHelpers;

namespace Glance.Core.Tests.Rendering;

public class GraphTests
{
    const string LineJson = """{"type":"historyGraph","id":"g","x":0,"y":0,"sensor":"s","width":300,"height":100,"min":0,"max":100,"windowSeconds":60,"area":false,"grid":false,"color":"#00FF00"}""";
    const string AreaJson = """{"type":"historyGraph","id":"g","x":0,"y":0,"sensor":"s","width":300,"height":100,"min":0,"max":100,"windowSeconds":60,"area":true,"grid":false,"color":"#00FF00"}""";
    const string GridJson = """{"type":"historyGraph","id":"g","x":0,"y":0,"sensor":"s","width":300,"height":100,"min":0,"max":100,"windowSeconds":60,"area":false,"grid":true,"color":"#00FF00","trackColor":"#FF0000"}""";
    const string SparkJson = """{"type":"sparkline","id":"g","x":0,"y":0,"sensor":"s","width":100,"height":20,"min":0,"max":100,"windowSeconds":10,"color":"#00FF00"}""";

    static IEnumerable<double?> Flat(int n, double? v = 50) => Enumerable.Repeat(v, n);

    [Fact] public void Flat_history_draws_line_at_matching_height()
    {
        var b = Graph(LineJson, Flat(60));
        Assert.Equal(new SKColor(0, 255, 0), C(b, 150, 50));
        Assert.Equal(0, C(b, 150, 20).Alpha);
    }

    [Fact] public void Area_mode_fills_below_line()
    {
        var b = Graph(AreaJson, Flat(60));
        Assert.True(C(b, 150, 80).Alpha > 0);
        Assert.Equal(0, C(b, 150, 20).Alpha);
    }

    [Fact] public void Null_samples_leave_gaps()
    {
        var b = Graph(LineJson, Enumerable.Repeat<double?>(null, 30).Concat(Flat(30)));
        Assert.Equal(0, C(b, 60, 50).Alpha);
        Assert.Equal(new SKColor(0, 255, 0), C(b, 250, 50));
    }

    [Fact] public void NaN_and_infinity_samples_are_gaps()
    {
        // samples 20..22 are non-finite: x = i * 299/59 = ~101..111
        var b = Graph(LineJson, Flat(20).Concat(new double?[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }).Concat(Flat(37)));
        Assert.Equal(new SKColor(0, 255, 0), C(b, 50, 50));
        Assert.Equal(0, C(b, 106, 50).Alpha);
        Assert.Equal(new SKColor(0, 255, 0), C(b, 250, 50));
    }

    [Fact] public void Grid_draws_horizontal_lines_in_track_colour()
    {
        var b = Graph(GridJson, Flat(60, 100));
        Assert.Equal(new SKColor(255, 0, 0), C(b, 150, 20));
        Assert.Equal(0, C(b, 150, 10).Alpha);
    }

    [Fact] public void Sparkline_spans_full_width()
    {
        var b = Graph(SparkJson, Flat(10));
        Assert.Equal(new SKColor(0, 255, 0), C(b, 2, 10));
        Assert.Equal(new SKColor(0, 255, 0), C(b, 97, 10));
    }

    [Fact] public void Short_history_is_left_padded_with_gaps()
    {
        var b = Graph(LineJson, Flat(30));
        Assert.Equal(0, C(b, 60, 50).Alpha);
        Assert.Equal(new SKColor(0, 255, 0), C(b, 250, 50));
    }

    [Theory]
    [InlineData(LineJson, 60)]
    [InlineData(SparkJson, 10)]
    public void History_needs_report_window_seconds(string json, int seconds)
    {
        var layout = GlanceJson.Deserialize<Layout>($$"""{"name":"t","width":400,"height":400,"items":[{{json}}]}""")!;
        var hub = HubFactory.NewHub(new FakeSensorSource(("s", "cpu", SensorKind.Load)));
        var needs = new OverlayRenderer().HistoryNeeds(layout, hub);
        Assert.Equal(seconds, needs["s"]);
    }
}
