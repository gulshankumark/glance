using Glance.Core.Rendering;
using SkiaSharp;
using static Glance.Testing.TestMedia;

namespace Glance.Core.Tests.Rendering;

public class FrameCompositorTests
{
    /// <summary>Compared before encoding, every pixel, against the pre-Task-15 compose → fit → rotate path.</summary>
    [Theory]
    [InlineData(1280, 480, 0, 1)] [InlineData(1280, 480, 180, 1)]     // 1:1 (decoded straight into the surface)
    [InlineData(800, 480, 0, 2)] [InlineData(800, 480, 180, 2)]       // fitted at scale 1 (bars left and right)
    [InlineData(1280, 480, 90, 6)] [InlineData(1280, 480, 270, 6)]    // fitted at scale 0.375 (resampled once instead of twice)
    public void Matches_compose_then_fit_and_rotate(int layoutW, int layoutH, int rotation, int tolerance)
    {
        var jpeg = Gradient(layoutW, layoutH);
        using var overlay = Overlay(layoutW, layoutH);
        using var expected = Reference(jpeg, overlay, rotation);

        using var c = new FrameCompositor(layoutW, layoutH, 1280, 480, rotation);
        c.SetOverlay(overlay.Copy());
        c.Compose(jpeg);
        var actual = c.DeviceSurface;

        Assert.InRange(MaxChannelDiff(expected, actual), 0, tolerance);
        foreach (var (x, y) in new[] { (0, 0), (1279, 0), (0, 479), (1279, 479) })   // corners explicitly
            Assert.InRange(Diff(expected.GetPixel(x, y), actual.GetPixel(x, y)), 0, tolerance);
    }

    [Theory] [InlineData(0)] [InlineData(180)]
    public void Fitted_layout_keeps_exact_black_bars_across_frames(int rotation)
    {
        using var c = new FrameCompositor(800, 480, 1280, 480, rotation);   // centred: bars at x 0-239 and 1040-1279
        c.Compose(Solid(800, 480, SKColors.White));
        c.Compose(Solid(800, 480, new SKColor(200, 200, 200)));
        var d = c.DeviceSurface;
        for (var y = 0; y < 480; y += 7)
        {
            foreach (var x in new[] { 0, 120, 239, 1040, 1160, 1279 }) Assert.Equal(SKColors.Black, d.GetPixel(x, y));
            Assert.Equal(new SKColor(200, 200, 200), d.GetPixel(240, y));
            Assert.Equal(new SKColor(200, 200, 200), d.GetPixel(1039, y));
        }
    }

    [Theory] [InlineData(0, 250, 15)] [InlineData(180, 1029, 464)]
    public void Overlay_follows_the_fit_and_rotation(int rotation, int x, int y)
    {
        using var overlay = new SKBitmap(800, 480);
        overlay.Erase(SKColors.Transparent);
        using (var cv = new SKCanvas(overlay))
        using (var lime = new SKPaint { Color = SKColors.Lime })
            cv.DrawRect(10, 10, 10, 10, lime);   // layout (10..20, 10..20) → device x +240 (and rotated for 180)
        using var c = new FrameCompositor(800, 480, 1280, 480, rotation);
        c.SetOverlay(overlay);
        c.Compose(Solid(800, 480, SKColors.Black));
        Assert.Equal(SKColors.Lime, c.DeviceSurface.GetPixel(x, y));
        Assert.Equal(SKColors.Black, c.DeviceSurface.GetPixel(x + 12, y));
    }

    [Fact] public void Upscaled_frame_is_filtered_not_blocky()
    {
        using var small = new SKBitmap(16, 8);
        small.Erase(SKColors.Black);
        using (var cv = new SKCanvas(small))
        using (var white = new SKPaint { Color = SKColors.White })
            cv.DrawRect(8, 0, 8, 8, white);
        using var c = new FrameCompositor(1280, 480, 1280, 480, 0);
        c.Compose(JpegEncoder.Encode(small).Data);   // 16x8 stretched to 1280x480: the seam spans ~80 px
        var maxStep = 0;
        for (var x = 1; x < 1280; x++)
            maxStep = Math.Max(maxStep, Diff(c.DeviceSurface.GetPixel(x - 1, 240), c.DeviceSurface.GetPixel(x, 240)));
        Assert.InRange(maxStep, 0, 20);   // nearest-neighbour would jump ~255 at the seam
    }

    [Fact] public void Rotation_180_moves_frame_and_overlay_to_opposite_corners()
    {
        using var overlay = new SKBitmap(1280, 480);
        overlay.Erase(SKColors.Transparent);
        using (var cv = new SKCanvas(overlay))
        using (var lime = new SKPaint { Color = SKColors.Lime })
            cv.DrawRect(1270, 470, 10, 10, lime);
        using var c = new FrameCompositor(1280, 480, 1280, 480, 180);
        c.SetOverlay(overlay.Copy());
        c.Compose(CornerJpeg(1280, 480, (0, 0)));
        using var dev = SKBitmap.Decode(c.Encode().Data);
        Assert.True(IsRedish(dev.GetPixel(1277, 477)));
        var p = dev.GetPixel(4, 4);
        Assert.True(p.Green > 200 && p.Red < 60, p.ToString());
    }

