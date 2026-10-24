using System.Security.AccessControl;
using System.Security.Principal;
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

    /// <summary>Owned by the test account (no elevation needed); only it has full control; nothing inherited.</summary>
    private static byte[] OnlyMe()
    {
        var me = WindowsIdentity.GetCurrent().User!;
        var dacl = new RawAcl(GenericAcl.AclRevision, 1);
        dacl.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, (int)FileSystemRights.FullControl, me, false, null));
        var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected | ControlFlags.SelfRelative,
            me, null, null, dacl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }

    [Fact]
    public void CreateNew_creates_the_file_with_its_descriptor_from_the_start_and_refuses_an_existing_name()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "a.txt");

        FileLinks.CreateNew(file, OnlyMe(), "hello"u8);

        Assert.Equal("hello", File.ReadAllText(file));
        var sddl = LinkFixtures.RawSddl(file);
        Assert.StartsWith($"O:{WindowsIdentity.GetCurrent().User!.Value}D:P", sddl);   // protected: nothing inherited
        Assert.Single(new RawSecurityDescriptor(sddl).DiscretionaryAcl!.Cast<GenericAce>());
        Assert.Throws<IOException>(() => FileLinks.CreateNew(file, OnlyMe(), "again"u8));
        Assert.Equal("hello", File.ReadAllText(file));
    }

    [Fact]
    public void MoveInto_with_replace_takes_the_place_of_an_existing_entry()
    {
        using var dir = new TempDir();
        var into = Directory.CreateDirectory(Path.Combine(dir.Path, "into")).FullName;
        File.WriteAllText(Path.Combine(into, "a.txt"), "old");
        var file = Path.Combine(dir.Path, "new.txt");
        File.WriteAllText(file, "new");

        using (var folder = FileLinks.OpenFolder(into))
            Assert.True(FileLinks.MoveInto(file, folder, "a.txt", replace: true));

        Assert.False(File.Exists(file));
        Assert.Equal("new", File.ReadAllText(Path.Combine(into, "a.txt")));
    }

    [Fact]
    public void OpenToRead_reads_content_and_descriptor_through_one_handle_and_is_null_when_missing()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(file, "abc");
        using (var entry = FileLinks.OpenToRead(file)!)
        {
            Assert.NotNull(entry.ReadDescriptor());
            var buffer = new byte[8];
            Assert.Equal(3, RandomAccess.Read(entry.Handle, buffer, 0));
        }
        Assert.Null(FileLinks.OpenToRead(Path.Combine(dir.Path, "gone.txt")));
    }
}
