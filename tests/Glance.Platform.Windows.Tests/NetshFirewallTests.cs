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
}
