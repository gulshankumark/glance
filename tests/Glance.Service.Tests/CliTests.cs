using System.Net;
using System.Net.Sockets;
using Glance.Service;

namespace Glance.Service.Tests;

public class CliTests
{
    [Theory]
    [InlineData]
    [InlineData("--unknown")]
    [InlineData("run")]
    public void Unknown_args_return_null(params string[] args) => Assert.Null(Cli.TryRun(args));

    [Theory]
    [InlineData("--wait-ready")]
    [InlineData("--wait-ready", "soon")]
    [InlineData("--wait-ready", "-3")]
    public void Wait_ready_without_a_number_is_a_usage_error(params string[] args) => Assert.Equal(2, Cli.TryRun(args));

    [Fact]
    public async Task Wait_ready_times_out_with_exit_1()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();   // nothing listens here now
        Assert.Equal(1, await Cli.WaitReady(new Uri($"http://localhost:{port}/"), 1));
    }

    [Fact]
    public void ReadSettings_of_a_missing_dir_gives_defaults_and_creates_nothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "glance-cli-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(8735, Cli.ReadSettings(root).Port);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void ReadSettings_never_writes_repairs_or_corrupt_copies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "glance-cli-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(root, "settings.json");
            File.WriteAllText(file, """{"port":99999,"https":true}""");
            var s = Cli.ReadSettings(root);
            Assert.True(s.Https);
            Assert.Equal(8735, s.Port);
            Assert.Equal("""{"port":99999,"https":true}""", File.ReadAllText(file));
            Assert.Equal([file], Directory.GetFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
