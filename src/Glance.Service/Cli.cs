using System.Globalization;
using Glance.Core.Layouts;
using Glance.Platform.Windows;

namespace Glance.Service;

/// <summary>Installer-facing command line modes. <see cref="TryRun"/> returns null when the service itself should start.</summary>
public static class Cli
{
    public static int? TryRun(string[] args, TextWriter? output = null)
    {
        output ??= Console.Out;
        if (args.Length == 0) return null;
        switch (args[0])
        {
            case "--prepare-data-dir":
                DataDirectoryAcl.CreateHardenedIfMissing(GlancePaths.DefaultRoot());
                return 0;
            case "--print-url":
                output.WriteLine(ServiceAddress.LocalUrl(ReadSettings(GlancePaths.DefaultRoot()), Security.StartupBinding.Configured));
                return 0;
            case "--wait-ready":
                if (args.Length < 2 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                {
                    Console.Error.WriteLine("usage: --wait-ready <seconds>");
                    return 2;
                }
                return WaitReady(ServiceAddress.LocalUrl(ReadSettings(GlancePaths.DefaultRoot()), Security.StartupBinding.Configured), seconds).GetAwaiter().GetResult();
            default:
                return null;
        }
    }

    /// <summary>Read-only: never writes settings.json or .corrupt files, never creates directories. Missing or unreadable gives defaults.</summary>
    internal static AppSettings ReadSettings(string root)
    {
        try { return SettingsRepair.Load(File.ReadAllText(Path.Combine(root, "settings.json"))).Settings; }
        catch { return new AppSettings(); }
    }

    internal static async Task<int> WaitReady(Uri baseUrl, int seconds)
    {
        using var handler = new HttpClientHandler
        {
            // The service's own certificate is self-signed: trust it for localhost only.
            ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || request.RequestUri?.Host == "localhost",
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var health = new Uri(baseUrl, "api/health");
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            try
            {
                using var response = await http.GetAsync(health);
                if (response.StatusCode == System.Net.HttpStatusCode.OK) return 0;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
            if (DateTime.UtcNow >= deadline) return 1;
            await Task.Delay(500);
        }
    }
}
