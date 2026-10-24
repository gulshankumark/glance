using System.Security.AccessControl;
using System.Security.Principal;
using Glance.Platform.Windows;
using Glance.Testing;

namespace Glance.Platform.Windows.Tests;

public class FileTakeoverMarkerTests
{
    private const string Name = "quarantine-20261024T101010Z";
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdminsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    private static RawSecurityDescriptor Raw(string path)
    {
        using var h = FileLinks.OpenSecurity(path);
        return new RawSecurityDescriptor(h.ReadDescriptor()!, 0);
    }

    [Fact]
    public void The_marker_sits_next_to_the_data_directory()
    {
        using var dir = new TempDir();
        Assert.Equal(Path.Combine(dir.Path, "Glance.takeover-pending"), new FileTakeoverMarker(dir.Path).Path);
    }

    [Fact]
    public void A_missing_marker_reads_as_nothing_pending_and_clears_quietly()
    {
        using var dir = new TempDir();
        var warnings = new List<string>();
        var marker = new FileTakeoverMarker(dir.Path, warnings.Add);
        Assert.Null(marker.Read());
        marker.Clear();
        Assert.Empty(warnings);
    }

    [Fact]   // runs unelevated too: the test account owns the file
    public void File_marker_owned_by_user_is_ignored_and_deleted()
    {
        using var dir = new TempDir();
        var warnings = new List<string>();
        var marker = new FileTakeoverMarker(dir.Path, warnings.Add);
        File.WriteAllText(marker.Path, Name);   // the right content, from the wrong owner
        LinkFixtures.SetOwner(marker.Path, Me);

        Assert.Null(marker.Read());

        Assert.False(File.Exists(marker.Path));
        Assert.Contains("owned by", Assert.Single(warnings));
    }

    [Fact]   // runs unelevated too: the test account owns the file
    public void A_planted_marker_held_open_by_its_planter_is_ignored_without_blocking_startup()
    {
        using var dir = new TempDir();
        var warnings = new List<string>();
        var marker = new FileTakeoverMarker(dir.Path, warnings.Add);
        File.WriteAllText(marker.Path, Name);
        LinkFixtures.SetOwner(marker.Path, Me);
        using (new FileStream(marker.Path, FileMode.Open, FileAccess.Read, FileShare.None))   // any user can hold it so
        {
            Assert.Null(marker.Read());
        }

        var warning = Assert.Single(warnings);
        Assert.StartsWith($"Ignored the takeover marker {marker.Path}", warning);
        Assert.Contains("owned by", warning);
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void A_genuine_marker_that_cannot_be_read_is_kept_and_the_read_throws()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var warnings = new List<string>();
        var marker = new FileTakeoverMarker(dir.Path, warnings.Add);
        marker.Write(Name);
        using (new FileStream(marker.Path, FileMode.Open, FileAccess.Read, FileShare.None))   // e.g. AV scanning at boot
        {
            Assert.ThrowsAny<IOException>(() => marker.Read());
        }

        Assert.True(File.Exists(marker.Path));   // a genuine marker survives a transient error
        Assert.Equal(Name, File.ReadAllText(marker.Path));
        Assert.Empty(warnings);
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void File_marker_round_trips_with_hardened_acl()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var warnings = new List<string>();
        var marker = new FileTakeoverMarker(dir.Path, warnings.Add);

        marker.Write(Name);

        Assert.Equal(new[] { marker.Path }, Directory.GetFiles(dir.Path));   // no temp file left behind
        Assert.Equal(Name, File.ReadAllText(marker.Path));
        var sd = Raw(marker.Path);
        Assert.Equal(AdminsSid, sd.Owner);
        Assert.True(sd.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected));
        var aces = sd.DiscretionaryAcl!.Cast<CommonAce>().ToList();
        Assert.Equal(new[] { AdminsSid, SystemSid }.OrderBy(s => s.Value), aces.Select(a => a.SecurityIdentifier).OrderBy(s => s.Value));
        Assert.All(aces, a =>
        {
            Assert.Equal(AceQualifier.AccessAllowed, a.AceQualifier);
            Assert.Equal((int)FileSystemRights.FullControl, a.AccessMask);
            Assert.Equal(AceFlags.None, a.AceFlags);
        });
        Assert.Equal(Name, marker.Read());

        marker.Clear();

        Assert.False(File.Exists(marker.Path));
        Assert.Null(marker.Read());
        marker.Clear();   // nothing there: fine
        Assert.Empty(warnings);
    }

    [Theory, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    [InlineData("quarantine-2026")]
    [InlineData("quarantine-20261024T101010Z\r\n")]
    [InlineData(@"..\..\Windows")]
    [InlineData("quarantine-٢٠٢٦١٠٢٤T101010Z")]   // digits, but not ASCII ones
    public void File_marker_with_bad_content_is_ignored(string content)
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var warnings = new List<string>();
        var marker = new FileTakeoverMarker(dir.Path, warnings.Add);
        marker.Write(Name);
        File.WriteAllText(marker.Path, content);   // Administrators may write it; its ACL stays

        Assert.Null(marker.Read());

        Assert.False(File.Exists(marker.Path));
        Assert.Contains("content", Assert.Single(warnings));
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void File_marker_writable_by_users_is_ignored()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var warnings = new List<string>();
        var marker = new FileTakeoverMarker(dir.Path, warnings.Add);
        marker.Write(Name);
        var info = new FileInfo(marker.Path);
        var sec = info.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write, AccessControlType.Allow));
        info.SetAccessControl(sec);

        Assert.Null(marker.Read());

        Assert.False(File.Exists(marker.Path));
        Assert.Single(warnings);
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void File_marker_that_is_a_symlink_is_ignored()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var elsewhere = Directory.CreateDirectory(Path.Combine(dir.Path, "elsewhere")).FullName;
        var trusted = new FileTakeoverMarker(elsewhere);
        trusted.Write(Name);   // a genuine, trusted marker file ...
        var parent = Directory.CreateDirectory(Path.Combine(dir.Path, "parent")).FullName;
        var warnings = new List<string>();
        var marker = new FileTakeoverMarker(parent, warnings.Add);
        File.CreateSymbolicLink(marker.Path, trusted.Path);   // ... reached through a link

        Assert.Null(marker.Read());

        Assert.False(new FileInfo(marker.Path).Exists);   // the link is gone ...
        Assert.Equal(Name, File.ReadAllText(trusted.Path));   // ... its target is not
        Assert.Single(warnings);
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Write_replaces_a_preplanted_user_file()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var marker = new FileTakeoverMarker(dir.Path);
        File.WriteAllText(marker.Path, "quarantine-20200101T000000Z");
        LinkFixtures.SetOwner(marker.Path, Me);

        marker.Write(Name);

        Assert.Equal(AdminsSid, Raw(marker.Path).Owner);
        Assert.Equal(Name, marker.Read());
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Write_replaces_a_planted_symlink_and_leaves_its_target_alone()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var victim = Path.Combine(dir.Path, "victim.txt");
        File.WriteAllText(victim, "keep");
        var before = LinkFixtures.Sddl(victim);
        var parent = Directory.CreateDirectory(Path.Combine(dir.Path, "parent")).FullName;
        var marker = new FileTakeoverMarker(parent);
        File.CreateSymbolicLink(marker.Path, victim);

        marker.Write(Name);

        Assert.Equal("keep", File.ReadAllText(victim));
        Assert.Equal(before, LinkFixtures.Sddl(victim));
        Assert.Null(FileLinks.ReparseTag(marker.Path));
        Assert.Equal(Name, marker.Read());
    }
}
