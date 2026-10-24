using Glance.Core.Layouts;
using Glance.Platform.Windows;
using Glance.Service.Security;

namespace Glance.Service;

/// <summary>
/// Owns the "Glance web UI" firewall rule: it exists (for the current port) only while network access is on and the
/// web UI is really bound to the configured network endpoints. Failures are logged, never fatal; not elevated = no-op.
/// </summary>
public sealed class FirewallSync(SettingsStore settings, WebBindingState binding, IFirewall firewall, Func<bool> isElevated, ILogger<FirewallSync> log)
    : IHostedService, IDisposable
{
    private readonly object _gate = new();
    private bool _subscribed, _synced, _skipLogged;
    private int? _applied; // valid only while _synced: what the last successful apply left in the firewall

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
            try
            {
                if (!isElevated())
                {
                    if (!_skipLogged) { log.LogWarning("Not elevated: the firewall rule is not managed"); _skipLogged = true; }
                    return;
                }
                var s = settings.Current;
                int? desired = binding.Mode == StartupBinding.Configured && s.NetworkAccess ? s.Port : null;
                // Never trust the firewall's current state (RulePort cannot tell "absent" from "unreadable"):
                // the first sync, and any sync after a failure, applies the desired state unconditionally.
                if (_synced && _applied == desired) return;
                if (desired is { } port) firewall.Upsert(port); else firewall.Remove();
                _applied = desired;
                _synced = true;
            }
            catch (Exception e)
            {
                _synced = false; // a failed upsert may have deleted the old rule: re-apply on the next sync
                log.LogWarning(e, "Updating the firewall rule failed; it will be retried on the next change");
            }
        }
    }
}
