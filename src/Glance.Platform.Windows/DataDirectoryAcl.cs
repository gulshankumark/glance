using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;

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
        /// <summary>
        /// Someone else owned the directory (or an earlier takeover was interrupted): ownership was taken and everything
        /// that was inside moved to <see cref="AclResult.QuarantineDir"/>, so Glance starts from defaults (warn the user).
        /// </summary>
        Quarantined,
    }

    /// <summary>
    /// What <see cref="Enforce(string, ITakeoverMarker)"/> did. <paramref name="QuarantineDir"/> is the full path of
    /// the folder the old contents were moved to (only when Quarantined). <paramref name="Actions"/> are
    /// human-readable lines for the log.
    /// </summary>
    public sealed record AclResult(Outcome Outcome, string? QuarantineDir, IReadOnlyList<string> Actions);

    /// <summary>
    /// Quarantine folder names: <c>quarantine-</c> and the UTC time the takeover began. ASCII digits only, and nothing
    /// after the Z (<c>$</c> would also allow a final newline).
    /// </summary>
    public static readonly Regex QuarantineName = new(@"^quarantine-[0-9]{8}T[0-9]{6}Z\z", RegexOptions.CultureInvariant);

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
    /// Creates <paramref name="root"/> already hardened (the descriptor is set at creation, never by name afterwards) and
    /// returns true; when anything exists at that path (directory, file or link, even a dangling one) or its parent is
    /// missing, touches nothing and returns false. An existing directory is the service's own takeover handling to deal with.
    /// </summary>
    public static bool CreateHardenedIfMissing(string root)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(root));
        if (parent is null || !Directory.Exists(parent)) return false;
        try { File.GetAttributes(root); return false; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        new DirectoryInfo(root).Create(Hardened());
        return true;
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
    /// Makes <paramref name="root"/> hardened (see <see cref="Hardened"/>) and makes sure nothing in it came from
    /// anyone but SYSTEM or Administrators.
    /// <para>When the root's owner is SYSTEM/Administrators and no takeover is pending in <paramref name="marker"/>:
    /// a hardened root keeps its contents and only entries owned by someone else, carrying explicit entries, with
    /// unreadable descriptors or other inherited entries are reset (Tightened, or AlreadySecure when nothing was);
    /// otherwise the root and everything inside are reset (Tightened).</para>
    /// <para>When anyone else owns the root (or its owner cannot be read), its contents may have been planted, and
    /// resetting their permissions would not make them trustworthy. They are quarantined, in this order, so that a
    /// crash at any point is resumed at the next start rather than leaving planted files to be trusted:
    /// (1) write <paramref name="marker"/> with the quarantine folder name <c>quarantine-&lt;UTC yyyyMMddTHHmmssZ&gt;</c>,
    /// before anything changes; (2) take ownership of the root and harden its DACL, the root only, by handle;
    /// (3) move every other entry, except existing quarantine folders, into the quarantine folder by renames that
    /// never follow a link; (4) harden the quarantine tree (Administrators owner, the root's entries inherited,
    /// explicit entries dropped, links deleted); then walk the root once more as for a hardened root; (5) clear the
    /// marker. The result is Quarantined with <see cref="AclResult.QuarantineDir"/> set.</para>
    /// <para>When the marker is set at the start, the takeover resumes at step 2 with the folder it names, whoever
    /// owns the root: every step can run again.</para>
    /// Every walk is done by <see cref="Walk"/>: each directory is locked by handle before its contents are looked at,
    /// links are deleted (never followed or re-permissioned; a deletion makes the result Tightened) and no descriptor
    /// change propagates.
    /// Throws when the directory cannot be secured: the caller must not go on using it.
    /// </summary>
    public static AclResult Enforce(string root, ITakeoverMarker marker) => Enforce(root, marker, HandleSteps.Instance);

    internal static AclResult Enforce(string root, ITakeoverMarker marker, IEnforceSteps steps)
    {
        var dir = new DirectoryInfo(root);
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException($"The data directory {root} is a link; refusing to use it.");
        // To set an owner on objects we cannot open otherwise, and to read link counts whatever a planted DACL says.
        Privileges.Enable("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");
        var pending = marker.Read();
        var actions = new List<string>();
        if (pending is null)
        {
            var current = steps.ReadRoot(dir);
            if (ReasonFor(current, EntryKind.Root) is null)
            {
                // The root can be fine while an interrupted earlier reset left children behind that anyone could have planted.
                var changed = false;
                Walk(dir, "", isRoot: true, resetAll: false, actions, ref changed, steps);
                return new AclResult(changed ? Outcome.Tightened : Outcome.AlreadySecure, null, actions);
            }
            if (current is not null && IsTrustedOwner(new RawSecurityDescriptor(current, 0).Owner))
            {
                var removed = false;
                Walk(dir, "", isRoot: true, resetAll: true, actions, ref removed, steps);
                actions.Add("reset the data directory and everything inside it");
                return new AclResult(Outcome.Tightened, null, actions);
            }
        }

        var name = pending ?? $"quarantine-{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}";
        if (pending is null) marker.Write(name);                                         // 1
        else actions.Add($"resuming the interrupted quarantine into {name}");
        var ignored = false;
        steps.Secure(dir, ".", EntryKind.Root, resetAll: true, actions, ref ignored);    // 2
        var quarantine = MoveAside(dir, name, actions);                                  // 3
        Walk(quarantine, name, isRoot: false, resetAll: true, actions, ref ignored, steps); // 4
        Walk(dir, "", isRoot: true, resetAll: false, actions, ref ignored, steps);
        marker.Clear();                                                                  // 5
        actions.Add($"moved the data directory's previous contents to {name}");
        return new AclResult(Outcome.Quarantined, quarantine.FullName, actions);
    }

    /// <summary>
    /// Moves every entry of <paramref name="root"/> except quarantine folders into <paramref name="root"/>\<paramref name="name"/>,
    /// creating it when needed (one already there from an interrupted run is reused). Each entry is renamed through a
    /// handle on itself, into the folder's own handle: links move as links. An entry whose name is taken in the folder
    /// gets a ".1", ".2", ... suffix. Only to be called once the root is locked.
    /// </summary>
    internal static DirectoryInfo MoveAside(DirectoryInfo root, string name, List<string> actions)
    {
        var path = Path.Combine(root.FullName, name);
        // Planted before the root was locked (the name is guessable): a link here would take the moves elsewhere.
        if (FileLinks.DeleteIf(path, i => i.IsNameLink || i.IsHardLink) is { } existing && (existing.IsNameLink || existing.IsHardLink))
            actions.Add($"{name}: link deleted");
        if (File.Exists(path) && FileLinks.DeleteIf(path, _ => true) is not null)
            actions.Add($"{name}: a file in the way of the quarantine folder was deleted");
        Directory.CreateDirectory(path);
        using var folder = FileLinks.OpenFolder(path);
        if (folder.Info.ReparseTag is { } tag)
            throw new IOException($"The quarantine folder {path} is a reparse point (tag 0x{tag:X8}); refusing to move into it.");
        foreach (var entry in root.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }).ToList())
        {
            if (entry is DirectoryInfo && QuarantineName.IsMatch(entry.Name)) continue;   // this one, or an older one
            var target = entry.Name;
            for (var n = 1; !FileLinks.MoveInto(entry.FullName, folder, target); n++)
            {
                if (n > 1000) throw new IOException($"Could not move {entry.FullName} into {path}: every name is taken.");
                target = $"{entry.Name}.{n}";
            }
            actions.Add(target == entry.Name ? $"{entry.Name}: moved to {name}" : $"{entry.Name}: moved to {Path.Combine(name, target)}");
        }
        return new DirectoryInfo(path);
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
        /// <summary>
        /// Resets the entry's owner and DACL when <paramref name="resetAll"/> or when it needs it. False when the entry
        /// is no plain entry any more (turned into a link, so deleted or left alone, or gone): not to be walked into.
        /// </summary>
        bool Secure(FileSystemInfo entry, string rel, EntryKind kind, bool resetAll, List<string> actions, ref bool changed);

        /// <summary>See <see cref="DataDirectoryAcl.RemoveIfLink"/>.</summary>
        bool RemoveIfLink(FileSystemInfo entry, string rel, List<string> actions, ref bool changed);
    }

    /// <summary>The steps of <see cref="Enforce(string, ITakeoverMarker, IEnforceSteps)"/>; a seam so tests can watch their order.</summary>
    internal interface IEnforceSteps : IWalkSteps
    {
        /// <summary>The root's owner and DACL in self-relative form, read through its own handle; null when unreadable.</summary>
        byte[]? ReadRoot(DirectoryInfo root);
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
        // Stop when it is no plain directory any more: it turned into a link since it was listed (or vanished).
        if (!steps.Secure(dir, relative.Length == 0 ? "." : relative, isRoot ? EntryKind.Root : EntryKind.Directory, resetAll, actions, ref changed))
            return;
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
    internal sealed class HandleSteps : IEnforceSteps
    {
        public static readonly HandleSteps Instance = new();

        public byte[]? ReadRoot(DirectoryInfo root)
        {
            using var h = FileLinks.OpenSecurity(root.FullName);
            if (h.Info.ReparseTag is not null)
                throw new IOException($"The data directory {root.FullName} is a link; refusing to use it.");
            return h.ReadDescriptor();
        }

        public bool Secure(FileSystemInfo entry, string rel, EntryKind kind, bool resetAll, List<string> actions, ref bool changed)
        {
            FileLinks.SecuredEntry handle;
            try { handle = FileLinks.OpenSecurity(entry.FullName); }
            catch (FileNotFoundException) when (kind != EntryKind.Root) { return false; }   // gone since it was listed
            using (var h = handle)
            {
                if (kind == EntryKind.Root && h.Info.ReparseTag is not null)
                    throw new IOException($"The data directory {entry.FullName} is a link; refusing to use it.");
                // Checked again through this handle: an entry that became a link is never re-permissioned.
                if (kind == EntryKind.Root || (h.Info.ReparseTag is null && !h.Info.IsHardLink))
                {
                    var reason = resetAll ? "" : ReasonFor(h.ReadDescriptor(), kind);
                    if (reason is null) return true;
                    h.WriteDescriptor(Descriptor(kind, AdminsSid));
                    if (!resetAll) actions.Add($"{rel}: reset ({reason})");
                    changed = true;
                    return true;
                }
            }
            // Deleted, or another reparse point left alone: either way not a directory to walk into.
            DataDirectoryAcl.RemoveIfLink(entry, rel, actions, ref changed);
            return false;
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

    /// <summary>Why the entry with <paramref name="descriptor"/> (null: unreadable) must be reset, or null when it is fine.</summary>
    internal static string? ReasonFor(byte[]? descriptor, EntryKind kind)
    {
        if (descriptor is null) return "security descriptor unreadable";
        if (kind == EntryKind.Root)
        {
            // A hardened root's entries are explicit by design: it is fine exactly when it is hardened.
            var root = new DirectorySecurity();
            root.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Owner | AccessControlSections.Access);
            return IsHardened(root) ? null : "not hardened";
        }
        FileSystemSecurity sec = kind != EntryKind.File ? new DirectorySecurity() : new FileSecurity();
        sec.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Owner | AccessControlSections.Access);
        if (ResetReason(sec) is { } reason) return reason;
        // Owner and explicit entries are fine; the inherited ones must be exactly what the locked parent passes down.
        // Not so after a walk was interrupted: a child created before then keeps what %ProgramData% passed down
        // (Users may create entries, CREATOR OWNER), and nothing re-derives it now that nothing propagates.
        var expected = new RawSecurityDescriptor(Descriptor(kind, AdminsSid), 0);
        var actual = new RawSecurityDescriptor(descriptor, 0);
        if (actual.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected) || !SameEntries(actual.DiscretionaryAcl, expected.DiscretionaryAcl))
            return "inherited access entries differ from the hardened set";
        return null;
    }

    /// <summary>The same entries as a multiset (type, flags, rights, SID): order does not matter.</summary>
    private static bool SameEntries(RawAcl? a, RawAcl? b)
    {
        static List<string> Keys(RawAcl? acl)
        {
            var keys = new List<string>();
            if (acl is null) return keys;
            foreach (GenericAce ace in acl)
                keys.Add(ace is KnownAce k ? $"{k.AceType}|{k.AceFlags}|{k.AccessMask:X8}|{k.SecurityIdentifier.Value}" : $"other|{ace.AceType}");
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
        return a is not null && b is not null && Keys(a).SequenceEqual(Keys(b));
    }

    internal static bool IsTrustedOwner(IdentityReference? owner) => owner is SecurityIdentifier sid && (sid == SystemSid || sid == AdminsSid);

    internal static partial class Privileges
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
