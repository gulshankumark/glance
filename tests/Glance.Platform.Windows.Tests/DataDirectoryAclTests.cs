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
    public void Hardened_root_with_only_a_link_is_AlreadySecure_and_leaves_link_and_target_alone()
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
            Assert.Equal(DataDirectoryAcl.Outcome.AlreadySecure, result.Outcome);
            Assert.Contains(result.Actions, a => a.Contains("logs"));
            Assert.True(Directory.Exists(link));
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
