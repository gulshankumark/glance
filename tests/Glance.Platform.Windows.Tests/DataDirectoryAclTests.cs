using System.Security.AccessControl;
using System.Security.Principal;
using Glance.Platform.Windows;
using Glance.Testing;

namespace Glance.Platform.Windows.Tests;

public class DataDirectoryAclTests
{
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdminsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    [Fact]
    public void Hardened_descriptor_is_admin_owned_protected_and_grants_only_system_and_administrators()
    {
        var s = DataDirectoryAcl.Hardened();
        Assert.Equal(AdminsSid, s.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(s.AreAccessRulesProtected);   // nothing inherited from %ProgramData%
        var rules = s.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        Assert.Equal(2, rules.Count);
        Assert.Equal(new[] { AdminsSid, SystemSid }.OrderBy(x => x.Value), rules.Select(r => (SecurityIdentifier)r.IdentityReference).OrderBy(x => x.Value));
        Assert.All(rules, r =>
        {
            Assert.Equal(AccessControlType.Allow, r.AccessControlType);
            Assert.Equal(FileSystemRights.FullControl, r.FileSystemRights);
            Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, r.InheritanceFlags);
        });
        Assert.True(DataDirectoryAcl.IsHardened(s));
    }

    [Fact]
    public void A_directory_with_inherited_user_access_is_not_hardened()
    {
        using var dir = new TempDir();
        var actual = new DirectoryInfo(dir.Path).GetAccessControl();
        Assert.False(DataDirectoryAcl.IsHardened(actual));   // owned by the test user, inherits from its parent
    }

    [Fact]
    public void Hardened_check_rejects_a_users_entry_or_a_non_admin_owner()
    {
        var withUsers = DataDirectoryAcl.Hardened();
        withUsers.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        Assert.False(DataDirectoryAcl.IsHardened(withUsers));

        var userOwned = DataDirectoryAcl.Hardened();
        userOwned.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
        Assert.False(DataDirectoryAcl.IsHardened(userOwned));

        var systemOwned = DataDirectoryAcl.Hardened();
        systemOwned.SetOwner(SystemSid);
        Assert.True(DataDirectoryAcl.IsHardened(systemOwned));
    }

