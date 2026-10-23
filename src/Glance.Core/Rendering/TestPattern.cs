using SkiaSharp;

namespace Glance.Core.Rendering;

/// <summary>
/// A full-screen calibration pattern: 16 grey steps from 0 to 255 across the top half; the bottom half split into
/// black, the default layout background, the default track colour over that background, and white, each labelled.
/// </summary>
public static class TestPattern
{
    private static readonly SKColor Background = SKColor.Parse("#101820");
    private static readonly SKColor Track = SKColor.Parse("#33FFFFFF");

    /// <summary>Draws the pattern over the whole <paramref name="width"/> x <paramref name="height"/> canvas (layout orientation).</summary>
    public static void Draw(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Black);
        var half = height / 2;
        using var fill = new SKPaint { IsAntialias = false };
        for (var i = 0; i < 16; i++)
        {
            var v = (byte)(i * 17);   // 0, 17, ..., 255
            fill.Color = new SKColor(v, v, v);
            canvas.DrawRect(SKRect.Create(i * width / 16, 0, (i + 1) * width / 16 - i * width / 16, half), fill);
        }

        var panels = new (string Label, SKColor[] Layers, SKColor Text)[]
        {
            ("Black", [SKColors.Black], SKColors.White),
            ("#101820", [Background], SKColors.White),
            ("#33FFFFFF on #101820", [Background, Track], SKColors.White),
            ("White", [SKColors.White], SKColors.Black),
        };
        var panelH = height - half;
        using var font = new SKFont(SKTypeface.Default, Math.Max(8, Math.Min(panelH / 10f, 28f)));
        using var text = new SKPaint { IsAntialias = true };
        for (var i = 0; i < panels.Length; i++)
        {
            var (label, layers, textColor) = panels[i];
            var rect = SKRect.Create(i * width / panels.Length, half, (i + 1) * width / panels.Length - i * width / panels.Length, panelH);
            foreach (var layer in layers)
            {
                fill.Color = layer;
                canvas.DrawRect(rect, fill);
            }
            var size = font.Size;
            var measured = font.MeasureText(label);
            if (measured > rect.Width * 0.9f) font.Size = size * rect.Width * 0.9f / measured;
            text.Color = textColor;
            canvas.DrawText(label, rect.MidX, rect.Bottom - font.Size, SKTextAlign.Center, font, text);
            font.Size = size;
        }
    }
}
