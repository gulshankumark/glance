using System.Diagnostics;

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

    private static void Mklink(string kind, string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[] { "/c", "mklink", kind, link, target }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"mklink {kind} failed: {output}");
    }
}