    [Theory]
    [InlineData(false, true, true)]    // default %ProgramData%\Glance, service as LocalSystem/admin
    [InlineData(true, true, false)]    // GLANCE_DATA override (dev, tests): left alone
    [InlineData(false, false, false)]  // not elevated: cannot set an admin owner
    public void Enforced_only_for_the_default_data_dir_when_elevated(bool overridden, bool elevated, bool expected) =>
        Assert.Equal(expected, DataDirectoryAcl.ShouldEnforce(overridden, elevated));

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Enforce_takes_ownership_resets_children_and_removes_planted_links()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "Glance");
        var child = Directory.CreateDirectory(Path.Combine(root, "layouts"));
        File.WriteAllText(Path.Combine(child.FullName, "a.json"), "{}");
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var childSecurity = child.GetAccessControl();
        childSecurity.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.FullControl, AccessControlType.Allow));
        child.SetAccessControl(childSecurity);
        var target = Directory.CreateDirectory(Path.Combine(dir.Path, "elsewhere"));
        var link = Path.Combine(root, "logs");
        Directory.CreateSymbolicLink(link, target.FullName);

        Assert.NotEqual(DataDirectoryAcl.Outcome.AlreadySecure, DataDirectoryAcl.Enforce(root).Outcome);

        Assert.True(DataDirectoryAcl.IsHardened(new DirectoryInfo(root).GetAccessControl()));
        Assert.False(Directory.Exists(link));          // the planted link is gone ...
        Assert.True(Directory.Exists(target.FullName)); // ... without touching what it pointed at
        var childRules = new DirectoryInfo(child.FullName).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        Assert.Empty(childRules);                      // only what the root passes down
        Assert.Equal(AdminsSid, new FileInfo(Path.Combine(child.FullName, "a.json")).GetAccessControl().GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(DataDirectoryAcl.Outcome.AlreadySecure, DataDirectoryAcl.Enforce(root).Outcome);
    }

    private static DirectorySecurity CleanDir()
    {
        var s = new DirectorySecurity();
        s.SetOwner(AdminsSid);
        s.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
        return s;
    }

    [Fact]
    public void NeedsReset_is_false_for_a_trusted_owner_with_no_explicit_rules()
    {
        Assert.False(DataDirectoryAcl.NeedsReset(CleanDir()));
        var system = CleanDir();
        system.SetOwner(SystemSid);
        Assert.False(DataDirectoryAcl.NeedsReset(system));
    }

    [Fact]
    public void NeedsReset_is_true_for_an_entry_owned_by_someone_else()
    {
        var s = CleanDir();
        s.SetOwner(WindowsIdentity.GetCurrent().User!);
        if (s.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier o && (o == AdminsSid || o == SystemSid))
            s.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
        Assert.True(DataDirectoryAcl.NeedsReset(s));
    }

    [Fact]
    public void NeedsReset_is_true_for_an_explicit_rule_on_directories_and_files()
    {
        var s = CleanDir();
        s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        Assert.True(DataDirectoryAcl.NeedsReset(s));

        var f = new FileSecurity();
        f.SetOwner(AdminsSid);
        Assert.False(DataDirectoryAcl.NeedsReset(f));
        f.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.Read, AccessControlType.Allow));
        Assert.True(DataDirectoryAcl.NeedsReset(f));
    }

    [Fact]
    public void Hard_link_in_data_dir_is_deleted_and_target_acl_unchanged()
    {
        using var dir = new TempDir();
        var root = Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;
        var outside = Path.Combine(dir.Path, "outside.txt");   // stands in for a file elsewhere on the machine
        File.WriteAllText(outside, "keep");
        var before = LinkFixtures.Sddl(outside);
        var link = Path.Combine(root, "settings.json");
        LinkFixtures.HardLink(link, outside);
        var actions = new List<string>();
        var changed = false;

        Assert.True(DataDirectoryAcl.RemoveIfLink(new FileInfo(link), "settings.json", actions, ref changed));

        Assert.True(changed);
        Assert.False(File.Exists(link));
        Assert.Equal("keep", File.ReadAllText(outside));
        Assert.Equal(before, LinkFixtures.Sddl(outside));
        Assert.Contains("settings.json: hard link deleted", actions);
    }

    [Fact]
    public void Junction_to_outside_dir_is_removed_target_untouched()
    {
        using var dir = new TempDir();
        var root = Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(dir.Path, "Windows")).FullName;   // stands in for C:\Windows
        File.WriteAllText(Path.Combine(target, "system.ini"), "keep");
        var before = LinkFixtures.Sddl(target);
        var fileBefore = LinkFixtures.Sddl(Path.Combine(target, "system.ini"));
        var junction = Path.Combine(root, "logs");
        LinkFixtures.Junction(junction, target);
        var actions = new List<string>();
        var changed = false;

        Assert.True(DataDirectoryAcl.RemoveIfLink(new DirectoryInfo(junction), "logs", actions, ref changed));

        Assert.True(changed);
        Assert.False(Directory.Exists(junction));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "system.ini")));
        Assert.Equal(before, LinkFixtures.Sddl(target));
        Assert.Equal(fileBefore, LinkFixtures.Sddl(Path.Combine(target, "system.ini")));
        Assert.Contains("logs: link deleted", actions);
    }

    [Fact]
    public void Symlink_is_removed()
    {
        using var dir = new TempDir();
        var root = Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;
        var targetDir = Directory.CreateDirectory(Path.Combine(dir.Path, "elsewhere")).FullName;
        var targetFile = Path.Combine(targetDir, "a.txt");
        File.WriteAllText(targetFile, "keep");
        var dirLink = Path.Combine(root, "layouts");
        var fileLink = Path.Combine(root, "settings.json");
        if (!LinkFixtures.TrySymlink(dirLink, targetDir, directory: true) || !LinkFixtures.TrySymlink(fileLink, targetFile, directory: false))
            return;   // symlinks need SeCreateSymbolicLinkPrivilege or Developer Mode; the elevated run covers them
        var actions = new List<string>();
        var changed = false;

        Assert.True(DataDirectoryAcl.RemoveIfLink(new DirectoryInfo(dirLink), "layouts", actions, ref changed));
        Assert.True(DataDirectoryAcl.RemoveIfLink(new FileInfo(fileLink), "settings.json", actions, ref changed));

        Assert.True(changed);
        Assert.False(Directory.Exists(dirLink));
        Assert.False(File.Exists(fileLink));
        Assert.Equal("keep", File.ReadAllText(targetFile));
        Assert.Equal(new[] { "layouts: link deleted", "settings.json: link deleted" }, actions);
    }

    [Fact]
    public void Only_symlink_and_mount_point_tags_count_as_links()
    {
        Assert.True(new FileLinks.Info(1, FileLinks.IO_REPARSE_TAG_SYMLINK).IsNameLink);
        Assert.True(new FileLinks.Info(1, FileLinks.IO_REPARSE_TAG_MOUNT_POINT).IsNameLink);
        Assert.False(new FileLinks.Info(1, 0x80000017).IsNameLink);   // IO_REPARSE_TAG_WOF: a compressed ordinary file
        Assert.False(new FileLinks.Info(1, 0x9000301A).IsNameLink);   // a cloud-files placeholder
        Assert.False(new FileLinks.Info(1, null).IsNameLink);
    }

    [Fact]
    public void An_ordinary_directory_is_not_a_link()
    {
        using var dir = new TempDir();
        var sub = Directory.CreateDirectory(Path.Combine(dir.Path, "layouts"));
        var actions = new List<string>();
        var changed = false;
        Assert.False(DataDirectoryAcl.RemoveIfLink(sub, "layouts", actions, ref changed));
        Assert.False(changed);
        Assert.Empty(actions);
        Assert.True(sub.Exists);
    }

    private sealed class RecordingSteps : DataDirectoryAcl.IWalkSteps
    {
        public List<string> Calls { get; } = new();

        public void Secure(FileSystemInfo entry, string rel, DataDirectoryAcl.EntryKind kind, bool resetAll, List<string> actions, ref bool changed) =>
            Calls.Add($"secure {kind} {rel}");

        public bool RemoveIfLink(FileSystemInfo entry, string rel, List<string> actions, ref bool changed)
        {
            Calls.Add($"links {rel}");
            return DataDirectoryAcl.RemoveIfLink(entry, rel, actions, ref changed);
        }
    }

    [Fact]
    public void Walk_locks_each_directory_then_removes_its_links_then_handles_children()
    {
        using var dir = new TempDir();
        var root = Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(dir.Path, "outside")).FullName;
        var outsideFile = Path.Combine(outside, "a.txt");
        File.WriteAllText(outsideFile, "keep");
        File.WriteAllText(Path.Combine(root, "a.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "layouts", "deep"));
        File.WriteAllText(Path.Combine(root, "layouts", "c.json"), "{}");
        LinkFixtures.HardLink(Path.Combine(root, "layouts", "deep", "b.json"), outsideFile);
        LinkFixtures.Junction(Path.Combine(root, "logs"), outside);
        var steps = new RecordingSteps();
        var actions = new List<string>();
        var changed = false;

        DataDirectoryAcl.Walk(new DirectoryInfo(root), "", isRoot: true, resetAll: true, actions, ref changed, steps);

        var layouts = "layouts";
        var deep = Path.Combine("layouts", "deep");
        Assert.Equal(new[]
        {
            "secure Root .",                                            // 1. lock the directory
            "links a.json", "links layouts", "links logs",              // 2. links among its entries
            "secure File a.json",                                       // 3. remaining files
            $"secure Directory {layouts}",                              // 4. recurse: same order inside
            $"links {Path.Combine(layouts, "c.json")}", $"links {deep}",
            $"secure File {Path.Combine(layouts, "c.json")}",
            $"secure Directory {deep}",
            $"links {Path.Combine(deep, "b.json")}",
        }, steps.Calls);
        Assert.True(changed);
        Assert.Equal(new[] { Path.Combine(deep, "b.json") + ": hard link deleted", "logs: link deleted" }, actions.Order());
        Assert.False(File.Exists(Path.Combine(root, "layouts", "deep", "b.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "logs")));
        Assert.True(File.Exists(Path.Combine(root, "a.json")));
        Assert.Equal("keep", File.ReadAllText(outsideFile));
    }

    [Fact]
    public void Descriptor_written_by_handle_changes_only_that_directory()
    {
        using var dir = new TempDir();
        var target = Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;
        var child = Path.Combine(target, "settings.json");   // a by-name write would push the new entries into this
        File.WriteAllText(child, "{}");
        var childBefore = LinkFixtures.RawSddl(child);
        var me = WindowsIdentity.GetCurrent().User!;   // unelevated: we may only make ourselves the owner

        using (var h = FileLinks.OpenSecurity(target))
            h.WriteDescriptor(DataDirectoryAcl.Descriptor(DataDirectoryAcl.EntryKind.Root, me));

        var sddl = LinkFixtures.Sddl(target);
        Assert.StartsWith($"O:{me.Value}", sddl);
        Assert.Contains("D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", sddl);   // protected, inheritable, SYSTEM + Administrators only
        Assert.Equal(childBefore, LinkFixtures.RawSddl(child));   // the stored descriptor: nothing propagated

        // Clean up: as owner we may still rewrite the DACL.
        var restore = new DirectorySecurity();
        restore.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(target).SetAccessControl(restore);
    }

    [Fact]
    public void A_hardened_root_needs_no_reset()
    {
        var root = DataDirectoryAcl.Descriptor(DataDirectoryAcl.EntryKind.Root, AdminsSid);
        Assert.Null(DataDirectoryAcl.ReasonFor(root, DataDirectoryAcl.EntryKind.Root));
        var system = DataDirectoryAcl.Descriptor(DataDirectoryAcl.EntryKind.Root, SystemSid);
        Assert.Null(DataDirectoryAcl.ReasonFor(system, DataDirectoryAcl.EntryKind.Root));
        var mine = DataDirectoryAcl.Descriptor(DataDirectoryAcl.EntryKind.Root, WindowsIdentity.GetCurrent().User!);
        Assert.NotNull(DataDirectoryAcl.ReasonFor(mine, DataDirectoryAcl.EntryKind.Root));
        Assert.NotNull(DataDirectoryAcl.ReasonFor(null, DataDirectoryAcl.EntryKind.Root));
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Enforce_twice_is_AlreadySecure_the_second_time()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var root = Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "layouts"));
        File.WriteAllText(Path.Combine(root, "layouts", "a.json"), "{}");

        Assert.NotEqual(DataDirectoryAcl.Outcome.AlreadySecure, DataDirectoryAcl.Enforce(root).Outcome);
        var second = DataDirectoryAcl.Enforce(root);

        Assert.Equal(DataDirectoryAcl.Outcome.AlreadySecure, second.Outcome);
        Assert.Empty(second.Actions);
    }

    [Fact]
    public void Descriptors_below_the_root_carry_exactly_what_the_root_passes_down()
    {
        var dirSd = new RawSecurityDescriptor(DataDirectoryAcl.Descriptor(DataDirectoryAcl.EntryKind.Directory, AdminsSid), 0);
        var fileSd = new RawSecurityDescriptor(DataDirectoryAcl.Descriptor(DataDirectoryAcl.EntryKind.File, AdminsSid), 0);
        Assert.Equal("O:BAD:AI(A;OICIID;FA;;;SY)(A;OICIID;FA;;;BA)", dirSd.GetSddlForm(AccessControlSections.Owner | AccessControlSections.Access));
        Assert.Equal("O:BAD:AI(A;ID;FA;;;SY)(A;ID;FA;;;BA)", fileSd.GetSddlForm(AccessControlSections.Owner | AccessControlSections.Access));
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Links_are_removed_before_the_root_dacl_changes()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var root = Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;
        var outside = Path.Combine(dir.Path, "outside.txt");   // inherits from the temp dir: a root DACL change would reach it
        File.WriteAllText(outside, "keep");
        var before = LinkFixtures.Sddl(outside);
        LinkFixtures.HardLink(Path.Combine(root, "settings.json"), outside);

        var result = DataDirectoryAcl.Enforce(root);

        Assert.NotEqual(DataDirectoryAcl.Outcome.AlreadySecure, result.Outcome);
        Assert.Contains("settings.json: hard link deleted", result.Actions);
        Assert.False(File.Exists(Path.Combine(root, "settings.json")));
        Assert.Equal(before, LinkFixtures.Sddl(outside));
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Hard_link_in_user_writable_subdirectory_is_deleted_and_target_acl_unchanged()
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        using var dir = new TempDir();
        var root = Directory.CreateDirectory(Path.Combine(dir.Path, "Glance")).FullName;
        var sub = Directory.CreateDirectory(Path.Combine(root, "layouts"));
        var sec = sub.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        sub.SetAccessControl(sec);
        var outside = Path.Combine(dir.Path, "outside.txt");   // stands in for a file elsewhere on the machine
        File.WriteAllText(outside, "keep");
        var before = LinkFixtures.Sddl(outside);
        LinkFixtures.HardLink(Path.Combine(sub.FullName, "a.json"), outside);

        var result = DataDirectoryAcl.Enforce(root);

        Assert.Contains(Path.Combine("layouts", "a.json") + ": hard link deleted", result.Actions);
        Assert.False(File.Exists(Path.Combine(sub.FullName, "a.json")));
        Assert.Equal("keep", File.ReadAllText(outside));
        Assert.Equal(before, LinkFixtures.Sddl(outside));
        Assert.Empty(new DirectoryInfo(sub.FullName).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)));
    }

    [Fact]
    public void An_ordinary_file_is_not_a_link()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "a.json");
        File.WriteAllText(file, "{}");
        var actions = new List<string>();
        var changed = false;
        Assert.False(DataDirectoryAcl.RemoveIfLink(new FileInfo(file), "a.json", actions, ref changed));
        Assert.False(changed);
        Assert.Empty(actions);
        Assert.True(File.Exists(file));
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Hardened_root_with_hard_link_deletes_it_and_leaves_target_acl_alone()
    {
        string? outside = null;
        string before = "";
        var result = EnforceHardenedRootWith(r =>
        {
            outside = Path.Combine(Path.GetDirectoryName(r)!, "outside.txt");
            File.WriteAllText(outside, "keep");
            before = LinkFixtures.Sddl(outside);
            LinkFixtures.HardLink(Path.Combine(r, "planted.json"), outside);
        }, out var root, out var dir);
        using (dir)
        {
            Assert.Equal(DataDirectoryAcl.Outcome.Tightened, result.Outcome);
            Assert.Contains("planted.json: hard link deleted", result.Actions);
            Assert.False(File.Exists(Path.Combine(root, "planted.json")));
            Assert.Equal(before, LinkFixtures.Sddl(outside!));
        }
    }

    private static DataDirectoryAcl.AclResult EnforceHardenedRootWith(Action<string> arrange, out string root, out TempDir dir)
    {
        Assert.True(DataDirectoryAcl.IsElevated(), "run elevated");
        dir = new TempDir();
        root = Path.Combine(dir.Path, "Glance");
        Directory.CreateDirectory(root);
        Assert.Equal(DataDirectoryAcl.Outcome.Tightened, DataDirectoryAcl.Enforce(root).Outcome);   // harden the root first
        arrange(root);
        return DataDirectoryAcl.Enforce(root);
    }

    [Fact, Trait("Category", "Hardware")]   // needs an elevated test run (owner runs these)
    public void Hardened_root_with_child_owned_by_user_resets_child()
    {
        var result = EnforceHardenedRootWith(r =>
        {
            var f = Path.Combine(r, "settings.json");
            File.WriteAllText(f, "{}");
            var sec = new FileSecurity();
            sec.SetOwner(WindowsIdentity.GetCurrent().User!);
            new FileInfo(f).SetAccessControl(sec);
        }, out var root, out var dir);
        using (dir)
        {
            Assert.Equal(DataDirectoryAcl.Outcome.Tightened, result.Outcome);
            Assert.NotEmpty(result.Actions);
            var after = new FileInfo(Path.Combine(root, "settings.json")).GetAccessControl();
            Assert.Equal(AdminsSid, after.GetOwner(typeof(SecurityIdentifier)));
            Assert.Empty(after.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)));
        }
    }

    [Fact, Trait("Category", "Hardware")]
    public void Hardened_root_with_child_explicit_ace_resets_child()
    {
        var result = EnforceHardenedRootWith(r =>
        {
            var sub = Directory.CreateDirectory(Path.Combine(r, "layouts"));
            var sec = sub.GetAccessControl();
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));
            sub.SetAccessControl(sec);
        }, out var root, out var dir);
        using (dir)
        {
            Assert.Equal(DataDirectoryAcl.Outcome.Tightened, result.Outcome);
            Assert.Contains(result.Actions, a => a.Contains("layouts"));
            Assert.Empty(new DirectoryInfo(Path.Combine(root, "layouts")).GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)));
        }
    }

    [Fact, Trait("Category", "Hardware")]
    public void Hardened_root_with_unreadable_child_resets_it_instead_of_throwing()
    {
        var me = WindowsIdentity.GetCurrent().User!;
        var result = EnforceHardenedRootWith(r =>
        {
            var sub = Directory.CreateDirectory(Path.Combine(r, "planted"));
            var sec = new DirectorySecurity();
            sec.SetOwner(me);
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            sec.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            sub.SetAccessControl(sec);   // only the current user; SYSTEM/Administrators cannot even read it
        }, out var root, out var dir);
        using (dir)
        {
            Assert.Equal(DataDirectoryAcl.Outcome.Tightened, result.Outcome);
            Assert.Contains(result.Actions, a => a.Contains("planted"));
            var after = new DirectoryInfo(Path.Combine(root, "planted")).GetAccessControl();
            Assert.Equal(AdminsSid, after.GetOwner(typeof(SecurityIdentifier)));
            Assert.Empty(after.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)));
        }
    }

    [Fact, Trait("Category", "Hardware")]
    public void Hardened_root_with_only_a_link_is_Tightened_deletes_link_and_leaves_target_alone()
    {
        string? target = null;
        string? link = null;
        string before = "";
        var result = EnforceHardenedRootWith(r =>
        {
            target = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(r)!, "elsewhere")).FullName;
            before = new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            link = Path.Combine(r, "logs");
            Directory.CreateSymbolicLink(link, target);
        }, out _, out var dir);
        using (dir)
        {
            Assert.Equal(DataDirectoryAcl.Outcome.Tightened, result.Outcome);
            Assert.Contains("logs: link deleted", result.Actions);
            Assert.False(Directory.Exists(link));
            Assert.True(Directory.Exists(target));
            Assert.Equal(before, new DirectoryInfo(target!).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All));
        }
    }

    [Fact, Trait("Category", "Hardware")]
    public void Hardened_root_with_clean_children_is_AlreadySecure()
    {
        var result = EnforceHardenedRootWith(r =>
        {
            Directory.CreateDirectory(Path.Combine(r, "layouts"));
            File.WriteAllText(Path.Combine(r, "layouts", "a.json"), "{}");
        }, out _, out var dir);
        using (dir)
        {
            Assert.Equal(DataDirectoryAcl.Outcome.AlreadySecure, result.Outcome);
            Assert.Empty(result.Actions);
        }
    }
}
