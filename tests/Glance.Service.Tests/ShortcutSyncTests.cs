using Glance.Core.Layouts;
using Glance.Service.Security;
using Glance.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Service.Tests;

public class ShortcutSyncTests
{
    private static string Url(string path) =>
        File.ReadAllLines(path).Single(l => l.StartsWith("URL=")).Substring(4);

    private static (TempDir dir, SettingsStore store, string shortcut) Arrange(Action<AppSettings>? seed = null)
    {
        var dir = new TempDir();
        var store = new SettingsStore(new GlancePaths(dir.Path));
        if (seed is not null) store.Update(seed);
        var shortcut = Path.Combine(dir.Path, "Glance.url");
        File.WriteAllText(shortcut, "[InternetShortcut]\r\nURL=http://stale/\r\n");
        return (dir, store, shortcut);
    }

    [Fact]
    public async Task Sync_rewrites_on_port_change()
    {
        var (dir, store, sc) = Arrange();
        using var _ = dir;
        using var sync = new ShortcutSync(store, new WebBindingState(StartupBinding.Configured), [sc], NullLogger<ShortcutSync>.Instance);
        await sync.StartAsync(default);
        Assert.Equal("http://localhost:8735/", Url(sc));

        store.Update(s => s.Port = 9100);
        Assert.Equal("http://localhost:9100/", Url(sc));

        store.Update(s => { s.NetworkAccess = true; s.Https = true; s.PasswordHash = "x"; });
        Assert.Equal("https://localhost:9100/", Url(sc));
    }

    [Fact]
    public async Task Sync_ignores_unrelated_setting_change()
    {
        var (dir, store, sc) = Arrange();
        using var _ = dir;
        using var sync = new ShortcutSync(store, new WebBindingState(StartupBinding.Configured), [sc], NullLogger<ShortcutSync>.Instance);
        await sync.StartAsync(default);
        File.WriteAllText(sc, "[InternetShortcut]\r\nURL=http://user-edited/\r\n");

        store.Update(s => s.PasswordHash = "x");

        Assert.Equal("http://user-edited/", Url(sc));
    }

    [Fact]
    public async Task Sync_uses_fallback_address_when_fallback()
    {
        var (dir, store, sc) = Arrange(s => s.Port = 9100);
        using var _ = dir;
        using var sync = new ShortcutSync(store, new WebBindingState(StartupBinding.LoopbackFallback), [sc], NullLogger<ShortcutSync>.Instance);
        await sync.StartAsync(default);
        Assert.Equal("http://localhost:8735/", Url(sc));

        store.Update(s => s.Port = 9200);
        Assert.Equal("http://localhost:8735/", Url(sc));
    }

    [Fact]
    public async Task Stop_unsubscribes()
    {
        var (dir, store, sc) = Arrange();
        using var _ = dir;
        using var sync = new ShortcutSync(store, new WebBindingState(StartupBinding.Configured), [sc], NullLogger<ShortcutSync>.Instance);
        await sync.StartAsync(default);
        await sync.StopAsync(default);
        store.Update(s => s.Port = 9300);
        Assert.Equal("http://localhost:8735/", Url(sc));
    }
}
