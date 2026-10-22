using Glance.Core.Layouts;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class SegmentedBarRenderer
{
    public static void Draw(SKCanvas canvas, SegmentedBarItem b, RenderContext ctx)
    {
        var st = GaugeState.Resolve(b, ctx);
        var n = Math.Clamp(b.Segments, 1, LayoutLimits.MaxSegments);
        var gap = Math.Clamp(b.Gap, LayoutLimits.MinGap, LayoutLimits.MaxGap);
        var length = b.Vertical ? b.Height : b.Width;
        var size = (length - gap * (n - 1)) / n;
        var lit = (int)Math.Floor(st.Fraction * n + 1e-9);
        using var paint = new SKPaint { IsAntialias = true };
        for (int i = 0; i < n; i++)
        {
            var offset = i * (size + gap);
            var rect = b.Vertical
                ? new SKRect(b.X, b.Y + b.Height - offset - size, b.X + b.Width, b.Y + b.Height - offset)
                : new SKRect(b.X + offset, b.Y, b.X + offset + size, b.Y + b.Height);
            if (i < lit)
            {
                var blockValue = st.Min + (i + 0.5) / n * (st.Max - st.Min);
                paint.Color = TextItems.ParseColor(GaugeMath.ColorFor(blockValue, b.Thresholds, b.Color));
            }
            else paint.Color = st.Track;
            canvas.DrawRect(rect, paint);
        }
        BarRenderer.DrawCaption(canvas, b, st, ctx, new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height));
    }
}
