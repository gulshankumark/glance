using Glance.Core.Layouts;
using Glance.Core.Notices;
using Glance.Platform.Windows;
using Serilog;

namespace Glance.Service;

/// <summary>Locks the data directory down at startup, before anything else reads or writes it.</summary>
public static class DataDirectoryStartup
{
    /// <summary>
    /// Opens the data directory at <paramref name="root"/>. When <paramref name="shouldEnforce"/>, the root is first
    /// created already hardened if missing (<paramref name="createHardened"/>), then <paramref name="enforce"/>d, and only
    /// then are its subfolders created: nothing is created as the service through a link planted at the root, and a
    /// root the service creates never carries %ProgramData%'s inherited access. Without enforcement only the folders
    /// are created, as before.
    /// </summary>
    public static (GlancePaths Paths, DataDirectoryAcl.AclResult? Result) Open(string root, bool shouldEnforce,
        ITakeoverMarker marker, Func<string, bool> createHardened, Func<string, ITakeoverMarker, DataDirectoryAcl.AclResult> enforce)
    {
        var result = Enforce(root, shouldEnforce, marker, (r, m) =>
        {
            createHardened(r);
            return enforce(r, m);
        });
        return (new GlancePaths(root), result);
    }

    /// <summary>
    /// Runs <paramref name="enforce"/> on the data directory, with <paramref name="marker"/> to resume an interrupted
    /// quarantine, when <paramref name="shouldEnforce"/>. A failure is written to the service file log (where the
    /// installer's "see the log" points) and rethrown unchanged, so the service still stops and the Event Log still
    /// records it.
    /// </summary>
    public static DataDirectoryAcl.AclResult? Enforce(string root, bool shouldEnforce, ITakeoverMarker marker,
        Func<string, ITakeoverMarker, DataDirectoryAcl.AclResult> enforce)
    {
        if (!shouldEnforce) return null;
        try { return enforce(root, marker); }
        catch (Exception ex)
        {
            LogFailure(root, ex);
            throw;
        }
    }

    /// <summary>
    /// After a quarantine, tells the user where their old files went and that settings were reset. The notice id is
    /// the quarantine folder's name, so dismissing it hides it for good while a later quarantine shows again.
    /// </summary>
    public static void AddQuarantineNotice(NoticeBoard board, DataDirectoryAcl.AclResult? result)
    {
        if (result is not { Outcome: DataDirectoryAcl.Outcome.Quarantined, QuarantineDir: { } folder }) return;
        board.Add(new Notice(Path.GetFileName(folder), "warning",
            $"Glance found files in its data folder it couldn't trust and moved them to {folder}. " +
            "Settings were reset; set your password again if you use network access."));
    }

    private static void LogFailure(string root, Exception failure)
    {
        // The directory could not be secured, so its log folder may hold links a local user planted: appending through
        // one would write to a file elsewhere with the service's rights. Only the Event Log gets the failure then.
        // Everything here is best effort: nothing it throws may replace the failure the caller rethrows.
        try
        {
            if (new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            var logs = Path.Combine(root, "logs");
            if (!DataDirectoryAcl.IsLinkFree(logs)) return;
            using var log = new LoggerConfiguration()
                .WriteTo.File(Path.Combine(logs, "glance-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();
            log.Fatal(failure, "The data directory {Root} could not be secured; Glance cannot start", root);
        }
        catch (Exception) { }   // whatever it is, the original failure is the one that matters
    }
}
