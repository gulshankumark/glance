using System.Text.Json.Serialization;

namespace Glance.Core.Layouts;

public sealed class Layout
{
    public string Name { get; set; } = "";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 480;
    public Background Background { get; set; } = new ColorBackground();
    public List<LayoutItem> Items { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ColorBackground), "color")]
[JsonDerivedType(typeof(ImageBackground), "image")]
[JsonDerivedType(typeof(AnimationBackground), "animation")]
public abstract class Background { }

public sealed class ColorBackground : Background { public string Color { get; set; } = "#000000"; }
public sealed class ImageBackground : Background { public string AssetId { get; set; } = ""; }
/// <summary>GIF and .zt both import to animation assets.</summary>
public sealed class AnimationBackground : Background { public string AssetId { get; set; } = ""; }

public record FontSpec(string Family = "Inter", float Size = 32, int Weight = 400);

public enum TextAlign { Left, Center, Right }

public record Threshold(double From, string Color);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextItem), "text")]
[JsonDerivedType(typeof(SensorItem), "sensor")]
[JsonDerivedType(typeof(ClockItem), "clock")]
[JsonDerivedType(typeof(ImageItem), "image")]
[JsonDerivedType(typeof(BarItem), "bar")]
[JsonDerivedType(typeof(SegmentedBarItem), "segmentedBar")]
[JsonDerivedType(typeof(ArcGaugeItem), "arcGauge")]
[JsonDerivedType(typeof(DialItem), "dial")]
[JsonDerivedType(typeof(HistoryGraphItem), "historyGraph")]
[JsonDerivedType(typeof(SparklineItem), "sparkline")]
public abstract class LayoutItem
{
    public string Id { get; set; } = "";
    /// <summary>Top-left of the item; text items align relative to X.</summary>
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class TextItem : LayoutItem
{
    public string Text { get; set; } = "";
    public FontSpec Font { get; set; } = new();
    public string Color { get; set; } = "#FFFFFF";
    public TextAlign Align { get; set; } = TextAlign.Left;
}

public sealed class SensorItem : LayoutItem
{
    public string Sensor { get; set; } = "";
    public string? Label { get; set; }
    public bool ShowUnit { get; set; } = true;
    public int Decimals { get; set; }
    public FontSpec Font { get; set; } = new();
    public string Color { get; set; } = "#FFFFFF";
    public TextAlign Align { get; set; } = TextAlign.Left;
}

public sealed class ClockItem : LayoutItem
{
    public string? Format { get; set; }
    public string TimeZone { get; set; } = "local";
    public string? Label { get; set; }
    public FontSpec Font { get; set; } = new();
    public string Color { get; set; } = "#FFFFFF";
    public TextAlign Align { get; set; } = TextAlign.Left;
}

public sealed class ImageItem : LayoutItem
{
    public string AssetId { get; set; } = "";
    public float Width { get; set; }
    public float Height { get; set; }
}

public abstract class GaugeItem : LayoutItem
{
    public string Sensor { get; set; } = "";
    public float Width { get; set; }
    public float Height { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string Color { get; set; } = "#4AAFFF";
    public string TrackColor { get; set; } = "#33FFFFFF";
    public List<Threshold> Thresholds { get; set; } = new();
    public bool ShowValue { get; set; }
    public string? Label { get; set; }
    public FontSpec Font { get; set; } = new();
}

public sealed class BarItem : GaugeItem
{
    public bool Vertical { get; set; }
    public bool Rounded { get; set; } = true;
}

public sealed class SegmentedBarItem : GaugeItem
{
    public int Segments { get; set; } = 10;
    public int Gap { get; set; } = 4;
    public bool Vertical { get; set; }
}

public sealed class ArcGaugeItem : GaugeItem
{
    public float StartAngle { get; set; } = 135;
    public float SweepAngle { get; set; } = 270;
    public float Thickness { get; set; } = 16;
}

public sealed class DialItem : GaugeItem
{
    public float StartAngle { get; set; } = 135;
    public float SweepAngle { get; set; } = 270;
    public int Ticks { get; set; } = 10;
    public string NeedleColor { get; set; } = "#FF4444";
}

public sealed class HistoryGraphItem : GaugeItem
{
    public int WindowSeconds { get; set; } = 60;
    public bool Area { get; set; } = true;
    public bool Grid { get; set; } = true;
}

public sealed class SparklineItem : GaugeItem
{
    public int WindowSeconds { get; set; } = 60;
}
