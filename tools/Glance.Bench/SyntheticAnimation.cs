using Glance.Core.Media;
using SkiaSharp;

namespace Glance.Bench;

/// <summary>Moving-gradient-plus-noise frames, encoded at quality 85 to roughly 60 KB each (like a real .zt).</summary>
public static class SyntheticAnimation
{
    public static Animation Create(int count, int w, int h, int delayMs)
    {
        var frames = new byte[count][];
        var rnd = new Random(42);
        using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var noise = new byte[w * h];
        rnd.NextBytes(noise);
        for (var f = 0; f < count; f++)
        {
            unsafe
            {
                var p = (uint*)bmp.GetPixels();
                var phase = f * 7;
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var n = (noise[(y * w + x + f * 131) % noise.Length] & 0x03) - 2;
                    var r = Clamp(((x + phase) & 255) + n);
                    var g = Clamp(((y * 2 + phase / 2) & 255) + n);
                    var b = Clamp((((x + y) / 3 + phase) & 255) + n);
                    p[y * w + x] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                }
            }
            bmp.NotifyPixelsChanged();
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            frames[f] = data.ToArray();
        }
        return new Animation(w, h, frames, Enumerable.Repeat(delayMs, count).ToArray());
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
