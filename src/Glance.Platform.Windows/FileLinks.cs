using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Glance.Platform.Windows;

/// <summary>
/// Reads and removes file-system links without following them: every entry is opened with
/// <c>FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS</c>, so the handle is the link itself, never its target.
/// </summary>
internal static partial class FileLinks
{
    public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C, IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;

    /// <summary>What an entry is, read from the entry itself.</summary>
    /// <param name="ReparseTag">Null when the entry is not a reparse point.</param>
    public readonly record struct Info(uint LinkCount, uint? ReparseTag)
    {
        /// <summary>A second name for a file that may live outside the directory being walked.</summary>
        public bool IsHardLink => LinkCount > 1;

        /// <summary>A symbolic link or a junction (mount point): a name that redirects elsewhere.</summary>
        public bool IsNameLink => ReparseTag is IO_REPARSE_TAG_SYMLINK or IO_REPARSE_TAG_MOUNT_POINT;
    }

    /// <summary>Number of names the file has (1 for a directory or an ordinary file).</summary>
    public static uint LinkCount(string path) => Read(path).LinkCount;

    /// <summary>The entry's own reparse tag, or null when it is not a reparse point.</summary>
    public static uint? ReparseTag(string path) => Read(path).ReparseTag;

    public static Info Read(string path)
    {
        using var h = Open(path, FileReadAttributes) ?? throw LastError(path);
        return Query(h, path);
    }

