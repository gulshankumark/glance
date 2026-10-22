using Glance.Core.Rendering;
using SkiaSharp;

namespace Glance.Core.Tests.Rendering;

/// <summary>Behaviour every <see cref="IJpegCodec"/> must have. Also compiled into the Windows platform tests.</summary>
public abstract class JpegCodecContractTests
{
    protected abstract IJpegCodec Codec { get; }

    [Theory] [InlineData(1280, 480)] [InlineData(37, 21)]
    public void Round_trip_keeps_size_and_colour(int w, int h)
    {
        using var src = Blocks(w, h);
        using var srcPixels = src.PeekPixels();
        var jpeg = Codec.Encode(srcPixels, 90);
        Assert.True(JpegHeader.TryReadSize(jpeg, out var jw, out var jh));
        Assert.Equal((w, h), (jw, jh));

        using var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var dstPixels = dst.PeekPixels();
        Assert.True(Codec.TryDecode(jpeg, dstPixels));
        foreach (var (x, y) in new[] { (w / 8, h / 8), (w * 7 / 8, h / 8), (w / 8, h * 7 / 8), (w * 7 / 8, h * 7 / 8) })
        {
            SKColor a = src.GetPixel(x, y), b = dst.GetPixel(x, y);
            Assert.True(Math.Abs(a.Red - b.Red) <= 16 && Math.Abs(a.Green - b.Green) <= 16 && Math.Abs(a.Blue - b.Blue) <= 16,
                $"({x},{y}) {a} vs {b}");
            Assert.Equal(255, b.Alpha);
        }
    }

    [Fact] public void Output_is_baseline_with_4_2_0_chroma()
    {
        using var src = Blocks(64, 32);
        using var pixels = src.PeekPixels();
        var jpeg = Codec.Encode(pixels, 90);
        var sof = FindMarker(jpeg, 0xC0);   // SOF0 = baseline DCT
        Assert.True(sof > 0, "no baseline frame header");
        Assert.Equal(3, jpeg[sof + 9]);           // components
        Assert.Equal(0x22, jpeg[sof + 11]);       // Y sampled 2x2 ...
        Assert.Equal(0x11, jpeg[sof + 14]);       // ... Cb and Cr 1x1
        Assert.Equal(0x11, jpeg[sof + 17]);
    }

    [Fact] public void Lower_quality_gives_smaller_output()
    {
        using var src = Noise(320, 240);
        using var pixels = src.PeekPixels();
        Assert.True(Codec.Encode(pixels, 40).Length < Codec.Encode(pixels, 90).Length);
    }

    [Fact] public void Invalid_data_or_wrong_size_decodes_false_without_throwing()
    {
        using var dst = new SKBitmap(new SKImageInfo(64, 32, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var pixels = dst.PeekPixels();
        var rnd = new byte[5000];
        new Random(3).NextBytes(rnd);
        Assert.False(Codec.TryDecode(Array.Empty<byte>(), pixels));
        Assert.False(Codec.TryDecode(rnd, pixels));
        Assert.False(Codec.TryDecode(new byte[] { 0xFF, 0xD8, 0xFF }, pixels));
        Assert.False(Codec.TryDecode(Glance.Testing.TestMedia.Png(64, 32, SKColors.Red), pixels));
        using var src = Blocks(32, 32);
        using var srcPixels = src.PeekPixels();
        Assert.False(Codec.TryDecode(Codec.Encode(srcPixels, 90), pixels));   // 32x32 into 64x32
    }

    [Fact] public void Safe_to_use_from_several_threads()
    {
        using var src = Blocks(320, 240);
        using var srcPixels = src.PeekPixels();
        var jpeg = Codec.Encode(srcPixels, 90);
        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            using var dst = new SKBitmap(new SKImageInfo(320, 240, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using var pixels = dst.PeekPixels();
            Assert.True(Codec.TryDecode(jpeg, pixels));
            using var p2 = src.PeekPixels();
            Assert.True(Codec.Encode(p2, 80).Length > 0);
        });
    }

    private static int FindMarker(byte[] jpeg, byte marker)
    {
        for (var i = 2; i + 1 < jpeg.Length; i++)
            if (jpeg[i] == 0xFF && jpeg[i + 1] == marker) return i;
        return -1;
    }

    /// <summary>Four flat colour quadrants (grey-free so chroma matters).</summary>
    private static SKBitmap Blocks(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var c = new SKCanvas(bmp);
        using var paint = new SKPaint();
        (SKColor Color, float X, float Y)[] quads =
            { (new SKColor(200, 40, 40), 0, 0), (new SKColor(40, 180, 60), w / 2f, 0), (new SKColor(30, 60, 200), 0, h / 2f), (new SKColor(230, 200, 40), w / 2f, h / 2f) };
        foreach (var (color, x, y) in quads)
        {
            paint.Color = color;
            c.DrawRect(x, y, w / 2f + 1, h / 2f + 1, paint);
        }
        return bmp;
    }

    private static SKBitmap Noise(int w, int h)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var rnd = new Random(7);
        var bytes = new byte[w * h * 4];
        rnd.NextBytes(bytes);
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bmp.GetPixels(), bytes.Length);
        bmp.NotifyPixelsChanged();
        return bmp;
    }
}

public class SkiaJpegCodecTests : JpegCodecContractTests
{
    protected override IJpegCodec Codec => SkiaJpegCodec.Instance;
}

public class JpegHeaderTests
{
    [Fact] public void Reads_size_from_the_frame_header()
    {
        Assert.True(JpegHeader.TryReadSize(Glance.Testing.TestMedia.CornerJpeg(123, 45, (0, 0)), out var w, out var h));
        Assert.Equal((123, 45), (w, h));
        Assert.False(JpegHeader.TryReadSize(Glance.Testing.TestMedia.Png(4, 4, SKColors.Red), out _, out _));
        Assert.False(JpegHeader.TryReadSize(new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00 }, out _, out _));
    }
}
