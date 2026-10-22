using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Glance.Core.Tests;

public static class GoldenAssert
{
    private static readonly string GoldenDir = Path.Combine(Path.GetDirectoryName(SourceFile())!, "Golden");

    private static string SourceFile([CallerFilePath] string p = "") => p;

    public static void Match(SKBitmap actual, string name, int channelTolerance = 8, double maxDiffFraction = 0.002)
    {
        var path = Path.Combine(GoldenDir, name + ".png");
        if (!File.Exists(path) || Environment.GetEnvironmentVariable("GLANCE_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(GoldenDir);
            using var data = SKImage.FromBitmap(actual).Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
            Assert.Fail("golden written — review the PNG and re-run");
        }

        using var expected = SKBitmap.Decode(path);
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        long diff = 0;
        for (int y = 0; y < actual.Height; y++)
            for (int x = 0; x < actual.Width; x++)
            {
                var a = actual.GetPixel(x, y); var e = expected.GetPixel(x, y);
                if (Math.Abs(a.Red - e.Red) > channelTolerance || Math.Abs(a.Green - e.Green) > channelTolerance ||
                    Math.Abs(a.Blue - e.Blue) > channelTolerance || Math.Abs(a.Alpha - e.Alpha) > channelTolerance) diff++;
            }
        var fraction = (double)diff / (actual.Width * actual.Height);
        Assert.True(fraction <= maxDiffFraction, $"golden '{name}' differs in {fraction:P3} of pixels (max {maxDiffFraction:P3})");
    }
}
