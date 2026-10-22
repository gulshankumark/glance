using System.Text.Json;
using Glance.Core.Layouts;

namespace Glance.Core.Tests.Layouts;

public class LayoutJsonTests
{
    public const string Sample = """
    { "name":"Gaming","width":1280,"height":480,
      "background":{"type":"color","color":"#101820"},
      "items":[
        {"type":"sensor","id":"a","x":40,"y":40,"sensor":"role:cpu.temperature","label":"CPU","decimals":0},
        {"type":"clock","id":"b","x":900,"y":40,"timeZone":"Asia/Tokyo","label":"Tokyo"},
        {"type":"arcGauge","id":"c","x":40,"y":200,"sensor":"role:gpu.load","width":200,"height":200,
         "thresholds":[{"from":0,"color":"#00FF00"},{"from":80,"color":"#FF0000"}]},
        {"type":"historyGraph","id":"d","x":300,"y":200,"sensor":"role:gpu.temperature","width":400,"height":150,"windowSeconds":300}
      ]}
    """;

    private static Layout Parse(string itemJson) => JsonSerializer.Deserialize<Layout>(
        $$"""{"name":"L","width":1280,"height":480,"background":{"type":"color","color":"#000000"},"items":[{{itemJson}}]}""",
        GlanceJson.Options)!;

