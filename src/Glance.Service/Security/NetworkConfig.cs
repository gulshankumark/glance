using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Glance.Core.Layouts;

namespace Glance.Service.Security;

/// <summary>What the web UI binds at startup (see <see cref="NetworkConfig.ApplyAtStartup"/>).</summary>
public enum StartupBinding
{
    /// <summary>The endpoints the settings ask for.</summary>
    Configured,
    /// <summary>Those could not be bound: loopback on the default port instead, until the settings change or a restart.</summary>
    LoopbackFallback,
    /// <summary>Nothing could be bound: no web UI, but screens and sensors keep running.</summary>
    None,
}

/// <summary>How the running service is bound; becomes <see cref="StartupBinding.Configured"/> after a successful network change.</summary>
public sealed class WebBindingState(StartupBinding mode)
{
    private int _mode = (int)mode;

    /// <summary>Raised after <see cref="Mode"/> changes value.</summary>
    public event Action? Changed;

    public StartupBinding Mode
    {
        get => (StartupBinding)Volatile.Read(ref _mode);
        set
        {
            if (Interlocked.Exchange(ref _mode, (int)value) == (int)value) return;
            try { Changed?.Invoke(); } catch { /* subscriber faults must not break callers */ }
        }
    }
}

/// <summary>
/// Writes the Kestrel endpoints for the settings to kestrel.json, which Kestrel reloads on change. settings.json is
/// the source of truth: kestrel.json is rebuilt from it at every start and written under the settings lock.
/// </summary>
public static class NetworkConfig
{
    /// <summary>The addresses Kestrel listens on for <paramref name="s"/>: loopback, or every address with network access.</summary>
    public static IReadOnlyList<IPEndPoint> Endpoints(AppSettings s) => s.NetworkAccess
        ? [new IPEndPoint(IPAddress.Any, s.Port), new IPEndPoint(IPAddress.IPv6Any, s.Port)]
        : [new IPEndPoint(IPAddress.Loopback, s.Port), new IPEndPoint(IPAddress.IPv6Loopback, s.Port)];

    /// <summary>
    /// Test-binds <paramref name="ep"/> with exclusive address use, so a port another program holds (on this or a
    /// wildcard address) counts as taken. IPv6 endpoints count as free on a machine without IPv6.
    /// </summary>
    public static bool CanBind(IPEndPoint ep)
    {
        if (ep.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6) return true;
        try
        {
            using var socket = new Socket(ep.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
            if (ep.AddressFamily == AddressFamily.InterNetworkV6) socket.DualMode = false;
            socket.Bind(ep);
            return true;
        }
        catch (SocketException) { return false; }
    }

    public static bool CanBindAll(AppSettings s, Func<IPEndPoint, bool>? canBind = null) => Endpoints(s).All(canBind ?? CanBind);

    /// <summary>
    /// The port Kestrel actually holds for <paramref name="mode"/>: the loopback fallback always listens on
    /// <see cref="AppSettings.DefaultPort"/>, whatever <paramref name="configuredPort"/> says. Null when nothing is bound.
    /// </summary>
    public static int? HeldPort(StartupBinding mode, int configuredPort) => mode switch
    {
        StartupBinding.Configured => configuredPort,
        StartupBinding.LoopbackFallback => AppSettings.DefaultPort,
        _ => null,
    };

    /// <summary>The startup error for a binding other than the configured one, naming the port actually bound; else null.</summary>
    public static string? StartupMessage(StartupBinding mode, AppSettings s) => mode switch
    {
        StartupBinding.LoopbackFallback =>
            $"The web UI could not use port {s.Port}{(s.NetworkAccess ? " on the network" : "")}; it is on " +
            $"http://localhost:{HeldPort(mode, s.Port)} (this computer only) until the network settings change or Glance restarts",
        StartupBinding.None =>
            $"No web UI: neither port {s.Port} nor the default port {AppSettings.DefaultPort} could be bound. " +
            "Screens and sensors keep running; free the port and restart Glance",
        _ => null,
    };

    /// <param name="configuredUsable">False when the configured endpoints cannot work whatever the port (no usable certificate).</param>
    public static StartupBinding ChooseStartupBinding(AppSettings s, bool configuredUsable, Func<IPEndPoint, bool> canBind)
    {
        if (configuredUsable && CanBindAll(s, canBind)) return StartupBinding.Configured;
        return CanBindAll(new AppSettings(), canBind) ? StartupBinding.LoopbackFallback : StartupBinding.None;
    }

    /// <summary>
    /// At startup: makes sure an HTTPS certificate exists when the settings need one, picks the endpoints (configured,
    /// else loopback on the default port, else none) and writes kestrel.json for them under the settings lock. A
    /// fallback never changes the settings themselves.
    /// </summary>
    public static StartupBinding ApplyAtStartup(GlancePaths paths, SettingsStore settings, Func<IPEndPoint, bool> canBind)
    {
        var usable = true;
        var current = settings.Current;
        if (current.NetworkAccess && current.Https && !SelfSignedCert.IsUsable(paths.HttpsPfx, current.HttpsPfxPassword))
        {
            try { settings.Update(s => SelfSignedCert.EnsureCreated(paths, s)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                usable = false;
            }
        }
        var choice = StartupBinding.None;
        settings.WithCurrent(s =>
        {
            choice = ChooseStartupBinding(s, usable, canBind);
            Write(paths, choice == StartupBinding.LoopbackFallback ? new AppSettings() : s);
        });
        return choice;
    }

    public static void Write(GlancePaths paths, AppSettings s)
    {
        var endpoints = new JsonObject();
        if (!s.NetworkAccess)
        {
            endpoints["Loopback4"] = new JsonObject { ["Url"] = $"http://127.0.0.1:{s.Port}" };
            endpoints["Loopback6"] = new JsonObject { ["Url"] = $"http://[::1]:{s.Port}" };
        }
        else if (s.Https)
        {
            endpoints["Any4"] = Https($"https://0.0.0.0:{s.Port}", paths, s);
            endpoints["Any6"] = Https($"https://[::]:{s.Port}", paths, s);
        }
        else
        {
            endpoints["Any4"] = new JsonObject { ["Url"] = $"http://0.0.0.0:{s.Port}" };
            endpoints["Any6"] = new JsonObject { ["Url"] = $"http://[::]:{s.Port}" };
        }
        var root = new JsonObject { ["Kestrel"] = new JsonObject { ["Endpoints"] = endpoints } };
        // Atomic: Kestrel reloads on change and must never see a half-written file.
        AtomicFile.Write(paths.KestrelConfig, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonObject Https(string url, GlancePaths paths, AppSettings s) => new()
    {
        ["Url"] = url,
        ["Certificate"] = new JsonObject { ["Path"] = paths.HttpsPfx, ["Password"] = s.HttpsPfxPassword },
    };
}
