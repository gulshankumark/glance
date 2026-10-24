using System.Text.Json;

namespace Glance.Core.Layouts;

/// <summary>
/// Copies the themes shipped next to the service into the layout store, once per theme name. A theme the person
/// deleted or replaced is never copied again or overwritten; a broken theme file is skipped, never fatal.
/// </summary>
public static class ThemeSeeder
{
    /// <returns>The names of the layouts that were saved.</returns>
    public static IReadOnlyList<string> Seed(string themesDir, LayoutStore layouts, SettingsStore settings, Action<string>? warn = null)
    {
        var copied = new List<string>();
        if (!Directory.Exists(themesDir)) return copied;

        var done = new HashSet<string>(settings.Current.SeededThemes ?? new List<string>(), StringComparer.Ordinal);
        var recorded = new List<string>();
        foreach (var file in Directory.EnumerateFiles(themesDir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(file), GlanceJson.Options);
                var name = layout?.Name;
                if (layout is null || string.IsNullOrEmpty(name)) throw new InvalidDataException("no layout name");
                if (done.Contains(name) || recorded.Contains(name)) continue;
                if (layouts.Get(name) is null)
                {
                    layouts.Save(layout);
                    copied.Add(name);
                }
                recorded.Add(name);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                warn?.Invoke($"Skipped shipped theme '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        if (recorded.Count > 0)
            settings.Update(s => { foreach (var n in recorded) if (!s.SeededThemes.Contains(n)) s.SeededThemes.Add(n); });
        return copied;
    }
}
