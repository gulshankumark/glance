using Glance.Core.Layouts;

namespace Glance.Core.Notices;

/// <summary>A message for the person using the web UI. <paramref name="Severity"/> is "warning" or "error".</summary>
public sealed record Notice(string Id, string Severity, string Text);

/// <summary>Startup and runtime notices; a dismissed id is remembered in settings so it stays hidden after a restart.</summary>
public sealed class NoticeBoard(SettingsStore settings)
{
    public const int MaxDismissed = 50;

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

    /// <summary>Hides a notice on the board; an unknown id is ignored. Only the newest <see cref="MaxDismissed"/> ids are kept.</summary>
    public void Dismiss(string id)
    {
        lock (_gate) if (!_notices.Any(n => n.Id == id)) return;
        settings.Update(s =>
        {
            if (s.DismissedNotices.Contains(id)) return;
            s.DismissedNotices.Add(id);
            if (s.DismissedNotices.Count > MaxDismissed)
                s.DismissedNotices.RemoveRange(0, s.DismissedNotices.Count - MaxDismissed);
        });
    }
}
