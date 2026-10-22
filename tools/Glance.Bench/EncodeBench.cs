using System.Diagnostics;
using Glance.Core.Media;
using Glance.Core.Rendering;
using SkiaSharp;

namespace Glance.Bench;

/// <summary>Decode and encode cost (CPU of this thread's process, single-threaded loop) and size, per codec and quality.</summary>
public static class EncodeBench
{
    public static void Run(Animation animation, IReadOnlyList<IJpegCodec> codecs)
    {
        var frames = animation.JpegFrames.Take(60).ToList();
        using var bmp = new SKBitmap(new SKImageInfo(1280, 480, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using var pixels = bmp.PeekPixels();
        Console.WriteLine();
        Console.WriteLine("[encode] codec: decode ms/frame | encode ms/frame and KB/frame by quality (4:2:0)");
        foreach (var codec in codecs)
        {
            foreach (var f in frames) codec.TryDecode(f, pixels);   // warm-up
            var decodeMs = Time(() => { foreach (var f in frames) codec.TryDecode(f, pixels); }) / frames.Count;
            var line = $"  {codec.GetType().Name,-14} decode {decodeMs:F2} ms |";
            foreach (var q in new[] { 90, 80, 70 })
            {
                codec.Encode(pixels, q);   // warm-up
                long size = 0;
                var ms = Time(() => { for (var i = 0; i < 30; i++) size += codec.Encode(pixels, q).Length; }) / 30;
                line += $" q{q} {ms:F2} ms {size / 30 / 1024.0:F1} KB |";
            }
            Console.WriteLine(line);
        }
    }

    private static double Time(Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        return sw.Elapsed.TotalMilliseconds;
    }
}
