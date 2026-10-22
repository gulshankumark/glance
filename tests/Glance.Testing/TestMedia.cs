using SkiaSharp;

namespace Glance.Testing;

/// <summary>Small media fixtures shared by test projects.</summary>
public static class TestMedia
{
    // Two-frame 1x1 GIF: frame 0 black (palette 0) 100 ms, frame 1 white (palette 1) 200 ms.
    public const string Gif2 =
        "474946383961" + "01000100" + "800000" + "000000FFFFFF" +          // header, 1x1, 2-colour palette
        "21F904000A000000" + "2C000000000100010000" + "0202440100" +       // GCE 10cs, image, LZW pixel 0
        "21F9040014000000" + "2C000000000100010000" + "02024C0100" +       // GCE 20cs, image, LZW pixel 1
        "3B";

    // 2x1 GIF, palette: 0 = 404040, 1 = white, 2 = 808080, 3 = black (grey only, so JPEG chroma subsampling cannot blur it).
    // Frame 0: full canvas, pixels [white, grey], disposal 1 (do not dispose), 100 ms.
    // Frame 1: 1x1 sub-rectangle at x=1 painting palette 3 (black), 100 ms. Pixel 0 must keep frame 0's white,
    // which only happens if the importer composites over the RequiredFrame (frame 0).
    // LZW (min code size 2, 3-bit codes): clear=4, eoi=5; [1,2] -> 8C 0A, [3] -> 5C 01.
    public const string GifSubRectKeep =
        "474946383961" + "02000100" + "81" + "0000" + "404040FFFFFF808080000000" +
        "21F904040A000000" + "2C000000000200010000" + "02028C0A00" +
        "21F904040A000000" + "2C010000000100010000" + "02025C0100" +
        "3B";

    // Same canvas and palette, but frame 0 has disposal 2 (restore to background), so frame 1 starts from a
    // cleared canvas: its sub-rectangle paints palette 1 (white, code 4C 01) at x=1 and pixel 0 is transparent (renders black).
    public const string GifSubRectRestore =
        "474946383961" + "02000100" + "81" + "0000" + "404040FFFFFF808080000000" +
        "21F904080A000000" + "2C000000000200010000" + "02028C0A00" +
        "21F904040A000000" + "2C010000000100010000" + "02024C0100" +
        "3B";

    public static byte[] TinyJpeg(SKColor color)
    {
        using var bmp = new SKBitmap(8, 8);
        bmp.Erase(color);
        return Encode(bmp, SKEncodedImageFormat.Jpeg);
    }

    /// <summary>Black w x h JPEG with a red 6x6 square whose top-left is <paramref name="corner"/> (clamped to fit).</summary>
    public static byte[] CornerJpeg(int w, int h, (int X, int Y) corner)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(SKColors.Black);
        using (var c = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            var x = Math.Min(corner.X, w - 6);
            var y = Math.Min(corner.Y, h - 6);
            c.DrawRect(x, y, 6, 6, paint);
        }
        return Encode(bmp, SKEncodedImageFormat.Jpeg);
    }

    public static byte[] Png(int w, int h, SKColor color)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(color);
        return Encode(bmp, SKEncodedImageFormat.Png);
    }

    /// <summary>Tolerates JPEG error.</summary>
    public static bool IsRedish(SKColor c) => c.Red > 180 && c.Green < 80 && c.Blue < 80;

    /// <summary>Builds a .zt file: 0xDC, count, cumulative-ms table, then length-prefixed JPEG frames.</summary>
    public static byte[] Zt(params (int cumMs, byte[] jpg)[] f)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)0xDC);
        w.Write(f.Length);
        foreach (var x in f) w.Write(x.cumMs);
        foreach (var x in f) { w.Write(x.jpg.Length); w.Write(x.jpg); }
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] Encode(SKBitmap bmp, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
