using Glance.Core.Layouts;
using Glance.Platform.Windows;
using Glance.Service.Security;

namespace Glance.Service;

/// <summary>
/// Keeps the Start-menu and desktop shortcuts (when they exist) pointing at the address the service is really
/// reachable on. Runs on start and whenever the port, HTTPS or network-access setting changes.
/// </summary>
public sealed class ShortcutSync(SettingsStore settings, WebBindingState binding, IReadOnlyList<string> paths, ILogger<ShortcutSync> log)
    : IHostedService, IDisposable
{
    private readonly object _gate = new();
    private (int Port, bool Https, bool NetworkAccess, StartupBinding Mode)? _last;
    private bool _subscribed;

    public Task StartAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_subscribed) { settings.Changed += OnChanged; binding.Changed += OnChanged; _subscribed = true; }
        }
        Sync();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) { Unsubscribe(); return Task.CompletedTask; }

    public void Dispose() => Unsubscribe();

    private void Unsubscribe()
    {
        lock (_gate)
        {
            if (_subscribed) { settings.Changed -= OnChanged; binding.Changed -= OnChanged; _subscribed = false; }
        }
    }

    private void OnChanged() => Sync();

    private void Sync()
    {
        lock (_gate)
        {
            var s = settings.Current;
            var key = (s.Port, s.Https, s.NetworkAccess, binding.Mode);
            if (_last == key) return;
            try
            {
                UrlShortcuts.RewriteExisting(paths, ServiceAddress.LocalUrl(s, binding.Mode),
                    (p, why) => log.LogWarning("Shortcut {Path} was not updated: {Reason}", p, why));
                // Skipped/failed writes are retried only on the next relevant change or restart: shortcuts are best-effort.
                _last = key;
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Updating shortcuts failed");
            }
        }
    }
}
