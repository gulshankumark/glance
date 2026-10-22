using System.Diagnostics;
using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Rendering;
using Glance.Testing;
using SkiaSharp;

namespace Glance.Bench;

/// <summary>Single-thread cost per composed frame, split by stage (Stopwatch wall time on an otherwise idle thread).</summary>
public static class StageBench
{
    private const int Passes = 2;   // frames measured = Passes x frame count, after one warm-up pass

    public static void Run(Animation animation, IReadOnlyList<IJpegCodec> codecs)
    {
        var layout = Scenario.Animated;
        var src = Scenario.NewSource();
        src.Values["cpu/t"] = 55; src.Values["gpu/t"] = 61;
        var hub = HubFactory.NewHub(src);
        var renderer = new OverlayRenderer();
        hub.SetInterest(renderer.HistoryNeeds(layout, hub).ToDictionary(kv => kv.Key, kv => kv.Value));
        hub.Tick();
        var ctx = new RenderContext(hub, new AppSettings(), DateTimeOffset.Now, new FakeAssetStore(), new FontProvider());
        using var overlay = renderer.Render(layout, ctx);

        Measure("before (decode -> compose -> rotate copy -> SKImage copy + encode)", animation, new Baseline(layout, overlay));
        foreach (var codec in codecs)
            Measure($"after, {codec.GetType().Name} (decode into device surface + reverse -> overlay tiles -> encode in place)", animation,
                new Compositor(layout, overlay, codec));
    }

    private static void Measure(string name, Animation animation, IPipeline pipeline)
    {
        var t = new double[4];
        long bytes = 0, totalSize = 0;
        var frames = 0;
        for (var pass = -1; pass < Passes; pass++)   // pass -1 warms up
        {
            var measured = pass >= 0;
            foreach (var jpeg in animation.JpegFrames)
            {
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var size = pipeline.Frame(jpeg, measured ? t : new double[4]);
                if (!measured) continue;
                bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
                totalSize += size;
                frames++;
            }
        }
        var ms = t.Select(x => x / frames).ToArray();
        var total = ms.Sum();
        Console.WriteLine();
        Console.WriteLine($"[stages] {name}");
        Console.WriteLine($"  decode {ms[0]:F2} ms | compose {ms[1]:F2} ms | rotate {ms[2]:F2} ms | encode {ms[3]:F2} ms | total {total:F2} ms/frame");
        Console.WriteLine($"  managed alloc {bytes / frames / 1024.0:F1} KB/frame | output {totalSize / frames / 1024.0:F1} KB/frame | " +
                          $"=> {total * 24 / 10:F1}% of one core at 24 fps (pipeline only)");
        if (pipeline is IDisposable d) d.Dispose();
    }

    public interface IPipeline
    {
        /// <summary>Produces one device frame; adds stage times (ms) to t[0..3]; returns the JPEG size.</summary>
        int Frame(byte[] jpeg, double[] t);
    }

    /// <summary>The DisplaySession animated path as it was before Task 15 (replicated here for comparison).</summary>
    private sealed class Baseline(Layout layout, SKBitmap overlay) : IPipeline
    {
        public int Frame(byte[] jpeg, double[] t)
        {
            var sw = Stopwatch.StartNew();
            using var decoded = SKBitmap.Decode(jpeg);
            t[0] += Lap(sw);
            using var image = new SKBitmap(layout.Width, layout.Height);
            using (var canvas = new SKCanvas(image))
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(decoded, 0, 0);
                canvas.DrawBitmap(overlay, 0, 0);
            }
            t[1] += Lap(sw);
            using var device = Orientation.ToDevice(image, 180);
            t[2] += Lap(sw);
            var data = JpegEncoder.Encode(device).Data;
            t[3] += Lap(sw);
            return data.Length;
        }
    }

    /// <summary>The current DisplaySession animated path (FrameCompositor); rotation happens inside compose.</summary>
    private sealed class Compositor : IPipeline, IDisposable
    {
        private readonly FrameCompositor _c;

        public Compositor(Layout layout, SKBitmap overlay, IJpegCodec codec)
        {
            _c = new FrameCompositor(layout.Width, layout.Height, 1280, 480, rotation: 180, codec);
            _c.SetOverlay(overlay.Copy());
        }

        public int Frame(byte[] jpeg, double[] t)
        {
            var sw = Stopwatch.StartNew();
            _c.Decode(jpeg);
            t[0] += Lap(sw);
            _c.Draw();
            t[1] += Lap(sw);
            var data = _c.Encode().Data;
            t[3] += Lap(sw);
            return data.Length;
        }

        public void Dispose() => _c.Dispose();
    }

    private static double Lap(Stopwatch sw)
    {
        var ms = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        return ms;
    }
}
