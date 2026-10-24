using Glance.Core.Layouts;
using Glance.Service.Security;

namespace Glance.Service;

/// <summary>The one place that knows where the web UI is reachable from this machine.</summary>
public static class ServiceAddress
{
    public static Uri LocalUrl(AppSettings s, StartupBinding binding) => binding switch
    {
        StartupBinding.LoopbackFallback => new Uri($"http://localhost:{AppSettings.DefaultPort}/"),
        _ => new Uri($"{(s.NetworkAccess && s.Https ? "https" : "http")}://localhost:{s.Port}/"),
    };
}
