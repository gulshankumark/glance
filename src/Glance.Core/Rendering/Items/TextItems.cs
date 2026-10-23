using Glance.Core.Layouts;
using Glance.Core.Sensors;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class TextItems
{
    public static void DrawText(SKCanvas canvas, FontProvider fonts, string text, float x, float y,
        FontSpec? spec, string color, TextAlign align)
    {
        using var font = CreateFont(fonts, spec);
        using var paint = new SKPaint { IsAntialias = true, Color = ParseColor(color) };
        var origin = Origin(font, text, x, y, align);
        canvas.DrawText(text, origin.X, origin.Y, SKTextAlign.Left, font, paint);
    }

    /// <summary>Ink bounds of exactly what <see cref="DrawText"/> draws with the same arguments.</summary>
    public static SKRect MeasureText(FontProvider fonts, string text, float x, float y, FontSpec? spec, TextAlign align)
    {
        using var font = CreateFont(fonts, spec);
        var origin = Origin(font, text, x, y, align);
        font.MeasureText(text, out var bounds);
        bounds.Offset(origin);
        return bounds;
    }

    private static SKFont CreateFont(FontProvider fonts, FontSpec? spec)
    {
        var fontSpec = LayoutLimits.SafeFont(spec);
        return new SKFont(fonts.Get(fontSpec), fontSpec.Size) { Edging = SKFontEdging.SubpixelAntialias };
    }

    /// <summary>Left end of the baseline: <paramref name="y"/> is the top of the ascent, <paramref name="x"/> the alignment point.</summary>
    private static SKPoint Origin(SKFont font, string text, float x, float y, TextAlign align)
    {
        var width = font.MeasureText(text);
        var left = align switch { TextAlign.Center => x - width / 2, TextAlign.Right => x - width, _ => x };
        return new SKPoint(left, y - font.Metrics.Ascent);
    }

    public static SKColor ParseColor(string color) => SKColor.TryParse(color, out var c) ? c : SKColors.White;

    public static void Draw(SKCanvas canvas, TextItem t, RenderContext ctx) =>
        DrawText(canvas, ctx.Fonts, t.Text, t.X, t.Y, t.Font, t.Color, t.Align);

    public static SKRect Measure(TextItem t, RenderContext ctx) =>
        MeasureText(ctx.Fonts, t.Text, t.X, t.Y, t.Font, t.Align);

    public static void Draw(SKCanvas canvas, SensorItem s, RenderContext ctx) =>
        DrawText(canvas, ctx.Fonts, Content(s, ctx), s.X, s.Y, s.Font, s.Color, s.Align);

    public static SKRect Measure(SensorItem s, RenderContext ctx) =>
        MeasureText(ctx.Fonts, Content(s, ctx), s.X, s.Y, s.Font, s.Align);

    public static void Draw(SKCanvas canvas, ClockItem c, RenderContext ctx) =>
        DrawText(canvas, ctx.Fonts, Content(c, ctx), c.X, c.Y, c.Font, c.Color, c.Align);

    public static SKRect Measure(ClockItem c, RenderContext ctx) =>
        MeasureText(ctx.Fonts, Content(c, ctx), c.X, c.Y, c.Font, c.Align);

    /// <summary>The string a sensor item shows: label and formatted current value.</summary>
    private static string Content(SensorItem s, RenderContext ctx)
    {
        var id = ctx.Sensors.Resolve(SensorRef.Parse(s.Sensor));
        double? value = null;
        if (id is not null && ctx.Sensors.Current.Values.TryGetValue(id, out var v)) value = v;
        var kind = id is null ? null : ctx.Sensors.Info(id)?.Kind;
        return WithLabel(s.Label, ValueFormatter.Format(value, kind, s.Decimals, s.ShowUnit, ctx.Settings.Unit));
    }

    /// <summary>The string a clock item shows: label and formatted time.</summary>
    private static string Content(ClockItem c, RenderContext ctx) =>
        WithLabel(c.Label, ClockFormatter.Format(ctx.Now, c.TimeZone, c.Format, ctx.Settings.Clock24h));

    private static string WithLabel(string? label, string value) =>
        string.IsNullOrEmpty(label) ? value : $"{label} {value}";
}
