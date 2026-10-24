using Glance.Core.Layouts;
using Glance.Testing;

namespace Glance.Core.Tests.Layouts;

public class ThemeSeederTests
{
    private static string ThemeJson(string name) =>
        LayoutJsonTests.Sample.Replace("\"Gaming\"", $"\"{name}\"");

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _dir = new();
        public string Themes { get; }
        public LayoutStore Layouts { get; }
        public SettingsStore Settings { get; }

        public Fixture(params string[] themeNames)
        {
            var paths = new GlancePaths(Path.Combine(_dir.Path, "data"));
            Layouts = new LayoutStore(paths, TimeProvider.System);
            Settings = new SettingsStore(paths);
            Themes = Path.Combine(_dir.Path, "themes");
            Directory.CreateDirectory(Themes);
            foreach (var n in themeNames) File.WriteAllText(Path.Combine(Themes, n + ".json"), ThemeJson(n));
        }

        public void Dispose() { Layouts.Dispose(); _dir.Dispose(); }
    }

    [Fact]
    public void Seeds_missing_themes_and_records_them()
    {
        using var f = new Fixture("Blend", "Gaming");
        var copied = ThemeSeeder.Seed(f.Themes, f.Layouts, f.Settings);
        Assert.Equal(new[] { "Blend", "Gaming" }, copied.OrderBy(x => x));
        Assert.NotNull(f.Layouts.Get("Blend"));
        Assert.NotNull(f.Layouts.Get("Gaming"));
        Assert.Equal(new[] { "Blend", "Gaming" }, f.Settings.Current.SeededThemes.OrderBy(x => x));
    }

    [Fact]
    public void Deleted_theme_is_not_reseeded()
    {
        using var f = new Fixture("Blend");
        ThemeSeeder.Seed(f.Themes, f.Layouts, f.Settings);
        Assert.True(f.Layouts.Delete("Blend"));
        Assert.Empty(ThemeSeeder.Seed(f.Themes, f.Layouts, f.Settings));
        Assert.Null(f.Layouts.Get("Blend"));
    }

    [Fact]
    public void Existing_layout_with_theme_name_is_not_overwritten()
    {
        using var f = new Fixture("Blend");
        var mine = System.Text.Json.JsonSerializer.Deserialize<Layout>(ThemeJson("Blend"), GlanceJson.Options)!;
        mine.Items.RemoveAt(0);
        f.Layouts.Save(mine);
        Assert.Empty(ThemeSeeder.Seed(f.Themes, f.Layouts, f.Settings));
        Assert.Equal(3, f.Layouts.Get("Blend")!.Items.Count);
        Assert.Contains("Blend", f.Settings.Current.SeededThemes);
    }

    [Fact]
    public void Reseeds_after_settings_reset()
    {
        using var f = new Fixture("Blend");
        Assert.Empty(f.Settings.Current.SeededThemes);
        Assert.Equal(new[] { "Blend" }, ThemeSeeder.Seed(f.Themes, f.Layouts, f.Settings));
        Assert.NotNull(f.Layouts.Get("Blend"));
    }

    [Fact]
    public void Missing_folder_returns_empty_and_does_not_throw()
    {
        using var f = new Fixture();
        Assert.Empty(ThemeSeeder.Seed(Path.Combine(f.Themes, "nope"), f.Layouts, f.Settings));
        Assert.Empty(f.Settings.Current.SeededThemes);
    }

    [Fact]
    public void Broken_theme_is_skipped_with_a_warning_and_the_rest_are_seeded()
    {
        using var f = new Fixture("Blend");
        File.WriteAllText(Path.Combine(f.Themes, "bad.json"), "{ nope");
        File.WriteAllText(Path.Combine(f.Themes, "wide.json"), ThemeJson("Wide").Replace("1280", "99999"));
        var warnings = new List<string>();
        var copied = ThemeSeeder.Seed(f.Themes, f.Layouts, f.Settings, warnings.Add);
        Assert.Equal(new[] { "Blend" }, copied);
        Assert.Equal(2, warnings.Count);
        Assert.Equal(new[] { "Blend" }, f.Settings.Current.SeededThemes);
    }
}
