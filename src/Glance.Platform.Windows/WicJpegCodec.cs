using System.Runtime.InteropServices;
using Glance.Core.Rendering;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Glance.Platform.Windows;

/// <summary>
/// JPEG through the Windows Imaging Component, several times faster than SkiaSharp's bundled codec. One imaging
/// factory is shared (WIC factories are free-threaded); decoders, encoders and streams are created per call and
/// released before returning. Output is baseline JPEG with 4:2:0 chroma subsampling.
/// </summary>
public sealed class WicJpegCodec : IJpegCodec
{
    private static readonly IntPtr QualityName = Marshal.StringToCoTaskMemUni("ImageQuality");          // process lifetime
    private static readonly IntPtr SubsamplingName = Marshal.StringToCoTaskMemUni("JpegYCrCbSubsampling");
    private const int OutputSlack = 64 * 1024;   // room for headers and incompressible data on top of the raw size

    private readonly IWICImagingFactory _factory;

    /// <exception cref="PlatformNotSupportedException">Not 64-bit Windows.</exception>
    /// <exception cref="COMException">WIC is unavailable.</exception>
    public WicJpegCodec()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("WIC JPEG needs 64-bit Windows");
        _factory = (IWICImagingFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(Wic.ImagingFactoryClsid, throwOnError: true)!)!;
    }

    /// <summary>The WIC codec if it works on this machine (checked with a round trip), otherwise Skia; the fallback is logged once.</summary>
    public static IJpegCodec CreateOrFallback(ILogger log)
    {
        try
        {
            var wic = new WicJpegCodec();
            using var probe = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Opaque));
            probe.Erase(SKColors.Teal);
            using var pixels = probe.PeekPixels();
            if (!wic.TryDecode(wic.Encode(pixels, 90), pixels)) throw new InvalidOperationException("WIC JPEG round trip failed");
            return wic;
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Windows Imaging Component JPEG codec unavailable; using the slower built-in codec");
            return SkiaJpegCodec.Instance;
        }
    }

    public unsafe bool TryDecode(ReadOnlySpan<byte> jpeg, SKPixmap target)
    {
        if (target.ColorType != SKColorType.Bgra8888 || jpeg.IsEmpty) return false;
        IWICStream? stream = null;
        IWICBitmapDecoder? decoder = null;
        IWICBitmapSource? frame = null;
        IWICFormatConverter? converter = null;
        try
        {
            fixed (byte* data = jpeg)
            {
                _factory.CreateStream(out stream);
                stream.InitializeFromMemory((IntPtr)data, (uint)jpeg.Length);
                var jpegFormat = Wic.ContainerFormatJpeg;
                _factory.CreateDecoder(ref jpegFormat, IntPtr.Zero, out decoder);
                decoder.Initialize(stream, Wic.DecodeMetadataCacheOnDemand);
                decoder.GetFrame(0, out frame);
                frame.GetSize(out var w, out var h);
                if (w != target.Width || h != target.Height) return false;
                _factory.CreateFormatConverter(out converter);
                var bgra = Wic.PixelFormat32bppBGRA;
                converter.Initialize(frame, ref bgra, Wic.DitherTypeNone, IntPtr.Zero, 0, Wic.PaletteTypeCustom);
                converter.CopyPixels(IntPtr.Zero, (uint)target.RowBytes, (uint)(target.RowBytes * target.Height), target.GetPixels());
                return true;
            }
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidCastException)
        {
            return false;
        }
        finally
        {
            Release(converter);
            Release(frame);
            Release(decoder);
            Release(stream);
        }
    }

    public unsafe byte[] Encode(SKPixmap source, int quality)
    {
        if (source.ColorType != SKColorType.Bgra8888) throw new ArgumentException("BGRA8888 pixels expected", nameof(source));
        uint w = (uint)source.Width, h = (uint)source.Height, stride = (uint)source.RowBytes, size = stride * h;
        var capacity = size + OutputSlack;
        var buffer = NativeMemory.Alloc(capacity);   // only the pages actually written get committed
        IWICStream? stream = null;
        IWICBitmapEncoder? encoder = null;
        IWICBitmapFrameEncode? frame = null;
        IPropertyBag2? options = null;
        IWICBitmapSource? bitmap = null;
        try
        {
            _factory.CreateStream(out stream);
            stream.InitializeFromMemory((IntPtr)buffer, capacity);
            var jpegFormat = Wic.ContainerFormatJpeg;
            _factory.CreateEncoder(ref jpegFormat, IntPtr.Zero, out encoder);
            encoder.Initialize(stream, Wic.EncoderNoCache);
            encoder.CreateNewFrame(out frame, out options);

            var bags = stackalloc PropBag2[2];
            var values = stackalloc Variant[2];
            bags[0] = new PropBag2 { PstrName = QualityName };
            values[0] = new Variant { Vt = Wic.VtR4, R4 = Math.Clamp(quality, 1, 100) / 100f };
            bags[1] = new PropBag2 { PstrName = SubsamplingName };
            values[1] = new Variant { Vt = Wic.VtUi1, Ui1 = Wic.JpegYCrCbSubsampling420 };
            options.Write(2, (IntPtr)bags, (IntPtr)values);

            frame.Initialize(options);
            frame.SetSize(w, h);
            var format = Wic.PixelFormat32bppBGR;
            frame.SetPixelFormat(ref format);
            if (format == Wic.PixelFormat32bppBGR) frame.WritePixels(h, stride, size, source.GetPixels());
            else
            {
                // the encoder wants another layout (24bpp BGR): let WIC convert from a wrapper over the pixels
                var bgr = Wic.PixelFormat32bppBGR;
                _factory.CreateBitmapFromMemory(w, h, ref bgr, stride, size, source.GetPixels(), out bitmap);
                frame.WriteSource(bitmap, IntPtr.Zero);
            }
            frame.Commit();
            encoder.Commit();
            stream.Seek(0, Wic.StreamSeekCur, out var length);
            return new ReadOnlySpan<byte>(buffer, (int)length).ToArray();
        }
        finally
        {
            Release(bitmap);
            Release(options);
            Release(frame);
            Release(encoder);
            Release(stream);
            NativeMemory.Free(buffer);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null) Marshal.FinalReleaseComObject(com);
    }
}
