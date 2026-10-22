using System.Text.RegularExpressions;
using static Glance.Core.Layouts.LayoutLimits;

namespace Glance.Core.Layouts;

public static partial class LayoutValidator
{
    [GeneratedRegex("^[A-Za-z0-9 _-]{1,64}$")]
    private static partial Regex NameRegex();

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex ColorRegex();

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Names double as file names, so reject separators, dots, trailing spaces and device names.</summary>
    public static bool IsValidName(string name) =>
        name is not null && NameRegex().IsMatch(name) && name.TrimEnd() == name && !Reserved.Contains(name.Trim());

    public static bool IsValidColor(string? c) => c is not null && ColorRegex().IsMatch(c);

    public static IReadOnlyList<string> Validate(Layout l)
    {
        var e = new List<string>();
        if (!IsValidName(l.Name)) e.Add($"invalid layout name '{l.Name}'");
        if (l.Width is < 1 or > 4096) e.Add("width must be 1..4096");
        if (l.Height is < 1 or > 4096) e.Add("height must be 1..4096");

        switch (l.Background)
        {
            case ColorBackground c when !IsValidColor(c.Color): e.Add($"background: invalid color '{c.Color}'"); break;
            case ImageBackground i when string.IsNullOrWhiteSpace(i.AssetId): e.Add("background: assetId is required"); break;
            case AnimationBackground a when string.IsNullOrWhiteSpace(a.AssetId): e.Add("background: assetId is required"); break;
            case null: e.Add("background is required"); break;
        }

        var ids = new HashSet<string>();
        if (l.Items is null) { e.Add("items is required"); return e; }
        foreach (var item in l.Items)
        {
            if (item is null) { e.Add("item is null"); continue; }
            var p = $"item '{item.Id}': ";
            if (string.IsNullOrWhiteSpace(item.Id)) e.Add("item id is required");
            else if (!ids.Add(item.Id)) e.Add(p + "duplicate id");
            ValidateItem(item, p, e);
        }
        return e;
    }

    private static void ValidateItem(LayoutItem item, string p, List<string> e)
    {
        void Color(string c) { if (!IsValidColor(c)) e.Add($"{p}invalid color '{c}'"); }
        void Font(FontSpec? f)
        {
            if (f is null || string.IsNullOrWhiteSpace(f.Family)) e.Add(p + "invalid font");
            else FontBounds(f, p, e);
        }

        if (!InRange(item.X, MinPosition, MaxPosition) || !InRange(item.Y, MinPosition, MaxPosition))
            e.Add($"{p}x and y must be {MinPosition}..{MaxPosition}");
        switch (item)
        {
            case TextItem t: Color(t.Color); Font(t.Font); break;
            case SensorItem s:
                if (string.IsNullOrWhiteSpace(s.Sensor)) e.Add(p + "sensor is required");
                Color(s.Color); Font(s.Font);
                if (s.Decimals is < 0 or > 3) e.Add(p + "decimals must be 0..3");
                break;
            case ClockItem c: Color(c.Color); Font(c.Font); break;
            case ImageItem i:
                if (string.IsNullOrWhiteSpace(i.AssetId)) e.Add(p + "assetId is required");
                Size(i.Width, i.Height, p, e);
                break;
            case GaugeItem g: ValidateGauge(g, p, e); break;
        }
    }

    private static void ValidateGauge(GaugeItem g, string p, List<string> e)
    {
        if (string.IsNullOrWhiteSpace(g.Sensor)) e.Add(p + "sensor is required");
        Size(g.Width, g.Height, p, e);
        if (g.Min is { } min && g.Max is { } max && min >= max) e.Add(p + "min must be less than max");
        foreach (var c in new[] { g.Color, g.TrackColor })
            if (!IsValidColor(c)) e.Add($"{p}invalid color '{c}'");
        if (g.Font is not null) FontBounds(g.Font, p, e);   // a gauge without font (family) uses Inter

        double prev = double.NegativeInfinity;
        if (g.Thresholds is null) e.Add(p + "thresholds must not be null");
        foreach (var t in g.Thresholds ?? new())
        {
            if (t is null) { e.Add(p + "threshold is null"); continue; }
            if (!IsValidColor(t.Color)) e.Add($"{p}invalid color '{t.Color}'");
            if (t.From < prev) { e.Add(p + "thresholds must be sorted ascending by from"); break; }
            prev = t.From;
        }

        switch (g)
        {
            case SegmentedBarItem s:
                if (s.Segments is < MinSegments or > MaxSegments) e.Add($"{p}segments must be {MinSegments}..{MaxSegments}");
                if (s.Gap is < MinGap or > MaxGap) e.Add($"{p}gap must be {MinGap}..{MaxGap}");
                break;
            case ArcGaugeItem a:
                Angles(a.StartAngle, a.SweepAngle, p, e);
                if (!InRange(a.Thickness, MinThickness, MaxThickness)) e.Add($"{p}thickness must be {MinThickness}..{MaxThickness}");
                break;
            case DialItem d:
                if (!IsValidColor(d.NeedleColor)) e.Add($"{p}invalid color '{d.NeedleColor}'");
                Angles(d.StartAngle, d.SweepAngle, p, e);
                if (d.Ticks is < MinTicks or > MaxTicks) e.Add($"{p}ticks must be {MinTicks}..{MaxTicks}");
                break;
            case HistoryGraphItem h:
                if (h.WindowSeconds is < 1 or > 1800) e.Add(p + "windowSeconds must be 1..1800");
                break;
            case SparklineItem sp:
                if (sp.WindowSeconds is < 1 or > 1800) e.Add(p + "windowSeconds must be 1..1800");
                break;
        }
    }

    private static void FontBounds(FontSpec f, string p, List<string> e)
    {
        if (!InRange(f.Size, MinFontSize, MaxFontSize)) e.Add($"{p}font size must be {MinFontSize}..{MaxFontSize}");
        if (f.Weight is < MinFontWeight or > MaxFontWeight) e.Add($"{p}font weight must be {MinFontWeight}..{MaxFontWeight}");
    }

    private static void Size(float w, float h, string p, List<string> e)
    {
        if (!InRange(w, MinItemSize, MaxItemSize) || !InRange(h, MinItemSize, MaxItemSize))
            e.Add($"{p}width and height must be {MinItemSize}..{MaxItemSize}");
    }

    private static void Angles(float start, float sweep, string p, List<string> e)
    {
        if (!InRange(start, MinStartAngle, MaxStartAngle)) e.Add($"{p}startAngle must be {MinStartAngle}..{MaxStartAngle}");
        if (!InRange(sweep, MinSweepAngle, MaxSweepAngle)) e.Add($"{p}sweepAngle must be {MinSweepAngle}..{MaxSweepAngle}");
    }
}
