using Glance.Core.Layouts;
using Glance.Platform.Windows;
using Glance.Service.Security;
using Glance.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Service.Tests;

public class FirewallSyncTests
{
    private sealed class FakeFirewall : IFirewall
    {
        public int? Port;
        public List<string> Calls = [];
        public bool Fail;
        public int? RulePort() { Calls.Add("show"); return Port; }
        public void Upsert(int port) { if (Fail) throw new InvalidOperationException("netsh failed"); Calls.Add($"upsert {port}"); Port = port; }
        public void Remove() { if (Fail) throw new InvalidOperationException("netsh failed"); Calls.Add("remove"); Port = null; }
        public string[] Changes => Calls.Where(c => c != "show").ToArray();
    }

    private sealed class CountingLog : ILogger<FirewallSync>
    {
        public int Warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel l, EventId id, TState st, Exception? e, Func<TState, Exception?, string> f) { if (l == LogLevel.Warning) Warnings++; }
    }

    private static (TempDir dir, SettingsStore store) Arrange(Action<AppSettings>? seed = null)
    {
        var dir = new TempDir();
        var store = new SettingsStore(new GlancePaths(dir.Path));
        if (seed is not null) store.Update(seed);
        return (dir, store);
    }

    private static FirewallSync Make(SettingsStore store, WebBindingState b, FakeFirewall fw, bool elevated = true, ILogger<FirewallSync>? log = null) =>
        new(store, b, fw, () => elevated, log ?? NullLogger<FirewallSync>.Instance);

    [Fact]
    public async Task Network_on_upserts_port()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall();
        using var sync = Make(store, new WebBindingState(StartupBinding.Configured), fw);
        await sync.StartAsync(default);
        Assert.Equal(["upsert 8735"], fw.Changes);
    }

    [Fact]
    public async Task Network_off_removes_rule()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall();
        using var sync = Make(store, new WebBindingState(StartupBinding.Configured), fw);
        await sync.StartAsync(default);
        store.Update(s => s.NetworkAccess = false);
        Assert.Equal(["upsert 8735", "remove"], fw.Changes);
        Assert.Null(fw.Port);
    }

    [Fact]
    public async Task Port_change_moves_rule()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall();
        using var sync = Make(store, new WebBindingState(StartupBinding.Configured), fw);
        await sync.StartAsync(default);
        store.Update(s => s.Port = 9100);
        Assert.Equal(["upsert 8735", "upsert 9100"], fw.Changes);
        Assert.Equal(9100, fw.Port);
    }

    [Fact]
    public async Task Fallback_binding_removes_rule()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall { Port = 8735 };
        using var sync = Make(store, new WebBindingState(StartupBinding.LoopbackFallback), fw);
        await sync.StartAsync(default);
        Assert.Equal(["remove"], fw.Changes);
    }

    [Fact]
    public async Task Rule_added_when_binding_leaves_fallback()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall();
        var binding = new WebBindingState(StartupBinding.LoopbackFallback);
        using var sync = Make(store, binding, fw);
        await sync.StartAsync(default);
        Assert.Empty(fw.Changes);
        binding.Mode = StartupBinding.Configured;
        Assert.Equal(["upsert 8735"], fw.Changes);
    }

    [Fact]
    public async Task Firewall_failure_is_logged_not_thrown()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall { Fail = true };
        var log = new CountingLog();
        using var sync = Make(store, new WebBindingState(StartupBinding.Configured), fw, log: log);
        await sync.StartAsync(default);
        Assert.Equal(1, log.Warnings);
        Assert.Empty(fw.Changes);

        fw.Fail = false;
        store.Update(s => s.PasswordHash = "x"); // any change retries
        Assert.Equal(["upsert 8735"], fw.Changes);
    }

    [Fact]
    public async Task Startup_matching_rule_makes_no_change()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall { Port = 8735 };
        using var sync = Make(store, new WebBindingState(StartupBinding.Configured), fw);
        await sync.StartAsync(default);
        Assert.Empty(fw.Changes);
    }

    [Fact]
    public async Task Startup_stale_rule_is_fixed()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall { Port = 1234 };
        using var sync = Make(store, new WebBindingState(StartupBinding.Configured), fw);
        await sync.StartAsync(default);
        Assert.Equal(["upsert 8735"], fw.Changes);
    }

    [Fact]
    public async Task Not_elevated_does_nothing_and_warns_once()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall();
        var log = new CountingLog();
        using var sync = Make(store, new WebBindingState(StartupBinding.Configured), fw, elevated: false, log: log);
        await sync.StartAsync(default);
        store.Update(s => s.Port = 9100);
        Assert.Empty(fw.Calls);
        Assert.Equal(1, log.Warnings);
    }

    [Fact]
    public async Task Stop_unsubscribes()
    {
        var (dir, store) = Arrange(s => s.NetworkAccess = true);
        using var _ = dir;
        var fw = new FakeFirewall();
        using var sync = Make(store, new WebBindingState(StartupBinding.Configured), fw);
        await sync.StartAsync(default);
        await sync.StopAsync(default);
        store.Update(s => s.Port = 9300);
        Assert.Equal(["upsert 8735"], fw.Changes);
    }
}