    /// <summary>
    /// Reads the entry and, when <paramref name="shouldDelete"/> says so, deletes it through the same handle (no window
    /// in which the name could be swapped). Deleting a link removes only the link: its target is never opened. Returns
    /// null when the entry no longer exists.
    /// </summary>
    public static Info? DeleteIf(string path, Func<Info, bool> shouldDelete)
    {
        // DELETE may be refused (sharing, a planted DACL); reading still tells the caller what the entry is.
        using var h = Open(path, FileReadAttributes | Delete) ?? Open(path, FileReadAttributes);
        if (h is null)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ErrorFileNotFound or ErrorPathNotFound) return null;
            throw LastError(path, error);
        }
        var info = Query(h, path);
        if (!shouldDelete(info)) return info;
        if (!MarkForDeletion(h)) throw LastError(path);
        return info;
    }

    /// <summary>
    /// Opens the entry itself (never what it points to) to read and write its own security descriptor. The descriptor
    /// is written with SetKernelObjectSecurity, which changes this one object only: nothing is propagated to children,
    /// unlike SetNamedSecurityInfo/SetSecurityInfo, which push inherited entries into everything below.
    /// </summary>
    public static SecuredEntry OpenSecurity(string path)
    {
        // Without READ_CONTROL when a planted DACL refuses it even to backup semantics: the descriptor is then unreadable.
        var h = Open(path, ReadControl | WriteDac | WriteOwner | FileReadAttributes)
            ?? Open(path, WriteDac | WriteOwner | FileReadAttributes)
            ?? throw LastError(path);
        try { return new SecuredEntry(h, path, Query(h, path)); }
        catch { h.Dispose(); throw; }
    }

    /// <summary>
    /// Opens the entry itself (never what it points to) to read its content and security descriptor through
    /// <see cref="SecuredEntry.Handle"/>. Null when it does not exist.
    /// </summary>
    public static SecuredEntry? OpenToRead(string path)
    {
        var h = Open(path, ReadControl | FileReadData | FileReadAttributes);
        if (h is null)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ErrorFileNotFound or ErrorPathNotFound) return null;
            throw LastError(path, error);
        }
        try { return new SecuredEntry(h, path, Query(h, path)); }
        catch { h.Dispose(); throw; }
    }

    /// <summary>
    /// Opens the entry itself (never what it points to) for its attributes and security descriptor only, without
    /// its content. Sharing modes do not cover these rights, so this succeeds while another handle denies sharing.
    /// Null when it does not exist.
    /// </summary>
    public static SecuredEntry? OpenMetadata(string path)
    {
        var h = Open(path, ReadControl | FileReadAttributes);
        if (h is null)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is ErrorFileNotFound or ErrorPathNotFound) return null;
            throw LastError(path, error);
        }
        try { return new SecuredEntry(h, path, Query(h, path)); }
        catch { h.Dispose(); throw; }
    }

    /// <summary>
    /// Creates the file at <paramref name="path"/> with <paramref name="descriptor"/> (self-relative) from its first
    /// moment, writes <paramref name="content"/> and flushes it. Fails when anything, a link included, has that name.
    /// </summary>
    public static unsafe void CreateNew(string path, byte[] descriptor, ReadOnlySpan<byte> content)
    {
        SafeFileHandle h;
        int error;
        fixed (byte* sd = descriptor)
        {
            var sa = new SecurityAttributes { Length = (uint)sizeof(SecurityAttributes), Descriptor = (IntPtr)sd };
            h = CreateFileW(path, GenericWrite, 0, (IntPtr)(&sa), CreateNewDisposition, FileAttributeNormal, IntPtr.Zero);
            error = Marshal.GetLastPInvokeError();
        }
        using (h)
        {
            if (h.IsInvalid) throw LastError(path, error);
            RandomAccess.Write(h, content, 0);
            if (!FlushFileBuffers(h)) throw LastError(path);
        }
    }

    /// <summary>
    /// Opens the directory itself (never what it points to) as the destination of <see cref="MoveInto"/>. The caller
    /// checks <see cref="SecuredEntry.Info"/>: a link opened here is the link, so moving into it would land in the link.
    /// </summary>
    public static SecuredEntry OpenFolder(string path)
    {
        var h = Open(path, FileReadAttributes | FileTraverse) ?? throw LastError(path);
        try { return new SecuredEntry(h, path, Query(h, path)); }
        catch { h.Dispose(); throw; }
    }

    /// <summary>
    /// Renames the entry at <paramref name="path"/> to <paramref name="newName"/> inside the directory open as
    /// <paramref name="folder"/> (same volume). The entry is opened itself, so a link moves as the link and its target
    /// is never opened; the destination is resolved from the folder's handle, not by name. False when
    /// <paramref name="newName"/> is taken there and <paramref name="replace"/> is off.
    /// </summary>
    public static bool MoveInto(string path, SecuredEntry folder, string newName, bool replace = false)
    {
        using var h = Open(path, Delete | FileReadAttributes) ?? throw LastError(path);
        var nameOffset = 2 * IntPtr.Size + 4;   // FILE_RENAME_INFORMATION: BOOLEAN (padded), HANDLE, ULONG, WCHAR[]
        var nameBytes = newName.Length * 2;
        var length = nameOffset + nameBytes + 2;
        var buffer = Marshal.AllocHGlobal(length);
        var added = false;
        try
        {
            folder.Handle.DangerousAddRef(ref added);
            var info = new byte[length];
            info[0] = replace ? (byte)1 : (byte)0;
            if (IntPtr.Size == 8) BitConverter.TryWriteBytes(info.AsSpan(IntPtr.Size), (long)folder.Handle.DangerousGetHandle());
            else BitConverter.TryWriteBytes(info.AsSpan(IntPtr.Size), (int)folder.Handle.DangerousGetHandle());
            BitConverter.TryWriteBytes(info.AsSpan(2 * IntPtr.Size), (uint)nameBytes);
            System.Text.Encoding.Unicode.GetBytes(newName, info.AsSpan(nameOffset));
            Marshal.Copy(info, 0, buffer, length);
            var status = NtSetInformationFile(h, out _, buffer, (uint)length, FileRenameInformation);
            if (status == 0) return true;
            if (status == StatusObjectNameCollision) return false;
            throw LastError(path, (int)RtlNtStatusToDosError(status));
        }
        finally
        {
            if (added) folder.Handle.DangerousRelease();
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>An entry opened by <see cref="OpenSecurity"/>; <see cref="Info"/> was read through the same handle.</summary>
    public sealed class SecuredEntry(SafeFileHandle handle, string path, Info info) : IDisposable
    {
        public Info Info { get; } = info;

        public SafeFileHandle Handle => handle;

        /// <summary>Owner and DACL in self-relative form, or null when they cannot be read.</summary>
        public byte[]? ReadDescriptor()
        {
            GetKernelObjectSecurity(handle, OwnerSecurityInformation | DaclSecurityInformation, null, 0, out var needed);
            if (needed == 0) return null;
            var sd = new byte[needed];
            return GetKernelObjectSecurity(handle, OwnerSecurityInformation | DaclSecurityInformation, sd, needed, out _) ? sd : null;
        }

        /// <summary>Sets owner and DACL (protection comes from the descriptor's control flags) on this object only.</summary>
        public void WriteDescriptor(byte[] selfRelative)
        {
            if (!SetKernelObjectSecurity(handle, OwnerSecurityInformation | DaclSecurityInformation, selfRelative)) throw LastError(path);
        }

        public void Dispose() => handle.Dispose();
    }

    private static Info Query(SafeFileHandle h, string path)
    {
        if (!GetFileInformationByHandle(h, out var byHandle)) throw LastError(path);
        if (!GetFileInformationByHandleEx(h, FileAttributeTagInfo, out var tag, (uint)Marshal.SizeOf<FileAttributeTag>()))
            throw LastError(path);
        var isReparse = (tag.FileAttributes & FileAttributeReparsePoint) != 0;
        return new Info(byHandle.NumberOfLinks, isReparse ? tag.ReparseTag : null);
    }

    /// <summary>
    /// Marks the open entry for deletion, ignoring a read-only attribute (clearing it first would change the attributes
    /// every name of a hard link shares). Falls back to plain delete-on-close where the extended form is not supported.
    /// </summary>
    private static bool MarkForDeletion(SafeFileHandle h)
    {
        var ex = new FileDispositionInfoEx { Flags = DispositionDelete | DispositionPosixSemantics | DispositionIgnoreReadOnly };
        if (SetFileInformationByHandle(h, FileDispositionInfoExClass, ref ex, (uint)Marshal.SizeOf<FileDispositionInfoEx>())) return true;
        var plain = new FileDispositionInfo { DeleteFile = 1 };
        return SetFileInformationByHandle(h, FileDispositionInfoClass, ref plain, (uint)Marshal.SizeOf<FileDispositionInfo>());
    }

    private static SafeFileHandle? Open(string path, uint access)
    {
        var h = CreateFileW(path, access, FileShareAll, IntPtr.Zero, OpenExisting, FlagOpenReparsePoint | FlagBackupSemantics, IntPtr.Zero);
        if (!h.IsInvalid) return h;
        var error = Marshal.GetLastPInvokeError();
        h.Dispose();
        Marshal.SetLastPInvokeError(error);
        return null;
    }

    private static Exception LastError(string path) => LastError(path, Marshal.GetLastPInvokeError());

    private static Exception LastError(string path, int error) => error switch
    {
        ErrorAccessDenied => new UnauthorizedAccessException($"Access to '{path}' is denied."),
        ErrorFileNotFound or ErrorPathNotFound => new FileNotFoundException($"'{path}' was not found.", path),
        _ => new IOException($"'{path}': {Marshal.GetPInvokeErrorMessage(error)}", unchecked((int)(0x80070000u | (uint)error))),
    };

    private const int FileRenameInformation = 10, StatusObjectNameCollision = unchecked((int)0xC0000035);
    private const uint FileTraverse = 0x20, FileReadData = 0x1, GenericWrite = 0x40000000;
    private const uint CreateNewDisposition = 1, FileAttributeNormal = 0x80;
    private const uint FileReadAttributes = 0x80, Delete = 0x10000, ReadControl = 0x20000, WriteDac = 0x40000, WriteOwner = 0x80000;
    private const uint OwnerSecurityInformation = 0x1, DaclSecurityInformation = 0x4;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4, OpenExisting = 3;
    private const uint FlagOpenReparsePoint = 0x00200000, FlagBackupSemantics = 0x02000000;
    private const uint FileAttributeReparsePoint = 0x400;
    private const int FileAttributeTagInfo = 9, FileDispositionInfoClass = 4, FileDispositionInfoExClass = 21;
    private const uint DispositionDelete = 0x1, DispositionPosixSemantics = 0x2, DispositionIgnoreReadOnly = 0x10;
    private const int ErrorFileNotFound = 2, ErrorPathNotFound = 3, ErrorAccessDenied = 5;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]   // FILETIMEs are two DWORDs: 4-byte aligned, 52 bytes in all
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTag
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfoEx { public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { public byte DeleteFile; }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition,
        uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle h, out ByHandleFileInformation info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle h, int infoClass, out FileAttributeTag info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref FileDispositionInfoEx info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref FileDispositionInfo info, uint size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetKernelObjectSecurity(SafeFileHandle h, uint info, [Out] byte[]? sd, uint length, out uint needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetKernelObjectSecurity(SafeFileHandle h, uint info, byte[] sd);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public IntPtr Descriptor;
        public int InheritHandle;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushFileBuffers(SafeFileHandle h);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { public IntPtr Status; public UIntPtr Information; }

    // The native call, because only it takes a destination relative to a directory handle (RootDirectory).
    [LibraryImport("ntdll.dll")]
    private static partial int NtSetInformationFile(SafeFileHandle h, out IoStatusBlock status, IntPtr info, uint length, int infoClass);

    [LibraryImport("ntdll.dll")]
    private static partial uint RtlNtStatusToDosError(int status);
}
