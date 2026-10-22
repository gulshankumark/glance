using Glance.Core.Layouts;
using Glance.Core.Sensors;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class TextItems
{
    public static void DrawText(SKCanvas canvas, FontProvider fonts, string text, float x, float y,
        FontSpec? spec, string color, TextAlign align)
    {
        var fontSpec = LayoutLimits.SafeFont(spec);
        using var font = new SKFont(fonts.Get(fontSpec), fontSpec.Size) { Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint { IsAntialias = true, Color = ParseColor(color) };
        var width = font.MeasureText(text);
        var left = align switch { TextAlign.Center => x - width / 2, TextAlign.Right => x - width, _ => x };
        canvas.DrawText(text, left, y - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
    }

    public static SKColor ParseColor(string color) => SKColor.TryParse(color, out var c) ? c : SKColors.White;

    public static void Draw(SKCanvas canvas, TextItem t, RenderContext ctx) =>
        DrawText(canvas, ctx.Fonts, t.Text, t.X, t.Y, t.Font, t.Color, t.Align);

    public static void Draw(SKCanvas canvas, SensorItem s, RenderContext ctx)
    {
        var id = ctx.Sensors.Resolve(SensorRef.Parse(s.Sensor));
        double? value = null;
        if (id is not null && ctx.Sensors.Current.Values.TryGetValue(id, out var v)) value = v;
        var kind = id is null ? null : ctx.Sensors.Info(id)?.Kind;
        var text = ValueFormatter.Format(value, kind, s.Decimals, s.ShowUnit, ctx.Settings.Unit);
        DrawText(canvas, ctx.Fonts, WithLabel(s.Label, text), s.X, s.Y, s.Font, s.Color, s.Align);
    }

    public static void Draw(SKCanvas canvas, ClockItem c, RenderContext ctx)
    {
        var time = ClockFormatter.Format(ctx.Now, c.TimeZone, c.Format, ctx.Settings.Clock24h);
        DrawText(canvas, ctx.Fonts, WithLabel(c.Label, time), c.X, c.Y, c.Font, c.Color, c.Align);
    }

    private static string WithLabel(string? label, string value) =>
        string.IsNullOrEmpty(label) ? value : $"{label} {value}";
}
