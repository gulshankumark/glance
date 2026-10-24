using System.Diagnostics;
using Glance.Core.Layouts;
using Glance.Platform.Windows;
using Glance.Testing;

namespace Glance.Service.Tests;

public class DataDirectoryStartupTests
{
    private static DataDirectoryAcl.AclResult Fail(string root) => throw new IOException("boom: the data directory is a link");

    [Fact]
    public void Enforce_failure_is_written_to_the_file_log_and_rethrown()
    {
        using var dir = new TempDir();
        var paths = new GlancePaths(dir.Path);

        var ex = Assert.Throws<IOException>(() => DataDirectoryStartup.Enforce(paths, shouldEnforce: true, Fail));

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

        Assert.Throws<IOException>(() => DataDirectoryStartup.Enforce(paths, shouldEnforce: true, Fail));

        Assert.Equal("keep", File.ReadAllText(outside));
    }

    [Fact]
    public void Nothing_is_enforced_when_not_asked()
    {
        using var dir = new TempDir();
        Assert.Null(DataDirectoryStartup.Enforce(new GlancePaths(dir.Path), shouldEnforce: false, Fail));
    }
}
