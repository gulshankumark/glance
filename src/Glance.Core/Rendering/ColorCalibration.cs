using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Glance.Core.Rendering;

/// <summary>
/// Per-screen gamma and brightness for the bytes sent to a device (never the preview): each colour channel v becomes
/// <c>round(255 · brightness/100 · (v/255)^gamma)</c>. Gamma above 1 darkens dark tones, for panels that show them
/// brighter than a monitor does.
/// </summary>
public sealed class ColorCalibration
{
    private readonly byte[] _table = new byte[256];

    private ColorCalibration(double gamma, int brightness)
    {
        Gamma = gamma;
        Brightness = brightness;
        for (var v = 0; v < 256; v++)
        {
            var mapped = Math.Round(255 * (brightness / 100.0) * Math.Pow(v / 255.0, gamma), MidpointRounding.AwayFromZero);
            _table[v] = (byte)Math.Clamp(mapped, 0, 255);
        }
    }

    public double Gamma { get; }
    public int Brightness { get; }

    /// <summary>The calibration, or null for the identity (gamma 1, brightness 100), so callers skip the pass.</summary>
    /// <exception cref="PlatformNotSupportedException">On a big-endian machine, where <see cref="Apply(Span{byte})"/> would change alpha.</exception>
    public static ColorCalibration? For(double gamma, int brightness)
    {
        if (gamma == 1 && brightness == 100) return null;
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("colour calibration needs a little-endian machine");
        return new ColorCalibration(gamma, brightness);
    }

    /// <summary>Maps the B, G and R bytes of every 4-byte BGRA (or RGBA) pixel in place; alpha is left as it is.</summary>
    public void Apply(Span<byte> bgra)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(bgra);
        ref var lut = ref MemoryMarshal.GetArrayDataReference(_table);   // indexes are bytes: always inside the table
        // Little-endian: byte 0 (B or R) is the low byte of each pixel and alpha (byte 3) the high byte.
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            pixels[i] = (p & 0xFF000000u)
                        | ((uint)Unsafe.Add(ref lut, (int)((p >> 16) & 0xFF)) << 16)
                        | ((uint)Unsafe.Add(ref lut, (int)((p >> 8) & 0xFF)) << 8)
                        | Unsafe.Add(ref lut, (int)(p & 0xFF));
        }
    }

    /// <summary>Applies the pass to a 32-bit bitmap's pixels in place.</summary>
    public void Apply(SKBitmap bitmap)
    {
        if (bitmap.BytesPerPixel != 4) throw new ArgumentException("calibration needs 32-bit pixels", nameof(bitmap));
        Apply(bitmap.GetPixelSpan());
        bitmap.NotifyPixelsChanged();
    }
}
