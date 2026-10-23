using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Glance.Core.Sessions;
using Glance.Testing;
using SkiaSharp;
using static Glance.Testing.TestMedia;

namespace Glance.Core.Tests.Sessions;

public class DisplaySessionTests
{
    private const string StillWithSensor =
        """{"name":"L","width":1280,"height":480,"background":{"type":"color","color":"#203040"},"items":[{"type":"sensor","id":"s","x":10,"y":10,"sensor":"cpu/t"}]}""";
    private const string AnimationOnly =
        """{"name":"L","width":1280,"height":480,"background":{"type":"animation","assetId":"anim"},"items":[]}""";
    private const string AnimationWithSensor =
        """{"name":"L","width":1280,"height":480,"background":{"type":"animation","assetId":"anim"},"items":[{"type":"sensor","id":"s","x":10,"y":10,"sensor":"cpu/t"}]}""";
    private const string RedTopLeftImage =
        """{"name":"L","width":1280,"height":480,"background":{"type":"image","assetId":"red"},"items":[]}""";

    [Fact] public async Task Still_layout_sends_every_second_and_reuses_unchanged_frame()
    {
        using var h = new SessionHarness(layoutJson: StillWithSensor);
        h.Values["cpu/t"] = 50;
        var run = h.Start();
        await h.Advance(TimeSpan.FromSeconds(3));
        Assert.True(h.Conn.Frames.Count >= 3);
        Assert.Equal(1, h.RenderCount);                                 // unchanged → re-sent bytes
        Assert.Equal(h.Conn.Frames[0].Data.ToArray(), h.Conn.Frames[^1].Data.ToArray());
        h.Values["cpu/t"] = 60;
        await h.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, h.RenderCount);
        await h.Stop(run);
    }

    [Fact] public async Task Image_background_is_decoded_once_per_plan_across_overlay_changes()
    {
        using var h = new SessionHarness(layoutJson: RedTopLeftImage.Replace("\"items\":[]",
            "\"items\":[{\"type\":\"sensor\",\"id\":\"s\",\"x\":600,\"y\":10,\"sensor\":\"cpu/t\"}]"));
        h.Assets.SetImage("red", CornerJpeg(1280, 480, (0, 0)));
        h.Values["cpu/t"] = 50;
        var run = h.Start();
        for (var t = 0; t < 5; t++)
        {
            h.Values["cpu/t"] = 51 + t;   // a new overlay every second
            await h.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.True(h.RenderCount >= 5);
        Assert.Equal(1, h.Assets.ImageRequests);
        using var p = SKBitmap.Decode(h.Session.GetPreviewJpeg());
        Assert.True(IsRedish(p.GetPixel(2, 2)));               // the cached background is still drawn ...
        Assert.True(HasBrightPixel(p, 600, 10, 120, 50));      // ... under the latest overlay
        await h.Stop(run);
    }

    [Fact] public async Task No_active_layout_sends_fallback_screen()
    {
        using var h = new SessionHarness(layoutJson: null);
        var run = h.Start();
        await h.Advance(TimeSpan.FromSeconds(2));
        Assert.True(h.Conn.Frames.Count >= 2);   // re-sent as keep-alive
        Assert.Null(h.Session.LayoutName);
        await h.Stop(run);
    }

    [Fact] public async Task Animation_without_items_passes_frames_through_with_delays()
    {
        using var h = new SessionHarness(layoutJson: AnimationOnly, animation: TwoFrames(delays: new[] { 41, 41 }), rotation: 0);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(41 * 4));
        Assert.Equal(h.Animation.JpegFrames[0], h.Conn.Frames[0].Data.ToArray());
        Assert.Equal(h.Animation.JpegFrames[1], h.Conn.Frames[1].Data.ToArray());
        Assert.Equal(5, h.Conn.Frames.Count);   // t = 0, 41, 82, 123, 164 ms
        Assert.Equal(0, h.RenderCount);
        await h.Stop(run);
    }

    [Fact] public async Task Animation_without_items_is_pre_rotated_for_the_device()
    {
        var anim = new Animation(1280, 480, new[] { CornerJpeg(1280, 480, (0, 0)) }, new[] { 100 });
        using var h = new SessionHarness(layoutJson: AnimationOnly, animation: anim, rotation: 180);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(250));
        using var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
        Assert.True(IsRedish(dev.GetPixel(1277, 477)));
        Assert.Equal(h.Conn.Frames[0].Data.ToArray(), h.Conn.Frames[^1].Data.ToArray());   // rotated once, then reused
        await h.Stop(run);
    }

    [Fact] public async Task Animation_with_items_composites_every_frame()
    {
        using var h = new SessionHarness(layoutJson: AnimationWithSensor, animation: TwoFrames(new[] { 41, 41 }));
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(41 * 4));
        Assert.NotEqual(h.Animation.JpegFrames[0], h.Conn.Frames[0].Data.ToArray());
        Assert.True(h.Conn.Frames.Count >= 4);
        Assert.Equal(1, h.RenderCount);   // overlay re-rendered at most once per second
        await h.Stop(run);
    }

    [Fact] public async Task Preview_is_layout_oriented_when_device_rotated_180()
    {
        using var h = new SessionHarness(layoutJson: RedTopLeftImage, rotation: 180);
        h.Assets.SetImage("red", CornerJpeg(1280, 480, (0, 0)));
        var run = h.Start();
        await h.Advance(TimeSpan.FromSeconds(1));
        using var p = SKBitmap.Decode(h.Session.GetPreviewJpeg());
        Assert.True(IsRedish(p.GetPixel(2, 2)));
        using var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
        Assert.True(IsRedish(dev.GetPixel(1277, 477)));
        await h.Stop(run);
    }

    [Fact] public async Task Animated_preview_is_layout_oriented_with_the_overlay_when_device_rotated_180()
    {
        var anim = new Animation(1280, 480, new[] { CornerJpeg(1280, 480, (0, 0)) }, new[] { 100 });
        using var h = new SessionHarness(layoutJson: AnimationWithSensor.Replace("\"x\":10", "\"x\":600"), animation: anim, rotation: 180);
        h.Values["cpu/t"] = 50;
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(250));
        using var p = SKBitmap.Decode(h.Session.GetPreviewJpeg());
        Assert.True(IsRedish(p.GetPixel(2, 2)));
        using var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
        Assert.True(IsRedish(dev.GetPixel(1277, 477)));
        Assert.True(HasBrightPixel(p, 600, 10, 120, 50));      // sensor text where the layout puts it ...
        Assert.True(HasBrightPixel(dev, 560, 420, 120, 50));   // ... and upside down on the device
        await h.Stop(run);
    }

    [Fact] public async Task Animated_preview_stays_available_across_overlay_changes()
    {
        // The codec runs while each device frame is being built, i.e. in the window right after an overlay change.
        var nulls = 0;
        DisplaySession? session = null;
        var inHook = false;   // the preview itself encodes through the codec
        var codec = new HookedCodec(() =>
        {
            if (inHook) return;
            inHook = true;
            if (session!.FramesSent > 0 && session.GetPreviewJpeg() is null) nulls++;
            inHook = false;
        });
        using var h = new SessionHarness(layoutJson: AnimationWithSensor, animation: TwoFrames(new[] { 41, 41 }), codec: codec);
        session = h.Session;
        h.Values["cpu/t"] = 50;
        var run = h.Start();
        for (var t = 0; t < 4; t++)
        {
            h.Values["cpu/t"] = 51 + t;   // a new overlay every second
            await h.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.True(h.RenderCount >= 4);
        Assert.True(codec.Encodes > 50);
        Assert.Equal(0, nulls);
        await h.Stop(run);
    }

    [Fact] public async Task Portrait_rotation_fits_a_landscape_layout_into_the_canvas()
    {
        using var h = new SessionHarness(layoutJson: RedTopLeftImage, rotation: 90);
        h.Assets.SetImage("red", Png(1280, 480, SKColors.Red));
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        using var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
        Assert.Equal((1280, 480), (dev.Width, dev.Height));
        // The 480x1280 canvas holds the layout scaled to 480x180 with black bars above and below;
        // rotated 90 degrees clockwise for the device, the bars are at the left and right edges.
        Assert.True(IsRedish(dev.GetPixel(640, 240)));
        Assert.True(IsBlack(dev.GetPixel(20, 240)));
        Assert.True(IsBlack(dev.GetPixel(1260, 240)));
        await h.Stop(run);
    }

    [Fact] public async Task Layout_change_renders_immediately()
    {
        using var h = new SessionHarness(layoutJson: StillWithSensor);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(500));
        var sent = h.Conn.Frames.Count;
        h.Layouts.Save(GlanceJson.Deserialize<Layout>(StillWithSensor.Replace("#203040", "#00FF00"))!);
        await h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, h.RenderCount);
        Assert.Equal(sent + 1, h.Conn.Frames.Count);
        using var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
        Assert.True(dev.GetPixel(640, 400).Green > 200);
        await h.Stop(run);
    }

    [Fact] public async Task Temperature_unit_change_re_renders_the_active_layout_at_once()
    {
        using var h = new SessionHarness(layoutJson: StillWithSensor);
        h.Values["cpu/t"] = 50;
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(500));
        var sent = h.Conn.Frames.Count;
        h.Settings.Update(s => s.Unit = TemperatureUnit.Fahrenheit);
        await h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, h.RenderCount);
        Assert.Equal(sent + 1, h.Conn.Frames.Count);
        Assert.NotEqual(h.Conn.Frames[0].Data.ToArray(), h.Conn.Frames[^1].Data.ToArray());
        await h.Stop(run);
    }

    [Fact] public void Overlay_key_follows_the_temperature_unit()
    {
        var src = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature));
        var hub = HubFactory.NewHub(src);
        hub.SetInterest(new Dictionary<string, int> { ["cpu/t"] = 0 });
        src.Values["cpu/t"] = 50; hub.Tick();
        var layout = GlanceJson.Deserialize<Layout>(StillWithSensor)!;
        string Key(TemperatureUnit unit) => OverlayKey.Compute(layout,
            new RenderContext(hub, new AppSettings { Unit = unit }, DateTimeOffset.UnixEpoch, new FakeAssetStore(), new FontProvider()),
            new OverlayRenderer());
        Assert.Contains("122 °F", Key(TemperatureUnit.Fahrenheit));
        Assert.NotEqual(Key(TemperatureUnit.Celsius), Key(TemperatureUnit.Fahrenheit));
    }

    [Fact] public async Task Session_registers_its_sensor_interest_and_clears_it_when_it_ends()
    {
        using var h = new SessionHarness(layoutJson: StillWithSensor);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        h.Hub.ClearInterest("harness");
        h.Hub.Tick();
        Assert.Equal(new[] { "cpu/t" }, h.Source.LastRequested);
        Assert.Equal("L", h.Session.LayoutName);
        await h.Stop(run);
        h.Hub.Tick();
        Assert.Empty(h.Source.LastRequested);
    }

    [Fact] public async Task Fps_counts_frames_sent_over_the_last_five_seconds()
    {
        using var h = new SessionHarness(layoutJson: AnimationOnly, animation: TwoFrames(new[] { 100, 100 }));
        var run = h.Start();
        await h.Advance(TimeSpan.FromSeconds(6));
        Assert.InRange(h.Session.Fps, 9.5, 10.5);
        Assert.Equal(61, h.Session.FramesSent);   // t = 0, 100, ..., 6000 ms
        await h.Stop(run);
    }

    [Fact] public async Task Animation_on_a_slow_device_still_notices_invalidation_and_cancellation()
    {
        using var h = new SessionHarness(layoutJson: AnimationOnly, animation: TwoFrames(new[] { 20, 20 }));
        h.Conn.BeforeSend = ct => Task.Delay(TimeSpan.FromMilliseconds(50), h.Time, ct);   // every frame is late
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(300));
        Assert.InRange(h.Conn.Frames.Count, 5, 7);   // paced by the device: one frame per 50 ms send
        h.Layouts.Save(GlanceJson.Deserialize<Layout>(StillWithSensor.Replace("#203040", "#FF0000"))!);
        await h.Advance(TimeSpan.FromMilliseconds(200));
        using var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
        Assert.True(IsRedish(dev.GetPixel(640, 400)));
        await h.Stop(run);
        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact] public async Task Passthrough_frames_are_prepared_lazily_so_the_first_frame_goes_out_at_once()
    {
        byte[] blue = Jpeg(SKColors.Blue), green = Jpeg(SKColors.Green);
        var frames = new AccessRecordingList(Enumerable.Range(0, 30).Select(i => i % 2 == 0 ? blue : green).ToArray());
        using var h = new SessionHarness(layoutJson: AnimationOnly, animation: new Animation(1280, 480, frames, Enumerable.Repeat(100, 30).ToArray()), rotation: 180);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Single(h.Conn.Frames);
        Assert.Equal(new[] { 0 }, frames.Accessed);   // only the frame on screen has been prepared
        await h.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(new[] { 0, 1, 2, 3 }, frames.Accessed);
        await h.Stop(run);
    }

    [Fact] public async Task Stored_frame_over_the_device_limit_is_re_encoded()
    {
        byte[] big;
        using (var noise = RenderHelpers.Noise(1280, 480, seed: 1))
        using (var image = SKImage.FromBitmap(noise))
        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 100))
            big = data.ToArray();
        Assert.True(big.Length > JpegEncoder.MaxBytes);
        using var h = new SessionHarness(layoutJson: AnimationOnly, animation: new Animation(1280, 480, new[] { big }, new[] { 100 }));
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.InRange(h.Conn.Frames[0].Data.Length, 1, JpegEncoder.MaxBytes);
        using var dev = SKBitmap.Decode(h.Conn.Frames[0].Data.ToArray());
        Assert.Equal((1280, 480), (dev.Width, dev.Height));
        await h.Stop(run);
    }

    [Fact] public async Task Render_failure_shows_an_error_screen_until_the_next_change()
    {
        using var h = new SessionHarness(layoutJson: StillWithSensor);
        h.Renderer.Throw = new InvalidOperationException("boom");
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(2500));
        Assert.False(run.IsCompleted);
        Assert.InRange(h.Conn.Frames.Count, 3, 4);   // error screen kept alive every second
        using (var err = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray()))
            Assert.True(IsBlack(err.GetPixel(640, 400)));
        h.Renderer.Throw = null;
        h.Session.Invalidate();
        await h.Advance(TimeSpan.FromMilliseconds(1));
        using var ok = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
        var c = ok.GetPixel(640, 400);   // the layout's #203040 background
        Assert.InRange(c.Red, 20, 45);
        Assert.InRange(c.Blue, 50, 80);
        await h.Stop(run);
    }

    [Fact] public async Task Device_without_jpeg_support_keeps_the_session_alive_without_sending()
    {
        using var h = new SessionHarness(layoutJson: StillWithSensor,
            conn: new FakeConnection(formats: new[] { Glance.Core.Devices.PixelFormat.Rgb565LE }));
        var run = h.Start();
        await h.Advance(TimeSpan.FromSeconds(2));
        Assert.False(run.IsCompleted);
        Assert.Empty(h.Conn.Frames);
        await h.Stop(run);
    }

    [Fact] public async Task Connection_failure_ends_run_with_exception()
    {
        using var h = new SessionHarness(layoutJson: StillWithSensor);
        h.Conn.ThrowOnSend = new IOException("unplugged");
        var run = h.Start();
        await h.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<IOException>(() => run);
    }

    [Fact] public void Overlay_key_changes_only_when_what_is_drawn_changes()
    {
        var src = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature));
        var hub = HubFactory.NewHub(src);
        hub.SetInterest(new Dictionary<string, int> { ["cpu/t"] = 0 });
        var layout = GlanceJson.Deserialize<Layout>(StillWithSensor)!;
        var r = new OverlayRenderer();
        string Key() => OverlayKey.Compute(layout,
            new RenderContext(hub, new AppSettings(), DateTimeOffset.UnixEpoch, new FakeAssetStore(), new FontProvider()), r);
        src.Values["cpu/t"] = 50.2; hub.Tick();
        var a = Key();
        src.Values["cpu/t"] = 50.4; hub.Tick();
        Assert.Equal(a, Key());   // both format as "50 °C"
        src.Values["cpu/t"] = 51; hub.Tick();
        var b = Key();
        Assert.NotEqual(a, b);
        layout.Items[0].X = 20;
        Assert.NotEqual(b, Key());
    }

    [Fact] public async Task Overlay_fps_cap_skips_frames_but_keeps_animation_speed()
    {
        using var h = new SessionHarness(layoutJson: AnimationWithSensor, animation: GrayFrames(10, 41));
        h.Settings.Update(s => s.MaxOverlayAnimationFps = 12);
        var run = h.Start();
        await h.Advance(TimeSpan.FromSeconds(2));
        var sent = h.Conn.Frames.Select(f => GrayIndex(f.Data.ToArray())).ToArray();
        Assert.InRange(sent.Length, 23, 25);   // ~12 fps instead of ~24
        // Emission k (at k * 83.3 ms) shows the frame current on the unchanged 410 ms loop: ~2 frames further each time.
        var expected = Enumerable.Range(0, sent.Length).Select(k => (int)(k * 1000.0 / 12 % 410 / 41)).ToArray();
        Assert.Equal(expected, sent);
        Assert.Equal(1, h.RenderCount);   // overlay still re-rendered at most once per second, and only on change
        await h.Stop(run);
    }

    [Fact] public async Task Overlay_without_fps_cap_shows_every_frame_at_native_timing()
    {
        using var h = new SessionHarness(layoutJson: AnimationWithSensor, animation: GrayFrames(10, 41));
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(41 * 12));
        var sent = h.Conn.Frames.Select(f => GrayIndex(f.Data.ToArray())).ToArray();
        Assert.Equal(Enumerable.Range(0, 13).Select(i => i % 10).ToArray(), sent);   // t = 0, 41, ..., 492 ms
        await h.Stop(run);
    }

    [Fact] public async Task Overlay_fps_cap_at_or_above_the_native_rate_changes_nothing()
    {
        async Task<byte[][]> Frames(int? cap)
        {
            using var h = new SessionHarness(layoutJson: AnimationWithSensor, animation: GrayFrames(10, 41));
            h.Settings.Update(s => s.MaxOverlayAnimationFps = cap);
            var run = h.Start();
            await h.Advance(TimeSpan.FromSeconds(2));
            await h.Stop(run);
            return h.Conn.Frames.Select(f => f.Data.ToArray()).ToArray();
        }
        var native = await Frames(null);
        Assert.Equal(49, native.Length);   // t = 0, 41, ..., 1968 ms
        Assert.Equal(native, await Frames(25));
        Assert.Equal(native, await Frames(60));
    }

    [Fact] public async Task Overlay_fps_cap_does_not_affect_passthrough()
    {
        using var h = new SessionHarness(layoutJson: AnimationOnly, animation: TwoFrames(new[] { 41, 41 }));
        h.Settings.Update(s => s.MaxOverlayAnimationFps = 12);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(41 * 4));
        Assert.Equal(5, h.Conn.Frames.Count);   // t = 0, 41, 82, 123, 164 ms
        Assert.Equal(h.Animation.JpegFrames[1], h.Conn.Frames[1].Data.ToArray());
        await h.Stop(run);
    }

    [Fact] public async Task Overlay_fps_cap_keeps_long_frames_alive_every_second()
    {
        var anim = new Animation(1280, 480, new[] { GrayJpeg(0), GrayJpeg(25) }, new[] { 2500, 100 });
        using var h = new SessionHarness(layoutJson: AnimationWithSensor, animation: anim);
        h.Settings.Update(s => s.MaxOverlayAnimationFps = 1);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(2400));
        Assert.Equal(new[] { 0, 0, 0 }, h.Conn.Frames.Select(f => GrayIndex(f.Data.ToArray())).ToArray());   // t = 0, 1000, 2000 ms
        await h.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1, GrayIndex(h.Conn.Frames[^1].Data.ToArray()));   // frame 1 is due at 2500 ms
        await h.Stop(run);
    }

    private const string GreyStill =
        """{"name":"L","width":1280,"height":480,"background":{"type":"color","color":"#808080"},"items":[]}""";

    private static void Calibrate(SessionHarness h, double gamma, int brightness = 100) =>
        h.Settings.Update(s => { s.Devices[h.Conn.Info.Key].Gamma = gamma; s.Devices[h.Conn.Info.Key].Brightness = brightness; });

    private static int SentGrey(SessionHarness h, int index = -1)
    {
        using var dev = SKBitmap.Decode(h.Conn.Frames[index < 0 ? h.Conn.Frames.Count + index : index].Data.ToArray());
        return dev.GetPixel(640, 400).Red;
    }

    [Theory] [InlineData(0)] [InlineData(180)]
    public async Task Calibration_darkens_the_sent_still_frame_but_not_the_preview(int rotation)
    {
        async Task<(int Grey, byte[] Preview)> Run(double gamma)
        {
            using var h = new SessionHarness(layoutJson: GreyStill, rotation: rotation);
            Calibrate(h, gamma);
            var run = h.Start();
            await h.Advance(TimeSpan.FromMilliseconds(10));
            var result = (SentGrey(h), h.Session.GetPreviewJpeg()!);
            await h.Stop(run);
            return result;
        }
        var plain = await Run(1);
        var calibrated = await Run(2.2);
        Assert.InRange(plain.Grey, 124, 132);
        Assert.InRange(calibrated.Grey, 50, 62);    // round(255 * (128/255)^2.2) = 56
        Assert.Equal(plain.Preview, calibrated.Preview);
    }

    [Fact]
    public async Task Calibration_applies_to_a_layout_fitted_into_the_canvas()
    {
        using var h = new SessionHarness(layoutJson: GreyStill.Replace("\"width\":1280,\"height\":480", "\"width\":640,\"height\":240"));
        Calibrate(h, 2.2);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        Assert.InRange(SentGrey(h), 50, 62);
        using var p = SKBitmap.Decode(h.Session.GetPreviewJpeg());
        Assert.Equal((640, 240), (p.Width, p.Height));
        Assert.InRange((int)p.GetPixel(320, 200).Red, 124, 132);
        await h.Stop(run);
    }

    [Fact]
    public async Task Calibration_change_applies_to_the_next_frame_without_reconnecting()
    {
        using var h = new SessionHarness(layoutJson: GreyStill);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        Assert.InRange(SentGrey(h), 124, 132);
        var preview = h.Session.GetPreviewJpeg();
        Calibrate(h, 1, brightness: 50);
        await h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.InRange(SentGrey(h), 60, 68);
        Assert.Equal(preview, h.Session.GetPreviewJpeg());
        Assert.False(run.IsCompleted);
        await h.Stop(run);
    }

    [Fact]
    public async Task Passthrough_frames_are_rebuilt_when_the_calibration_changes()
    {
        var anim = new Animation(1280, 480, new[] { GrayJpeg(128) }, new[] { 100 });
        using var h = new SessionHarness(layoutJson: AnimationOnly, animation: anim);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(anim.JpegFrames[0], h.Conn.Frames[^1].Data.ToArray());   // identity: the stored bytes as they are
        Calibrate(h, 2.2);
        await h.Advance(TimeSpan.FromMilliseconds(250));
        Assert.InRange(SentGrey(h), 50, 62);
        Calibrate(h, 1, brightness: 50);
        await h.Advance(TimeSpan.FromMilliseconds(250));
        Assert.InRange(SentGrey(h), 60, 68);
        Assert.Equal(anim.JpegFrames[0], h.Session.GetPreviewJpeg());
        Calibrate(h, 1);
        await h.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(anim.JpegFrames[0], h.Conn.Frames[^1].Data.ToArray());
        await h.Stop(run);
    }

    [Theory] [InlineData(0)] [InlineData(180)]
    public async Task Calibration_applies_to_animated_frames_with_an_overlay(int rotation)
    {
        var anim = new Animation(1280, 480, new[] { GrayJpeg(128) }, new[] { 100 });
        using var h = new SessionHarness(layoutJson: AnimationWithSensor, animation: anim, rotation: rotation);
        h.Values["cpu/t"] = 50;
        Calibrate(h, 2.2);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(250));
        Assert.InRange(SentGrey(h), 50, 62);
        using var p = SKBitmap.Decode(h.Session.GetPreviewJpeg());
        Assert.InRange((int)p.GetPixel(640, 400).Red, 124, 132);
        await h.Stop(run);
    }

    [Fact]
    public async Task Fallback_screen_is_calibrated_too()
    {
        using var h = new SessionHarness(layoutJson: null);
        Calibrate(h, 1, brightness: 10);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        using var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
        Assert.False(HasBrightPixel(dev, 0, 0, 1280, 480));   // the white message is dimmed to ~10 %
        using var p = SKBitmap.Decode(h.Session.GetPreviewJpeg());
        Assert.True(HasBrightPixel(p, 0, 0, 1280, 480));
        await h.Stop(run);
    }

    [Fact]
    public async Task Test_pattern_shows_for_its_duration_then_the_layout_returns()
    {
        using var h = new SessionHarness(layoutJson: GreyStill);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        var renders = h.Conn.Frames.Count;
        h.Session.ShowTestPattern(TimeSpan.FromSeconds(3));
        await h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(renders + 1, h.Conn.Frames.Count);
        using (var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray()))
        {
            Assert.True(IsBlack(dev.GetPixel(5, 5)));                       // first grey step
            Assert.True(dev.GetPixel(1274, 5) is { Red: > 240, Green: > 240, Blue: > 240 });   // last grey step
        }
        using (var p = SKBitmap.Decode(h.Session.GetPreviewJpeg()))
            Assert.True(IsBlack(p.GetPixel(5, 5)));
        await h.Advance(TimeSpan.FromMilliseconds(2900));
        Assert.True(IsBlack(PixelOf(h.Conn.Frames[^1], 5, 5)));          // still the pattern, kept alive
        await h.Advance(TimeSpan.FromMilliseconds(200));
        Assert.InRange(SentGrey(h), 124, 132);                            // back to the layout
        Assert.InRange(PixelOf(h.Conn.Frames[^1], 5, 5).Red, 124, 132);
        await h.Stop(run);
    }

    [Fact]
    public async Task Showing_the_test_pattern_again_extends_it()
    {
        using var h = new SessionHarness(layoutJson: GreyStill);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        h.Session.ShowTestPattern(TimeSpan.FromSeconds(3));            // until 3010 ms
        await h.Advance(TimeSpan.FromMilliseconds(2000));
        h.Session.ShowTestPattern(TimeSpan.FromSeconds(3));            // until 5010 ms
        await h.Advance(TimeSpan.FromMilliseconds(2900));
        Assert.True(IsBlack(PixelOf(h.Conn.Frames[^1], 5, 5)));       // past the first deadline: still the pattern
        await h.Advance(TimeSpan.FromMilliseconds(200));
        Assert.InRange(PixelOf(h.Conn.Frames[^1], 5, 5).Red, 124, 132);
        await h.Stop(run);
    }

    [Fact]
    public async Task Stopping_an_expired_test_pattern_does_not_rebuild_the_frame_plan()
    {
        using var h = new SessionHarness(layoutJson: GreyStill);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        h.Session.ShowTestPattern(TimeSpan.FromSeconds(1));
        await h.Advance(TimeSpan.FromMilliseconds(1100));                // expired on its own; the layout is back
        var renders = h.RenderCount;
        var sent = h.Conn.Frames.Count;
        h.Session.StopTestPattern();
        await h.Advance(TimeSpan.FromMilliseconds(5));
        Assert.Equal(renders, h.RenderCount);
        Assert.Equal(sent, h.Conn.Frames.Count);                        // no immediate re-send from a new plan
        await h.Stop(run);
    }

    [Fact]
    public async Task Test_pattern_follows_rotation_and_calibration_and_stops_on_request()
    {
        using var h = new SessionHarness(layoutJson: GreyStill, rotation: 180);
        Calibrate(h, 1, brightness: 50);
        var run = h.Start();
        await h.Advance(TimeSpan.FromMilliseconds(10));
        h.Session.ShowTestPattern(TimeSpan.FromSeconds(120));
        await h.Advance(TimeSpan.FromMilliseconds(1));
        using (var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray()))
        {
            Assert.True(IsBlack(dev.GetPixel(1274, 474)));                  // layout top-left, upside down
            Assert.InRange((int)dev.GetPixel(5, 474).Red, 120, 136);        // white step at 50 % brightness
        }
        h.Session.StopTestPattern();
        await h.Advance(TimeSpan.FromMilliseconds(1));
        Assert.InRange(SentGrey(h), 60, 68);
        await h.Stop(run);
    }

    private static SKColor PixelOf(Glance.Core.Devices.EncodedFrame frame, int x, int y)
    {
        using var bmp = SKBitmap.Decode(frame.Data.ToArray());
        return bmp.GetPixel(x, y);
    }

    /// <summary>Frame i is uniform grey i * 25, so the frame shown can be told from a sent (re-encoded) frame.</summary>
    private static Animation GrayFrames(int count, int delayMs) =>
        new(1280, 480, Enumerable.Range(0, count).Select(i => GrayJpeg((byte)(i * 25))).ToArray(), Enumerable.Repeat(delayMs, count).ToArray());

    private static byte[] GrayJpeg(byte v) => Jpeg(new SKColor(v, v, v));

    private static int GrayIndex(byte[] deviceJpeg)
    {
        using var bmp = SKBitmap.Decode(deviceJpeg);
        return (int)Math.Round(bmp.GetPixel(640, 400).Red / 25.0);
    }

    private static Animation TwoFrames(int[] delays) =>
        new(1280, 480, new[] { Jpeg(SKColors.Blue), Jpeg(SKColors.Green) }, delays);

    private static byte[] Jpeg(SKColor color)
    {
        using var bmp = new SKBitmap(1280, 480);
        bmp.Erase(color);
        return JpegEncoder.Encode(bmp).Data;
    }

    /// <summary>Skia codec that calls <paramref name="onEncode"/> before every encode.</summary>
    private sealed class HookedCodec(Action onEncode) : IJpegCodec
    {
        public int Encodes;

        public bool TryDecode(ReadOnlySpan<byte> jpeg, SKPixmap target) => SkiaJpegCodec.Instance.TryDecode(jpeg, target);

        public byte[] Encode(SKPixmap source, int quality)
        {
            Encodes++;
            onEncode();
            return SkiaJpegCodec.Instance.Encode(source, quality);
        }
    }

    private static bool HasBrightPixel(SKBitmap b, int x0, int y0, int w, int h)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
                if (b.GetPixel(x, y) is { Red: > 200, Green: > 200, Blue: > 200 }) return true;
        return false;
    }

    private static bool IsBlack(SKColor c) => c.Red < 30 && c.Green < 30 && c.Blue < 30;

    /// <summary>Records which frame indexes were read.</summary>
    private sealed class AccessRecordingList(byte[][] items) : IReadOnlyList<byte[]>
    {
        private readonly SortedSet<int> _accessed = new();

        public int[] Accessed { get { lock (_accessed) return _accessed.ToArray(); } }
        public int Count => items.Length;

        public byte[] this[int index]
        {
            get
            {
                lock (_accessed) _accessed.Add(index);
                return items[index];
            }
        }

        public IEnumerator<byte[]> GetEnumerator()
        {
            for (var i = 0; i < Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
