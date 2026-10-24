using Glance.Platform.Windows;
using Glance.Testing;

namespace Glance.Platform.Windows.Tests;

public class UrlShortcutsTests
{
    [Fact]
    public void Rewrites_existing_shortcut_with_new_url()
    {
        using var dir = new TempDir();
        var p = Path.Combine(dir.Path, "Glance.url");
        File.WriteAllText(p, "[InternetShortcut]\r\nURL=http://localhost:1/\r\n");

        UrlShortcuts.RewriteExisting([p], new Uri("https://localhost:9443/"));

        var bytes = File.ReadAllBytes(p);
        Assert.Equal("[InternetShortcut]\r\nURL=https://localhost:9443/\r\n", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]); // no BOM
        Assert.Empty(Directory.GetFiles(dir.Path, ".*.tmp"));
    }

    [Fact]
    public void Does_not_create_missing_shortcut()
    {
        using var dir = new TempDir();
        var p = Path.Combine(dir.Path, "Glance.url");

        UrlShortcuts.RewriteExisting([p], new Uri("http://localhost:8735/"));

        Assert.False(File.Exists(p));
    }

    [Fact]
    public void Skips_shortcut_that_is_a_symlink()
    {
        using var dir = new TempDir();
        var target = Path.Combine(dir.Path, "target.txt");
        File.WriteAllText(target, "keep");
        var link = Path.Combine(dir.Path, "Glance.url");
        if (!LinkFixtures.TrySymlink(link, target, directory: false)) return; // no symlink privilege here

        UrlShortcuts.RewriteExisting([link], new Uri("http://localhost:8735/"));

        Assert.Equal("keep", File.ReadAllText(target));
        Assert.NotNull(FileLinks.ReparseTag(link));
    }
}
