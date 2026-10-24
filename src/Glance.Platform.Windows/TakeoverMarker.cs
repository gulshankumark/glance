using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Glance.Platform.Windows;

/// <summary>
/// Remembers, across a crash, that a quarantine of the data directory is in progress and which folder it moves into.
/// See <see cref="DataDirectoryAcl.Enforce(string, ITakeoverMarker)"/>.
/// </summary>
public interface ITakeoverMarker
{
    /// <summary>
    /// The quarantine folder name of an unfinished takeover, or null when none is pending. Throws when the marker
    /// cannot be read at all, so a takeover is never forgotten over a transient error.
    /// </summary>
    string? Read();

    /// <summary>Records that a takeover into <paramref name="quarantineDirName"/> has begun.</summary>
    void Write(string quarantineDirName);

    /// <summary>Records that the takeover finished. Nothing pending is fine.</summary>
    void Clear();
}

/// <summary>
/// The marker as a file next to the data directory, in <paramref name="directory"/> (%ProgramData% for the default
/// root): a file inside the root could not be written before its ownership is taken, which would leave a window in
/// which a crash forgets the takeover. Nothing goes to the registry.
/// <para>Any local user can create files in %ProgramData%, so the file is trusted only when it proves it came from
/// SYSTEM or Administrators: owned by one of them, nothing inherited, no entry letting anyone else write or delete it,
/// no link of any kind, and content that is a quarantine folder name. Anything else is deleted (the entry itself,
/// never what a link points to), reported through <paramref name="warn"/> and read as nothing pending. A user can
/// neither edit nor delete the trusted file (its DACL denies them, and Users may not delete others' files in
/// %ProgramData%), so a user can neither forge a resume nor cancel one.</para>
/// <para>Only a file that positively fails one of those checks is deleted. When the content cannot be read (a sharing
/// violation from a scanner at boot, or from a user holding a planted file open), the checks that need no content are
/// made on the metadata alone: a file that fails them is ignored as above (left in place if it cannot be deleted), so
/// a user cannot stop the service from starting; a file that passes them is genuine and left alone, and the read
/// throws, so startup stops and the next start resumes the takeover instead of forgetting it. So does a file whose
/// metadata cannot be read either.</para>
/// Every read and ACL write goes through a handle opened on the entry itself, never by name.
/// </summary>
public sealed class FileTakeoverMarker(string directory, Action<string>? warn = null) : ITakeoverMarker
{
    public const string FileName = "Glance.takeover-pending";

    // Allowed to anyone but SYSTEM/Administrators, any of these makes the file untrusted.
    private const int WriteRights = (int)(FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)
        | 0x40000000 /* GENERIC_WRITE */ | 0x10000000 /* GENERIC_ALL */;

