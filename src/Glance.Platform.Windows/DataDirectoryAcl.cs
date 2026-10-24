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
    /// When the root already is hardened, only entries owned by someone else, carrying explicit entries or with an
    /// unreadable descriptor are reset (the result is then Tightened).
    /// Either way the tree is walked by <see cref="Walk"/>: each directory is locked by handle before its contents are
    /// looked at, links are deleted (never followed or re-permissioned; a deletion makes the result Tightened) and no
    /// descriptor change propagates.
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
            Walk(dir, "", isRoot: true, resetAll: false, actions, ref changed, HandleSteps.Instance);
            return new AclResult(changed ? Outcome.Tightened : Outcome.AlreadySecure, null, actions);
        }
        var ownerTrusted = IsTrustedOwner(current.GetOwner(typeof(SecurityIdentifier)));

        // To set an owner on objects we cannot open otherwise, and to read link counts whatever a planted DACL says.
        Privileges.Enable("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");
        var removed = false;
        Walk(dir, "", isRoot: true, resetAll: true, actions, ref removed, HandleSteps.Instance);
        actions.Add("reset the data directory and everything inside it");
        return new AclResult(ownerTrusted ? Outcome.Tightened : Outcome.OwnershipTaken, null, actions);
    }

    /// <summary>
    /// True when writing to files in <paramref name="dir"/> cannot reach outside it through a link: the directory is
    /// no reparse point and holds no reparse points or hard links. False as well when that cannot be read.
    /// </summary>
    public static bool IsLinkFree(string dir)
    {
        try
        {
            if (FileLinks.Read(dir).ReparseTag is not null) return false;
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir, "*", new EnumerationOptions { AttributesToSkip = 0 }))
            {
                var info = FileLinks.Read(entry);
                if (info.ReparseTag is not null || info.IsHardLink) return false;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
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

    /// <summary>The steps of <see cref="Walk"/>; a seam so tests can watch their order.</summary>
    internal interface IWalkSteps
    {
        /// <summary>Resets the entry's owner and DACL when <paramref name="resetAll"/> or when it needs it.</summary>
        void Secure(FileSystemInfo entry, string rel, EntryKind kind, bool resetAll, List<string> actions, ref bool changed);

        /// <summary>See <see cref="DataDirectoryAcl.RemoveIfLink"/>.</summary>
        bool RemoveIfLink(FileSystemInfo entry, string rel, List<string> actions, ref bool changed);
    }

    internal enum EntryKind { Root, Directory, File }

    /// <summary>
    /// For each directory, the root first: (1) lock it through its own handle, so no untrusted user can add entries
    /// from then on; (2) delete the links among its entries; (3) reset the remaining files through their own
    /// handles; (4) recurse into the remaining subdirectories. Every descriptor is written by handle to that object
    /// only, so a change never propagates to an entry that has not been checked yet (a hard link planted meanwhile,
    /// say). With <paramref name="resetAll"/> every entry is reset; otherwise only those owned by someone else,
    /// carrying explicit entries or with an unreadable descriptor, each recorded in the actions. Any reset or deletion
    /// sets <paramref name="changed"/>.
    /// </summary>
    internal static void Walk(DirectoryInfo dir, string relative, bool isRoot, bool resetAll, List<string> actions, ref bool changed, IWalkSteps steps)
    {
        steps.Secure(dir, relative.Length == 0 ? "." : relative, isRoot ? EntryKind.Root : EntryKind.Directory, resetAll, actions, ref changed);
        var remaining = new List<(FileSystemInfo Entry, string Rel)>();
        foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }).ToList())
        {
            var rel = relative.Length == 0 ? entry.Name : Path.Combine(relative, entry.Name);
            if (!steps.RemoveIfLink(entry, rel, actions, ref changed)) remaining.Add((entry, rel));
        }
        foreach (var (entry, rel) in remaining)
            if (entry is not DirectoryInfo) steps.Secure(entry, rel, EntryKind.File, resetAll, actions, ref changed);
        foreach (var (entry, rel) in remaining)
            if (entry is DirectoryInfo sub) Walk(sub, rel, isRoot: false, resetAll, actions, ref changed, steps);
    }

    /// <summary>The real steps: every read and write goes through a handle on the entry itself, never by name.</summary>
    internal sealed class HandleSteps : IWalkSteps
    {
        public static readonly HandleSteps Instance = new();

        public void Secure(FileSystemInfo entry, string rel, EntryKind kind, bool resetAll, List<string> actions, ref bool changed)
        {
            bool becameLink;
            using (var h = FileLinks.OpenSecurity(entry.FullName))
            {
                if (kind == EntryKind.Root && h.Info.ReparseTag is not null)
                    throw new IOException($"The data directory {entry.FullName} is a link; refusing to use it.");
                // Checked again through this handle: an entry that became a link is never re-permissioned.
                becameLink = kind != EntryKind.Root && (h.Info.ReparseTag is not null || h.Info.IsHardLink);
                if (!becameLink)
                {
                    var reason = resetAll ? "" : ReasonFor(h.ReadDescriptor(), kind != EntryKind.File);
                    if (reason is null) return;
                    h.WriteDescriptor(Descriptor(kind, AdminsSid));
                    if (!resetAll) actions.Add($"{rel}: reset ({reason})");
                    changed = true;
                }
            }
            if (becameLink) DataDirectoryAcl.RemoveIfLink(entry, rel, actions, ref changed);
        }

        public bool RemoveIfLink(FileSystemInfo entry, string rel, List<string> actions, ref bool changed) =>
            DataDirectoryAcl.RemoveIfLink(entry, rel, actions, ref changed);
    }

    /// <summary>
    /// A self-relative descriptor owned by <paramref name="owner"/> granting SYSTEM and Administrators full control:
    /// for the root protected and inheritable (as <see cref="Hardened"/>), below it exactly what the root passes down.
    /// </summary>
    internal static byte[] Descriptor(EntryKind kind, SecurityIdentifier owner)
    {
        var flags = kind switch
        {
            EntryKind.Root => AceFlags.ContainerInherit | AceFlags.ObjectInherit,
            EntryKind.Directory => AceFlags.ContainerInherit | AceFlags.ObjectInherit | AceFlags.Inherited,
            _ => AceFlags.Inherited,
        };
        var dacl = new RawAcl(GenericAcl.AclRevision, 2);
        dacl.InsertAce(0, new CommonAce(flags, AceQualifier.AccessAllowed, (int)FileSystemRights.FullControl, SystemSid, false, null));
        dacl.InsertAce(1, new CommonAce(flags, AceQualifier.AccessAllowed, (int)FileSystemRights.FullControl, AdminsSid, false, null));
        var control = ControlFlags.DiscretionaryAclPresent | ControlFlags.SelfRelative
            | (kind == EntryKind.Root ? ControlFlags.DiscretionaryAclProtected : ControlFlags.DiscretionaryAclAutoInherited);
        var sd = new RawSecurityDescriptor(control, owner, null, null, dacl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }

    /// <summary>
    /// Deletes <paramref name="entry"/> when it is a symbolic link or junction, or a hard link (a file with more than
    /// one name): following the one or re-permissioning the other would change something outside the data directory.
    /// Any other reparse point (a compressed or cloud placeholder file, say) is not followed, not recursed into and
    /// left alone: what such a reparse point does with its own descriptor is up to its filter driver. Every entry is
    /// read, and deleted, through one handle that never follows a link, whatever the enumeration reported.
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

    private static string? ReasonFor(byte[]? descriptor, bool directory)
    {
        if (descriptor is null) return "security descriptor unreadable";
        FileSystemSecurity sec = directory ? new DirectorySecurity() : new FileSecurity();
        sec.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Owner | AccessControlSections.Access);
        return ResetReason(sec);
    }

    private static bool IsTrustedOwner(IdentityReference? owner) => owner is SecurityIdentifier sid && (sid == SystemSid || sid == AdminsSid);

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