    [Fact] public void Overlay_stays_drawn_after_the_callers_bitmap_is_disposed()
    {
        var overlay = new SKBitmap(1280, 480);
        overlay.Erase(SKColors.Transparent);
        using (var cv = new SKCanvas(overlay))
        using (var white = new SKPaint { Color = SKColors.White })
            cv.DrawRect(600, 200, 40, 40, white);
        using var c = new FrameCompositor(1280, 480, 1280, 480, 0);
        c.SetOverlay(overlay);
        overlay.Dispose();   // the preview owns and disposes it independently
        GC.Collect();
        GC.WaitForPendingFinalizers();
        c.Compose(TinyJpeg(SKColors.Black));
        Assert.Equal(SKColors.White, c.DeviceSurface.GetPixel(620, 220));
    }

    [Fact] public void Undecodable_frame_renders_black_under_the_overlay()
    {
        using var c = new FrameCompositor(1280, 480, 1280, 480, 180);
        c.Compose(CornerJpeg(1280, 480, (600, 200)));
        c.Compose(new byte[] { 1, 2, 3 });
        Assert.Equal(SKColors.Black, c.DeviceSurface.GetPixel(640, 240));
    }

    [Fact] public void Smaller_frame_is_stretched_to_the_layout()
    {
        using var c = new FrameCompositor(1280, 480, 1280, 480, 180);
        c.Compose(TinyJpeg(SKColors.Blue));
        Assert.True(c.DeviceSurface.GetPixel(5, 5).Blue > 200);
        Assert.True(c.DeviceSurface.GetPixel(1275, 475).Blue > 200);
    }

    [Fact] public void Uses_the_given_codec()
    {
        var codec = new CountingCodec();
        using var c = new FrameCompositor(1280, 480, 1280, 480, 180, codec);
        c.Compose(CornerJpeg(1280, 480, (0, 0)));
        c.Encode();
        Assert.Equal((1, 1), (codec.Decodes, codec.Encodes));
    }

    /// <summary>The pre-Task-15 path: compose at layout size, fit into the canvas, rotate (device image, not encoded).</summary>
    private static SKBitmap Reference(byte[] jpeg, SKBitmap overlay, int rotation)
    {
        using var image = new SKBitmap(overlay.Width, overlay.Height);
        using (var canvas = new SKCanvas(image))
        using (var frame = SKBitmap.Decode(jpeg))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(frame, 0, 0);
            canvas.DrawBitmap(overlay, 0, 0);
        }
        var (cw, ch) = Orientation.Canvas(1280, 480, rotation);
        using var fitted = (cw, ch) == (image.Width, image.Height) ? image.Copy() : Orientation.FitToCanvas(image, cw, ch);
        return Orientation.ToDevice(fitted, rotation);
    }

    private static byte[] Solid(int w, int h, SKColor color)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(color);
        return JpegEncoder.Encode(bmp).Data;
    }

    private static byte[] Gradient(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp))
        using (var paint = new SKPaint())
        {
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                new[] { SKColors.Navy, SKColors.Orange, SKColors.Teal }, SKShaderTileMode.Clamp);
            c.DrawRect(0, 0, w, h, paint);
        }
        return JpegEncoder.Encode(bmp).Data;
    }

    private static SKBitmap Overlay(int w, int h)
    {
        var bmp = new SKBitmap(w, h);
        bmp.Erase(SKColors.Transparent);
        using var c = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        c.DrawCircle(100, 100, 40, paint);
        paint.Color = new SKColor(255, 0, 0, 128);
        c.DrawRect(w - 380, 300, 200, 100, paint);
        return bmp;
    }

    private static int Diff(SKColor p, SKColor q) =>
        Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));

    private static int MaxChannelDiff(SKBitmap a, SKBitmap b)
    {
        Assert.Equal((a.Width, a.Height), (b.Width, b.Height));
        var max = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
                max = Math.Max(max, Diff(a.GetPixel(x, y), b.GetPixel(x, y)));
        return max;
    }

    private sealed class CountingCodec : IJpegCodec
    {
        public int Decodes, Encodes;

        public bool TryDecode(ReadOnlySpan<byte> jpeg, SKPixmap target)
        {
            Decodes++;
            return SkiaJpegCodec.Instance.TryDecode(jpeg, target);
        }

        public byte[] Encode(SKPixmap source, int quality)
        {
            Encodes++;
            return SkiaJpegCodec.Instance.Encode(source, quality);
        }
    }
}
