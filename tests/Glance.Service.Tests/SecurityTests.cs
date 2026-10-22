using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Glance.Service.Security;

namespace Glance.Service.Tests;

public class SecurityTests
{
    private static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static async Task EnableNetwork(GlanceFactory f, object? body = null)
    {
        var local = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await local.PutAsync("/api/settings/password", Json(new { @new = "correct horse" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await local.PutAsync("/api/settings/network", Json(body ?? new { enabled = true }))).StatusCode);
    }

    [Fact]
    public async Task Loopback_needs_no_login_but_foreign_host_is_rejected()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sensors")).StatusCode);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/sensors"); req.Headers.Host = "evil.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1:8735")]
    [InlineData("[::1]:8735")]
    [InlineData("LOCALHOST")]
    public async Task Loopback_host_names_are_accepted(string host)
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/health"); req.Headers.Host = host;
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Machine_name_host_is_accepted()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/health"); req.Headers.Host = Environment.MachineName.ToLowerInvariant() + ":8735";
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(req)).StatusCode);
    }

    [Theory]
    [InlineData("/api/events")]
    [InlineData("/api/preview")]
    [InlineData("/")]
    public async Task Foreign_host_is_rejected_for_streams_and_static_files(string path)
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, path); req.Headers.Host = "attacker.example:8735";
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)).StatusCode);
    }

    [Fact]
    public async Task Cross_origin_post_from_browser_is_rejected()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = Json(new { unit = "fahrenheit" }) };
        req.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("http://localhost:9999")]
    [InlineData("http://evil.example")]
    public async Task Cross_origin_multipart_upload_and_delete_are_rejected(string origin)
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var upload = new HttpRequestMessage(HttpMethod.Post, "/api/assets")
        {
            Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.png" }, { new StringContent("image"), "kind" } },
        };
        upload.Headers.Add("Origin", origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(upload)).StatusCode);

        var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/layouts/Anything");
        delete.Headers.Add("Origin", origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(delete)).StatusCode);
    }

    [Fact]
    public async Task Same_origin_mutation_is_allowed()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = Json(new { unit = "fahrenheit" }) };
        req.Headers.Add("Origin", "http://localhost");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(req)).StatusCode);
    }

    [Theory]
    [InlineData("PUT", "/api/layouts/X", "text/plain")]
    [InlineData("POST", "/api/render", "text/plain")]
    [InlineData("POST", "/api/render", "application/x-www-form-urlencoded")]
    [InlineData("PUT", "/api/settings", "text/plain")]
    public async Task Mutating_api_requests_need_json_or_multipart(string method, string path, string contentType)
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient();
        var req = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent("{}", Encoding.UTF8, contentType) };
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Remote_denied_when_network_access_off_and_x_forwarded_for_ignored()
    {
        await using var f = new GlanceFactory(); var c = f.CreateClient(remoteIp: "192.168.1.50");
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/sensors"); req.Headers.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Enabling_network_requires_password_then_remote_must_login()
    {
        await using var f = new GlanceFactory(); var local = f.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await local.PutAsync("/api/settings/network", Json(new { enabled = true }))).StatusCode);
        await local.PutAsync("/api/settings/password", Json(new { @new = "correct horse" }));
        Assert.Equal(HttpStatusCode.OK, (await local.PutAsync("/api/settings/network", Json(new { enabled = true }))).StatusCode);
        Assert.Contains("0.0.0.0:8735", File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json")));

        var remote = f.CreateClient(remoteIp: "192.168.1.50");
        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.GetAsync("/api/sensors")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await remote.PostAsync("/api/auth/login", Json(new { password = "correct horse" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await remote.GetAsync("/api/sensors")).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_remote_gets_static_files_health_and_auth_state_only()
    {
        await using var f = new GlanceFactory(); await EnableNetwork(f);
        var remote = f.CreateClient(remoteIp: "192.168.1.50");
        Assert.Equal(HttpStatusCode.OK, (await remote.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await remote.GetAsync("/api/health")).StatusCode);
        var state = await remote.GetFromJsonAsync<JsonElement>("/api/auth/state");
        Assert.False(state.GetProperty("loopback").GetBoolean());
        Assert.False(state.GetProperty("authenticated").GetBoolean());
        Assert.True(state.GetProperty("networkAccess").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.PutAsync("/api/settings/network", Json(new { enabled = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.PostAsync("/api/auth/login", Json(new { password = "wrong password" }))).StatusCode);

        var localState = await f.CreateClient().GetFromJsonAsync<JsonElement>("/api/auth/state");
        Assert.True(localState.GetProperty("loopback").GetBoolean());
    }

    [Fact]
    public async Task Login_cookie_is_http_only_strict_and_logout_ends_the_session()
    {
        await using var f = new GlanceFactory(); await EnableNetwork(f);
        var remote = f.CreateClient(remoteIp: "192.168.1.50");
        var login = await remote.PostAsync("/api/auth/login", Json(new { password = "correct horse" }));
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("glance_auth=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie.Replace("samesite", ""), StringComparison.OrdinalIgnoreCase);   // plain HTTP request

        Assert.True((await remote.GetFromJsonAsync<JsonElement>("/api/auth/state")).GetProperty("authenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await remote.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await remote.GetAsync("/api/sensors")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_ends_other_sessions()
    {
        await using var f = new GlanceFactory(); var local = f.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await local.PutAsync("/api/settings/password", Json(new { @new = "short" }))).StatusCode);
        await EnableNetwork(f);
        var other = f.CreateClient(remoteIp: "192.168.1.60");
        await other.PostAsync("/api/auth/login", Json(new { password = "correct horse" }));
        var remote = f.CreateClient(remoteIp: "192.168.1.50");
        await remote.PostAsync("/api/auth/login", Json(new { password = "correct horse" }));

        Assert.Equal(HttpStatusCode.BadRequest, (await remote.PutAsync("/api/settings/password", Json(new { @new = "battery staple" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.PutAsync("/api/settings/password", Json(new { current = "wrong one", @new = "battery staple" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await remote.PutAsync("/api/settings/password", Json(new { current = "correct horse", @new = "battery staple" }))).StatusCode);

        Assert.True(PasswordHasher.Verify("battery staple", f.Settings.Current.PasswordHash));
        Assert.Equal(HttpStatusCode.OK, (await remote.GetAsync("/api/sensors")).StatusCode);         // the changer stays signed in
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/sensors")).StatusCode);  // everyone else is signed out
    }

    [Fact]
    public async Task Network_port_is_validated_and_disabling_restores_loopback_endpoints()
    {
        await using var f = new GlanceFactory(); var local = f.CreateClient();
        await local.PutAsync("/api/settings/password", Json(new { @new = "correct horse" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await local.PutAsync("/api/settings/network", Json(new { enabled = true, port = 80 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await local.PutAsync("/api/settings/network", Json(new { enabled = true, port = 70000 }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await local.PutAsync("/api/settings/network", Json(new { enabled = true, port = 9000 }))).StatusCode);
        var kestrel = File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json"));
        Assert.Contains("http://0.0.0.0:9000", kestrel);
        Assert.Contains("http://[::]:9000", kestrel);
        Assert.Equal(9000, f.Settings.Current.Port);

        Assert.Equal(HttpStatusCode.OK, (await local.PutAsync("/api/settings/network", Json(new { enabled = false }))).StatusCode);
        kestrel = File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json"));
        Assert.Contains("http://127.0.0.1:9000", kestrel);
        Assert.Contains("http://[::1]:9000", kestrel);
        Assert.DoesNotContain("0.0.0.0", kestrel);
        Assert.False(f.Settings.Current.NetworkAccess);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.CreateClient(remoteIp: "192.168.1.50").GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Https_creates_a_self_signed_certificate_once_and_points_kestrel_at_it()
    {
        await using var f = new GlanceFactory();
        await EnableNetwork(f, new { enabled = true, https = true });
        var pfx = Path.Combine(f.DataDir, "https.pfx");
        var password = f.Settings.Current.HttpsPfxPassword;
        Assert.False(string.IsNullOrEmpty(password));
        using (var cert = new X509Certificate2(pfx, password))
        {
            Assert.True(cert.HasPrivateKey);
            Assert.Equal(2048, cert.GetRSAPublicKey()!.KeySize);
            Assert.InRange(cert.NotAfter, DateTime.Now.AddYears(5).AddDays(-2), DateTime.Now.AddYears(5).AddDays(2));
            var san = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames().ToList();
            Assert.Contains("localhost", san);
            Assert.Contains(Environment.MachineName, san, StringComparer.OrdinalIgnoreCase);
        }
        var kestrel = JsonDocument.Parse(File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json"))).RootElement.GetProperty("Kestrel").GetProperty("Endpoints");
        var endpoint = kestrel.EnumerateObject().First().Value;
        Assert.Equal("https://0.0.0.0:8735", endpoint.GetProperty("Url").GetString());
        Assert.Equal(pfx, endpoint.GetProperty("Certificate").GetProperty("Path").GetString());
        Assert.Equal(password, endpoint.GetProperty("Certificate").GetProperty("Password").GetString());

        var bytes = File.ReadAllBytes(pfx);
        await f.CreateClient().PutAsync("/api/settings/network", Json(new { enabled = true, https = true }));
        Assert.Equal(bytes, File.ReadAllBytes(pfx));   // reused, not regenerated
        Assert.Equal(password, f.Settings.Current.HttpsPfxPassword);
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        await using var f = new GlanceFactory(); f.Settings.Update(s => { s.PasswordHash = PasswordHasher.Hash("correct horse"); s.NetworkAccess = true; });
        var remote = f.CreateClient(remoteIp: "192.168.1.51");
        for (int i = 0; i < 5; i++) await remote.PostAsync("/api/auth/login", Json(new { password = "wrong" }));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await remote.PostAsync("/api/auth/login", Json(new { password = "wrong" }))).StatusCode);
        // another address has its own budget
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient(remoteIp: "192.168.1.52").PostAsync("/api/auth/login", Json(new { password = "correct horse" }))).StatusCode);
    }
}
