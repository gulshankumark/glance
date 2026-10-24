using Glance.Core.Sessions;
using Glance.Testing;
using static Glance.Testing.ManagerHarness;

namespace Glance.Core.Tests.Sessions;

public class DeviceManagerTests
{
    [Fact] public async Task Connects_matching_device_and_creates_settings()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        var d = Assert.Single(h.Manager.Devices);
        Assert.Equal(DeviceStatus.Connected, d.Status);
        Assert.Equal("fake_SER1", d.Key);
        Assert.Equal((1280, 480), (d.Width, d.Height));
        var settings = h.Settings.Current.Devices["fake_SER1"];
        Assert.Equal(0, settings.Rotation);
        Assert.Null(settings.ActiveLayout);
        Assert.NotNull(h.Manager.Session("fake_SER1"));
    }

    [Fact] public async Task Unsupported_model_is_listed_and_not_retried()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.Next = new Glance.Core.Devices.ConnectResult.Unsupported("81/01", "SER2");
        await h.Manager.RescanAsync();
        var d = Assert.Single(h.Manager.Devices);
        Assert.Equal(DeviceStatus.Unsupported, d.Status);
        Assert.Equal("81/01", d.ModelCode);
        await h.Advance(TimeSpan.FromSeconds(12));
        Assert.Equal(1, h.Driver.ConnectCalls);
    }

    [Fact] public async Task Busy_device_retries_with_exponential_backoff_capped_at_30s()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.AlwaysFail = "handshake timeout";
        await h.Manager.RescanAsync();
        var delays = new List<TimeSpan>();
        for (int i = 0; i < 7; i++) { var s = h.Manager.Devices.Single(); delays.Add(s.NextRetry!.Value - h.Time.GetUtcNow()); await h.AdvanceTo(s.NextRetry!.Value); }
        Assert.Equal(new[] { 1, 2, 4, 8, 16, 30, 30 }, delays.Select(d => (int)d.TotalSeconds));
        Assert.Equal(DeviceStatus.Retrying, h.Manager.Devices.Single().Status);
        Assert.Contains("handshake timeout", h.Manager.Devices.Single().Message);
        Assert.Equal(8, h.Driver.ConnectCalls);   // no extra attempts between the scheduled retries
    }

    [Fact] public async Task Retry_success_resets_backoff()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.AlwaysFail = "busy";
        await h.Manager.RescanAsync();
        await h.AdvanceTo(h.Manager.Devices.Single().NextRetry!.Value);   // second failure, next delay 2 s
        h.Driver.AlwaysFail = null; h.Driver.Next = Connected("SER1");
        await h.AdvanceTo(h.Manager.Devices.Single().NextRetry!.Value);
        Assert.Equal(DeviceStatus.Connected, h.Manager.Devices.Single().Status);
        Assert.Null(h.Manager.Devices.Single().NextRetry);
    }

    [Fact] public async Task Unplug_during_streaming_then_replug_reconnects()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        var first = h.LastConnection;
        first.ThrowOnSend = new IOException("gone"); h.Usb.Clear();
        await h.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(h.Manager.Devices);
        Assert.True(first.Disposed);
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(DeviceStatus.Connected, h.Manager.Devices.Single().Status);
        Assert.NotSame(first, h.LastConnection);
    }

    [Fact] public async Task Send_failure_with_device_still_plugged_retries_after_a_delay()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1"));
        h.Driver.Next = new Glance.Core.Devices.ConnectResult.Connected(
            new FakeConnection("fake", "SER1") { ThrowOnSend = new IOException("broken pipe") });
        await h.Manager.RescanAsync();
        await h.Settle();
        var d = h.Manager.Devices.Single();
        Assert.Equal(DeviceStatus.Retrying, d.Status);
        Assert.Contains("broken pipe", d.Message);
        Assert.True(h.LastConnection.Disposed);
        h.Driver.Next = Connected("SER1");
        await h.AdvanceTo(d.NextRetry!.Value);
        Assert.Equal(DeviceStatus.Connected, h.Manager.Devices.Single().Status);
    }

    [Fact] public async Task Hotplug_notification_triggers_immediate_rescan()
    {
        using var h = new ManagerHarness();
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        h.Usb.Notify();
        await h.Settle();
        Assert.Equal(DeviceStatus.Connected, h.Manager.Devices.Single().Status);
    }

    [Fact] public async Task Periodic_fallback_finds_devices_without_notification()
    {
        using var h = new ManagerHarness();
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(DeviceStatus.Connected, h.Manager.Devices.Single().Status);
    }

    [Fact] public async Task DevicesChanged_fires_on_state_changes()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        var count = 0; h.Manager.DevicesChanged += () => Interlocked.Increment(ref count);
        await h.Manager.RescanAsync();
        Assert.True(count >= 1);
        var before = count;
        h.Usb.Clear();
        await h.Manager.RescanAsync();
        Assert.True(count > before);
    }

    [Fact] public async Task Stopping_ends_sessions_and_disposes_connections()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        await h.Manager.StopAsync(CancellationToken.None);
        Assert.True(h.LastConnection.Disposed);
        Assert.Empty(h.Manager.Devices);
    }

    private static FakeConnection FailingConn(Func<Task>? onDispose = null) =>
        new("fake", "SER1") { ThrowOnSend = new IOException("broken pipe"), OnDispose = onDispose };

    [Fact] public async Task Throwing_enumerator_does_not_spin_the_retry_timer()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.AlwaysFail = "busy";
        await h.Manager.RescanAsync();
        h.Usb.ThrowOnEnumerate = new IOException("enumeration broke");
        var before = h.Usb.EnumerateCalls;
        await h.Advance(TimeSpan.FromSeconds(20));
        Assert.InRange(h.Usb.EnumerateCalls - before, 1, 10);
        h.Usb.ThrowOnEnumerate = null;
        await h.Advance(TimeSpan.FromSeconds(10));
        Assert.True(h.Driver.ConnectCalls > 1);   // recovered and retrying again
    }

    [Fact] public async Task Throwing_matches_does_not_block_other_devices()
    {
        using var h = new ManagerHarness(); h.Driver.ThrowOnMatchPath = "bad";
        h.Usb.Add(Dev("bad")); h.Usb.Add(Dev("good")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        Assert.Equal(DeviceStatus.Connected, Assert.Single(h.Manager.Devices).Status);
    }

    [Fact] public async Task Enumerator_throwing_at_startup_keeps_manager_running_and_recovers()
    {
        using var h = new ManagerHarness(u => u.ThrowOnEnumerate = new IOException("not ready"));
        Assert.False(h.Manager.ExecuteTask!.IsCompleted);
        h.Usb.ThrowOnEnumerate = null; h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(DeviceStatus.Connected, h.Manager.Devices.Single().Status);
    }

    [Fact] public async Task Connect_then_fail_device_escalates_backoff()
    {
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1"));
        h.Driver.Always = () => new Glance.Core.Devices.ConnectResult.Connected(FailingConn());
        await h.Manager.RescanAsync();
        await h.Settle();
        var gaps = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            var d = h.Manager.Devices.Single();
            Assert.Equal(DeviceStatus.Retrying, d.Status);
            gaps.Add((int)(d.NextRetry!.Value - h.Time.GetUtcNow()).TotalSeconds);
            await h.AdvanceTo(d.NextRetry!.Value);
        }
        Assert.Equal(new[] { 1, 2, 4, 8 }, gaps);
    }

    [Fact] public async Task Stop_waits_for_a_failed_sessions_connection_to_be_released()
    {
        var started = new TaskCompletionSource(); var gate = new TaskCompletionSource();
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1"));
        h.Driver.Next = new Glance.Core.Devices.ConnectResult.Connected(FailingConn(() => { started.TrySetResult(); return gate.Task; }));
        await h.Manager.RescanAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stop = h.Manager.StopAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.False(stop.IsCompleted);
        gate.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(h.LastConnection.Disposed);
    }

    [Fact] public async Task Replacement_connect_waits_for_old_connection_release()
    {
        var started = new TaskCompletionSource(); var gate = new TaskCompletionSource();
        using var h = new ManagerHarness(); h.Usb.Add(Dev("p1"));
        h.Driver.Next = new Glance.Core.Devices.ConnectResult.Connected(FailingConn(() => { started.TrySetResult(); return gate.Task; }));
        await h.Manager.RescanAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Usb.Clear();
        var rescan = h.Manager.RescanAsync();
        await Task.Delay(100);
        Assert.False(rescan.IsCompleted);
        Assert.NotEmpty(h.Manager.Devices);   // entry stays until its connection is released
        gate.SetResult();
        await rescan.WaitAsync(TimeSpan.FromSeconds(10));
        await h.Settle();
        Assert.Empty(h.Manager.Devices);
    }

    private static void SaveLayout(ManagerHarness h, string name) =>
        h.Layouts.Save(System.Text.Json.JsonSerializer.Deserialize<Glance.Core.Layouts.Layout>(
            Glance.Core.Tests.Layouts.LayoutJsonTests.Sample.Replace("\"Gaming\"", $"\"{name}\""), Glance.Core.Layouts.GlanceJson.Options)!);

    private static async Task Replug(ManagerHarness h, string serial)
    {
        h.LastConnection.ThrowOnSend = new IOException("gone"); h.Usb.Clear();
        await h.Advance(TimeSpan.FromSeconds(2));
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected(serial);
        await h.Advance(TimeSpan.FromSeconds(6));
    }

    [Fact] public async Task New_device_gets_Blend()
    {
        using var h = new ManagerHarness(); SaveLayout(h, "Blend");
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        var s = h.Settings.Current.Devices["fake_SER1"];
        Assert.Equal("Blend", s.ActiveLayout);
        Assert.True(s.DefaultLayoutApplied);
    }

    [Fact] public async Task Existing_device_with_null_layout_gets_Blend_once()
    {
        using var h = new ManagerHarness(); SaveLayout(h, "Blend");
        h.Settings.Update(s => s.Devices["fake_SER1"] = new Glance.Core.Layouts.DeviceSettings());
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        Assert.Equal("Blend", h.Settings.Current.Devices["fake_SER1"].ActiveLayout);
        Assert.True(h.Settings.Current.Devices["fake_SER1"].DefaultLayoutApplied);
    }

    [Fact] public async Task Cleared_layout_stays_cleared()
    {
        using var h = new ManagerHarness(); SaveLayout(h, "Blend");
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        h.Settings.Update(s => s.Devices["fake_SER1"].ActiveLayout = null);
        await Replug(h, "SER1");
        Assert.Equal(DeviceStatus.Connected, h.Manager.Devices.Single().Status);
        Assert.Null(h.Settings.Current.Devices["fake_SER1"].ActiveLayout);
    }

    [Fact] public async Task Device_with_layout_keeps_it()
    {
        using var h = new ManagerHarness(); SaveLayout(h, "Blend"); SaveLayout(h, "Mine");
        h.Settings.Update(s => s.Devices["fake_SER1"] = new Glance.Core.Layouts.DeviceSettings { ActiveLayout = "Mine" });
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        var d = h.Settings.Current.Devices["fake_SER1"];
        Assert.Equal("Mine", d.ActiveLayout);
        Assert.True(d.DefaultLayoutApplied);
    }

    [Fact] public async Task Missing_Blend_leaves_null_and_marks_applied()
    {
        using var h = new ManagerHarness();
        h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
        await h.Manager.RescanAsync();
        var d = h.Settings.Current.Devices["fake_SER1"];
        Assert.Null(d.ActiveLayout);
        Assert.True(d.DefaultLayoutApplied);
    }
}
