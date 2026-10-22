using System.Reflection;

namespace Glance.Service.Api;

public static class HealthApi
{
    public static void MapHealthApi(this IEndpointRouteBuilder app)
    {
        var version = typeof(HealthApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? "unknown";
        app.MapGet("/api/health", () => new { status = "ok", version });
    }
}
