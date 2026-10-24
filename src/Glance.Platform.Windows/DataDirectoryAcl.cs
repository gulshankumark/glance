using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Glance.Platform.Windows;

/// <summary>
/// Locks the service's data directory down. %ProgramData% lets every local user read and create files in new
/// subfolders, which would expose settings.json (password hash), kestrel.json, https.pfx and keys/, and let a user
/// plant files or links where a LocalSystem service writes. The hardened directory is owned by Administrators and
/// its DACL is protected (nothing inherited) and grants full control to SYSTEM and Administrators only; everything
/// inside inherits that.
/// </summary>
public static partial class DataDirectoryAcl
{
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdminsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public enum Outcome
    {
        /// <summary>Already owned by SYSTEM/Administrators with the hardened DACL; nothing touched.</summary>
        AlreadySecure,
        /// <summary>The owner was fine; the DACL (and everything inside) was reset.</summary>
        Tightened,
        /// <summary>Someone else owned the directory: ownership was taken and everything inside reset (log a warning).</summary>
        OwnershipTaken,
        /// <summary>Reserved: the tree was moved aside and rebuilt (not produced yet).</summary>
        Quarantined,
    }

    /// <summary>What <see cref="Enforce"/> did. <paramref name="Actions"/> are human-readable lines for the log.</summary>
    public sealed record AclResult(Outcome Outcome, string? QuarantineDir, IReadOnlyList<string> Actions);

    /// <summary>Only the default %ProgramData%\Glance, and only when elevated (only an admin can set an admin owner).</summary>
    public static bool ShouldEnforce(bool dataDirOverridden, bool elevated) => !dataDirOverridden && elevated;

