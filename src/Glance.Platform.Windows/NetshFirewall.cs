using System.Diagnostics;
using System.Globalization;

namespace Glance.Platform.Windows;

/// <summary>Manages the rule through <c>netsh advfirewall firewall</c> (argument list, no shell).</summary>
public sealed class NetshFirewall : IFirewall
{
    public int? RulePort()
    {
        var (code, output) = Netsh("show", "rule", $"name={IFirewall.RuleName}");
        return code == 0 ? ParseLocalPort(output) : null;
    }

    public void Upsert(int port)
    {
        DeleteIgnoringMissing();
        var program = Environment.ProcessPath ?? throw new InvalidOperationException("The service executable path is unknown");
        var (code, output) = Netsh("add", "rule", $"name={IFirewall.RuleName}", "dir=in", "action=allow",
            "protocol=TCP", $"localport={port}", $"program={program}");
        if (code != 0) throw new InvalidOperationException($"netsh add rule failed ({code}): {output.Trim()}");
    }

    public void Remove() => DeleteIgnoringMissing();

    private static void DeleteIgnoringMissing()
    {
        var (code, output) = Netsh("delete", "rule", $"name={IFirewall.RuleName}");
        if (code == 0) return;
        // Non-zero is also what a missing rule gives; exit codes (not text) tell the two apart.
        var (showCode, _) = Netsh("show", "rule", $"name={IFirewall.RuleName}");
        if (!DeleteSucceeded(code, showCode))
            throw new InvalidOperationException($"netsh delete rule failed ({code}) and the rule still exists: {output.Trim()}");
    }

    /// <summary>The rule is gone if delete succeeded, or if delete failed and the rule can no longer be shown.</summary>
    public static bool DeleteSucceeded(int deleteExit, int showExit) => deleteExit == 0 || showExit != 0;

    /// <summary>
    /// Reads the first <c>LocalPort:</c> value from <c>netsh ... show rule</c> output. Only the field label is matched;
    /// "Any" or a missing/non-numeric value yields null.
    /// </summary>
    public static int? ParseLocalPort(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            if (!t.StartsWith("LocalPort:", StringComparison.OrdinalIgnoreCase)) continue;
            var v = t["LocalPort:".Length..].Trim();
            return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : null;
        }
        return null;
    }

    private static (int Code, string Output) Netsh(params string[] args)
    {
        var psi = new ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        psi.ArgumentList.Add("advfirewall");
        psi.ArgumentList.Add("firewall");
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("netsh could not be started");
        var err = p.StandardError.ReadToEndAsync();
        var outp = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(30_000))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("netsh did not finish within 30 seconds");
        }
        return (p.ExitCode, outp + err.GetAwaiter().GetResult());
    }
}
