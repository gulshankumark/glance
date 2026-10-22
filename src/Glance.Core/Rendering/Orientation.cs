using SkiaSharp;

namespace Glance.Core.Rendering;

public static class Orientation
{
    public static (int W, int H) Canvas(int nativeW, int nativeH, int rotation) =>
        Check(rotation) is 90 or 270 ? (nativeH, nativeW) : (nativeW, nativeH);

    /// <summary>Rotates the canvas clockwise by <paramref name="rotation"/> degrees to give the device image.</summary>
    public static SKBitmap ToDevice(SKBitmap canvas, int rotation) => Rotate(canvas, Check(rotation));

    public static SKBitmap FromDevice(SKBitmap device, int rotation) => Rotate(device, (360 - Check(rotation)) % 360);

    /// <summary>Scales the image to fit inside the canvas, keeping its aspect ratio, on black bars.</summary>
    public static SKBitmap FitToCanvas(SKBitmap layoutImage, int canvasW, int canvasH)
    {
        var result = new SKBitmap(canvasW, canvasH);
        using var c = new SKCanvas(result);
        c.Clear(SKColors.Black);
        var scale = Math.Min((float)canvasW / layoutImage.Width, (float)canvasH / layoutImage.Height);
        float w = layoutImage.Width * scale, h = layoutImage.Height * scale;
        var dest = SKRect.Create((canvasW - w) / 2, (canvasH - h) / 2, w, h);
        using var image = SKImage.FromBitmap(layoutImage);
        c.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return result;
    }

    private static int Check(int rotation) =>
        rotation is 0 or 90 or 180 or 270 ? rotation : throw new ArgumentOutOfRangeException(nameof(rotation), "rotation must be 0, 90, 180 or 270");

    private static SKBitmap Rotate(SKBitmap src, int clockwiseDegrees)
    {
        var swap = clockwiseDegrees is 90 or 270;
        var result = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height);
        using var c = new SKCanvas(result);
        c.Clear(SKColors.Transparent);
        switch (clockwiseDegrees)
        {
            case 90: c.Translate(src.Height, 0); break;
            case 180: c.Translate(src.Width, src.Height); break;
            case 270: c.Translate(0, src.Width); break;
        }
        c.RotateDegrees(clockwiseDegrees);
        c.DrawBitmap(src, 0, 0);
        return result;
    }
}
