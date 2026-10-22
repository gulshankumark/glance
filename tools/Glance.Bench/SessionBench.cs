using System.Diagnostics;
using Glance.Core.Devices;
using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Rendering;
using Glance.Core.Sessions;
using Glance.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Bench;

/// <summary>
/// A real <see cref="DisplaySession"/> on wall-clock time: still layout, then the animated layout (rotation 180),
/// then the still layout again, reporting process CPU, managed allocations and memory for each phase.
/// </summary>
public static class SessionBench
{
    public static async Task Run(Animation animation, int seconds, IJpegCodec codec, int? maxOverlayFps = null)
    {
        using var dir = new TempDir();
        var paths = new GlancePaths(dir.Path);
        var assets = new FakeAssetStore();
        assets.SetAnimation("anim", animation);
        using var layouts = new LayoutStore(paths, TimeProvider.System);
        layouts.Save(GlanceJson.Deserialize<Layout>(Scenario.AnimatedLayout)!);
        layouts.Save(GlanceJson.Deserialize<Layout>(Scenario.StillLayout)!);
        var settings = new SettingsStore(paths);
        var conn = new CountingConnection();
        settings.Update(s =>
        {
            s.Devices[conn.Info.Key] = new DeviceSettings { ActiveLayout = "Still", Rotation = 180 };
            s.MaxOverlayAnimationFps = maxOverlayFps;
        });

        var src = Scenario.NewSource();
        src.Values["cpu/t"] = 55; src.Values["gpu/t"] = 61;
        var hub = HubFactory.NewHub(src);
        using var cts = new CancellationTokenSource();
        var hubRun = hub.RunAsync(cts.Token);
        var session = new DisplaySession(conn, layouts, settings, hub, assets, new OverlayRenderer(), new FontProvider(),
            TimeProvider.System, NullLogger<DisplaySession>.Instance, codec);
        var run = Task.Run(() => session.RunAsync(cts.Token));

        Console.WriteLine();
        await Phase("still layout (baseline)", conn, warmup: 3, seconds: Math.Min(seconds, 10));
        settings.Update(s => s.Devices[conn.Info.Key].ActiveLayout = "Anim");
        await Phase("animation + 3 overlay items, rotation 180", conn, warmup: 3, seconds: seconds);
        settings.Update(s => s.Devices[conn.Info.Key].ActiveLayout = "Still");
        await Phase("still layout again, after the animation", conn, warmup: 5, seconds: Math.Min(seconds, 10));
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        await Task.Delay(1000);
        Console.WriteLine($"  after forced full GC: {Memory()}");
        Console.WriteLine($"  peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MB");

        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        try { await hubRun; } catch (OperationCanceledException) { }
    }

    private static async Task Phase(string name, CountingConnection conn, int warmup, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(warmup));
        var proc = Process.GetCurrentProcess();
        var cpu0 = proc.TotalProcessorTime;
        var alloc0 = GC.GetTotalAllocatedBytes(precise: true);
        var frames0 = conn.Frames;
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var wall = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        proc.Refresh();
        var cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
        var elapsed = wall.Elapsed.TotalMilliseconds;
        var frames = conn.Frames - frames0;
        var alloc = GC.GetTotalAllocatedBytes(precise: true) - alloc0;
        Console.WriteLine($"[session] {name}");
        Console.WriteLine($"  {frames / (elapsed / 1000):F1} fps | process CPU {cpuMs / elapsed * 100:F2}% of one core | " +
                          $"{cpuMs / Math.Max(frames, 1):F2} ms CPU/frame | managed alloc {alloc / (double)Math.Max(frames, 1) / 1024:F1} KB/frame " +
                          $"({alloc / (elapsed / 1000) / 1048576:F1} MB/s)");
        Console.WriteLine($"  GCs gen0/1/2 {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2} | " +
                          $"avg frame {conn.AverageBytes / 1024:F1} KB | {Memory()}");
    }

    private static string Memory()
    {
        var p = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo();
        return $"working set {p.WorkingSet64 / 1048576} MB, private {p.PrivateMemorySize64 / 1048576} MB, " +
               $"GC heap {info.HeapSizeBytes / 1048576} MB (committed {info.TotalCommittedBytes / 1048576} MB)";
    }

    /// <summary>Counts frames without keeping them (FakeConnection keeps every frame, which would swamp memory).</summary>
    private sealed class CountingConnection : IDisplayConnection
    {
        private long _frames, _bytes;
        public DisplayInfo Info { get; } = new("fake", "Bench screen", 1280, 480, new[] { PixelFormat.Jpeg }, "BENCH", "FAKE");
        public long Frames => Interlocked.Read(ref _frames);
        public double AverageBytes => Frames == 0 ? 0 : Interlocked.Read(ref _bytes) / (double)Frames;

        public Task SendFrameAsync(EncodedFrame frame, CancellationToken ct)
        {
            Interlocked.Add(ref _bytes, frame.Data.Length);
            Interlocked.Increment(ref _frames);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
