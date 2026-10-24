using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Glance.Platform.Windows.Tests;

/// <summary>Creates links the way a local user could: hard links and junctions need no privilege, symlinks may.</summary>
internal static class LinkFixtures
{
    public static void HardLink(string link, string target) => Mklink("/H", link, target);

    public static void Junction(string link, string target) => Mklink("/J", link, target);

    /// <summary>False when this account may not create symbolic links (no privilege and Developer Mode off).</summary>
    public static bool TrySymlink(string link, string target, bool directory)
    {
        try
        {
            if (directory) Directory.CreateSymbolicLink(link, target);
            else File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static string Sddl(string path) => Directory.Exists(path)
        ? new DirectoryInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.All)
        : new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.All);

    /// <summary>
    /// The descriptor exactly as stored, read through a handle. Reading by name (GetNamedSecurityInfo) re-derives the
    /// inherited flags from the parent, so it can report a change the object itself never had.
    /// </summary>
    public static string RawSddl(string path)
    {
        using var h = FileLinks.OpenSecurity(path);
        return new System.Security.AccessControl.RawSecurityDescriptor(h.ReadDescriptor()!, 0)
            .GetSddlForm(System.Security.AccessControl.AccessControlSections.Owner | System.Security.AccessControl.AccessControlSections.Access);
    }

    /// <summary>Sets the entry's owner through its own handle, keeping its DACL (to self needs no privilege; to Administrators, elevation).</summary>
    public static void SetOwner(string path, System.Security.Principal.SecurityIdentifier owner)
    {
        using var h = FileLinks.OpenSecurity(path);
        var sd = new System.Security.AccessControl.RawSecurityDescriptor(h.ReadDescriptor()!, 0) { Owner = owner };
        var bytes = new byte[sd.BinaryLength];
        sd.GetBinaryForm(bytes, 0);
        h.WriteDescriptor(bytes);
    }

    private static void Mklink(string kind, string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[] { "/c", "mklink", kind, link, target }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"mklink {kind} failed: {output}");
    }

    /// <summary>
    /// What any user can do to their own file: open it with DELETE and set the legacy (non-POSIX) delete disposition.
    /// While the returned handle stays open the name stays in the folder, but every new open of it fails.
    /// </summary>
    public static SafeFileHandle HoldDeletePending(string path)
    {
        // DELETE | FILE_READ_ATTRIBUTES, share all, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT
        var h = CreateFileW(path, 0x10000 | 0x80, 0x1 | 0x2 | 0x4, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileInformationByHandle(h, 4, ref info, (uint)Marshal.SizeOf<FileDispositionInfo>()))   // class 4: FileDispositionInfo
        {
            var error = Marshal.GetLastWin32Error();
            h.Dispose();
            throw new Win32Exception(error);
        }
        return h;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { public byte DeleteFile; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref FileDispositionInfo info, uint size);
}
