using Glance.Platform.Windows;

namespace Glance.Platform.Windows.Tests;

public class NetshFirewallTests
{
    private const string Sample =
        "\r\nRule Name:                            Glance web UI\r\n" +
        "----------------------------------------------------------------------\r\n" +
        "Enabled:                              Yes\r\n" +
        "Direction:                            In\r\n" +
        "Profiles:                             Domain,Private,Public\r\n" +
        "Grouping:\r\n" +
        "LocalIP:                              Any\r\n" +
        "RemoteIP:                             Any\r\n" +
        "Protocol:                             TCP\r\n" +
        "LocalPort:                            8735\r\n" +
        "RemotePort:                           Any\r\n" +
        "Edge traversal:                       No\r\n" +
        "Action:                               Allow\r\nOk.\r\n";

    [Fact] public void Parses_local_port() => Assert.Equal(8735, NetshFirewall.ParseLocalPort(Sample));

    [Fact] public void No_match_is_null() =>
        Assert.Null(NetshFirewall.ParseLocalPort("No rules match the specified criteria.\r\n"));

    [Fact] public void Any_is_null() => Assert.Null(NetshFirewall.ParseLocalPort("LocalPort:   Any\r\n"));

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, true)]   // delete failed, rule not showable: already gone
    [InlineData(1, 0, false)]  // delete failed, rule still there
    public void Delete_succeeded_uses_exit_codes_only(int del, int show, bool ok) =>
        Assert.Equal(ok, NetshFirewall.DeleteSucceeded(del, show));

    [Fact]
    public void Run_times_out_on_a_hung_process()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() =>
            NetshFirewall.Run("ping.exe", ["-n", "30", "127.0.0.1"], TimeSpan.FromSeconds(1)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }

    [Fact]
    public void Run_returns_exit_code_and_output()
    {
        var (code, output) = NetshFirewall.Run("cmd.exe", ["/c", "echo hi"], TimeSpan.FromSeconds(10));
        Assert.Equal(0, code);
        Assert.Contains("hi", output);
    }
}
