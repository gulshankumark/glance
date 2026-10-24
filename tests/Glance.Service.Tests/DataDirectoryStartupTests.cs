using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Glance.Core.Layouts;
using Glance.Platform.Windows;
using Glance.Testing;

namespace Glance.Service.Tests;

public class DataDirectoryStartupTests
{
    private static DataDirectoryAcl.AclResult Fail(string root, ITakeoverMarker marker) => throw new IOException("boom: the data directory is a link");

    private sealed class NoMarker : ITakeoverMarker
    {
        public string? Read() => null;
        public void Write(string quarantineDirName) { }
        public void Clear() { }
    }

    private static readonly ITakeoverMarker Marker = new NoMarker();

    [Fact]
    public void Enforce_gets_the_root_and_the_marker()
    {
        using var dir = new TempDir();
        var paths = new GlancePaths(dir.Path);
        (string Root, ITakeoverMarker Marker)? seen = null;
        var ok = new DataDirectoryAcl.AclResult(DataDirectoryAcl.Outcome.AlreadySecure, null, []);

        var result = DataDirectoryStartup.Enforce(paths.Root, shouldEnforce: true, Marker, (root, marker) => { seen = (root, marker); return ok; });

        Assert.Same(ok, result);
        Assert.Equal((paths.Root, Marker), seen);
    }

    [Fact]
    public void Enforce_failure_is_written_to_the_file_log_and_rethrown()
    {
        using var dir = new TempDir();
        var paths = new GlancePaths(dir.Path);

        var ex = Assert.Throws<IOException>(() => DataDirectoryStartup.Enforce(paths.Root, shouldEnforce: true, Marker, Fail));

        Assert.StartsWith("boom", ex.Message);   // rethrown unchanged, so the Event Log still gets it
        var log = Assert.Single(Directory.GetFiles(paths.Logs, "glance-*.log"));
        var text = File.ReadAllText(log);
        Assert.Contains("could not be secured", text);
        Assert.Contains("boom: the data directory is a link", text);
    }

    [Fact]
    public void Enforce_failure_is_not_logged_through_a_planted_hard_link()
    {
        using var dir = new TempDir();
        var paths = new GlancePaths(Path.Combine(dir.Path, "Glance"));
        var outside = Path.Combine(dir.Path, "outside.txt");
        File.WriteAllText(outside, "keep");
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "/c", "mklink", "/H", Path.Combine(paths.Logs, $"glance-{DateTime.Now:yyyyMMdd}.log"), outside })
            psi.ArgumentList.Add(a);
        using (var p = Process.Start(psi)!) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); Assert.Equal(0, p.ExitCode); }

        Assert.Throws<IOException>(() => DataDirectoryStartup.Enforce(paths.Root, shouldEnforce: true, Marker, Fail));

        Assert.Equal("keep", File.ReadAllText(outside));
    }

    [Fact]
    public void Enforce_failure_is_rethrown_unchanged_when_the_root_cannot_be_read()
    {
        // Elevated (the owner's runs, CI's admin runner) the deny entries cannot hide the root: other tests in this
        // process enable SeBackupPrivilege, which reads through them. The scenario only exists unelevated.
        if (Glance.Platform.Windows.DataDirectoryAcl.IsElevated()) return;
        using var dir = new TempDir();
        var parent = Directory.CreateDirectory(Path.Combine(dir.Path, "parent")).FullName;
        var paths = new GlancePaths(Path.Combine(parent, "Glance"));
        var me = WindowsIdentity.GetCurrent().User!;
        var denyRoot = new FileSystemAccessRule(me, FileSystemRights.ReadAttributes | FileSystemRights.ReadData, AccessControlType.Deny);
        var denyParent = new FileSystemAccessRule(me, FileSystemRights.ReadData, AccessControlType.Deny);   // no listing either
        Change(paths.Root, s => s.AddAccessRule(denyRoot));
        Change(parent, s => s.AddAccessRule(denyParent));
        try
        {
            Assert.ThrowsAny<Exception>(() => _ = new DirectoryInfo(paths.Root).Attributes);   // the precondition: no stat

            var ex = Assert.Throws<IOException>(() => DataDirectoryStartup.Enforce(paths.Root, shouldEnforce: true, Marker, Fail));

            Assert.StartsWith("boom", ex.Message);
        }
        finally
        {
            Change(parent, s => s.RemoveAccessRule(denyParent));
            Change(paths.Root, s => s.RemoveAccessRule(denyRoot));
        }
    }

    private static void Change(string path, Action<DirectorySecurity> change)
    {
        var info = new DirectoryInfo(path);
        var sec = info.GetAccessControl(AccessControlSections.Access);
        change(sec);
        info.SetAccessControl(sec);
    }

    [Fact]
    public void Nothing_is_enforced_when_not_asked()
    {
        using var dir = new TempDir();
        Assert.Null(DataDirectoryStartup.Enforce(new GlancePaths(dir.Path).Root, shouldEnforce: false, Marker, Fail));
    }

    [Fact]
    public void Open_hardens_and_enforces_the_root_before_any_subfolder_exists()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "Glance");
        var calls = new List<string>();
        var ok = new DataDirectoryAcl.AclResult(DataDirectoryAcl.Outcome.AlreadySecure, null, []);
        string[] Subfolders() => ["layouts", "assets", "logs"];
        bool AnySubfolder() => Subfolders().Any(s => Directory.Exists(Path.Combine(root, s)));

        var (paths, result) = DataDirectoryStartup.Open(root, shouldEnforce: true, Marker,
            r =>
            {
                calls.Add("create:" + r);
                Assert.False(Directory.Exists(r));   // nothing was created before the root got its hardened descriptor
                Directory.CreateDirectory(r);
                return true;
            },
            (r, m) =>
            {
                calls.Add("enforce:" + r);
                Assert.Same(Marker, m);
                Assert.False(AnySubfolder());   // enforce sees only what was already there
                return ok;
            });

        Assert.Equal(["create:" + root, "enforce:" + root], calls);
        Assert.Same(ok, result);
        Assert.Equal(root, paths.Root);
        Assert.All(Subfolders(), s => Assert.True(Directory.Exists(Path.Combine(root, s))));
    }

    [Fact]
    public void Open_creates_no_subfolder_when_enforce_fails()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "Glance");

        Assert.Throws<IOException>(() => DataDirectoryStartup.Open(root, shouldEnforce: true, Marker,
            r => { Directory.CreateDirectory(r); return true; }, Fail));

        Assert.False(Directory.Exists(Path.Combine(root, "layouts")));
        Assert.False(Directory.Exists(Path.Combine(root, "assets")));
    }

    [Fact]
    public void Open_without_enforcement_only_creates_the_folders()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "Glance");

        var (paths, result) = DataDirectoryStartup.Open(root, shouldEnforce: false, Marker,
            _ => throw new InvalidOperationException("must not harden"), Fail);

        Assert.Null(result);
        Assert.True(Directory.Exists(paths.Layouts));
        Assert.True(Directory.Exists(paths.Logs));
    }
}
