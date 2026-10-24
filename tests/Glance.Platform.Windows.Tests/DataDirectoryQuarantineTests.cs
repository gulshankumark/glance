using System.Security.Principal;
using System.Text.RegularExpressions;
using Glance.Platform.Windows;
using Glance.Testing;

namespace Glance.Platform.Windows.Tests;

/// <summary>
/// The takeover order and its resume. Most tests use <see cref="FakeEnforceSteps"/> (no permission is changed, so
/// they run unelevated) over real files: the moves and link deletions are real.
/// </summary>
public class DataDirectoryQuarantineTests
{
    private const string Pending = "quarantine-20261024T101010Z";
    private static readonly Regex QuarantineName = new(@"^quarantine-\d{8}T\d{6}Z$");

    private static readonly string[] Planted =
        new[] { "https.pfx", Path.Combine("keys", "key-1.xml"), Path.Combine("layouts", "x.json"), "settings.json" }.Order(StringComparer.Ordinal).ToArray();

    private static string NewRoot(TempDir dir) => Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;

    /// <summary>What a local user could leave behind before the service first secures the directory.</summary>
    private static void Plant(string root)
    {
        File.WriteAllText(Path.Combine(root, "settings.json"), """{"planted":true}""");
        Directory.CreateDirectory(Path.Combine(root, "keys"));
        File.WriteAllText(Path.Combine(root, "keys", "key-1.xml"), "<key/>");
        File.WriteAllText(Path.Combine(root, "https.pfx"), "pfx");
        Directory.CreateDirectory(Path.Combine(root, "layouts"));
        File.WriteAllText(Path.Combine(root, "layouts", "x.json"), "{}");
    }

    private static string[] Files(string dir) => Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
        .Select(f => Path.GetRelativePath(dir, f)).Order(StringComparer.Ordinal).ToArray();

    private static string[] Entries(string dir) => Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Takeover_writes_the_marker_then_locks_the_root_then_moves_hardens_and_clears()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        Plant(root);
        var log = new List<string>();
        var marker = new FakeMarker(log: log);

        var result = DataDirectoryAcl.Enforce(root, marker, new FakeEnforceSteps(FakeEnforceSteps.UserOwnedRoot, log));

