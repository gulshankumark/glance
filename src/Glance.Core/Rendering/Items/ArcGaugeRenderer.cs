using Glance.Core.Layouts;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class ArcGaugeRenderer
{
    public static void Draw(SKCanvas canvas, ArcGaugeItem a, RenderContext ctx)
    {
        var st = GaugeState.Resolve(a, ctx);
        var cx = a.X + a.Width / 2;
        var cy = a.Y + a.Height / 2;
        var thickness = LayoutLimits.Clamp(a.Thickness, LayoutLimits.MinThickness, LayoutLimits.MaxThickness, 16);
        var (start, sweep) = SafeAngles(a.StartAngle, a.SweepAngle);
        var radius = Math.Min(a.Width, a.Height) / 2 - thickness / 2;
        if (radius > 0)
        {
            var oval = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = thickness };
            Stroke(canvas, paint, oval, start, sweep, st.Track);
            if (st.Fraction > 0) Stroke(canvas, paint, oval, start, sweep * (float)st.Fraction, st.Color);
        }
        DrawCaption(canvas, a, st, ctx, cx, cy);
    }

    /// <summary>Start and sweep clamped to <see cref="LayoutLimits"/> (NaN gives the defaults 135 and 270).</summary>
    internal static (float Start, float Sweep) SafeAngles(float start, float sweep) =>
        (LayoutLimits.Clamp(start, LayoutLimits.MinStartAngle, LayoutLimits.MaxStartAngle, 135),
         LayoutLimits.Clamp(sweep, LayoutLimits.MinSweepAngle, LayoutLimits.MaxSweepAngle, 270));

    private static void Stroke(SKCanvas canvas, SKPaint paint, SKRect oval, float start, float sweep, SKColor color)
    {
        using var path = new SKPath();
        path.AddArc(oval, start, sweep);
        paint.Color = color;
        canvas.DrawPath(path, paint);
    }

    /// <summary>Value centred on (<paramref name="cx"/>, <paramref name="cy"/>), label just below it.</summary>
    internal static void DrawCaption(SKCanvas canvas, GaugeItem g, GaugeState st, RenderContext ctx, float cx, float cy)
    {
        var font = LayoutLimits.SafeFont(g.Font);
        var size = font.Size;
        if (g.ShowValue)
            TextItems.DrawText(canvas, ctx.Fonts, st.Text(ctx), cx, cy - size / 2, font, "#FFFFFF", TextAlign.Center);
        if (!string.IsNullOrEmpty(g.Label))
            TextItems.DrawText(canvas, ctx.Fonts, g.Label, cx, g.ShowValue ? cy + size / 2 + 2 : cy - size / 2,
                font, "#FFFFFF", TextAlign.Center);
    }
}
