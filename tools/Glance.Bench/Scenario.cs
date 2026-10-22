using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Glance.Testing;

namespace Glance.Bench;

/// <summary>The measured layout: the animation background plus two sensor texts and a clock.</summary>
public static class Scenario
{
    public const string AnimatedLayout = """
        {"name":"Anim","width":1280,"height":480,"background":{"type":"animation","assetId":"anim"},"items":[
          {"type":"sensor","id":"cpu","x":40,"y":60,"sensor":"cpu/t","label":"CPU","font":{"size":48}},
          {"type":"sensor","id":"gpu","x":40,"y":160,"sensor":"gpu/t","label":"GPU","font":{"size":48}},
          {"type":"clock","id":"clk","x":1240,"y":400,"align":"right","format":"HH:mm:ss","font":{"size":56}}]}
        """;

    public const string StillLayout = """
        {"name":"Still","width":1280,"height":480,"background":{"type":"color","color":"#102030"},"items":[
          {"type":"sensor","id":"cpu","x":40,"y":60,"sensor":"cpu/t","label":"CPU","font":{"size":48}}]}
        """;

    public static FakeSensorSource NewSource() =>
        new(("cpu/t", "cpu", SensorKind.Temperature), ("gpu/t", "gpu", SensorKind.Temperature));

    public static Layout Animated => GlanceJson.Deserialize<Layout>(AnimatedLayout)!;
}
