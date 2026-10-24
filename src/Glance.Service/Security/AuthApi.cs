using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Glance.Core.Layouts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Glance.Service.Security;

public static class AuthApi
{
    public const string Scheme = "glance";
    private const string LoginPolicy = "login";
    private const string StampClaim = "glance:pwd";
    private const int MinPasswordLength = 8;

    public sealed record LoginBody(string? Password);
    public sealed record PasswordBody(string? Current, string? New);
    public sealed record NetworkBody(bool? Enabled, bool? Https, int? Port);

    /// <summary>Cookie login, its data-protection keys and the login rate limit.</summary>
    public static void AddGlanceSecurity(this IServiceCollection services, GlancePaths paths)
    {
        services.AddDataProtection()
            .SetApplicationName("Glance")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.Root, "keys")))
            .ProtectKeysWithDpapi(protectToLocalMachine: true);

        services.AddAuthentication(Scheme).AddCookie(Scheme, o =>
        {
            o.Cookie.Name = "glance_auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(7);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            // A cookie issued before the password last changed is no longer valid.
            o.Events.OnValidatePrincipal = c =>
            {
                var hash = c.HttpContext.RequestServices.GetRequiredService<SettingsStore>().Current.PasswordHash;
                if (hash is null || c.Principal?.FindFirstValue(StampClaim) != Stamp(hash)) c.RejectPrincipal();
                return Task.CompletedTask;
            };
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public static void MapAuthApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginBody body, HttpContext ctx, SettingsStore settings) =>
        {
            var hash = settings.Current.PasswordHash;
            if (hash is null || body.Password is null || !PasswordHasher.Verify(body.Password, hash))
                return Results.Json(new { error = "wrong password" }, statusCode: StatusCodes.Status401Unauthorized);
            await SignIn(ctx, hash);
            return Results.Ok(new { authenticated = true });
        }).RequireRateLimiting(LoginPolicy);

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(Scheme);
            return Results.NoContent();
        });

        app.MapGet("/api/auth/state", (HttpContext ctx, SettingsStore settings) => new
        {
            loopback = AccessGate.IsLoopback(ctx.Connection.RemoteIpAddress),
            authenticated = ctx.User.Identity?.IsAuthenticated == true,
            networkAccess = settings.Current.NetworkAccess,
        });

        app.MapPut("/api/settings/password", async (PasswordBody body, HttpContext ctx, SettingsStore settings) =>
        {
            if (body.New is null || body.New.Length < MinPasswordLength)
                return Results.BadRequest(new { error = $"password must be at least {MinPasswordLength} characters" });
            var existing = settings.Current.PasswordHash;
            if (existing is not null)
            {
                if (body.Current is null) return Results.BadRequest(new { error = "current password required" });
                if (!PasswordHasher.Verify(body.Current, existing))
                    return Results.Json(new { error = "current password is wrong" }, statusCode: StatusCodes.Status403Forbidden);
            }
            var hash = PasswordHasher.Hash(body.New);
            settings.Update(s => s.PasswordHash = hash);
            if (ctx.User.Identity?.IsAuthenticated == true) await SignIn(ctx, hash);   // keep the caller's session; all others end
            return Results.Ok(new { hasPassword = true });
        }).RequireRateLimiting(LoginPolicy);

        app.MapPut("/api/settings/network", (NetworkBody body, SettingsStore settings, GlancePaths paths, WebBindingState binding) =>
        {
            if (body.Enabled is not { } enabled) return Results.BadRequest(new { error = "enabled is required" });
            if (body.Port is < 1024 or > 65535) return Results.BadRequest(new { error = "port must be 1024..65535" });
            if (enabled && settings.Current.PasswordHash is null)
                return Results.BadRequest(new { error = "set a password before enabling network access" });
            AppSettings? current = null;
            try
            {
                settings.Update(s =>
                {
                    // The fallback listens on the default port, not the configured one.
                    var heldPort = NetworkConfig.HeldPort(binding.Mode, s.Port);
                    s.NetworkAccess = enabled;
                    if (body.Https is { } https) s.Https = https;
                    if (body.Port is { } port) s.Port = port;
                    // The port Kestrel holds now is ours to move between addresses (a probe would only find ourselves);
                    // any other port must be free first, or the reload would leave the UI with no endpoint.
                    if (s.Port != heldPort && !NetworkConfig.CanBindAll(s))
                        throw new PortUnavailableException($"port {s.Port} is in use by another program");
                    if (s.NetworkAccess && s.Https) SelfSignedCert.EnsureCreated(paths, s);
                }, committed =>
                {
                    NetworkConfig.Write(paths, committed);
                    current = committed;
                });
            }
            catch (PortUnavailableException ex) { return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict); }
            binding.Mode = StartupBinding.Configured;
            return Results.Ok(new { networkAccess = current!.NetworkAccess, https = current.Https, port = current.Port });
        });
    }

    private sealed class PortUnavailableException(string message) : Exception(message);

    private static Task SignIn(HttpContext ctx, string passwordHash)
    {
        var identity = new ClaimsIdentity([new Claim(StampClaim, Stamp(passwordHash))], Scheme);
        return ctx.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>Identifies the password a cookie was issued for without putting the hash itself in the cookie.</summary>
    private static string Stamp(string passwordHash) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));
}
