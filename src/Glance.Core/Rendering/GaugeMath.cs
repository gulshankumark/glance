using Glance.Core.Layouts;
using Glance.Core.Rendering.Items;
using Glance.Core.Sensors;
using SkiaSharp;

namespace Glance.Core.Rendering;

public static class GaugeMath
{
    public static (double Min, double Max) Range(GaugeItem g, SensorKind? kind, IReadOnlyList<double?> history, double? value)
    {
        double min, max;
        if (kind is SensorKind.Load or SensorKind.Temperature) (min, max) = (0, 100);
        else
        {
            var peak = history.Append(value).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max();
            (min, max) = (0, Math.Max(peak, 0) * 11 / 10);
        }
        min = g.Min ?? min;
        max = g.Max ?? max;
        if (max < min + 1) max = min + 1;
        return (min, max);
    }

    public static double Fraction(double? value, double min, double max) =>
        value is not { } v || max <= min ? 0 : Math.Clamp((v - min) / (max - min), 0, 1);

    public static string ColorFor(double? value, IReadOnlyList<Threshold> t, string fallback)
    {
        if (value is not { } v) return fallback;
        var color = fallback;
        var best = double.NegativeInfinity;
        foreach (var th in t)
            if (th.From <= v && th.From >= best) { best = th.From; color = th.Color; }
        return color;
    }
}

/// <summary>Everything a gauge renderer needs, resolved once from the hub.</summary>
internal readonly record struct GaugeState(double? Value, SensorKind? Kind, double Min, double Max, double Fraction, SKColor Color, SKColor Track)
{
    private const int AutoRangeSeconds = 300;

    public static GaugeState Resolve(GaugeItem g, RenderContext ctx)
    {
        var id = ctx.Sensors.Resolve(SensorRef.Parse(g.Sensor));
        double? value = null;
        if (id is not null && ctx.Sensors.Current.Values.TryGetValue(id, out var v)) value = v;
        var kind = id is null ? null : ctx.Sensors.Info(id)?.Kind;
        var history = id is not null && (g.Min is null || g.Max is null)
            ? ctx.Sensors.History(id, AutoRangeSeconds) : Array.Empty<double?>();
        var (min, max) = GaugeMath.Range(g, kind, history, value);
        return new GaugeState(value, kind, min, max, GaugeMath.Fraction(value, min, max),
            TextItems.ParseColor(GaugeMath.ColorFor(value, g.Thresholds, g.Color)), TextItems.ParseColor(g.TrackColor));
    }

    public string Text(RenderContext ctx) => ValueFormatter.Format(Value, Kind, 0, true, ctx.Settings.Unit);
}
