namespace Glance.Core.Layouts;

/// <summary>
/// Bounds on the layout numbers that drive rendering work. <see cref="LayoutValidator"/> rejects values outside them,
/// and renderers clamp to them as well, so a hand-edited value that never went through validation can never loop for
/// ever or allocate without limit.
/// <list type="bullet">
/// <item>Item <c>x</c>/<c>y</c>: -4096..8192 (one maximum layout size either side of the canvas).</item>
/// <item>Item <c>width</c>/<c>height</c> (images, gauges): 1..4096, the maximum layout size.</item>
/// <item>Font size 4..512, font weight 100..900 (CSS range). A gauge without a font or font family uses Inter.</item>
/// <item>Dial ticks 0..100; segmented bar segments 2..50 and gap 0..100; arc thickness 1..500.</item>
/// <item>Start angle -360..720, sweep angle 1..360 (degrees, arc gauges and dials).</item>
/// </list>
/// </summary>
public static class LayoutLimits
{
    public const float MinPosition = -4096, MaxPosition = 8192;
    public const float MinItemSize = 1, MaxItemSize = 4096;
    public const float MinFontSize = 4, MaxFontSize = 512;
    public const int MinFontWeight = 100, MaxFontWeight = 900;
    public const int MinTicks = 0, MaxTicks = 100;
    public const int MinSegments = 2, MaxSegments = 50;
    public const int MinGap = 0, MaxGap = 100;
    public const float MinThickness = 1, MaxThickness = 500;
    public const float MinStartAngle = -360, MaxStartAngle = 720;
    public const float MinSweepAngle = 1, MaxSweepAngle = 360;

    /// <summary>False for NaN as well as for values outside [lo, hi].</summary>
    public static bool InRange(double v, double lo, double hi) => v >= lo && v <= hi;

    /// <summary>Clamps into [lo, hi]; NaN becomes <paramref name="fallback"/>.</summary>
    public static float Clamp(float v, float lo, float hi, float fallback) => float.IsNaN(v) ? fallback : Math.Clamp(v, lo, hi);

    /// <summary>The font to render with: Inter when missing or without family, size and weight clamped.</summary>
    public static FontSpec SafeFont(FontSpec? f)
    {
        if (f is null) return new FontSpec();
        var family = string.IsNullOrWhiteSpace(f.Family) ? "Inter" : f.Family;
        var size = Clamp(f.Size, MinFontSize, MaxFontSize, new FontSpec().Size);
        var weight = Math.Clamp(f.Weight, MinFontWeight, MaxFontWeight);
        return family == f.Family && size == f.Size && weight == f.Weight ? f : new FontSpec(family, size, weight);
    }
}
