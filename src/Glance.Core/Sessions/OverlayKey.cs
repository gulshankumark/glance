using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sensors;

namespace Glance.Core.Sessions;

public static class OverlayKey
{
    /// <summary>
    /// Everything the overlay depends on: a hash of the layout JSON, each item's displayed sensor value or clock
    /// text, and (when any item draws history) the hub's sample time, since history advances once per sample.
    /// Equal keys mean the overlay renders identically.
    /// </summary>
    public static string Compute(Layout l, RenderContext ctx, OverlayRenderer r)
    {
        var hub = ctx.Sensors;
        var values = hub.Current.Values;
        var sb = new StringBuilder(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(l, GlanceJson.Options))));
        foreach (var item in l.Items)
        {
            sb.Append('|');
            switch (item)
            {
                case SensorItem s:
                {
                    var id = hub.Resolve(SensorRef.Parse(s.Sensor));
                    var kind = id is null ? null : hub.Info(id)?.Kind;
                    sb.Append(ValueFormatter.Format(Value(id), kind, s.Decimals, s.ShowUnit, ctx.Settings.Unit));
                    break;
                }
                case GaugeItem g:
                {
                    var id = hub.Resolve(SensorRef.Parse(g.Sensor));
                    sb.Append(id).Append('=').Append(Value(id)?.ToString("R", CultureInfo.InvariantCulture)).Append(ctx.Settings.Unit);
                    break;
                }
                case ClockItem c:
                    sb.Append(ClockFormatter.Format(ctx.Now, c.TimeZone, c.Format, ctx.Settings.Clock24h));
                    break;
            }
        }
        if (r.HistoryNeeds(l, hub).Values.Any(seconds => seconds > 0))
            sb.Append("|history@").Append(hub.Current.At.UtcTicks);
        return sb.ToString();

        double? Value(string? id) => id is not null && values.TryGetValue(id, out var v) ? v : null;
    }
}
