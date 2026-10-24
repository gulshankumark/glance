namespace Glance.Platform.Windows;

/// <summary>The one inbound firewall rule that lets other machines reach the web UI.</summary>
public interface IFirewall
{
    public const string RuleName = "Glance web UI";

    /// <summary>The local port the rule allows, or null when the rule does not exist.</summary>
    int? RulePort();

    /// <summary>Makes exactly one rule exist, allowing <paramref name="port"/> for the service executable.</summary>
    void Upsert(int port);

    /// <summary>Deletes the rule; a no-op when it does not exist.</summary>
    void Remove();
}
