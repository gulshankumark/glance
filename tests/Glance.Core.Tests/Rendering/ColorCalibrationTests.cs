using Glance.Core.Layouts;
using Glance.Core.Rendering;
using SkiaSharp;

namespace Glance.Core.Tests.Rendering;

public class ColorCalibrationTests
{
    [Fact]
    public void Identity_is_null_so_no_pass_runs()
    {
        Assert.Null(ColorCalibration.For(1, 100));
        Assert.NotNull(ColorCalibration.For(1.05, 100));
        Assert.NotNull(ColorCalibration.For(1, 99));
    }

    [Fact]
    public void Gamma_maps_through_the_curve_and_keeps_the_ends()
    {
        var c = ColorCalibration.For(2.2, 100)!;
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Mapped(c, 0, alpha: 0));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Mapped(c, 255, alpha: 255));
        Assert.Equal(new byte[] { 56, 56, 56, 128 }, Mapped(c, 128));   // round(255 * (128/255)^2.2), alpha kept
    }

    [Fact]
    public void Brightness_scales_and_leaves_alpha()
    {
        var c = ColorCalibration.For(1, 50)!;
        Assert.Equal(new byte[] { 128, 128, 128, 255 }, Mapped(c, 255, alpha: 255));
        byte[] px = [10, 20, 30, 77, 200, 100, 0, 1];   // two pixels, alpha 77 and 1
        c.Apply(px);
        Assert.Equal(new byte[] { 5, 10, 15, 77, 100, 50, 0, 1 }, px);
    }

    [Fact]
    public void Calibration_applies_per_channel_on_a_bitmap()
    {
        using var bmp = new SKBitmap(new SKImageInfo(4, 2, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(new SKColor(255, 128, 0));
        ColorCalibration.For(2.2, 100)!.Apply(bmp.GetPixelSpan());
        Assert.Equal(new SKColor(255, 56, 0), bmp.GetPixel(3, 1));
    }

    [Theory]
    [InlineData(0.4, 100, "gamma must be 0.5..3")]
    [InlineData(3.01, 100, "gamma must be 0.5..3")]
    [InlineData(double.NaN, 100, "gamma must be 0.5..3")]
    [InlineData(1.0, 0, "brightness must be 10..100")]
    [InlineData(1.0, 101, "brightness must be 10..100")]
    public void Validate_rejects_bad_stored_values(double gamma, int brightness, string message)
    {
        var s = new AppSettings { Devices = { ["k"] = new DeviceSettings { Gamma = gamma, Brightness = brightness } } };
        Assert.Equal(new[] { $"device 'k': {message}" }, s.Validate());
    }

    [Fact]
    public void Validate_accepts_the_range_ends_and_defaults()
    {
        Assert.Empty(new AppSettings { Devices = { ["a"] = new DeviceSettings() } }.Validate());
        Assert.Empty(new AppSettings { Devices = { ["a"] = new DeviceSettings { Gamma = 0.5, Brightness = 10 } } }.Validate());
        Assert.Empty(new AppSettings { Devices = { ["a"] = new DeviceSettings { Gamma = 3, Brightness = 100 } } }.Validate());
        Assert.Equal(1.0, new DeviceSettings().Gamma);
        Assert.Equal(100, new DeviceSettings().Brightness);
    }

    /// <summary>One BGRA pixel of grey <paramref name="v"/> with alpha <paramref name="alpha"/>, after the pass.</summary>
    private static byte[] Mapped(ColorCalibration c, byte v, byte alpha = 128)
    {
        byte[] px = [v, v, v, alpha];
        c.Apply(px);
        return px;
    }
}