    /// <summary>Running as LocalSystem or with an elevated administrator token.</summary>
    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return id.IsSystem || new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>The security descriptor the data directory gets: Administrators owner, SYSTEM + Administrators full, nothing inherited.</summary>
    public static DirectorySecurity Hardened()
    {
        var s = new DirectorySecurity();
        s.SetOwner(AdminsSid);
        s.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { SystemSid, AdminsSid })
            s.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return s;
    }

    /// <summary>
    /// True when <paramref name="s"/> is owned by SYSTEM or Administrators, is protected from inheritance and has no
    /// entry for anyone but SYSTEM and Administrators, both with full control passed down to everything inside.
    /// </summary>
    public static bool IsHardened(DirectorySecurity s)
    {
        if (!IsTrustedOwner(s.GetOwner(typeof(SecurityIdentifier))) || !s.AreAccessRulesProtected) return false;
        var full = new HashSet<SecurityIdentifier>();
        foreach (FileSystemAccessRule r in s.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            if (r.IdentityReference is not SecurityIdentifier sid || (sid != SystemSid && sid != AdminsSid)) return false;
            if (r.AccessControlType == AccessControlType.Allow && r.FileSystemRights.HasFlag(FileSystemRights.FullControl)
                && r.PropagationFlags == PropagationFlags.None
                && r.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit))
                full.Add(sid);
        }
        return full.Count == 2;
    }

    /// <summary>
    /// Makes <paramref name="root"/> hardened (see <see cref="Hardened"/>). When it was not already, everything inside
    /// is reset too: owned by Administrators and explicit entries dropped so only the root's are inherited, since any
    /// local user could have planted them before.
    /// When the root already is hardened, its contents are walked without following links: every entry owned by someone
    /// else, carrying explicit entries or with an unreadable descriptor is reset (the result is then Tightened).
    /// Either way links are deleted first, before any DACL changes, and are never followed or re-permissioned (see
    /// <see cref="RemoveLinks"/>); a deletion makes the result Tightened.
    /// Throws when the directory cannot be secured: the caller must not go on using it.
    /// </summary>
    public static AclResult Enforce(string root)
    {
        var dir = new DirectoryInfo(root);
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException($"The data directory {root} is a link; refusing to use it.");
        var current = dir.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        var actions = new List<string>();
        if (IsHardened(current))
        {
            // The root can be fine while an interrupted earlier reset left children behind that anyone could have planted.
            Privileges.Enable("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");
            var changed = false;
            RemoveLinks(dir, "", actions, ref changed);   // before any child's DACL changes: that propagates downwards
            ResetUntrustedChildren(dir, "", actions, ref changed);
            return new AclResult(changed ? Outcome.Tightened : Outcome.AlreadySecure, null, actions);
        }
        var ownerTrusted = IsTrustedOwner(current.GetOwner(typeof(SecurityIdentifier)));

        // To set an owner on objects we cannot open otherwise, and to read link counts whatever a planted DACL says.
        Privileges.Enable("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");
        var removed = false;
        // Links go before the root DACL changes: a new DACL propagates to the children, a hard-linked file among them.
        RemoveLinks(dir, "", actions, ref removed);
        dir.SetAccessControl(OwnerOnly<DirectorySecurity>());   // owner first: the old DACL may not let us write a new one
        dir.SetAccessControl(Hardened());
        ResetChildren(dir, "", actions, ref removed);
        actions.Add("reset the data directory and everything inside it");
        return new AclResult(ownerTrusted ? Outcome.Tightened : Outcome.OwnershipTaken, null, actions);
    }

    /// <summary>
    /// True when an entry must be reset: not owned by SYSTEM/Administrators, or carrying an explicit (non-inherited)
    /// access rule. Pure: reads the descriptor only.
    /// </summary>
    internal static bool NeedsReset(FileSystemSecurity sec) => ResetReason(sec) is not null;

    private static string? ResetReason(FileSystemSecurity sec)
    {
        if (!IsTrustedOwner(sec.GetOwner(typeof(SecurityIdentifier)))) return "owner is not SYSTEM/Administrators";
        if (sec.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).Count > 0)
            return "has explicit access entries";
        return null;
    }

    /// <summary>
    /// Walks without following links; resets only the entries that need it (including ones whose descriptor cannot be
    /// read: a planted DACL can deny even SYSTEM). Links are deleted first and count as a change.
    /// </summary>
    private static void ResetUntrustedChildren(DirectoryInfo dir, string relative, List<string> actions, ref bool changed)
    {
        foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }).ToList())
        {
            var rel = relative.Length == 0 ? entry.Name : Path.Combine(relative, entry.Name);
            if (RemoveIfLink(entry, rel, actions, ref changed)) continue;
            if (entry is DirectoryInfo sub)
            {
                var reason = ReasonFor(() => sub.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access));
                if (reason is not null)
                {
                    sub.SetAccessControl(OwnerOnly<DirectorySecurity>());
                    sub.SetAccessControl(InheritOnly<DirectorySecurity>());
                    actions.Add($"{rel}: reset ({reason})");
                    changed = true;
                }
                ResetUntrustedChildren(sub, rel, actions, ref changed);
            }
            else if (entry is FileInfo file)
            {
                var reason = ReasonFor(() => file.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access));
                if (reason is null) continue;
                file.SetAccessControl(OwnerOnly<FileSecurity>());
                file.SetAccessControl(InheritOnly<FileSecurity>());
                actions.Add($"{rel}: reset ({reason})");
                changed = true;
            }
        }
    }

    /// <summary>
    /// Deletes every link under <paramref name="dir"/> (see <see cref="RemoveIfLink"/>) before any ACL there changes,
    /// since setting a directory's DACL propagates inherited entries into its children. A directory that cannot be
    /// listed yet (a planted DACL) is skipped with an action: the ACL walk, which checks every entry for links again,
    /// covers it once it has been reset.
    /// </summary>
    internal static void RemoveLinks(DirectoryInfo dir, string relative, List<string> actions, ref bool changed)
    {
        List<FileSystemInfo> entries;
        try { entries = dir.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }).ToList(); }
        catch (UnauthorizedAccessException)
        {
            actions.Add($"{(relative.Length == 0 ? "." : relative)}: not listable before the reset; its links are removed during it");
            return;
        }
        foreach (var entry in entries)
        {
            var rel = relative.Length == 0 ? entry.Name : Path.Combine(relative, entry.Name);
            if (RemoveIfLink(entry, rel, actions, ref changed)) continue;
            if (entry is DirectoryInfo sub) RemoveLinks(sub, rel, actions, ref changed);
        }
    }

    /// <summary>
    /// Deletes <paramref name="entry"/> when it is a symbolic link or junction, or a hard link (a file with more than
    /// one name): following the one or re-permissioning the other would change something outside the data directory.
    /// Any other reparse point (a compressed or cloud placeholder file, say) is not followed, not recursed into and
    /// left alone: .NET reads and writes ACLs by name, which cannot be guaranteed not to reach through it. Every entry
    /// is read, and deleted, through one handle that never follows a link, whatever the enumeration reported.
    /// True when the caller must skip the entry (deleted, left alone, or gone already).
    /// </summary>
    internal static bool RemoveIfLink(FileSystemInfo entry, string rel, List<string> actions, ref bool changed)
    {
        var info = FileLinks.DeleteIf(entry.FullName, i => i.IsNameLink || i.IsHardLink);
        if (info is not { } i) return true;
        if (i.IsNameLink || i.IsHardLink)
        {
            actions.Add(i.IsNameLink ? $"{rel}: link deleted" : $"{rel}: hard link deleted");
            changed = true;
            return true;
        }
        if (i.ReparseTag is { } tag)
        {
            actions.Add($"{rel}: reparse point (tag 0x{tag:X8}) left alone (not followed)");
            return true;
        }
        return false;
    }

    private static string? ReasonFor(Func<FileSystemSecurity> read)
    {
        try { return ResetReason(read()); }
        catch (UnauthorizedAccessException) { return "security descriptor unreadable"; }
    }

    private static bool IsTrustedOwner(IdentityReference? owner) => owner is SecurityIdentifier sid && (sid == SystemSid || sid == AdminsSid);

    private static void ResetChildren(DirectoryInfo dir, string relative, List<string> actions, ref bool removed)
    {
        foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }).ToList())
        {
            var rel = relative.Length == 0 ? entry.Name : Path.Combine(relative, entry.Name);
            if (RemoveIfLink(entry, rel, actions, ref removed)) continue;
            if (entry is DirectoryInfo sub)
            {
                sub.SetAccessControl(OwnerOnly<DirectorySecurity>());
                sub.SetAccessControl(InheritOnly<DirectorySecurity>());
                ResetChildren(sub, rel, actions, ref removed);
            }
            else if (entry is FileInfo file)
            {
                file.SetAccessControl(OwnerOnly<FileSecurity>());
                file.SetAccessControl(InheritOnly<FileSecurity>());
            }
        }
    }

    private static T OwnerOnly<T>() where T : FileSystemSecurity, new()
    {
        var s = new T();
        s.SetOwner(AdminsSid);
        return s;
    }

    /// <summary>No explicit entries and not protected: the object gets exactly what its parent passes down.</summary>
    private static T InheritOnly<T>() where T : FileSystemSecurity, new()
    {
        var s = new T();
        s.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
        return s;
    }

    private static partial class Privileges
    {
        private const uint SePrivilegeEnabled = 0x2;
        private const uint TokenAdjustPrivileges = 0x20, TokenQuery = 0x8;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]   // TOKEN_PRIVILEGES with one LUID_AND_ATTRIBUTES
        private struct TokenPrivilege
        {
            public uint Count;
            public long Luid;
            public uint Attributes;
        }

        /// <summary>Best effort: a privilege the token lacks stays off, and the ACL call that needed it reports the failure.</summary>
        public static void Enable(params string[] names)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token)) return;
            try
            {
                foreach (var name in names)
                {
                    if (!LookupPrivilegeValueW(null, name, out var luid)) continue;
                    var tp = new TokenPrivilege { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
                    AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally { CloseHandle(token); }
        }

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool LookupPrivilegeValueW(string? system, string name, out long luid);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
            ref TokenPrivilege newState, uint length, IntPtr previous, IntPtr returnLength);

        [LibraryImport("kernel32.dll")]
        private static partial IntPtr GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(IntPtr handle);
    }
}
