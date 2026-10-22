using System.Diagnostics;
using Glance.Bench;
using Glance.Core.Rendering;
using Glance.Platform.Windows;

// Performance benchmark for the animated-with-overlay display path (spec §2 CPU targets).
//   stages  - per-stage cost of one composed frame (decode / compose / rotate / encode), single thread
//   session - a real DisplaySession on wall-clock time at the animation's own frame rate: process CPU per frame,
//             managed allocations per frame, then memory after switching to a still layout
//   encode  - decode and encode cost and size per codec and quality
// Usage: dotnet run -c Release --project tools/Glance.Bench -- [stages|session|encode|all] [seconds] [skia|wic] [--cap fps]
//   (codec defaults to wic; stages and encode always show both codecs; --cap sets maxOverlayAnimationFps for session)
var capAt = Array.IndexOf(args, "--cap");
int? cap = capAt >= 0 ? int.Parse(args[capAt + 1]) : null;
if (capAt >= 0) args = args.Where((_, i) => i != capAt && i != capAt + 1).ToArray();
var mode = args.Length > 0 ? args[0] : "all";
var seconds = args.Length > 1 ? int.Parse(args[1]) : 20;
IJpegCodec codec = args.Length > 2 && args[2] == "skia" ? SkiaJpegCodec.Instance : new WicJpegCodec();

Console.WriteLine($"Glance.Bench  mode={mode}  codec={codec.GetType().Name}  cap={cap?.ToString() ?? "none"}  ServerGC={System.Runtime.GCSettings.IsServerGC}  " +
                  $"CPUs={Environment.ProcessorCount}  {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.Write("Generating 253 synthetic 1280x480 frames... ");
var sw = Stopwatch.StartNew();
var animation = SyntheticAnimation.Create(253, 1280, 480, delayMs: 41);
Console.WriteLine($"{sw.ElapsedMilliseconds} ms, average {animation.JpegFrames.Average(f => f.Length) / 1024:F1} KB/frame");

IJpegCodec[] codecs = { SkiaJpegCodec.Instance, new WicJpegCodec() };
if (mode is "stages" or "all") StageBench.Run(animation, codecs);
if (mode is "encode" or "all") EncodeBench.Run(animation, codecs);
if (mode is "session" or "all") await SessionBench.Run(animation, seconds, codec, cap);