        Assert.Equal(DataDirectoryAcl.Outcome.Quarantined, result.Outcome);
        var name = Path.GetFileName(result.QuarantineDir)!;
        Assert.Matches(QuarantineName, name);
        Assert.Equal(Path.Combine(root, name), result.QuarantineDir);
        Assert.Equal(new[] { name }, Entries(root));
        Assert.Equal(Planted, Files(result.QuarantineDir!));
        Assert.Null(marker.Value);
        // 1. marker, 2. the root alone, 4. the quarantine tree (every entry reset), 5. clear.
        Assert.Equal($"marker write {name}", log[0]);
        Assert.Equal("secure Root . (all)", log[1]);
        Assert.Equal($"secure Directory {name} (all)", log[2]);
        Assert.Contains($"secure File {Path.Combine(name, "keys", "key-1.xml")} (all)", log);
        Assert.Equal("marker clear", log[^1]);
    }

    [Fact]
    public void Marker_is_written_before_ownership_changes()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        Plant(root);
        var log = new List<string>();
        var marker = new FakeMarker(log: log);

        Assert.Throws<UnauthorizedAccessException>(() =>
            DataDirectoryAcl.Enforce(root, marker, new FakeEnforceSteps(FakeEnforceSteps.UserOwnedRoot, log, failOnRoot: true)));

        Assert.Matches(QuarantineName, marker.Value!);   // still set: the next start resumes
        Assert.Equal(new[] { $"marker write {marker.Value}", "secure Root . (all)" }, log);
        Assert.Equal(Planted, Files(root));                // nothing moved before the root was locked
    }

    [Fact]
    public void Crash_after_ownership_resumes_on_next_start()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        Plant(root);   // the root was already locked; the files are still where they were planted
        var log = new List<string>();
        var marker = new FakeMarker(Pending, log);

        var result = DataDirectoryAcl.Enforce(root, marker, new FakeEnforceSteps(FakeEnforceSteps.HardenedRoot, log));

        Assert.Equal(DataDirectoryAcl.Outcome.Quarantined, result.Outcome);
        Assert.Equal(Path.Combine(root, Pending), result.QuarantineDir);
        Assert.Equal(new[] { Pending }, Entries(root));
        Assert.Equal(Planted, Files(result.QuarantineDir!));
        Assert.Null(marker.Value);
        Assert.DoesNotContain(log, l => l.StartsWith("marker write"));   // resumed from step 2
        Assert.Equal("secure Root . (all)", log[0]);
    }

    [Fact]
    public void Crash_mid_move_resumes_into_same_folder()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        Plant(root);
        var q = Directory.CreateDirectory(Path.Combine(root, Pending)).FullName;
        File.Move(Path.Combine(root, "settings.json"), Path.Combine(q, "settings.json"));
        Directory.Move(Path.Combine(root, "keys"), Path.Combine(q, "keys"));
        var marker = new FakeMarker(Pending);

        var result = DataDirectoryAcl.Enforce(root, marker, new FakeEnforceSteps(FakeEnforceSteps.HardenedRoot));

        Assert.Equal(DataDirectoryAcl.Outcome.Quarantined, result.Outcome);
        Assert.Equal(new[] { Pending }, Entries(root));   // one folder only
        Assert.Equal(Planted, Files(q));
        Assert.Null(marker.Value);
    }

    [Fact]
    public void Existing_quarantine_folders_are_not_nested()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        Plant(root);
        const string old = "quarantine-20250101T000000Z";
        Directory.CreateDirectory(Path.Combine(root, old));
        File.WriteAllText(Path.Combine(root, old, "settings.json"), "{}");

        var result = DataDirectoryAcl.Enforce(root, new FakeMarker(), new FakeEnforceSteps(FakeEnforceSteps.UserOwnedRoot));

        var name = Path.GetFileName(result.QuarantineDir)!;
        Assert.NotEqual(old, name);
        Assert.Equal(new[] { old, name }.Order(StringComparer.Ordinal), Entries(root));
        Assert.Equal(new[] { "settings.json" }, Files(Path.Combine(root, old)));
        Assert.Equal(Planted, Files(result.QuarantineDir!));
    }

    [Fact]
    public void A_trusted_root_without_a_marker_is_not_quarantined()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        Plant(root);
        var marker = new FakeMarker();

        var result = DataDirectoryAcl.Enforce(root, marker, new FakeEnforceSteps(FakeEnforceSteps.HardenedRoot));

        Assert.Equal(DataDirectoryAcl.Outcome.AlreadySecure, result.Outcome);
        Assert.Null(result.QuarantineDir);
        Assert.Equal(Planted, Files(root));
        Assert.Null(marker.Value);
    }

    [Fact]
    public void A_name_taken_in_the_quarantine_folder_keeps_both_entries()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        var q = Directory.CreateDirectory(Path.Combine(root, Pending)).FullName;
        File.WriteAllText(Path.Combine(q, "settings.json"), "first");
        File.WriteAllText(Path.Combine(root, "settings.json"), "second");

        DataDirectoryAcl.Enforce(root, new FakeMarker(Pending), new FakeEnforceSteps(FakeEnforceSteps.HardenedRoot));

        Assert.Equal(new[] { Pending }, Entries(root));
        Assert.Equal("first", File.ReadAllText(Path.Combine(q, "settings.json")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(q, "settings.json.1")));
    }

    [Fact]
    public void Takeover_moves_a_planted_junction_as_the_link_and_then_deletes_it()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        var outside = Directory.CreateDirectory(Path.Combine(dir.Path, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        LinkFixtures.Junction(Path.Combine(root, "logs"), outside);

        var result = DataDirectoryAcl.Enforce(root, new FakeMarker(), new FakeEnforceSteps(FakeEnforceSteps.UserOwnedRoot));

        var name = Path.GetFileName(result.QuarantineDir)!;
        Assert.Contains($"{Path.Combine(name, "logs")}: link deleted", result.Actions);
        Assert.Empty(Entries(result.QuarantineDir!));
        Assert.Equal(new[] { "keep.txt" }, Entries(outside));   // the target was neither moved nor touched
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public void A_link_named_like_the_pending_folder_is_replaced_by_a_real_folder()
    {
        using var dir = new TempDir();
        var root = NewRoot(dir);
        Plant(root);
        var outside = Directory.CreateDirectory(Path.Combine(dir.Path, "outside")).FullName;
        LinkFixtures.Junction(Path.Combine(root, Pending), outside);

        var result = DataDirectoryAcl.Enforce(root, new FakeMarker(Pending), new FakeEnforceSteps(FakeEnforceSteps.HardenedRoot));

        Assert.Null(FileLinks.ReparseTag(result.QuarantineDir!));
        Assert.Equal(Planted, Files(result.QuarantineDir!));
        Assert.Empty(Entries(outside));                    // nothing was moved through the link
    }

    [Fact]
    public void MoveInto_moves_a_junction_itself_not_its_target()
    {
        using var dir = new TempDir();
        var outside = Directory.CreateDirectory(Path.Combine(dir.Path, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        var into = Directory.CreateDirectory(Path.Combine(dir.Path, "into")).FullName;
        var link = Path.Combine(dir.Path, "link");
        LinkFixtures.Junction(link, outside);

        using (var folder = FileLinks.OpenFolder(into))
            Assert.True(FileLinks.MoveInto(link, folder, "moved"));

        Assert.False(Directory.Exists(link));
        Assert.Equal(FileLinks.IO_REPARSE_TAG_MOUNT_POINT, FileLinks.ReparseTag(Path.Combine(into, "moved")));
        Assert.Equal(new[] { "keep.txt" }, Entries(outside));
    }

    [Fact]
    public void MoveInto_refuses_a_taken_name()
    {
        using var dir = new TempDir();
        var into = Directory.CreateDirectory(Path.Combine(dir.Path, "into")).FullName;
        File.WriteAllText(Path.Combine(into, "a.txt"), "first");
        var file = Path.Combine(dir.Path, "a.txt");
        File.WriteAllText(file, "second");

        using (var folder = FileLinks.OpenFolder(into))
            Assert.False(FileLinks.MoveInto(file, folder, "a.txt"));

        Assert.Equal("first", File.ReadAllText(Path.Combine(into, "a.txt")));
        Assert.Equal("second", File.ReadAllText(file));
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Takeover_with_planted_settings_quarantines_everything()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var root = NewRoot(dir);
        Plant(root);
        LinkFixtures.SetOwner(root, WindowsIdentity.GetCurrent().User!);   // as a local user who created it first
        var marker = new FakeMarker();

        var result = DataDirectoryAcl.Enforce(root, marker);

        Assert.Equal(DataDirectoryAcl.Outcome.Quarantined, result.Outcome);
        Assert.True(DataDirectoryAcl.IsHardened(new DirectoryInfo(root).GetAccessControl()));
        Assert.Equal(new[] { Path.GetFileName(result.QuarantineDir) }, Entries(root));
        Assert.Equal(Planted, Files(result.QuarantineDir!));
        Assert.Null(marker.Value);
        foreach (var entry in Directory.EnumerateFileSystemEntries(result.QuarantineDir!, "*", SearchOption.AllDirectories).Append(result.QuarantineDir!))
        {
            using var h = FileLinks.OpenSecurity(entry);
            var kind = Directory.Exists(entry) ? DataDirectoryAcl.EntryKind.Directory : DataDirectoryAcl.EntryKind.File;
            Assert.Null(DataDirectoryAcl.ReasonFor(h.ReadDescriptor(), kind));   // Administrators-owned, only what the root passes down
        }
        Assert.Equal(DataDirectoryAcl.Outcome.AlreadySecure, DataDirectoryAcl.Enforce(root, marker).Outcome);
    }
}
