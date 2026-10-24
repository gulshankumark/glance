using Glance.Core.Layouts;
using Glance.Service;
using Glance.Service.Security;

namespace Glance.Service.Tests;

public class ServiceAddressTests
{
    [Fact]
    public void Https_uses_https_and_configured_port() =>
        Assert.Equal("https://localhost:9443/", ServiceAddress.LocalUrl(new AppSettings { Https = true, Port = 9443 }, StartupBinding.Configured).ToString());

    [Fact]
    public void Fallback_uses_http_8735_even_with_https_on() =>
        Assert.Equal("http://localhost:8735/", ServiceAddress.LocalUrl(new AppSettings { Https = true, Port = 9443 }, StartupBinding.LoopbackFallback).ToString());

    [Fact]
    public void Plain_http_uses_port() =>
        Assert.Equal("http://localhost:9000/", ServiceAddress.LocalUrl(new AppSettings { Port = 9000 }, StartupBinding.Configured).ToString());
}
