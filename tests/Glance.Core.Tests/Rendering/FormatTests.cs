using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sensors;

namespace Glance.Core.Tests.Rendering;

public class FormatTests
{
    [Theory]
    [InlineData(51.62, SensorKind.Temperature, 0, TemperatureUnit.Celsius, "52 °C")]
    [InlineData(51.62, SensorKind.Temperature, 0, TemperatureUnit.Fahrenheit, "125 °F")]
    [InlineData(62.4, SensorKind.Load, 0, TemperatureUnit.Celsius, "62 %")]
    [InlineData(3836.0, SensorKind.Clock, 0, TemperatureUnit.Celsius, "3836 MHz")]
    [InlineData(37.21, SensorKind.Power, 1, TemperatureUnit.Celsius, "37.2 W")]
    [InlineData(1148.0, SensorKind.Fan, 0, TemperatureUnit.Celsius, "1148 RPM")]
    [InlineData(null, SensorKind.Fan, 0, TemperatureUnit.Celsius, "—")]
    public void Formats(double? v, SensorKind k, int d, TemperatureUnit u, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(v, k, d, showUnit: true, u));

    [Fact] public void Unit_can_be_hidden_and_other_kinds_have_none()
    {
        Assert.Equal("52", ValueFormatter.Format(51.62, SensorKind.Temperature, 0, false, TemperatureUnit.Celsius));
        Assert.Equal("5", ValueFormatter.Format(5.0, SensorKind.Other, 0, true, TemperatureUnit.Celsius));
        Assert.Equal("", ValueFormatter.Unit(SensorKind.Control, TemperatureUnit.Celsius));
    }

    [Fact] public void Clock_formats_zone_and_12h()
    {
        var now = new DateTimeOffset(2026, 10, 21, 14, 5, 9, TimeSpan.Zero);
        Assert.Equal("23:05", ClockFormatter.Format(now, "Asia/Tokyo", null, clock24h: true));
        Assert.Equal("11:05 PM", ClockFormatter.Format(now, "Asia/Tokyo", null, clock24h: false));
    }

    [Fact] public void Clock_custom_format_and_unknown_zone_falls_back_to_local()
    {
        var now = new DateTimeOffset(2026, 10, 21, 14, 5, 9, TimeSpan.Zero);
        Assert.Equal("2026-10-21", ClockFormatter.Format(now, "Asia/Tokyo", "yyyy-MM-dd", true));
        Assert.Equal(ClockFormatter.Format(now, "local", null, true), ClockFormatter.Format(now, "Nowhere/Land", null, true));
    }
}
