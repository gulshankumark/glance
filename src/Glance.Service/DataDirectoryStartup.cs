using Glance.Core.Layouts;
using Glance.Platform.Windows;
using Serilog;

namespace Glance.Service;

/// <summary>Locks the data directory down at startup, before anything else reads or writes it.</summary>
public static class DataDirectoryStartup
{
    /// <summary>
    /// Runs <paramref name="enforce"/> on the data directory, with <paramref name="marker"/> to resume an interrupted
    /// quarantine, when <paramref name="shouldEnforce"/>. A failure is written to the service file log (where the
    /// installer's "see the log" points) and rethrown unchanged, so the service still stops and the Event Log still
    /// records it.
    /// </summary>
    public static DataDirectoryAcl.AclResult? Enforce(GlancePaths paths, bool shouldEnforce, ITakeoverMarker marker,
        Func<string, ITakeoverMarker, DataDirectoryAcl.AclResult> enforce)
    {
        if (!shouldEnforce) return null;
        try { return enforce(paths.Root, marker); }
        catch (Exception ex)
        {
            LogFailure(paths, ex);
            throw;
        }
    }

    private static void LogFailure(GlancePaths paths, Exception failure)
    {
        // The directory could not be secured, so its log folder may hold links a local user planted: appending through
        // one would write to a file elsewhere with the service's rights. Only the Event Log gets the failure then.
        // Everything here is best effort: nothing it throws may replace the failure the caller rethrows.
        try
        {
            if (new DirectoryInfo(paths.Root).Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            if (!DataDirectoryAcl.IsLinkFree(paths.Logs)) return;
            using var log = new LoggerConfiguration()
                .WriteTo.File(Path.Combine(paths.Logs, "glance-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();
            log.Fatal(failure, "The data directory {Root} could not be secured; Glance cannot start", paths.Root);
        }
        catch (Exception) { }   // whatever it is, the original failure is the one that matters
    }
}
