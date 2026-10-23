using Glance.Core.Devices;
using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Glance.Core.Sessions;

/// <summary>
/// Drives one connected screen: renders the device's active layout, rotates it for the device, encodes and sends
/// frames, re-sending still frames at least once per second as keep-alive. Layout and settings changes re-render
/// immediately.
/// </summary>
public sealed class DisplaySession
{
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan OverlayInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FpsWindow = TimeSpan.FromSeconds(5);
    private const int MinFrameDelayMs = 20;

    private readonly IDisplayConnection _conn;
    private readonly LayoutStore _layouts;
    private readonly SettingsStore _settings;
    private readonly SensorHub _hub;
    private readonly IAssetStore _assets;
    private readonly OverlayRenderer _renderer;
    private readonly FontProvider _fonts;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly IJpegCodec _codec;

    private readonly object _wakeGate = new();
    private CancellationTokenSource _wake = new();

    private readonly object _previewGate = new();
    private SKBitmap? _previewBitmap;   // latest composed layout-orientation image, encoded on demand
    private byte[]? _previewJpeg;
    private byte[]? _previewFrame;      // animated layouts: the stored frame on screen and the overlay over it,
    private SKBitmap? _previewOverlay;  // composed on demand (the device frame is built in device orientation)

    private readonly Queue<long> _sendTimes = new();
    private int _framesSent;
    private volatile string? _layoutName;
    private Passthrough? _passthrough;
    private Exception? _sendFailure;   // the exception SendFrameAsync threw; only this ends the run
    private ColorCalibration? _calibration;   // of the current plan; applied only to bytes sent to the device
    private long _testPatternUntil;           // UTC ticks while the test pattern is shown, else 0

    /// <summary>Device frames for one animation background, each prepared on its first showing.</summary>
    private sealed record Passthrough(string AssetId, int Rotation, int LayoutW, int LayoutH, (double Gamma, int Brightness) Calibration,
        EncodedFrame?[] Frames);

    private sealed class Pacer(DateTimeOffset start)
    {
        public DateTimeOffset Next = start;
    }

    public DisplaySession(IDisplayConnection conn, LayoutStore layouts, SettingsStore settings, SensorHub hub,
        IAssetStore assets, OverlayRenderer renderer, FontProvider fonts, TimeProvider time, ILogger log, IJpegCodec? codec = null)
    {
        _conn = conn;
        _layouts = layouts;
        _settings = settings;
        _hub = hub;
        _assets = assets;
        _renderer = renderer;
        _fonts = fonts;
        _time = time;
        _log = log;
        _codec = codec ?? SkiaJpegCodec.Instance;
    }

    private string Key => _conn.Info.Key;
    private int DeviceW => _conn.Info.Width;
    private int DeviceH => _conn.Info.Height;

    public string? LayoutName => _layoutName;
    public int FramesSent => Volatile.Read(ref _framesSent);

    /// <summary>Frames sent per second, averaged over the last 5 s.</summary>
    public double Fps
    {
        get
        {
            lock (_sendTimes)
            {
                Prune();
                return _sendTimes.Count / FpsWindow.TotalSeconds;
            }
        }
    }

    /// <summary>Layout or settings changed: stop the current frame plan and render afresh.</summary>
    public void Invalidate()
    {
        lock (_wakeGate) _wake.Cancel();
    }

    /// <summary>
    /// Shows <see cref="TestPattern"/> instead of the layout for <paramref name="duration"/> (in memory only), through
    /// the same rotation and calibration as the layout.
    /// </summary>
    public void ShowTestPattern(TimeSpan duration)
    {
        Interlocked.Exchange(ref _testPatternUntil, (_time.GetUtcNow() + duration).UtcTicks);
        Invalidate();
    }

    /// <summary>Ends the test pattern early; the layout returns at once.</summary>
    public void StopTestPattern()
    {
        if (Interlocked.Exchange(ref _testPatternUntil, 0) != 0) Invalidate();
    }

