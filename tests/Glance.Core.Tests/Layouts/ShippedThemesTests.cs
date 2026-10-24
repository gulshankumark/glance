using System.Runtime.CompilerServices;
using System.Text.Json;
using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Glance.Testing;
using SkiaSharp;

namespace Glance.Core.Tests.Layouts;

/// <summary>The themes shipped in <c>src/Glance.Service/themes</c>: valid, complete, legible and pixel-stable.</summary>
public class ShippedThemesTests
{
    private static readonly string[] Names = { "Blend", "Clean", "Gauges", "Live graphs" };

    public static TheoryData<string> Themes => new(Names);

    private static readonly string ThemesDir = Path.GetFullPath(
        Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "..", "src", "Glance.Service", "themes"));

    private static string SourceFile([CallerFilePath] string p = "") => p;

    /// <summary>Parsed exactly as <see cref="ThemeSeeder"/> parses it, so property order matters here too.</summary>
    private static Layout Load(string name) =>
        JsonSerializer.Deserialize<Layout>(File.ReadAllText(Path.Combine(ThemesDir, name + ".json")), GlanceJson.Options)!;

    private static readonly string[] RequiredRoles =
    {
        Roles.CpuTemperature, Roles.CpuPowerPackage, Roles.CpuClock, Roles.CpuLoad,
        Roles.GpuTemperature, Roles.GpuClock, Roles.GpuPower, Roles.GpuMemoryUsed, Roles.GpuMemoryLoad,
        Roles.MemoryUsed, Roles.MemoryAvailable,
    };

    private static IEnumerable<string> Bindings(Layout l) => l.Items.Select(i => i switch
    {
        SensorItem s => s.Sensor,
        GaugeItem g => g.Sensor,
        _ => null,
    }).OfType<string>();

    [Theory, MemberData(nameof(Themes))]
    public void Every_shipped_theme_is_valid(string name)
    {
        var l = Load(name);
        Assert.Equal(name, l.Name);
        Assert.Empty(LayoutValidator.Validate(l));
        Assert.Equal((1280, 480), (l.Width, l.Height));
        Assert.IsType<ColorBackground>(l.Background);
        foreach (var b in Bindings(l))
        {
            var r = SensorRef.Parse(b);
            Assert.True(r.IsRole, $"'{b}' is bound to a sensor id, not a role");
            Assert.Contains(r.Value, Roles.All);
        }
    }

    [Theory, MemberData(nameof(Themes))]
    public void Every_shipped_theme_covers_required_roles(string name)
    {
        var l = Load(name);
        var roles = Bindings(l).Select(b => SensorRef.Parse(b).Value).ToHashSet();
        foreach (var role in RequiredRoles) Assert.Contains(role, roles);
        // Owner's call: the shipped themes show seconds with a fixed format, so they do not follow clock24h.
        Assert.NotEmpty(l.Items.OfType<ClockItem>());
    }

    [Fact]
    public void Every_theme_file_is_tested()
    {
        var files = Directory.EnumerateFiles(ThemesDir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).Order();
        Assert.Equal(Names.Order(), files);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Theme_renders_matches_golden(string name)
    {
        using var bmp = Render(Load(name), Typical, new DateTimeOffset(2026, 10, 21, 14, 5, 9, TimeSpan.Zero), new AppSettings());
        GoldenAssert.Match(bmp, "theme-" + name);
    }

    /// <summary>
    /// With the widest realistic values (°F, 12-hour clock, long date, four-digit clocks) every text item stays on
    /// screen and clear of every other text item.
    /// </summary>
    [Theory, MemberData(nameof(Themes))]
    public void Theme_text_stays_on_screen_and_apart_with_wide_values(string name)
    {
        var l = Load(name);
        var settings = new AppSettings { Clock24h = false, Unit = TemperatureUnit.Fahrenheit };
        var (hub, _) = NewHub(l, Widest);
        var ctx = new RenderContext(hub, settings, new DateTimeOffset(2026, 9, 30, 22, 58, 0, TimeSpan.Zero),
            new NoAssets(), new FontProvider());
        UtcClocks(l);
        var bounds = new OverlayRenderer().MeasureItems(l, ctx);
        var screen = new SKRect(0, 0, l.Width, l.Height);
        foreach (var (id, b) in bounds) Assert.True(screen.Contains(b), $"'{id}' {b} leaves the screen");

        var text = l.Items.Where(i => i is TextItem or SensorItem or ClockItem).Select(i => (i.Id, Rect: bounds[i.Id])).ToList();
        for (int i = 0; i < text.Count; i++)
            for (int j = i + 1; j < text.Count; j++)
                Assert.False(text[i].Rect.IntersectsWith(text[j].Rect),
                    $"'{text[i].Id}' {text[i].Rect} overlaps '{text[j].Id}' {text[j].Rect}");
    }

    // ---- fixtures ----

    private static readonly Dictionary<string, SensorKind> Kinds = new()
    {
        [Roles.CpuTemperature] = SensorKind.Temperature, [Roles.CpuLoad] = SensorKind.Load,
        [Roles.CpuClock] = SensorKind.Clock, [Roles.CpuPowerPackage] = SensorKind.Power,
        [Roles.CpuPowerCores] = SensorKind.Power, [Roles.CpuFan] = SensorKind.Fan,
        [Roles.GpuTemperature] = SensorKind.Temperature, [Roles.GpuLoad] = SensorKind.Load,
        [Roles.GpuClock] = SensorKind.Clock, [Roles.GpuMemoryClock] = SensorKind.Clock,
        [Roles.GpuMemoryLoad] = SensorKind.Load, [Roles.GpuFan] = SensorKind.Fan, [Roles.GpuPower] = SensorKind.Power,
        [Roles.MemoryLoad] = SensorKind.Load, [Roles.MemoryUsed] = SensorKind.Data,
        [Roles.MemoryAvailable] = SensorKind.Data, [Roles.GpuMemoryUsed] = SensorKind.Data,
    };

    private static readonly Dictionary<string, double> Typical = new()
    {
        [Roles.CpuTemperature] = 62, [Roles.CpuLoad] = 34, [Roles.CpuClock] = 4875, [Roles.CpuPowerPackage] = 88,
        [Roles.CpuPowerCores] = 61, [Roles.CpuFan] = 1240,
        [Roles.GpuTemperature] = 58, [Roles.GpuLoad] = 71, [Roles.GpuClock] = 2610, [Roles.GpuMemoryClock] = 10501,
        [Roles.GpuMemoryLoad] = 39, [Roles.GpuFan] = 1650, [Roles.GpuPower] = 214,
        [Roles.MemoryLoad] = 57, [Roles.MemoryUsed] = 18.2, [Roles.MemoryAvailable] = 13.8, [Roles.GpuMemoryUsed] = 6.2,
    };

    private static readonly Dictionary<string, double> Widest = new()
    {
        [Roles.CpuTemperature] = 99, [Roles.CpuLoad] = 100, [Roles.CpuClock] = 5999, [Roles.CpuPowerPackage] = 253,
        [Roles.CpuPowerCores] = 230, [Roles.CpuFan] = 2400,
        [Roles.GpuTemperature] = 99, [Roles.GpuLoad] = 100, [Roles.GpuClock] = 3105, [Roles.GpuMemoryClock] = 14001,
        [Roles.GpuMemoryLoad] = 100, [Roles.GpuFan] = 3600, [Roles.GpuPower] = 575,
        [Roles.MemoryLoad] = 100, [Roles.MemoryUsed] = 127.9, [Roles.MemoryAvailable] = 127.9, [Roles.GpuMemoryUsed] = 31.9,
    };

    private sealed class RoleIds : IRoleMap
    {
        public string? Match(string role, IReadOnlyList<SensorInfo> sensors) =>
            sensors.Any(s => s.Id == "s/" + role) ? "s/" + role : null;
    }

    /// <summary>
    /// A hub with one sensor per role and five minutes of history that wanders around <paramref name="values"/> and
    /// ends exactly on them, so graphs have a deterministic shape.
    /// </summary>
    private static (SensorHub Hub, FakeSensorSource Src) NewHub(Layout l, Dictionary<string, double> values)
    {
        var src = new FakeSensorSource(Roles.All.Select(r => ("s/" + r, r.Split('.')[0], Kinds[r])).ToArray());
        var hub = HubFactory.NewHub(src, new RoleIds());
        hub.SetInterest(new OverlayRenderer().HistoryNeeds(l, hub).ToDictionary(kv => kv.Key, kv => kv.Value));
        const int ticks = 300;
        for (int t = 0; t < ticks; t++)
        {
            int k = 0;
            foreach (var (role, v) in values)
            {
                k++;
                src.Values["s/" + role] = Math.Max(0, v * (1 + 0.15 * (Wobble(t, k) - Wobble(ticks - 1, k))));
            }
            hub.Tick();
        }
        return (hub, src);
    }

    private static double Wobble(int t, int k) =>
        Math.Sin(t / (7.0 + k)) * 0.6 + Math.Sin(t / (2.3 + k * 0.4)) * 0.25 + Math.Sin(t / 31.0 + k) * 0.5;

    /// <summary>"local" clocks would follow the machine running the test; the goldens are rendered in UTC.</summary>
    private static void UtcClocks(Layout l)
    {
        foreach (var c in l.Items.OfType<ClockItem>()) c.TimeZone = "UTC";
    }

    private static SKBitmap Render(Layout l, Dictionary<string, double> values, DateTimeOffset now, AppSettings settings)
    {
        var (hub, _) = NewHub(l, values);
        UtcClocks(l);
        var ctx = new RenderContext(hub, settings, now, new NoAssets(), new FontProvider());
        using var overlay = new OverlayRenderer().Render(l, ctx);
        var bmp = new SKBitmap(l.Width, l.Height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColor.Parse(((ColorBackground)l.Background).Color));
        canvas.DrawBitmap(overlay, 0, 0);
        return bmp;
    }
}
