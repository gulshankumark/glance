using Glance.Core.Layouts;

namespace Glance.Core.Notices;

/// <summary>A message for the person using the web UI. <paramref name="Severity"/> is "warning" or "error".</summary>
public sealed record Notice(string Id, string Severity, string Text);

/// <summary>Startup and runtime notices; a dismissed id is remembered in settings so it stays hidden after a restart.</summary>
public sealed class NoticeBoard(SettingsStore settings)
{
    private readonly object _gate = new();
    private readonly List<Notice> _notices = [];

    public void Add(Notice n)
    {
        lock (_gate)
        {
            if (_notices.Any(x => x.Id == n.Id)) return;
            _notices.Add(n);
        }
    }

    public IReadOnlyList<Notice> Active
    {
        get
        {
            var dismissed = settings.Current.DismissedNotices;
            lock (_gate) return _notices.Where(n => !dismissed.Contains(n.Id)).ToList();
        }
    }

    public void Dismiss(string id)
    {
        settings.Update(s =>
        {
            if (!s.DismissedNotices.Contains(id)) s.DismissedNotices.Add(id);
        });
    }
}