    /// <summary>The latest composed frame in layout orientation (before fitting and rotation), or null before the first frame.</summary>
    public byte[]? GetPreviewJpeg()
    {
        lock (_previewGate)
        {
            if (_previewJpeg is null && _previewBitmap is not null) _previewJpeg = JpegEncoder.Encode(_previewBitmap, codec: _codec).Data;
            if (_previewJpeg is null && _previewFrame is not null && _previewOverlay is not null)
            {
                using var image = new SKBitmap(_previewOverlay.Width, _previewOverlay.Height);
                using (var canvas = new SKCanvas(image))
                {
                    canvas.Clear(SKColors.Black);
                    DrawJpeg(canvas, _previewFrame, image.Width, image.Height);
                    canvas.DrawBitmap(_previewOverlay, 0, 0);
                }
                _previewJpeg = JpegEncoder.Encode(image, codec: _codec).Data;
            }
            return _previewJpeg;
        }
    }

    /// <summary>
    /// Runs until <paramref name="ct"/> is cancelled. Only an exception from the connection ends the run (it is
    /// rethrown); rendering problems are logged and shown on screen until the next layout or settings change.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        if (!_conn.Info.Formats.Contains(PixelFormat.Jpeg))
        {
            _log.LogError("{Screen} ({Key}) does not accept JPEG frames; nothing can be shown on it", _conn.Info.Name, Key);
            while (!ct.IsCancellationRequested) await IdleAsync(ct);
            return;
        }
        _layouts.Changed += OnLayoutChanged;
        _settings.Changed += Invalidate;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var plan = CancellationTokenSource.CreateLinkedTokenSource(ct, ResetWake());
                try { await RunPlanAsync(ct, plan.Token); }
                catch (OperationCanceledException) when (plan.IsCancellationRequested) { }
                catch (Exception ex) when (!ReferenceEquals(ex, _sendFailure))
                {
                    // Even the error screen failed: leave the screen as it is until something changes.
                    _log.LogError(ex, "Screen {Key}: nothing can be shown until the layout or settings change", Key);
                    while (!plan.IsCancellationRequested) await IdleAsync(plan.Token);
                }
            }
        }
        finally
        {
            _layouts.Changed -= OnLayoutChanged;
            _settings.Changed -= Invalidate;
            _hub.ClearInterest(Key);
            _passthrough = null;
            lock (_previewGate) ClearPreview();
        }
    }

    private void OnLayoutChanged(string _) => Invalidate();

    private CancellationToken ResetWake()
    {
        lock (_wakeGate)
        {
            if (_wake.IsCancellationRequested)
            {
                _wake.Dispose();
                _wake = new CancellationTokenSource();
            }
            return _wake.Token;
        }
    }

    /// <param name="runCt">Cancels sends (only when the whole run stops, so a frame is never cut short by an invalidation).</param>
    /// <param name="planCt">Cancels waits and rendering when the run stops or the session is invalidated.</param>
    private async Task RunPlanAsync(CancellationToken runCt, CancellationToken planCt)
    {
        var device = _settings.Current.Devices.GetValueOrDefault(Key);
        var rotation = device?.Rotation ?? 0;
        _calibration = ColorCalibration.For(device?.Gamma ?? 1, device?.Brightness ?? 100);
        var layout = device?.ActiveLayout is { } name ? _layouts.Get(name) : null;
        _layoutName = layout?.Name;
        _hub.SetInterest(Key, layout is null ? new Dictionary<string, int>() : _renderer.HistoryNeeds(layout, _hub));
        var (cw, ch) = Orientation.Canvas(DeviceW, DeviceH, rotation);

        if (TestPatternLeft() > TimeSpan.Zero)
        {
            await RunTestPatternAsync(rotation, cw, ch, runCt, planCt);
            return;
        }

        if (layout is null)
        {
            _passthrough = null;
            var message = device?.ActiveLayout is { } missing ? $"Layout '{missing}' not found" : FallbackScreen.RunningMessage(_settings.Current);
            await RunFallbackAsync(message, rotation, cw, ch, runCt, planCt);
            return;
        }
        try
        {
            if (layout.Background is AnimationBackground ab && _assets.GetAnimation(ab.AssetId) is { } animation)
            {
                if (layout.Items.Count == 0)
                {
                    await RunPassthroughAsync(layout, ab.AssetId, animation, rotation, cw, ch, runCt, planCt);
                    return;
                }
                _passthrough = null;
                await RunAnimatedAsync(layout, animation, rotation, runCt, planCt);
            }
            else
            {
                _passthrough = null;
                await RunStillAsync(layout, rotation, cw, ch, runCt, planCt);
            }
        }
        catch (Exception ex) when (!ReferenceEquals(ex, _sendFailure) && !(ex is OperationCanceledException && planCt.IsCancellationRequested))
        {
            _log.LogError(ex, "Layout '{Layout}' could not be rendered on {Key}", layout.Name, Key);
            _passthrough = null;
            await RunFallbackAsync($"Layout '{layout.Name}' could not be rendered", rotation, cw, ch, runCt, planCt);
        }
    }

    private async Task RunFallbackAsync(string message, int rotation, int cw, int ch, CancellationToken runCt, CancellationToken planCt)
    {
        var image = FallbackScreen.Render(cw, ch, message, _fonts);
        EncodedFrame frame;
        try { frame = EncodeForDevice(image, rotation); }
        catch
        {
            image.Dispose();
            throw;
        }
        SetPreview(image);
        while (true)
        {
            await SendAsync(frame, runCt);
            await WaitAsync(KeepAlive, planCt);
        }
    }

    private TimeSpan TestPatternLeft()
    {
        var until = Interlocked.Read(ref _testPatternUntil);
        return until == 0 ? TimeSpan.Zero : new DateTimeOffset(until, TimeSpan.Zero) - _time.GetUtcNow();
    }

    /// <summary>Sends the test pattern (kept alive every second) until it expires, then ends the plan.</summary>
    private async Task RunTestPatternAsync(int rotation, int cw, int ch, CancellationToken runCt, CancellationToken planCt)
    {
        var image = new SKBitmap(cw, ch);
        EncodedFrame frame;
        try
        {
            using (var canvas = new SKCanvas(image)) TestPattern.Draw(canvas, cw, ch);
            frame = EncodeForDevice(image, rotation);
        }
        catch
        {
            image.Dispose();
            throw;
        }
        SetPreview(image);
        var until = Interlocked.Read(ref _testPatternUntil);
        for (var left = TestPatternLeft(); left > TimeSpan.Zero; left = TestPatternLeft())
        {
            await SendAsync(frame, runCt);
            await WaitAsync(left < KeepAlive ? left : KeepAlive, planCt);
        }
        // Expired on its own: clear it (unless a new deadline was set meanwhile) so a later stop changes nothing.
        Interlocked.CompareExchange(ref _testPatternUntil, 0, until);
    }

    /// <summary>
    /// The background is drawn once per plan into a layout-size bitmap (an image background is read and decoded only
    /// then); each overlay change copies it and draws the overlay on top.
    /// </summary>
    private async Task RunStillAsync(Layout layout, int rotation, int cw, int ch, CancellationToken runCt, CancellationToken planCt)
    {
        using var background = new SKBitmap(layout.Width, layout.Height);
        using (var canvas = new SKCanvas(background)) DrawBackground(canvas, layout);
        string? lastKey = null;
        EncodedFrame? frame = null;
        while (true)
        {
            var ctx = Context();
            var key = OverlayKey.Compute(layout, ctx, _renderer);
            if (frame is null || key != lastKey)
            {
                frame = ComposeFrame(layout, rotation, cw, ch, canvas =>
                {
                    using var overlay = _renderer.Render(layout, ctx);
                    canvas.DrawBitmap(background, 0, 0);
                    canvas.DrawBitmap(overlay, 0, 0);
                });
                lastKey = key;
            }
            await SendAsync(frame, runCt);
            await WaitAsync(KeepAlive, planCt);
        }
    }

    private async Task RunPassthroughAsync(Layout layout, string assetId, Animation animation, int rotation, int cw, int ch,
        CancellationToken runCt, CancellationToken planCt)
    {
        var p = _passthrough;
        var calibration = (_calibration?.Gamma ?? 1, _calibration?.Brightness ?? 100);
        if (p is null || p.AssetId != assetId || p.Rotation != rotation || p.LayoutW != layout.Width || p.LayoutH != layout.Height
            || p.Calibration != calibration || p.Frames.Length != animation.JpegFrames.Count)
            _passthrough = p = new Passthrough(assetId, rotation, layout.Width, layout.Height, calibration,
                new EncodedFrame?[animation.JpegFrames.Count]);

        FrameCompositor? compositor = null;   // created on the first frame that needs re-encoding
        try
        {
            var pacer = new Pacer(_time.GetUtcNow());
            for (var i = 0; ; i = (i + 1) % p.Frames.Length)
            {
                var jpeg = animation.JpegFrames[i];
                var frame = p.Frames[i] ??= PrepareFrame(animation, jpeg, layout, rotation, cw, ch, ref compositor);
                SetPreview(jpeg);
                await SendAsync(frame, runCt);
                await HoldAsync(frame, pacer, animation.DelaysMs[i], runCt, planCt);
            }
        }
        finally
        {
            compositor?.Dispose();
        }
    }

    /// <summary>
    /// One stored frame as device bytes: the original JPEG when it needs no scaling, rotation or calibration and is
    /// within the device's size limit, otherwise decoded, fitted, rotated, calibrated and re-encoded.
    /// </summary>
    private EncodedFrame PrepareFrame(Animation animation, byte[] jpeg, Layout layout, int rotation, int cw, int ch,
        ref FrameCompositor? compositor)
    {
        if (rotation == 0 && _calibration is null && (animation.Width, animation.Height) == (layout.Width, layout.Height)
            && (layout.Width, layout.Height) == (cw, ch) && jpeg.Length <= JpegEncoder.MaxBytes)
            return new EncodedFrame(PixelFormat.Jpeg, DeviceW, DeviceH, jpeg);
        compositor ??= new FrameCompositor(layout.Width, layout.Height, DeviceW, DeviceH, rotation, _codec, _calibration);
        compositor.Compose(jpeg);
        return new EncodedFrame(PixelFormat.Jpeg, DeviceW, DeviceH, compositor.Encode().Data);
    }

    /// <summary>
    /// Every stored frame is decoded and composed with the overlay straight into the device orientation, then
    /// encoded; the overlay is re-rendered at most once per second, and only when what it shows has changed. With
    /// <see cref="AppSettings.MaxOverlayAnimationFps"/> below the animation's own rate, frames go out at most at that
    /// rate, each showing the frame current at that moment of the animation's timeline (the others are skipped).
    /// </summary>
    private async Task RunAnimatedAsync(Layout layout, Animation animation, int rotation, CancellationToken runCt, CancellationToken planCt)
    {
        using var compositor = new FrameCompositor(layout.Width, layout.Height, DeviceW, DeviceH, rotation, _codec, _calibration);
        var capped = CappedTimeline.CreateIfLimiting(animation.DelaysMs, MinFrameDelayMs, _settings.Current.MaxOverlayAnimationFps);
        string? key = null;
        var checkedAt = 0L;
        var pacer = new Pacer(_time.GetUtcNow());
        var start = pacer.Next;
        for (var i = 0; ; i = (i + 1) % animation.JpegFrames.Count)
        {
            if (capped is not null) i = capped.FrameAt(pacer.Next - start);
            if (key is null || _time.GetElapsedTime(checkedAt) >= OverlayInterval)
            {
                var ctx = Context();
                var k = OverlayKey.Compute(layout, ctx, _renderer);
                checkedAt = _time.GetTimestamp();
                if (k != key)
                {
                    var overlay = _renderer.Render(layout, ctx);
                    try { compositor.SetOverlay(overlay); }
                    catch
                    {
                        overlay.Dispose();
                        throw;
                    }
                    SetPreviewOverlay(overlay);
                    key = k;
                }
            }

            var jpeg = animation.JpegFrames[i];
            compositor.Compose(jpeg);
            var frame = new EncodedFrame(PixelFormat.Jpeg, DeviceW, DeviceH, compositor.Encode().Data);
            SetPreviewFrame(jpeg);
            await SendAsync(frame, runCt);
            if (capped is null) await HoldAsync(frame, pacer, animation.DelaysMs[i], runCt, planCt);
            else
            {
                pacer.Next = start + capped.NextEmission(pacer.Next - start);
                await HoldUntilAsync(frame, pacer, runCt, planCt);
            }
        }
    }

    /// <summary>Waits until the frame's display time is over, re-sending it every second meanwhile (keep-alive).</summary>
    private Task HoldAsync(EncodedFrame frame, Pacer pacer, int delayMs, CancellationToken runCt, CancellationToken planCt)
    {
        pacer.Next += TimeSpan.FromMilliseconds(Math.Max(delayMs, MinFrameDelayMs));
        return HoldUntilAsync(frame, pacer, runCt, planCt);
    }

    /// <summary>Waits until <see cref="Pacer.Next"/>, re-sending the frame every second meanwhile (keep-alive).</summary>
    private async Task HoldUntilAsync(EncodedFrame frame, Pacer pacer, CancellationToken runCt, CancellationToken planCt)
    {
        var now = _time.GetUtcNow();
        if (pacer.Next <= now)
        {
            // Running late (slow device or render): show the next frame now rather than trying to catch up, but
            // still notice invalidation and cancellation, and never loop without yielding.
            pacer.Next = now;
            planCt.ThrowIfCancellationRequested();
            runCt.ThrowIfCancellationRequested();
            await Task.Yield();
            return;
        }
        while (pacer.Next - now > KeepAlive)
        {
            await WaitAsync(KeepAlive, planCt);
            await SendAsync(frame, runCt);
            now = _time.GetUtcNow();
        }
        await WaitAsync(pacer.Next - now, planCt);
    }

    private RenderContext Context() => new(_hub, _settings.Current, _time.GetUtcNow(), _assets, _fonts);

    private void DrawBackground(SKCanvas canvas, Layout layout)
    {
        canvas.Clear(SKColors.Black);
        switch (layout.Background)
        {
            case ColorBackground c:
                if (SKColor.TryParse(c.Color, out var color)) canvas.Clear(color);
                else _log.LogWarning("Layout '{Layout}': invalid background colour '{Color}'", layout.Name, c.Color);
                break;
            case ImageBackground i:
                using (var bmp = _assets.GetImage(i.AssetId))
                {
                    if (bmp is null) _log.LogWarning("Layout '{Layout}': background image '{Asset}' is missing", layout.Name, i.AssetId);
                    else DrawStretched(canvas, bmp, layout.Width, layout.Height);
                }
                break;
            // An animation background whose asset is missing renders black.
        }
    }

    private static void DrawJpeg(SKCanvas canvas, byte[] jpeg, int width, int height)
    {
        using var bmp = SKBitmap.Decode(jpeg);
        if (bmp is not null) DrawStretched(canvas, bmp, width, height);
    }

    private static void DrawStretched(SKCanvas canvas, SKBitmap bmp, int width, int height)
    {
        if (bmp.Width == width && bmp.Height == height)
        {
            canvas.DrawBitmap(bmp, 0, 0);
            return;
        }
        using var image = SKImage.FromBitmap(bmp);
        canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    /// <summary>Draws a layout-size image, makes it the preview and returns it encoded for the device.</summary>
    private EncodedFrame ComposeFrame(Layout layout, int rotation, int cw, int ch, Action<SKCanvas> draw)
    {
        var image = new SKBitmap(layout.Width, layout.Height);
        try
        {
            using (var canvas = new SKCanvas(image)) draw(canvas);
            var frame = EncodeLayoutImage(image, rotation, cw, ch);
            SetPreview(image);
            return frame;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>Fits a layout-size image into the canvas if their sizes differ, then rotates and encodes it.</summary>
    private EncodedFrame EncodeLayoutImage(SKBitmap layoutImage, int rotation, int cw, int ch)
    {
        if (layoutImage.Width == cw && layoutImage.Height == ch) return EncodeForDevice(layoutImage, rotation);
        using var fitted = Orientation.FitToCanvas(layoutImage, cw, ch);
        return EncodeForDevice(fitted, rotation, scratch: true);
    }

    /// <summary>
    /// Rotates and calibrates a canvas-size image into device bytes. Unless it is <paramref name="scratch"/> (a private
    /// temporary), <paramref name="canvasImage"/> may be the preview and is never changed: calibration works on the
    /// rotated copy, or on a copy of its own at rotation 0.
    /// </summary>
    private EncodedFrame EncodeForDevice(SKBitmap canvasImage, int rotation, bool scratch = false)
    {
        if (rotation == 0 && (_calibration is null || scratch))
        {
            _calibration?.Apply(canvasImage);
            return new EncodedFrame(PixelFormat.Jpeg, DeviceW, DeviceH, JpegEncoder.Encode(canvasImage, codec: _codec).Data);
        }
        using var device = rotation == 0 ? canvasImage.Copy() : Orientation.ToDevice(canvasImage, rotation);
        _calibration?.Apply(device);
        return new EncodedFrame(PixelFormat.Jpeg, DeviceW, DeviceH, JpegEncoder.Encode(device, codec: _codec).Data);
    }

    /// <summary>Takes ownership of <paramref name="layoutImage"/>.</summary>
    private void SetPreview(SKBitmap layoutImage)
    {
        lock (_previewGate)
        {
            ClearPreview();
            _previewBitmap = layoutImage;
        }
    }

    private void SetPreview(byte[] layoutJpeg)
    {
        lock (_previewGate)
        {
            ClearPreview();
            _previewJpeg = layoutJpeg;
        }
    }

    /// <summary>
    /// Animated layouts: the overlay now drawn over each frame. Takes ownership of <paramref name="overlay"/>. Whatever
    /// the preview showed stays available until the next frame replaces it.
    /// </summary>
    private void SetPreviewOverlay(SKBitmap overlay)
    {
        lock (_previewGate)
        {
            _previewOverlay?.Dispose();
            _previewOverlay = overlay;
            if (_previewFrame is not null) _previewJpeg = null;   // re-composed with the new overlay on demand
        }
    }

    /// <summary>Animated layouts: the stored frame now on screen, composed with the preview overlay on demand.</summary>
    private void SetPreviewFrame(byte[] jpeg)
    {
        lock (_previewGate)
        {
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            _previewFrame = jpeg;
            _previewJpeg = null;
        }
    }

    private void ClearPreview()   // caller holds _previewGate
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewOverlay?.Dispose();
        _previewOverlay = null;
        _previewFrame = null;
        _previewJpeg = null;
    }

    private async Task SendAsync(EncodedFrame frame, CancellationToken ct)
    {
        try { await _conn.SendFrameAsync(frame, ct); }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            _sendFailure = ex;
            throw;
        }
        Interlocked.Increment(ref _framesSent);
        lock (_sendTimes)
        {
            _sendTimes.Enqueue(_time.GetTimestamp());
            Prune();
        }
    }

    private void Prune()   // caller holds _sendTimes
    {
        while (_sendTimes.Count > 0 && _time.GetElapsedTime(_sendTimes.Peek()) > FpsWindow) _sendTimes.Dequeue();
    }

    /// <summary>
    /// Timer wait. Continuations always resume on the thread pool, so an <see cref="Invalidate"/> caller never runs
    /// session work inline.
    /// </summary>
    /// <summary>Waits one keep-alive period; returns early, without throwing, on cancellation.</summary>
    private async Task IdleAsync(CancellationToken ct) =>
        await Task.Delay(KeepAlive, _time, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);

    private async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        await Task.Delay(delay, _time, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
        ct.ThrowIfCancellationRequested();
    }
}
