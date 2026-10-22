using Glance.Core.Layouts;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class DialRenderer
{
    public static void Draw(SKCanvas canvas, DialItem d, RenderContext ctx)
    {
        var st = GaugeState.Resolve(d, ctx);
        var cx = d.X + d.Width / 2;
        var cy = d.Y + d.Height / 2;
        var radius = Math.Min(d.Width, d.Height) / 2;
        var (start, sweep) = ArcGaugeRenderer.SafeAngles(d.StartAngle, d.SweepAngle);
        var ticks = Math.Clamp(d.Ticks, LayoutLimits.MinTicks, LayoutLimits.MaxTicks);
        if (radius > 8)
        {
            using var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, Color = st.Track };
            using (var path = new SKPath())
            {
                path.AddArc(new SKRect(cx - radius + 2, cy - radius + 2, cx + radius - 2, cy + radius - 2), start, sweep);
                canvas.DrawPath(path, track);
            }
            for (int i = 0; i <= ticks && ticks > 0; i++)
            {
                var (cos, sin) = Direction(start + sweep * i / ticks);
                canvas.DrawLine(cx + cos * radius * 0.85f, cy + sin * radius * 0.85f,
                    cx + cos * radius * 0.95f, cy + sin * radius * 0.95f, track);
            }
            var (nc, ns) = Direction(start + sweep * (float)st.Fraction);
            using var needle = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6,
                StrokeCap = SKStrokeCap.Round, Color = TextItems.ParseColor(d.NeedleColor),
            };
            canvas.DrawLine(cx, cy, cx + nc * radius * 0.8f, cy + ns * radius * 0.8f, needle);
            needle.Style = SKPaintStyle.Fill;
            canvas.DrawCircle(cx, cy, 8, needle);
        }
        ArcGaugeRenderer.DrawCaption(canvas, d, st, ctx, cx, cy + radius * 0.5f);
    }

    private static (float Cos, float Sin) Direction(float degrees)
    {
        var rad = degrees * MathF.PI / 180;
        return (MathF.Cos(rad), MathF.Sin(rad));
    }
}
