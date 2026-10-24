using Glance.Platform.Windows;
using Glance.Testing;

namespace Glance.Platform.Windows.Tests;

public class FileLinksTests
{
    [Fact]
    public void LinkCount_and_ReparseTag_report_correctly()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(file, "x");
        Assert.Equal(1u, FileLinks.LinkCount(file));
        Assert.Null(FileLinks.ReparseTag(file));
        Assert.Equal(1u, FileLinks.LinkCount(dir.Path));
        Assert.Null(FileLinks.ReparseTag(dir.Path));

        var second = Path.Combine(dir.Path, "b.txt");
        LinkFixtures.HardLink(second, file);
        Assert.Equal(2u, FileLinks.LinkCount(file));
        Assert.Equal(2u, FileLinks.LinkCount(second));
        Assert.Null(FileLinks.ReparseTag(second));

        var target = Directory.CreateDirectory(Path.Combine(dir.Path, "target")).FullName;
        var junction = Path.Combine(dir.Path, "junction");
        LinkFixtures.Junction(junction, target);
        Assert.Equal(FileLinks.IO_REPARSE_TAG_MOUNT_POINT, FileLinks.ReparseTag(junction));   // the link, not its target
        Assert.Null(FileLinks.ReparseTag(target));
    }

    [Fact]
    public void Symlink_reports_the_symlink_tag()
    {
        using var dir = new TempDir();
        var target = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(target, "x");
        var link = Path.Combine(dir.Path, "link.txt");
        if (!LinkFixtures.TrySymlink(link, target, directory: false)) return;   // needs SeCreateSymbolicLink or Developer Mode
        Assert.Equal(FileLinks.IO_REPARSE_TAG_SYMLINK, FileLinks.ReparseTag(link));
        Assert.Null(FileLinks.ReparseTag(target));
    }

    [Fact]
    public void DeleteIf_removes_only_the_hard_link_name()
    {
        using var dir = new TempDir();
        var outside = Path.Combine(dir.Path, "outside.txt");
        File.WriteAllText(outside, "keep");
        File.SetAttributes(outside, FileAttributes.ReadOnly);   // shared by both names: must not need clearing
        var link = Path.Combine(dir.Path, "link.txt");
        LinkFixtures.HardLink(link, outside);

        var info = FileLinks.DeleteIf(link, i => i.IsHardLink);

        Assert.True(info!.Value.IsHardLink);
        Assert.False(File.Exists(link));
        Assert.Equal("keep", File.ReadAllText(outside));
        Assert.Equal(1u, FileLinks.LinkCount(outside));
        Assert.True(File.GetAttributes(outside).HasFlag(FileAttributes.ReadOnly));
        File.SetAttributes(outside, FileAttributes.Normal);
    }

    [Fact]
    public void DeleteIf_keeps_an_entry_the_predicate_rejects_and_reports_a_missing_one_as_null()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(file, "x");
        Assert.False(FileLinks.DeleteIf(file, i => i.IsHardLink)!.Value.IsHardLink);
        Assert.True(File.Exists(file));
        Assert.Null(FileLinks.DeleteIf(Path.Combine(dir.Path, "gone.txt"), _ => true));
    }
}
