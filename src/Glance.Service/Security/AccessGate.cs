using System.Net;
using System.Net.NetworkInformation;
using Glance.Core.Layouts;
using Microsoft.Net.Http.Headers;

namespace Glance.Service.Security;

/// <summary>
/// Runs before static files and endpoints. Rejects foreign Host headers (DNS rebinding) and cross-origin or
/// non-JSON/multipart mutations; lets loopback callers through; requires network access and a login for everyone else.
/// The caller's address is the socket's: forwarded headers are never honoured.
/// </summary>
public sealed class AccessGate(RequestDelegate next, SettingsStore settings)
{
    private static readonly PathString Api = "/api";
    private static readonly PathString[] OpenToAnonymous = ["/api/auth/login", "/api/auth/state", "/api/health"];

    public Task InvokeAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        if (!IsAllowedHost(req.Host)) return Deny(ctx, StatusCodes.Status403Forbidden, "host not allowed");

        if (IsMutating(req.Method))
        {
            if (req.Headers.Origin.Count > 0 && !IsSameOrigin(req.Headers.Origin.ToString(), req))
                return Deny(ctx, StatusCodes.Status403Forbidden, "cross-origin request");
            if (req.Path.StartsWithSegments(Api) && HasBody(req) && !IsJsonOrMultipart(req.ContentType))
                return Deny(ctx, StatusCodes.Status415UnsupportedMediaType, "content type must be application/json or multipart/form-data");
        }

        if (IsLoopback(ctx.Connection.RemoteIpAddress)) return next(ctx);
        if (!settings.Current.NetworkAccess) return Deny(ctx, StatusCodes.Status403Forbidden, "network access is disabled");
        if (req.Path.StartsWithSegments(Api) && ctx.User.Identity?.IsAuthenticated != true
            && !OpenToAnonymous.Any(p => req.Path.Equals(p, StringComparison.OrdinalIgnoreCase)))
            return Deny(ctx, StatusCodes.Status401Unauthorized, "login required");
        return next(ctx);
    }

    public static bool IsLoopback(IPAddress? ip) =>
        ip is not null && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);

    private static Task Deny(HttpContext ctx, int status, string error)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error });
    }

    private static bool IsMutating(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    private static bool HasBody(HttpRequest req) =>
        req.ContentLength > 0 || req.ContentType is not null || req.Headers.TransferEncoding.Count > 0;

    private static bool IsJsonOrMultipart(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var mt)
        && (mt.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mt.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase));

    private static bool IsSameOrigin(string origin, HttpRequest req)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var requestPort = req.Host.Port ?? (req.IsHttps ? 443 : 80);
        return uri.Host.Equals(req.Host.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == requestPort;
    }

    private static bool IsAllowedHost(HostString host)
    {
        var name = host.Host;
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(name.Trim('[', ']'), out var ip)) return false;
        if (IsLoopback(ip)) return true;
        var bytes = ip.GetAddressBytes();
        return MachineAddresses.Current().Any(a => a.GetAddressBytes().AsSpan().SequenceEqual(bytes));
    }
}

/// <summary>The machine's current unicast addresses, refreshed at most every 30 seconds.</summary>
public static class MachineAddresses
{
    private static readonly object Gate = new();
    private static IPAddress[] _cached = [];
    private static long _expires;

    public static IReadOnlyList<IPAddress> Current()
    {
        lock (Gate)
        {
            if (Environment.TickCount64 < _expires) return _cached;
            try
            {
                _cached = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Select(u => u.Address)
                    .ToArray();
            }
            catch (NetworkInformationException) { /* keep the last known list */ }
            _expires = Environment.TickCount64 + 30_000;
            return _cached;
        }
    }
}
