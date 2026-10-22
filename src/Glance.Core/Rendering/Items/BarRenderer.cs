using Glance.Core.Layouts;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class BarRenderer
{
    public static void Draw(SKCanvas canvas, BarItem b, RenderContext ctx)
    {
        var st = GaugeState.Resolve(b, ctx);
        var rect = new SKRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
        canvas.Save();
        if (b.Rounded)
        {
            var r = Math.Min(b.Width, b.Height) / 2;
            using var clip = new SKRoundRect(rect, r, r);
            canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
        }
        using (var track = new SKPaint { IsAntialias = true, Color = st.Track }) canvas.DrawRect(rect, track);
        var fill = FillRect(rect, b.Vertical, (float)st.Fraction);
        using (var paint = new SKPaint { IsAntialias = true, Color = st.Color }) canvas.DrawRect(fill, paint);
        canvas.Restore();
        DrawCaption(canvas, b, st, ctx, rect);
    }

    internal static SKRect FillRect(SKRect rect, bool vertical, float fraction) => vertical
        ? new SKRect(rect.Left, rect.Bottom - rect.Height * fraction, rect.Right, rect.Bottom)
        : new SKRect(rect.Left, rect.Top, rect.Left + rect.Width * fraction, rect.Bottom);

    /// <summary>Value to the right of the bar, label above it.</summary>
    internal static void DrawCaption(SKCanvas canvas, GaugeItem g, GaugeState st, RenderContext ctx, SKRect rect)
    {
        var font = LayoutLimits.SafeFont(g.Font);
        if (g.ShowValue)
            TextItems.DrawText(canvas, ctx.Fonts, st.Text(ctx), rect.Right + 8, rect.MidY - font.Size / 2,
                font, "#FFFFFF", TextAlign.Left);
        if (!string.IsNullOrEmpty(g.Label))
            TextItems.DrawText(canvas, ctx.Fonts, g.Label, rect.Left, rect.Top - font.Size - 2,
                font, "#FFFFFF", TextAlign.Left);
    }
}
