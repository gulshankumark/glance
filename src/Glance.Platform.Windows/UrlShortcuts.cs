using Glance.Core.Layouts;

namespace Glance.Platform.Windows;

/// <summary>Keeps existing <c>.url</c> shortcuts pointing at the current address. Never creates one.</summary>
public static class UrlShortcuts
{
    /// <summary>
    /// For each path that exists as an ordinary file, atomically writes <c>[InternetShortcut]\r\nURL=&lt;url&gt;\r\n</c>.
    /// Missing files stay missing (a deleted shortcut stays deleted). A shortcut that is a symlink or junction is
    /// skipped, never followed. I/O failures are reported through <paramref name="skipped"/> and swallowed.
    /// </summary>
    public static void RewriteExisting(IEnumerable<string> paths, Uri url, Action<string, string>? skipped = null)
    {
        var text = $"[InternetShortcut]\r\nURL={url.AbsoluteUri}\r\n";
        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                if (FileLinks.Read(path).IsNameLink) { skipped?.Invoke(path, "it is a link"); continue; }
                AtomicFile.Write(path, text); // UTF-8 without BOM; ASCII for any URL
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                skipped?.Invoke(path, e.Message);
            }
        }
    }
}
