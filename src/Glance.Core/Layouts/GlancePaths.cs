namespace Glance.Core.Layouts;

/// <summary>Locations of all on-disk Glance data. Directories are created on construction.</summary>
public sealed class GlancePaths
{
    public GlancePaths(string root)
    {
        Root = root;
        Layouts = Path.Combine(root, "layouts");
        Assets = Path.Combine(root, "assets");
        Logs = Path.Combine(root, "logs");
        Settings = Path.Combine(root, "settings.json");
        KestrelConfig = Path.Combine(root, "kestrel.json");
        HttpsPfx = Path.Combine(root, "https.pfx");
        Directory.CreateDirectory(Layouts);
        Directory.CreateDirectory(Assets);
        Directory.CreateDirectory(Logs);
    }

    public string Root { get; }
    public string Settings { get; }
    public string Layouts { get; }
    public string Assets { get; }
    public string Logs { get; }
    public string KestrelConfig { get; }
    public string HttpsPfx { get; }

    /// <summary>True when GLANCE_DATA points the data directory elsewhere (development, tests).</summary>
    public static bool IsOverridden => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GLANCE_DATA"));

    /// <summary>GLANCE_DATA if set, else %ProgramData%\Glance.</summary>
    public static GlancePaths Default() => new(DefaultRoot());

    /// <summary>The directory <see cref="Default"/> uses, without creating anything.</summary>
    public static string DefaultRoot() => IsOverridden
        ? Environment.GetEnvironmentVariable("GLANCE_DATA")!
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Glance");
}
