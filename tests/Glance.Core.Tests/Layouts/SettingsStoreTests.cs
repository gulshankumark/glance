using Glance.Core.Layouts;
using Glance.Testing;

namespace Glance.Core.Tests.Layouts;

public class SettingsStoreTests
{
    [Fact]
    public void Settings_defaults_and_atomic_update()
    {
        using var dir = new TempDir(); var s = new SettingsStore(new GlancePaths(dir.Path));
        Assert.Equal(8735, s.Current.Port);
        s.Update(x => x.Devices["k"] = new DeviceSettings { Rotation = 180 });
        Assert.Equal(180, new SettingsStore(new GlancePaths(dir.Path)).Current.Devices["k"].Rotation);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public void Update_rejects_invalid_rotation_and_leaves_state_untouched()
    {
        using var dir = new TempDir(); var s = new SettingsStore(new GlancePaths(dir.Path));
        Assert.Throws<ArgumentException>(() => s.Update(x => x.Devices["k"] = new DeviceSettings { Rotation = 45 }));
        Assert.Empty(s.Current.Devices);
        Assert.False(File.Exists(Path.Combine(dir.Path, "settings.json")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Update_rejects_overlay_fps_cap_outside_1_to_60(int cap)
    {
        using var dir = new TempDir(); var s = new SettingsStore(new GlancePaths(dir.Path));
        Assert.Throws<ArgumentException>(() => s.Update(x => x.MaxOverlayAnimationFps = cap));
        Assert.Null(s.Current.MaxOverlayAnimationFps);
    }

    [Fact]
    public void Overlay_fps_cap_is_saved_as_camelCase()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path); var s = new SettingsStore(paths);
        s.Update(x => x.MaxOverlayAnimationFps = 1);
        s.Update(x => x.MaxOverlayAnimationFps = 60);
        Assert.Contains("\"maxOverlayAnimationFps\": 60", File.ReadAllText(paths.Settings));
        Assert.Equal(60, new SettingsStore(paths).Current.MaxOverlayAnimationFps);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_without_throwing()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path);
        File.WriteAllText(paths.Settings, "{ \"port\": ");
        var s = new SettingsStore(paths);
        Assert.Equal(8735, s.Current.Port);
        Assert.NotNull(s.LoadError);
    }

    [Fact]
    public void Repaired_file_is_saved_and_original_kept_as_corrupt()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path);
        const string original = """{"port":9000,"devices":{"k":{"gamma":9,"rotation":90}}}""";
        File.WriteAllText(paths.Settings, original);
        var s = new SettingsStore(paths);
        Assert.Equal(9000, s.Current.Port);
        Assert.Equal(1.0, s.Current.Devices["k"].Gamma);
        Assert.Equal(original, File.ReadAllText(paths.Settings + ".corrupt"));
        Assert.Contains("gamma", s.LoadError);
        Assert.NotEmpty(s.Repairs);
        var reloaded = new SettingsStore(paths);
        Assert.Equal(1.0, reloaded.Current.Devices["k"].Gamma);
        Assert.Null(reloaded.LoadError);
    }

    [Fact]
    public void Update_raises_Changed()
    {
        using var dir = new TempDir(); var s = new SettingsStore(new GlancePaths(dir.Path));
        var n = 0; s.Changed += () => n++;
        s.Update(x => x.Unit = TemperatureUnit.Fahrenheit);
        Assert.Equal(1, n);
        Assert.Equal(TemperatureUnit.Fahrenheit, new SettingsStore(new GlancePaths(dir.Path)).Current.Unit);
    }

    [Fact]
    public void GlancePaths_creates_directories_and_exposes_files()
    {
        using var dir = new TempDir(); var p = new GlancePaths(Path.Combine(dir.Path, "root"));
        Assert.True(Directory.Exists(p.Layouts) && Directory.Exists(p.Assets) && Directory.Exists(p.Logs));
        Assert.Equal(Path.Combine(p.Root, "settings.json"), p.Settings);
    }
}
