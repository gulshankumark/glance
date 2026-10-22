using Glance.Core.Sensors;
using Glance.Testing;
using static Glance.Testing.HubFactory;

namespace Glance.Core.Tests.Sensors;

public class SensorHubTests
{
    [Fact] public void Tick_reads_only_interest_ids_and_publishes()
    {
        var src = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature), ("gpu/t", "gpu", SensorKind.Temperature));
        src.Values["cpu/t"] = 51.6; src.Values["gpu/t"] = 53;
        var hub = NewHub(src);
        hub.SetInterest(new Dictionary<string, int> { ["gpu/t"] = 0 });
        hub.Tick();
        Assert.Equal(new[] { "gpu/t" }, src.LastRequested);
        Assert.Equal(53, hub.Current.Values["gpu/t"]);
    }

    [Fact] public void Non_finite_readings_become_null_in_current_values_and_history()
    {
        var src = new FakeSensorSource(("a", "cpu", SensorKind.Load), ("b", "cpu", SensorKind.Load), ("c", "cpu", SensorKind.Load));
        src.Values["a"] = double.NaN; src.Values["b"] = double.PositiveInfinity; src.Values["c"] = double.NegativeInfinity;
        var hub = NewHub(src);
        hub.SetInterest(new Dictionary<string, int> { ["a"] = 2, ["b"] = 0, ["c"] = 0 });
        hub.Tick();
        Assert.Null(hub.Current.Values["a"]);
        Assert.Null(hub.Current.Values["b"]);
        Assert.Null(hub.Current.Values["c"]);
        Assert.Equal(new double?[] { null, null }, hub.History("a", 2));
    }

    [Fact] public void Source_exception_makes_values_null_and_hub_survives()
    {
        var src = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature)) { ThrowOnRead = true };
        var hub = NewHub(src); hub.SetInterest(new Dictionary<string, int> { ["cpu/t"] = 0 });
        hub.Tick();
        Assert.Null(hub.Current.Values["cpu/t"]);
    }

    [Fact] public void History_oldest_first_padded_with_null_and_capped_at_1800()
    {
        var src = new FakeSensorSource(("x", "cpu", SensorKind.Load));
        var hub = NewHub(src); hub.SetInterest(new Dictionary<string, int> { ["x"] = 5000 });
        for (int i = 1; i <= 3; i++) { src.Values["x"] = i; hub.Tick(); }
        Assert.Equal(new double?[] { null, null, 1, 2, 3 }, hub.History("x", 5));
        for (int i = 0; i < 2000; i++) hub.Tick();
        Assert.Equal(1800, hub.History("x", 5000).Count);
    }

    [Fact] public void Pin_beats_role_map_and_missing_pin_resolves_to_null()
    {
        var src = new FakeSensorSource(("fan1", "motherboard", SensorKind.Fan), ("fan2", "motherboard", SensorKind.Fan));
        var hub = NewHub(src, new FixedRoleMap("cpu.fan", "fan1"));
        Assert.Equal("fan1", hub.Resolve(SensorRef.Parse("role:cpu.fan")));
        hub.SetPins(new Dictionary<string, string> { ["cpu.fan"] = "fan2" });
        Assert.Equal("fan2", hub.Resolve(SensorRef.Parse("role:cpu.fan")));
        hub.SetPins(new Dictionary<string, string> { ["cpu.fan"] = "gone" });
        Assert.Null(hub.Resolve(SensorRef.Parse("role:cpu.fan")));
        Assert.Equal("fan2", hub.Resolve(SensorRef.Parse("fan2")));
    }

    [Fact] public void Unknown_interest_ids_are_ignored_and_zero_seconds_keeps_no_history()
    {
        var src = new FakeSensorSource(("a", "cpu", SensorKind.Load));
        src.Values["a"] = 1;
        var hub = NewHub(src);
        hub.SetInterest(new Dictionary<string, int> { ["a"] = 0, ["nope"] = 10 });
        hub.Tick();
        Assert.Equal(new[] { "a" }, src.LastRequested);
        Assert.All(hub.History("a", 3), v => Assert.Null(v));
        Assert.All(hub.History("nope", 3), v => Assert.Null(v));
    }

    [Fact] public void Sensor_ref_parse_round_trips()
    {
        var r = SensorRef.Parse("role:cpu.load");
        Assert.True(r.IsRole); Assert.Equal("cpu.load", r.Value); Assert.Equal("role:cpu.load", r.ToString());
        var i = SensorRef.Parse("cpu/t");
        Assert.False(i.IsRole); Assert.Equal("cpu/t", i.ToString());
    }

    [Fact] public void Tick_raises_Updated()
    {
        var src = new FakeSensorSource(("a", "cpu", SensorKind.Load));
        var hub = NewHub(src); hub.SetInterest(new Dictionary<string, int> { ["a"] = 0 });
        SensorSnapshot? got = null; hub.Updated += s => got = s;
        hub.Tick();
        Assert.Same(hub.Current, got);
    }

    [Fact] public void Interest_from_several_owners_is_merged_with_max_history()
    {
        var src = new FakeSensorSource(("a", "cpu", SensorKind.Load), ("b", "gpu", SensorKind.Load));
        var hub = NewHub(src);
        hub.SetInterest("screen1", new Dictionary<string, int> { ["a"] = 0 });
        hub.SetInterest("screen2", new Dictionary<string, int> { ["a"] = 3, ["b"] = 0 });
        src.Values["a"] = 7;
        hub.Tick();
        Assert.Equal(new[] { "a", "b" }, src.LastRequested);
        Assert.Equal(new double?[] { null, null, 7 }, hub.History("a", 3));
    }

    [Fact] public void Clearing_an_owner_keeps_the_others_interest()
    {
        var src = new FakeSensorSource(("a", "cpu", SensorKind.Load), ("b", "gpu", SensorKind.Load));
        var hub = NewHub(src);
        hub.SetInterest(new Dictionary<string, int> { ["a"] = 0 });
        hub.SetInterest("screen1", new Dictionary<string, int> { ["a"] = 5, ["b"] = 0 });
        src.Values["a"] = 1;
        hub.Tick();
        hub.ClearInterest("screen1");
        hub.Tick();
        Assert.Equal(new[] { "a" }, src.LastRequested);
        Assert.All(hub.History("a", 3), v => Assert.Null(v));   // only the default owner remains, with no history
        hub.SetInterest(new Dictionary<string, int>());          // replaces the default owner's interest
        hub.Tick();
        Assert.Empty(src.LastRequested);
    }
}
