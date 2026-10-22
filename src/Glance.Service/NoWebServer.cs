using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;

namespace Glance.Service;

/// <summary>
/// Stands in for Kestrel when no web endpoint can be bound at startup, so the host (display sessions, sensors) still
/// runs: a screen must not go dark because the web UI cannot get a port.
/// </summary>
public sealed class NoWebServer : IServer
{
    public IFeatureCollection Features { get; } = new FeatureCollection();

    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() { }
}
