using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Sensors;

namespace Glance.Core.Rendering;

public record RenderContext(SensorHub Sensors, AppSettings Settings, DateTimeOffset Now, IAssetStore Assets, FontProvider Fonts);
