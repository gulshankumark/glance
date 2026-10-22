using System.Globalization;

namespace Glance.Core.Rendering;

public static class ClockFormatter
{
    public static string Format(DateTimeOffset now, string timeZone, string? format, bool clock24h)
    {
        var zone = TimeZoneInfo.Local;
        if (!string.IsNullOrWhiteSpace(timeZone) && !timeZone.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var f = string.IsNullOrWhiteSpace(format) ? (clock24h ? "HH:mm" : "h:mm tt") : format;
        try { return local.ToString(f, CultureInfo.InvariantCulture); }
        catch (FormatException) { return local.ToString(clock24h ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture); }
    }
}
