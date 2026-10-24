using Glance.Core.Layouts;

namespace Glance.Core.Tests.Layouts;

public class SettingsRepairTests
{
    [Fact]
    public void Bad_port_resets_only_port()
    {
        var (s, r) = SettingsRepair.Load("""{"port":"abc","passwordHash":"h"}""");
        Assert.Equal(8735, s.Port);
        Assert.Equal("h", s.PasswordHash);
        Assert.Contains(r, x => x.StartsWith("port"));
    }

    [Fact]
    public void Out_of_range_fps_resets_only_fps()
    {
        var (s, r) = SettingsRepair.Load("""{"maxOverlayAnimationFps":0,"port":9000}""");
        Assert.Null(s.MaxOverlayAnimationFps);
        Assert.Equal(9000, s.Port);
        Assert.Contains(r, x => x.StartsWith("maxOverlayAnimationFps"));
    }

    [Fact]
    public void Unknown_unit_enum_resets_unit()
    {
        var (s, r) = SettingsRepair.Load("""{"unit":"Kelvin","clock24h":false}""");
        Assert.Equal(TemperatureUnit.Celsius, s.Unit);
        Assert.False(s.Clock24h);
        Assert.Contains(r, x => x.StartsWith("unit"));
    }

    [Fact]
    public void Bad_gamma_resets_only_that_devices_gamma()
    {
        var (s, r) = SettingsRepair.Load("""{"devices":{"0416:5302":{"gamma":9,"rotation":90},"0416:8001":{"gamma":2}}}""");
        Assert.Equal(1.0, s.Devices["0416:5302"].Gamma);
        Assert.Equal(90, s.Devices["0416:5302"].Rotation);
        Assert.Equal(2, s.Devices["0416:8001"].Gamma);
        Assert.Contains(r, x => x.Contains("devices.0416:5302.gamma"));
    }

    [Fact]
    public void Bad_rotation_and_brightness_reset_per_device()
    {
        var (s, r) = SettingsRepair.Load("""{"devices":{"a":{"rotation":45,"brightness":5,"gamma":2},"b":{"rotation":180,"brightness":50}}}""");
        Assert.Equal(0, s.Devices["a"].Rotation);
        Assert.Equal(100, s.Devices["a"].Brightness);
        Assert.Equal(2, s.Devices["a"].Gamma);
        Assert.Equal(180, s.Devices["b"].Rotation);
        Assert.Equal(50, s.Devices["b"].Brightness);
        Assert.Contains(r, x => x.Contains("devices.a.rotation"));
        Assert.Contains(r, x => x.Contains("devices.a.brightness"));
    }

    [Fact]
    public void Non_object_device_is_dropped_others_kept()
    {
        var (s, r) = SettingsRepair.Load("""{"devices":{"a":5,"b":{"rotation":90},"c":null}}""");
        Assert.Equal(["b"], s.Devices.Keys);
        Assert.Contains(r, x => x.Contains("devices.a"));
        Assert.Contains(r, x => x.Contains("devices.c"));
    }

    [Fact]
    public void Bad_alias_and_pin_entries_are_dropped_others_kept()
    {
        var (s, r) = SettingsRepair.Load("""{"aliases":{"a":"x","b":3},"rolePins":{"cpu":"s1","gpu":{}}}""");
        Assert.Equal(["a"], s.Aliases.Keys);
        Assert.Equal(["cpu"], s.RolePins.Keys);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Null_dictionaries_become_empty()
    {
        var (s, _) = SettingsRepair.Load("""{"devices":null,"aliases":null,"rolePins":null}""");
        Assert.Empty(s.Devices); Assert.Empty(s.Aliases); Assert.Empty(s.RolePins);
    }

    [Fact]
    public void Clean_file_reports_no_repairs()
    {
        var (s, r) = SettingsRepair.Load("""{"port":9000,"unit":"fahrenheit","devices":{"a":{"gamma":2,"rotation":90}},"unknownField":1}""");
        Assert.Empty(r);
        Assert.Equal(9000, s.Port);
        Assert.Equal(TemperatureUnit.Fahrenheit, s.Unit);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("null")]
    [InlineData("{ \"port\": ")]
    public void Non_object_text_throws_JsonException(string json) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SettingsRepair.Load(json));

    [Fact]
    public void Dismissed_notices_survive_a_repair_of_another_field()
    {
        var (s, r) = SettingsRepair.Load("""{"port":"abc","dismissedNotices":["a","b"]}""");
        Assert.Equal(["a", "b"], s.DismissedNotices);
        Assert.Contains(r, x => x.StartsWith("port"));
        Assert.Empty(SettingsRepair.Load("{}").Item1.DismissedNotices);
    }

    [Fact]
    public void Bad_seededThemes_type_resets_to_empty()
    {
        var (s, r) = SettingsRepair.Load("""{"port":9000,"seededThemes":"Blend"}""");
        Assert.Empty(s.SeededThemes);
        Assert.Equal(9000, s.Port);
        Assert.Contains(r, x => x.StartsWith("seededThemes"));
        Assert.Equal(["Blend"], SettingsRepair.Load("""{"seededThemes":["Blend"]}""").Item1.SeededThemes);
    }
}
