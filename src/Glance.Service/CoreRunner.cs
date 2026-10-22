using Glance.Core.Layouts;
using Glance.Core.Sensors;
using Microsoft.Extensions.Hosting;

namespace Glance.Service;

/// <summary>Starts the always-on core pieces: layout file watching, sensor role pins and the 1 Hz sensor loop.</summary>
public sealed class CoreRunner(SensorHub hub, SettingsStore settings, LayoutStore layouts) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        void PushPins() => hub.SetPins(settings.Current.RolePins);
        PushPins();
        settings.Changed += PushPins;
        layouts.StartWatching();
        try { await hub.RunAsync(stoppingToken); }
        finally { settings.Changed -= PushPins; }
    }
}
