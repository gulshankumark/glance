using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Glance.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Glance.Service.Tests;

public class ApiTests
{
    // "type" deliberately comes after other properties: API bodies may order properties freely.
    private static string SampleLayout(string name, int windowSeconds = 60, int width = 1280, int height = 480) => $$"""
        {"name":"{{name}}","width":{{width}},"height":{{height}},"background":{"color":"#101010","type":"color"},
         "items":[{"id":"g","x":10,"y":10,"width":300,"height":100,"sensor":"role:cpu.temperature","windowSeconds":{{windowSeconds}},"type":"historyGraph"}]}
        """;

    private static byte[] Gif2Bytes => Convert.FromHexString(TestMedia.Gif2);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static StringContent Json(object o) => Json(JsonSerializer.Serialize(o));

    private static MultipartFormDataContent Upload(string fileName, byte[] data, string kind)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(data), "file", fileName }, { new StringContent(kind), "kind" } };
        return form;
    }

    /// <summary>The device is found by a background scan after the host starts, so wait for it.</summary>
    private static async Task<string> FirstDeviceKey(HttpClient c)
    {
        for (var i = 0; i < 100; i++)
        {
            var devices = await c.GetFromJsonAsync<JsonElement>("/api/devices");
            if (devices.GetArrayLength() > 0) return devices[0].GetProperty("key").GetString()!;
            await Task.Delay(100);
        }
        throw new TimeoutException("no device appeared");
    }

    [Fact]
    public async Task Layout_crud_and_validation()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync("/api/layouts/Gaming", Json(SampleLayout("Gaming")))).StatusCode);
        Assert.Contains("Gaming", await c.GetStringAsync("/api/layouts"));
        var got = await c.GetFromJsonAsync<JsonElement>("/api/layouts/Gaming");
        Assert.Equal("Gaming", got.GetProperty("name").GetString());
        var bad = await c.PutAsync("/api/layouts/Bad", Json(SampleLayout("Bad", windowSeconds: 4000)));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("windowSeconds", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0].GetString());
        Assert.False(File.Exists(Path.Combine(f.DataDir, "layouts", "Bad.json")));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/layouts/..%2Fevil", Json(SampleLayout("x")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/layouts/..%2Fevil", Json(SampleLayout("../evil")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/layouts/..%2Fevil")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync("/api/layouts/..%2Fevil")).StatusCode);
        Assert.False(File.Exists(Path.Combine(f.DataDir, "evil.json")));
    }

    [Fact]
    public async Task Layout_put_name_must_match_body_and_json_must_parse()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/layouts/One", Json(SampleLayout("Two")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/layouts/One", Json("{not json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/layouts/One", Json("null"))).StatusCode);
        // The layouts dir holds only the shipped themes seeded at startup; no rejected PUT wrote a file.
        var files = Directory.GetFiles(Path.Combine(f.DataDir, "layouts")).Select(Path.GetFileNameWithoutExtension).Order();
        Assert.Equal(new[] { "Blend", "Clean", "Gauges", "Live graphs" }, files);
    }

    [Fact]
    public async Task Layout_duplicate_and_delete()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        await c.PutAsync("/api/layouts/A", Json(SampleLayout("A")));
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/layouts/A/duplicate", Json(new { newName = "B" }))).StatusCode);
        Assert.Equal("B", (await c.GetFromJsonAsync<JsonElement>("/api/layouts/B")).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/layouts/A/duplicate", Json(new { newName = "B" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/layouts/A/duplicate", Json(new { newName = "../x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/layouts/Nope/duplicate", Json(new { newName = "C" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/layouts/B")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/layouts/B")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/layouts/B")).StatusCode);
    }

    [Fact]
    public async Task Device_rotation_and_active_layout()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        await c.PutAsync("/api/layouts/Gaming", Json(SampleLayout("Gaming")));
        var key = await FirstDeviceKey(c);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/devices/{key}", Json(new { rotation = 180, activeLayout = "Gaming" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync($"/api/devices/{key}", Json(new { rotation = 45 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync($"/api/devices/{key}", Json(new { activeLayout = "Nope" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsync("/api/devices/unknown", Json(new { rotation = 0 }))).StatusCode);
        var d = (await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0];
        Assert.Equal(180, d.GetProperty("rotation").GetInt32());
        Assert.Equal("Gaming", d.GetProperty("activeLayout").GetString());
        Assert.False(d.GetProperty("layoutMismatch").GetBoolean());
        Assert.Equal("connected", d.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Device_calibration_round_trips_and_is_validated()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        var key = await FirstDeviceKey(c);
        var d = (await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0];
        Assert.Equal(1.0, d.GetProperty("gamma").GetDouble());
        Assert.Equal(100, d.GetProperty("brightness").GetInt32());

        var ok = await c.PutAsync($"/api/devices/{key}", Json(new { gamma = 2.2, brightness = 80 }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(2.2, (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gamma").GetDouble());
        d = (await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0];
        Assert.Equal(2.2, d.GetProperty("gamma").GetDouble());
        Assert.Equal(80, d.GetProperty("brightness").GetInt32());

        foreach (var (body, error) in new[]
        {
            ("""{"gamma":0}""", "gamma must be 0.5..3"),
            ("""{"gamma":3.5}""", "gamma must be 0.5..3"),
            ("""{"gamma":"NaN"}""", "gamma must be 0.5..3"),
            ("""{"gamma":"Infinity"}""", "gamma must be 0.5..3"),
            ("""{"brightness":5}""", "brightness must be 10..100"),
            ("""{"brightness":101}""", "brightness must be 10..100"),
        })
        {
            var r = await c.PutAsync($"/api/devices/{key}", Json(body));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(error, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync($"/api/devices/{key}", Json("""{"gamma":"bright"}"""))).StatusCode);
        Assert.Equal(2.2, f.Settings.Current.Devices[key].Gamma);
        Assert.Equal(80, f.Settings.Current.Devices[key].Brightness);

        // Numbers as strings are read like rotation and brightness strings.
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/devices/{key}", Json("""{"gamma":"1.8","brightness":"90"}"""))).StatusCode);
        Assert.Equal(1.8, f.Settings.Current.Devices[key].Gamma);
        Assert.Equal(90, f.Settings.Current.Devices[key].Brightness);
    }

    [Fact]
    public async Task Test_pattern_starts_and_stops_and_unknown_device_is_404()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        var key = await FirstDeviceKey(c);
        for (var i = 0; i < 100 && f.Services.GetRequiredService<Glance.Core.Sessions.DeviceManager>().Session(key) is null; i++) await Task.Delay(50);
        // No active layout: the fallback screen is black at the top right, where the pattern's white step is.
        async Task<bool> TopRightWhite(bool expected)
        {
            for (var i = 0; i < 100; i++)
            {
                var frames = f.Connection.Frames;
                if (frames.Count > 0)
                {
                    using var bmp = SkiaSharp.SKBitmap.Decode(frames[^1].Data.ToArray());
                    if (bmp.GetPixel(1274, 5).Red > 240 == expected) return true;
                }
                await Task.Delay(50);
            }
            return false;
        }
        Assert.True(await TopRightWhite(false));
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/devices/{key}/test-pattern", null)).StatusCode);
        Assert.True(await TopRightWhite(true));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/devices/{key}/test-pattern")).StatusCode);
        Assert.True(await TopRightWhite(false));
        var unknown = await c.PostAsync("/api/devices/unknown/test-pattern", null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("unknown device", (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/devices/unknown/test-pattern")).StatusCode);
    }

    [Fact]
    public async Task Render_is_not_affected_by_device_calibration()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        var key = await FirstDeviceKey(c);
        async Task<byte[]> Render() =>
            await (await c.PostAsync($"/api/render?device={key}", Json(SampleLayout("Draft")))).Content.ReadAsByteArrayAsync();
        var before = await Render();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/devices/{key}", Json(new { gamma = 2.5, brightness = 40 }))).StatusCode);
        Assert.Equal(before, await Render());
    }

    [Fact]
    public async Task Devices_report_fps()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        var key = await FirstDeviceKey(c);
        for (var i = 0; i < 100 && f.Connection.Frames.Count == 0; i++) await Task.Delay(50);
        var d = (await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0];
        Assert.Equal(key, d.GetProperty("key").GetString());
        Assert.True(d.GetProperty("framesSent").GetInt32() >= 1);
        Assert.True(d.GetProperty("fps").GetDouble() > 0);
    }

    [Fact]
    public async Task Devices_without_session_report_zero_fps()
    {
        await using var f = new GlanceFactory(withDevice: true);
        f.Driver.Always = () => new Glance.Core.Devices.ConnectResult.Failed("busy");
        var c = f.CreateClient();
        await FirstDeviceKey(c);
        var d = (await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0];
        Assert.Equal(0, d.GetProperty("fps").GetDouble());
        Assert.Equal(0, d.GetProperty("framesSent").GetInt32());
    }

    [Fact]
    public async Task Put_device_with_null_activeLayout_clears_it_but_absent_keeps_it()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        await c.PutAsync("/api/layouts/Gaming", Json(SampleLayout("Gaming")));
        var key = await FirstDeviceKey(c);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/devices/{key}", Json(new { activeLayout = "Gaming" }))).StatusCode);

        var kept = await c.PutAsync($"/api/devices/{key}", Json("""{"rotation":180}"""));
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal("Gaming", (await kept.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("activeLayout").GetString());
        Assert.Equal("Gaming", f.Settings.Current.Devices[key].ActiveLayout);

        var cleared = await c.PutAsync($"/api/devices/{key}", Json("""{"activeLayout":null}"""));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var body = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("activeLayout").ValueKind);
        Assert.Equal(180, body.GetProperty("rotation").GetInt32());
        Assert.Null(f.Settings.Current.Devices[key].ActiveLayout);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync($"/api/devices/{key}", Json("""{"activeLayout":5}"""))).StatusCode);
    }

    [Fact]
    public async Task Settings_change_emits_devices_event()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        var key = await FirstDeviceKey(c);
        using var cts = new CancellationTokenSource();
        var resp = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(cts.Token));
        Assert.Equal("event: sensors", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/devices/{key}", Json(new { rotation = 90 }))).StatusCode);

        // Device-list changes may also produce devices events; wait for the one carrying the new rotation.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string? data = null;
        while (DateTime.UtcNow < deadline && data?.Contains("\"rotation\":90") != true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (line == "event: devices") data = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Contains("\"rotation\":90", data);
        cts.Cancel();
    }

    [Fact]
    public async Task Layout_size_not_matching_rotated_canvas_is_flagged()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        await c.PutAsync("/api/layouts/Wide", Json(SampleLayout("Wide")));   // 1280x480
        var key = await FirstDeviceKey(c);
        await c.PutAsync($"/api/devices/{key}", Json(new { rotation = 90, activeLayout = "Wide" }));   // canvas is 480x1280
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0].GetProperty("layoutMismatch").GetBoolean());
        await c.PutAsync($"/api/devices/{key}", Json(new { rotation = 0 }));
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0].GetProperty("layoutMismatch").GetBoolean());
    }

    [Fact]
    public async Task Render_endpoint_returns_jpeg_without_touching_device()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        var key = await FirstDeviceKey(c);
        var r = await c.PostAsync($"/api/render?device={key}", Json(SampleLayout("Draft", width: 640, height: 240)));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/jpeg", r.Content.Headers.ContentType!.MediaType);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.Equal(0xFF, bytes[0]);
        using var bmp = SkiaSharp.SKBitmap.Decode(bytes);
        Assert.Equal((640, 240), (bmp.Width, bmp.Height));
        Assert.False(File.Exists(Path.Combine(f.DataDir, "layouts", "Draft.json")));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/render", Json(SampleLayout("Bad", windowSeconds: 0)))).StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_garbage_and_accepts_gif()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/assets", Upload("evil.gif", "MZ\u0090\0"u8.ToArray(), "gif"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/assets", Upload("a.gif", Gif2Bytes, "bogus"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/assets", Json("{}"))).StatusCode);
        var ok = await c.PostAsync("/api/assets", Upload("a.gif", Gif2Bytes, "gif"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var info = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, info.GetProperty("frameCount").GetInt32());
        var id = info.GetProperty("id").GetString();
        Assert.Contains(id!, await c.GetStringAsync("/api/assets"));

        var thumb = await c.GetAsync($"/api/assets/{id}/thumbnail.jpg");
        Assert.Equal("image/jpeg", thumb.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/assets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/assets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/assets/..%2F..")).StatusCode);
    }

    [Fact]
    public async Task Kind_comes_from_field_not_file_name()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        // The declared kind decides, not the extension: GIF bytes declared as zt are rejected.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/assets", Upload("a.gif", Gif2Bytes, "zt"))).StatusCode);
    }

    [Fact]
    public async Task Thumbnail_is_at_most_320_wide()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        using var bmp = new SkiaSharp.SKBitmap(1000, 500);
        bmp.Erase(SkiaSharp.SKColors.Red);
        using var png = SkiaSharp.SKImage.FromBitmap(bmp).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var ok = await c.PostAsync("/api/assets", Upload("big.png", png.ToArray(), "image"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var id = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var bytes = await c.GetByteArrayAsync($"/api/assets/{id}/thumbnail.jpg");
        using var thumb = SkiaSharp.SKBitmap.Decode(bytes);
        Assert.Equal((320, 160), (thumb.Width, thumb.Height));
        Assert.Equal(File.ReadAllBytes(Path.Combine(f.DataDir, "assets", id!, "thumb.jpg")), bytes);   // the stored thumbnail, as is
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/assets/{Guid.NewGuid():N}/thumbnail.jpg")).StatusCode);
    }

    [Fact]
    public async Task Settings_never_leak_password_hash()
    {
        await using var f = new GlanceFactory(); f.Settings.Update(s => { s.PasswordHash = "pbkdf2-sha256$1$AA==$AA=="; s.HttpsPfxPassword = "secret-pfx"; });
        var json = await f.CreateClient().GetStringAsync("/api/settings");
        Assert.DoesNotContain("pbkdf2", json);
        Assert.DoesNotContain("secret-pfx", json);
        Assert.Contains("\"hasPassword\":true", json);
    }

    [Fact]
    public async Task Settings_put_changes_unit_and_clock_only()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync("/api/settings", Json(new { unit = "fahrenheit", clock24h = false, port = 1, networkAccess = true }))).StatusCode);
        Assert.Equal(Glance.Core.Layouts.TemperatureUnit.Fahrenheit, f.Settings.Current.Unit);
        Assert.False(f.Settings.Current.Clock24h);
        Assert.Equal(8735, f.Settings.Current.Port);
        Assert.False(f.Settings.Current.NetworkAccess);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/settings", Json(new { unit = "kelvin" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/settings", Json(new { unit = 7 }))).StatusCode);
    }

    [Fact]
    public async Task Settings_get_and_put_overlay_fps_cap_including_null()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        Assert.Contains("\"maxOverlayAnimationFps\":null", await c.GetStringAsync("/api/settings"));
        var put = await c.PutAsync("/api/settings", Json("""{"maxOverlayAnimationFps":12}"""));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Contains("\"maxOverlayAnimationFps\":12", await put.Content.ReadAsStringAsync());
        Assert.Equal(12, f.Settings.Current.MaxOverlayAnimationFps);
        Assert.Contains("\"maxOverlayAnimationFps\":12", await c.GetStringAsync("/api/settings"));
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync("/api/settings", Json(new { unit = "fahrenheit" }))).StatusCode);
        Assert.Equal(12, f.Settings.Current.MaxOverlayAnimationFps);   // absent: left as it is
        foreach (var bad in new[] { "0", "61", "12.5", "\"12\"" })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await c.PutAsync("/api/settings", Json($$"""{"maxOverlayAnimationFps":{{bad}}}"""))).StatusCode);
        Assert.Equal(12, f.Settings.Current.MaxOverlayAnimationFps);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsync("/api/settings", Json("""{"maxOverlayAnimationFps":null}"""))).StatusCode);
        Assert.Null(f.Settings.Current.MaxOverlayAnimationFps);
        Assert.Contains("\"maxOverlayAnimationFps\":null", await c.GetStringAsync("/api/settings"));
    }

    [Fact]
    public async Task Sensors_list_aliases_and_role_pins()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var list = await c.GetFromJsonAsync<JsonElement>("/api/sensors");
        Assert.Equal("cpu/t", list.GetProperty("sensors")[0].GetProperty("id").GetString());
        Assert.Equal("temperature", list.GetProperty("sensors")[0].GetProperty("kind").GetString());
        Assert.Equal("cpu/t", list.GetProperty("roles").GetProperty("cpu.temperature").GetString());
        Assert.Equal(JsonValueKind.Null, list.GetProperty("roles").GetProperty("gpu.load").ValueKind);
        Assert.Equal(JsonValueKind.Array, list.GetProperty("diagnostics").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/sensors/alias", Json(new { id = "cpu/t", alias = "CPU Temp" }))).StatusCode);
        Assert.Equal("CPU Temp", f.Settings.Current.Aliases["cpu/t"]);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/sensors/alias", Json(new { id = "/amdcpu/0/temperature/2", alias = "Die" }))).StatusCode);
        Assert.Equal("Die", f.Settings.Current.Aliases["/amdcpu/0/temperature/2"]);
        var notFound = await c.PutAsync("/api/sensors/alias", Json(new { id = "nope", alias = "x" }));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Contains("unknown sensor", await notFound.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/sensors/alias", Json(new { id = "cpu/t", alias = (string?)null }))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/sensors/alias", Json(new { id = "/amdcpu/0/temperature/2", alias = "" }))).StatusCode);
        Assert.Empty(f.Settings.Current.Aliases);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/roles/gpu.load", Json(new { sensorId = "cpu/t" }))).StatusCode);
        Assert.Equal("cpu/t", f.Settings.Current.RolePins["gpu.load"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/roles/gpu.load", Json(new { sensorId = "nope" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsync("/api/roles/bogus", Json(new { sensorId = (string?)null }))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/roles/gpu.load", Json(new { sensorId = (string?)null }))).StatusCode);
        Assert.Empty(f.Settings.Current.RolePins);
    }

    [Fact]
    public async Task Sensors_list_shows_persisted_role_pins()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var before = await c.GetFromJsonAsync<JsonElement>("/api/sensors");
        Assert.Empty(before.GetProperty("pins").EnumerateObject());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/roles/gpu.load", Json(new { sensorId = "cpu/t" }))).StatusCode);
        var pinned = await c.GetFromJsonAsync<JsonElement>("/api/sensors");
        Assert.Equal("cpu/t", pinned.GetProperty("pins").GetProperty("gpu.load").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsync("/api/roles/gpu.load", Json(new { sensorId = (string?)null }))).StatusCode);
        var after = await c.GetFromJsonAsync<JsonElement>("/api/sensors");
        Assert.False(after.GetProperty("pins").TryGetProperty("gpu.load", out _));
    }

    [Fact]
    public async Task Preview_is_404_until_rendered_then_jpeg_no_store()
    {
        await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/preview/unknown.jpg")).StatusCode);
        var key = await FirstDeviceKey(c);
        HttpResponseMessage r = null!;
        for (var i = 0; i < 100; i++)
        {
            r = await c.GetAsync($"/api/preview/{key}.jpg");
            if (r.StatusCode == HttpStatusCode.OK) break;
            await Task.Delay(100);
        }
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/jpeg", r.Content.Headers.ContentType!.MediaType);
        Assert.True(r.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Logs_zip_contains_locked_log_files()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var path = Path.Combine(f.DataDir, "logs", "test-locked.log");
        using (var locked = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            locked.Write("hello log"u8); locked.Flush();
            using var zip = new ZipArchive(new MemoryStream(await c.GetByteArrayAsync("/api/logs")));
            using var text = new StreamReader(zip.GetEntry("test-locked.log")!.Open());
            Assert.Equal("hello log", await text.ReadToEndAsync());
        }
    }

    [Fact]
    public async Task Events_stream_sends_sensor_event()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        using var s = await c.GetStreamAsync("/api/events");
        var reader = new StreamReader(s);
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("event: sensors", line);
        Assert.StartsWith("data: {\"cpu/t\":", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Events_stream_sends_devices_event_and_stops_on_disconnect()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        using var cts = new CancellationTokenSource();
        var resp = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(cts.Token));
        await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        f.Usb.Add(new Glance.Core.Devices.UsbDeviceInfo(0x1234, 0x0001, "fake-path", 37, 513, "Fake"));
        await f.Services.GetRequiredService<Glance.Core.Sessions.DeviceManager>().RescanAsync();
        string? line = null;
        for (var i = 0; i < 20 && line != "event: devices"; i++) line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("event: devices", line);
        cts.Cancel();   // must not hang or throw on the server side; the factory disposes cleanly
    }

    [Fact]
    public async Task Oversized_upload_is_413()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        // Test host limit is 64 KB (production: 64 MB). Just over the file limit, and far over the body limit.
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.PostAsync("/api/assets", Upload("a.gif", new byte[65537], "gif"))).StatusCode);
        var huge = await c.PostAsync("/api/assets", Upload("a.gif", new byte[300_000], "gif"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        Assert.Contains("too large", await huge.Content.ReadAsStringAsync());
        Assert.Equal(0, (await c.GetFromJsonAsync<JsonElement>("/api/assets")).GetArrayLength());
    }

    [Fact]
    public async Task Open_event_stream_makes_every_sensor_live_and_releases_on_disconnect()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var ui = f.Services.GetRequiredService<UiInterest>();
        var hub = f.Services.GetRequiredService<Glance.Core.Sensors.SensorHub>();
        Assert.False(ui.Active);

        using var cts = new CancellationTokenSource();
        var resp = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(cts.Token));
        await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(ui.Active);
        Assert.Equal(1, ui.OpenStreams);

        hub.Tick();
        Assert.Equal(50, hub.Current.Values["cpu/t"]);
        Assert.Equal(61, hub.Current.Values["/amdcpu/0/temperature/2"]);

        resp.Dispose();   // closing the response is how a TestServer client disconnects
        for (var i = 0; i < 100 && ui.OpenStreams > 0; i++) await Task.Delay(50);
        Assert.Equal(0, ui.OpenStreams);
        Assert.False(ui.Active);
        hub.Tick();
        Assert.Empty(hub.Current.Values);
    }

    [Fact]
    public async Task Concurrent_streams_are_ref_counted()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var ui = f.Services.GetRequiredService<UiInterest>();
        using var a = new CancellationTokenSource();
        using var b = new CancellationTokenSource();
        var ra = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, a.Token);
        var rb = await c.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, b.Token);
        var reader = new StreamReader(await rb.Content.ReadAsStreamAsync(b.Token));
        await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await new StreamReader(await ra.Content.ReadAsStreamAsync(a.Token)).ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, ui.OpenStreams);
        ra.Dispose();
        for (var i = 0; i < 100 && ui.OpenStreams > 1; i++) await Task.Delay(50);
        Assert.Equal(1, ui.OpenStreams);
        Assert.True(ui.Active);
        rb.Dispose();
        for (var i = 0; i < 100 && ui.Active; i++) await Task.Delay(50);
        Assert.False(ui.Active);
    }

    [Fact]
    public async Task Sensors_and_render_requests_hold_a_lease_that_expires()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var ui = f.Services.GetRequiredService<UiInterest>();
        var hub = f.Services.GetRequiredService<Glance.Core.Sensors.SensorHub>();
        ui.Lease = TimeSpan.FromMilliseconds(300);
        await c.GetStringAsync("/api/sensors");
        Assert.True(ui.Active);
        hub.Tick();
        Assert.Equal(50, hub.Current.Values["cpu/t"]);
        for (var i = 0; i < 100 && ui.Active; i++) await Task.Delay(50);
        Assert.False(ui.Active);
        await c.PostAsync("/api/render", Json(SampleLayout("Draft")));
        Assert.True(ui.Active);
        for (var i = 0; i < 100 && ui.Active; i++) await Task.Delay(50);
        Assert.False(ui.Active);
    }
}
