using System.Globalization;
using Glance.Core.Layouts;
using Glance.Core.Sensors;

namespace Glance.Core.Rendering;

public static class ValueFormatter
{
    public const string Missing = "—";

    public static string Format(double? value, SensorKind? kind, int decimals, bool showUnit, TemperatureUnit unit)
    {
        if (value is not { } v) return Missing;
        if (kind == SensorKind.Temperature && unit == TemperatureUnit.Fahrenheit) v = v * 9 / 5 + 32;
        var number = v.ToString("F" + Math.Clamp(decimals, 0, 6), CultureInfo.InvariantCulture);
        var u = kind is { } k && showUnit ? Unit(k, unit) : "";
        return u.Length == 0 ? number : $"{number} {u}";
    }

    public static string Unit(SensorKind kind, TemperatureUnit unit) => kind switch
    {
        SensorKind.Temperature => unit == TemperatureUnit.Fahrenheit ? "°F" : "°C",
        SensorKind.Load => "%",
        SensorKind.Clock => "MHz",
        SensorKind.Power => "W",
        SensorKind.Fan => "RPM",
        SensorKind.Data => "GB",
        SensorKind.SmallData => "MB",
        SensorKind.Voltage => "V",
        SensorKind.Throughput => "MB/s",
        _ => "",
    };
}
