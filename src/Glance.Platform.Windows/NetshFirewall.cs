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

    private static (int Code, string Output) Netsh(params string[] args) =>
        Run("netsh.exe", ["advfirewall", "firewall", .. args], TimeSpan.FromSeconds(30));

    /// <summary>
    /// Runs a process without a shell and returns its exit code and combined output. Output is read asynchronously, so a
    /// hung process (or one whose grandchild holds the pipe) cannot block past <paramref name="timeout"/>: on timeout
    /// the process tree is killed and <see cref="TimeoutException"/> thrown.
    /// </summary>
    public static (int Code, string Output) Run(string fileName, IEnumerable<string> args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"{fileName} could not be started");
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeout))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{fileName} did not finish within {timeout.TotalSeconds:0.#} seconds");
        }
        p.WaitForExit(); // flushes the async output readers
        // A grandchild may still hold a pipe open; never wait for it forever.
        Task.WaitAll([outTask, errTask], 5_000);
        var text = (outTask.IsCompletedSuccessfully ? outTask.Result : "") + (errTask.IsCompletedSuccessfully ? errTask.Result : "");
        return (p.ExitCode, text);
    }
}
