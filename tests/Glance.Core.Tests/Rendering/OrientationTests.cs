using Glance.Core.Rendering;
using SkiaSharp;
using static Glance.Core.Tests.RenderHelpers;

namespace Glance.Core.Tests.Rendering;

public class OrientationTests
{
    [Theory] [InlineData(0, 1280, 480)] [InlineData(90, 480, 1280)] [InlineData(180, 1280, 480)] [InlineData(270, 480, 1280)]
    public void Canvas_size(int rot, int w, int h) => Assert.Equal((w, h), Orientation.Canvas(1280, 480, rot));

    [Fact] public void Rotation_180_and_90_move_corner_pixel()
    {
        using var c = Solid(1280, 480, SKColors.Black); c.SetPixel(0, 0, SKColors.Red);
        Assert.Equal(SKColors.Red, Orientation.ToDevice(c, 180).GetPixel(1279, 479));
        using var p = Solid(480, 1280, SKColors.Black); p.SetPixel(0, 0, SKColors.Red);
        var d = Orientation.ToDevice(p, 90);
        Assert.Equal((1280, 480), (d.Width, d.Height));
        Assert.Equal(SKColors.Red, d.GetPixel(1279, 0));
        Assert.Equal(SKColors.Red, Orientation.FromDevice(d, 90).GetPixel(0, 0));
    }

    [Fact] public void Rotation_270_round_trips()
    {
        using var p = Solid(480, 1280, SKColors.Black); p.SetPixel(0, 0, SKColors.Red);
        var d = Orientation.ToDevice(p, 270);
        Assert.Equal(SKColors.Red, d.GetPixel(0, 479));
        Assert.Equal(SKColors.Red, Orientation.FromDevice(d, 270).GetPixel(0, 0));
    }

    [Fact] public void Fit_letterboxes_landscape_into_portrait()
    {
        var f = Orientation.FitToCanvas(Solid(1280, 480, SKColors.White), 480, 1280);
        Assert.Equal(SKColors.Black, f.GetPixel(240, 10));
        Assert.Equal(SKColors.White, f.GetPixel(240, 640));
    }
}