    [Fact]
    public void Roundtrips_polymorphic_items()
    {
        var l = JsonSerializer.Deserialize<Layout>(Sample, GlanceJson.Options)!;
        Assert.IsType<ColorBackground>(l.Background);
        Assert.Collection(l.Items, i => Assert.IsType<SensorItem>(i), i => Assert.IsType<ClockItem>(i),
                                   i => Assert.IsType<ArcGaugeItem>(i), i => Assert.IsType<HistoryGraphItem>(i));
        var again = JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(l, GlanceJson.Options), GlanceJson.Options)!;
        Assert.Equal(300, ((HistoryGraphItem)again.Items[3]).WindowSeconds);
        Assert.Equal("Asia/Tokyo", ((ClockItem)again.Items[1]).TimeZone);
        Assert.Equal(80, ((ArcGaugeItem)again.Items[2]).Thresholds[1].From);
    }

    [Fact]
    public void Defaults_apply_and_sample_is_valid()
    {
        var l = JsonSerializer.Deserialize<Layout>(Sample, GlanceJson.Options)!;
        Assert.Empty(LayoutValidator.Validate(l));
        var arc = (ArcGaugeItem)l.Items[2];
        Assert.Equal((135f, 270f, 16f), (arc.StartAngle, arc.SweepAngle, arc.Thickness));
        Assert.Equal("#4AAFFF", arc.Color);
        Assert.Equal(60, new SparklineItem().WindowSeconds);
    }

    [Fact]
    public void Enums_serialize_as_camelCase_strings()
    {
        var json = JsonSerializer.Serialize(new TextItem { Id = "t", Align = TextAlign.Center }, typeof(LayoutItem), GlanceJson.Options);
        Assert.Contains("\"align\": \"center\"", json);
        Assert.Contains("\"type\": \"text\"", json);
    }

    [Theory]
    [InlineData("""{"type":"bar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"min":5,"max":5}""", "min must be less than max")]
    [InlineData("""{"type":"historyGraph","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"windowSeconds":4000}""", "windowSeconds must be 1..1800")]
    [InlineData("""{"type":"sensor","id":"x","x":0,"y":0,"sensor":"s","decimals":7}""", "decimals must be 0..3")]
    [InlineData("""{"type":"text","id":"x","x":0,"y":0,"text":"hi","color":"red"}""", "invalid color 'red'")]
    [InlineData("""{"type":"segmentedBar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"segments":1}""", "segments must be 2..50")]
    [InlineData("""{"type":"bar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"thresholds":[{"from":50,"color":"#FFFFFF"},{"from":10,"color":"#000000"}]}""", "thresholds must be sorted ascending")]
    public void Validator_reports(string itemJson, string error) =>
        Assert.Contains(LayoutValidator.Validate(Parse(itemJson)), e => e.Contains(error));

    [Theory]
    [InlineData("""{"type":"dial","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"ticks":2147483647}""", "ticks must be 0..100")]
    [InlineData("""{"type":"dial","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"ticks":-1}""", "ticks must be 0..100")]
    [InlineData("""{"type":"segmentedBar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"gap":101}""", "gap must be 0..100")]
    [InlineData("""{"type":"segmentedBar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"gap":-1}""", "gap must be 0..100")]
    [InlineData("""{"type":"arcGauge","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"thickness":0}""", "thickness must be 1..500")]
    [InlineData("""{"type":"arcGauge","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"thickness":501}""", "thickness must be 1..500")]
    [InlineData("""{"type":"arcGauge","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"startAngle":721}""", "startAngle must be -360..720")]
    [InlineData("""{"type":"dial","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"startAngle":-361}""", "startAngle must be -360..720")]
    [InlineData("""{"type":"dial","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"sweepAngle":0}""", "sweepAngle must be 1..360")]
    [InlineData("""{"type":"arcGauge","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"sweepAngle":361}""", "sweepAngle must be 1..360")]
    [InlineData("""{"type":"text","id":"x","x":0,"y":0,"text":"a","font":{"size":10000}}""", "font size must be 4..512")]
    [InlineData("""{"type":"sensor","id":"x","x":0,"y":0,"sensor":"s","font":{"size":3}}""", "font size must be 4..512")]
    [InlineData("""{"type":"clock","id":"x","x":0,"y":0,"font":{"weight":50}}""", "font weight must be 100..900")]
    [InlineData("""{"type":"bar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"font":{"size":100000}}""", "font size must be 4..512")]
    [InlineData("""{"type":"dial","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"font":{"weight":1000}}""", "font weight must be 100..900")]
    [InlineData("""{"type":"bar","id":"x","x":0,"y":0,"sensor":"s","width":5000,"height":10}""", "width and height must be 1..4096")]
    [InlineData("""{"type":"image","id":"x","x":0,"y":0,"assetId":"a","width":10,"height":4097}""", "width and height must be 1..4096")]
    [InlineData("""{"type":"text","id":"x","x":10000,"y":0,"text":"a"}""", "x and y must be -4096..8192")]
    [InlineData("""{"type":"sensor","id":"x","x":0,"y":-5000,"sensor":"s"}""", "x and y must be -4096..8192")]
    public void Validator_bounds_numbers_that_drive_rendering_work(string itemJson, string error) =>
        Assert.Contains(LayoutValidator.Validate(Parse(itemJson)), e => e.Contains(error));

    [Theory]
    [InlineData("""{"type":"bar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"font":null}""")]
    [InlineData("""{"type":"bar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"font":{"family":null,"size":20}}""")]
    [InlineData("""{"type":"dial","id":"x","x":-4096,"y":8192,"sensor":"s","width":4096,"height":1,"ticks":100,"startAngle":-360,"sweepAngle":360,"font":{"size":512,"weight":900}}""")]
    [InlineData("""{"type":"arcGauge","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"thickness":500,"startAngle":720,"sweepAngle":1,"font":{"size":4,"weight":100}}""")]
    [InlineData("""{"type":"segmentedBar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"gap":100,"segments":50}""")]
    public void Validator_accepts_bounds_and_a_missing_gauge_font(string itemJson) =>
        Assert.Empty(LayoutValidator.Validate(Parse(itemJson)));

    [Fact]
    public void Validator_rejects_size_duplicate_ids_and_accepts_argb()
    {
        var l = Parse("""{"type":"text","id":"x","x":0,"y":0,"text":"a","color":"#80FFFFFF"},{"type":"text","id":"x","x":0,"y":0,"text":"b"}""");
        l.Width = 5000;
        var errors = LayoutValidator.Validate(l);
        Assert.Contains("width must be 1..4096", errors);
        Assert.Contains(errors, e => e.Contains("duplicate id"));
        Assert.DoesNotContain(errors, e => e.Contains("invalid color"));
    }

    [Theory]
    [InlineData("../evil")] [InlineData("a/b")] [InlineData("")] [InlineData("con.json")]
    [InlineData("CON")] [InlineData("nul")] [InlineData("Com1")] [InlineData("lpt9")] [InlineData("a\\b")]
    [InlineData("trailing ")]
    public void Rejects_bad_names(string n) => Assert.False(LayoutValidator.IsValidName(n));

    [Theory]
    [InlineData("Gaming")] [InlineData("my layout_2-b")] [InlineData("console")]
    public void Accepts_good_names(string n) => Assert.True(LayoutValidator.IsValidName(n));

    [Fact]
    public void Type_discriminator_may_appear_anywhere_in_the_object()
    {
        var l = GlanceJson.Deserialize<Layout>("""
        {"items":[{"id":"a","x":1,"sensor":"s","type":"sensor","decimals":2},
                  {"id":"b","width":5,"height":5,"sensor":"s","type":"bar"}],
         "background":{"color":"#101010","type":"color"},"name":"L"}
        """)!;
        Assert.IsType<ColorBackground>(l.Background);
        Assert.Equal(2, ((SensorItem)l.Items[0]).Decimals);
        Assert.IsType<BarItem>(l.Items[1]);
        Assert.Empty(LayoutValidator.Validate(l));
    }

    [Theory]
    [InlineData("""{"name":"L","width":1,"height":1,"background":{"type":"color","color":"#000000"},"items":null}""", "items is required")]
    [InlineData("""{"name":"L","width":1,"height":1,"background":{"type":"color","color":"#000000"},"items":[{"type":"bar","id":"x","sensor":"s","width":1,"height":1,"thresholds":null}]}""", "thresholds must not be null")]
    [InlineData("""{"name":"L","width":1,"height":1,"background":{"type":"color","color":"#000000"},"items":[{"type":"bar","id":"x","sensor":"s","width":1,"height":1,"thresholds":[null]}]}""", "threshold is null")]
    [InlineData("""{"name":"L","width":1,"height":1,"background":{"type":"color","color":"#000000"},"items":[null]}""", "item is null")]
    public void Hand_edited_nulls_are_reported_not_thrown(string json, string error)
    {
        var l = GlanceJson.Deserialize<Layout>(json)!;
        Assert.Contains(LayoutValidator.Validate(l), e => e.Contains(error));
        using var dir = new Glance.Testing.TempDir();
        var store = new LayoutStore(new GlancePaths(dir.Path), TimeProvider.System);
        Assert.Throws<ArgumentException>(() => store.Save(l));
        File.WriteAllText(Path.Combine(dir.Path, "layouts", "L.json"), json);
        store.Reload("L");
        Assert.True(store.Errors.ContainsKey("L"));
    }
}
