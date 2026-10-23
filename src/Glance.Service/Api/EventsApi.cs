using System.Text.Json;
using System.Threading.Channels;
using Glance.Core.Layouts;
using Glance.Core.Sensors;
using Glance.Core.Sessions;

namespace Glance.Service.Api;

public static class EventsApi
{
    public static void MapEventsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (HttpContext http, SensorHub hub, DeviceManager devices, SettingsStore settings, LayoutStore layouts, UiInterest ui) =>
        {
            var ct = http.RequestAborted;
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-store";

            using var interest = ui.OpenStream();
            var changed = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
            void OnDevicesChanged() => changed.Writer.TryWrite(true);
            devices.DevicesChanged += OnDevicesChanged;
            settings.Changed += OnDevicesChanged;   // rotation and active layout live in the settings
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                await Send(http, "sensors", SensorsPayload(hub), ct);
                var tick = timer.WaitForNextTickAsync(ct).AsTask();
                var dev = changed.Reader.WaitToReadAsync(ct).AsTask();
                while (!ct.IsCancellationRequested)
                {
                    var done = await Task.WhenAny(tick, dev);
                    if (done == dev)
                    {
                        if (!await dev) break;
                        changed.Reader.TryRead(out _);
                        await Send(http, "devices", JsonSerializer.Serialize(DevicesApi.Payload(devices, settings, layouts), ApiJson.Options), ct);
                        dev = changed.Reader.WaitToReadAsync(ct).AsTask();
                    }
                    else
                    {
                        if (!await tick) break;
                        await Send(http, "sensors", SensorsPayload(hub), ct);
                        tick = timer.WaitForNextTickAsync(ct).AsTask();
                    }
                }
            }
            catch (OperationCanceledException) { /* the client went away */ }
            finally
            {
                devices.DevicesChanged -= OnDevicesChanged;
                settings.Changed -= OnDevicesChanged;
            }
        });
    }

    private static string SensorsPayload(SensorHub hub)
    {
        var values = hub.Current.Values;
        var map = hub.Sensors.Select(s => s.Id).Distinct()
            .ToDictionary(id => id, id => values.TryGetValue(id, out var v) ? ApiJson.Finite(v) : null);
        return JsonSerializer.Serialize(map, ApiJson.Options);
    }

    private static async Task Send(HttpContext http, string name, string data, CancellationToken ct)
    {
        await http.Response.WriteAsync($"event: {name}\ndata: {data}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
