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

        Assert.NotEqual(DataDirectoryAcl.Outcome.AlreadySecure, DataDirectoryAcl.Enforce(root));

        Assert.True(DataDirectoryAcl.IsHardened(new DirectoryInfo(root).GetAccessControl()));
        Assert.False(Directory.Exists(link));          // the planted link is gone ...
        Assert.True(Directory.Exists(target.FullName)); // ... without touching what it pointed at
        var childRules = new DirectoryInfo(child.FullName).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        Assert.Empty(childRules);                      // only what the root passes down
        Assert.Equal(AdminsSid, new FileInfo(Path.Combine(child.FullName, "a.json")).GetAccessControl().GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(DataDirectoryAcl.Outcome.AlreadySecure, DataDirectoryAcl.Enforce(root));
    }
}