    /// <summary>Owner Administrators; SYSTEM and Administrators full control; nothing inherited.</summary>
    private static readonly byte[] Descriptor = BuildDescriptor();

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    public string? Read()
    {
        // To open the file whatever a planted DACL says.
        DataDirectoryAcl.Privileges.Enable("SeBackupPrivilege", "SeRestorePrivilege");
        string? reason, content = null;
        try
        {
            using var entry = FileLinks.OpenToRead(Path);
            if (entry is null) return null;
            reason = Untrusted(entry) ?? NotAMarker(entry, out content);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Any user can plant the file and hold it open without sharing, so the content cannot be read. The
            // metadata still can: a planted file is ignored as before, while a genuine one keeps the error, never
            // deleted. Should even the metadata fail, that error propagates.
            using var entry = FileLinks.OpenMetadata(Path);
            if (entry is null) return null;
            reason = Untrusted(entry);
            if (reason is null) throw;
        }
        if (reason is null) return content;

        try
        {
            FileLinks.DeleteIf(Path, _ => true);
            warn?.Invoke($"Ignored and deleted the takeover marker {Path}: {reason}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn?.Invoke($"Ignored the takeover marker {Path}: {reason}. It could not be deleted: {e.Message}");
        }
        return null;
    }

    public void Write(string quarantineDirName)
    {
        if (!DataDirectoryAcl.QuarantineName.IsMatch(quarantineDirName))
            throw new ArgumentException($"Not a quarantine folder name: {quarantineDirName}", nameof(quarantineDirName));
        // To delete whatever is already there (anyone's), and to make Administrators the owner.
        DataDirectoryAcl.Privileges.Enable("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");
        FileLinks.DeleteIf(Path, _ => true);

        // Created with its final ACL, so no one else can open it at any point; then renamed into place.
        var temp = $"{Path}.{Guid.NewGuid():N}.tmp";
        FileLinks.CreateNew(temp, Descriptor, Encoding.ASCII.GetBytes(quarantineDirName));
        try
        {
            using var folder = FileLinks.OpenFolder(directory);
            if (!FileLinks.MoveInto(temp, folder, FileName, replace: true))
                throw new IOException($"Could not put the takeover marker {Path} in place.");
        }
        catch
        {
            try { FileLinks.DeleteIf(temp, _ => true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }

        using var placed = FileLinks.OpenSecurity(Path);
        if (placed.Info.ReparseTag is not null || placed.Info.IsHardLink)
            throw new IOException($"The takeover marker {Path} was replaced by a link while it was written.");
        placed.WriteDescriptor(Descriptor);
    }

    public void Clear()
    {
        DataDirectoryAcl.Privileges.Enable("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");
        FileLinks.DeleteIf(Path, _ => true);
    }

    /// <summary>
    /// Why the opened file cannot be trusted by what it is, its owner and its permissions, or null when it can. Needs
    /// no access to the content.
    /// </summary>
    private static string? Untrusted(FileLinks.SecuredEntry entry)
    {
        if (entry.Info.ReparseTag is { } tag) return $"it is a link or another reparse point (tag 0x{tag:X8})";
        if (entry.Info.IsHardLink) return "it has more than one name (a hard link)";
        if (entry.ReadDescriptor() is not { } bytes) throw new IOException("The permissions of the takeover marker could not be read.");
        var sd = new RawSecurityDescriptor(bytes, 0);
        if (!DataDirectoryAcl.IsTrustedOwner(sd.Owner)) return $"it is owned by {sd.Owner?.Value ?? "no one"}, not SYSTEM or Administrators";
        if (sd.DiscretionaryAcl is null) return "it has no DACL, so everyone has full access";
        foreach (GenericAce ace in sd.DiscretionaryAcl)
        {
            if ((ace.AceFlags & AceFlags.Inherited) != 0) return "it inherits permissions";
            if (ace.AceType is AceType.AccessDenied or AceType.AccessDeniedObject or AceType.AccessDeniedCallback or AceType.AccessDeniedCallbackObject)
                continue;
            if (ace is not KnownAce known) return $"it has an unrecognised access entry ({ace.AceType})";
            if (!DataDirectoryAcl.IsTrustedOwner(known.SecurityIdentifier) && (known.AccessMask & WriteRights) != 0)
                return $"{known.SecurityIdentifier.Value} may change or delete it";
        }
        return null;
    }

    /// <summary>Why the content of the opened file is not a marker, or null with that <paramref name="content"/>.</summary>
    private static string? NotAMarker(FileLinks.SecuredEntry entry, out string? content)
    {
        content = null;
        var buffer = new byte[64];
        var read = RandomAccess.Read(entry.Handle, buffer, 0);
        var text = Encoding.ASCII.GetString(buffer, 0, read);
        if (read == buffer.Length || !DataDirectoryAcl.QuarantineName.IsMatch(text)) return "its content is not a quarantine folder name";
        content = text;
        return null;
    }

    private static byte[] BuildDescriptor()
    {
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var dacl = new RawAcl(GenericAcl.AclRevision, 2);
        dacl.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, (int)FileSystemRights.FullControl,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), false, null));
        dacl.InsertAce(1, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, (int)FileSystemRights.FullControl, admins, false, null));
        var sd = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected | ControlFlags.SelfRelative,
            admins, null, null, dacl);
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        return bytes;
    }
}
