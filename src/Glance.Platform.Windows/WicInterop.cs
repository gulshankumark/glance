using System.Runtime.InteropServices;

namespace Glance.Platform.Windows;

// Minimal hand-written COM declarations for the Windows Imaging Component (wincodec.h). Slot order must match the
// vtables exactly; methods Glance never calls are declared as parameterless placeholders that only reserve a slot.

internal static class Wic
{
    public static readonly Guid ImagingFactoryClsid = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    public static readonly Guid ContainerFormatJpeg = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");
    public static readonly Guid PixelFormat24bppBGR = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");
    public static readonly Guid PixelFormat32bppBGR = new("6fddc324-4e03-4bfe-b185-3d77768dc90e");
    public static readonly Guid PixelFormat32bppBGRA = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");

    public const int DecodeMetadataCacheOnDemand = 0;
    public const int EncoderNoCache = 2;
    public const int DitherTypeNone = 0;
    public const int PaletteTypeCustom = 0;
    public const uint StreamSeekCur = 1;
    public const byte JpegYCrCbSubsampling420 = 1;
    public const ushort VtR4 = 4, VtUi1 = 17;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropBag2
{
    public uint DwType;
    public ushort Vt;
    public ushort CfType;
    public uint DwHint;
    public IntPtr PstrName;
    public Guid Clsid;
}

/// <summary>A VARIANT holding a float or a byte (64-bit layout: 24 bytes, value at offset 8).</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct Variant
{
    [FieldOffset(0)] public ushort Vt;
    [FieldOffset(8)] public float R4;
    [FieldOffset(8)] public byte Ui1;
}

[ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICImagingFactory
{
    void CreateDecoderFromFilename();
    void CreateDecoderFromStream();
    void CreateDecoderFromFileHandle();
    void CreateComponentInfo();
    void CreateDecoder(ref Guid containerFormat, IntPtr vendor, out IWICBitmapDecoder decoder);
    void CreateEncoder(ref Guid containerFormat, IntPtr vendor, out IWICBitmapEncoder encoder);
    void CreatePalette();
    void CreateFormatConverter(out IWICFormatConverter converter);
    void CreateBitmapScaler();
    void CreateBitmapClipper();
    void CreateBitmapFlipRotator();
    void CreateStream(out IWICStream stream);
    void CreateColorContext();
    void CreateColorTransformer();
    void CreateBitmap();
    void CreateBitmapFromSource();
    void CreateBitmapFromSourceRect();
    void CreateBitmapFromMemory(uint width, uint height, ref Guid pixelFormat, uint stride, uint bufferSize, IntPtr buffer, out IWICBitmapSource bitmap);
}

[ComImport, Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapSource
{
    void GetSize(out uint width, out uint height);
    void GetPixelFormat(out Guid format);
    void GetResolution(out double dpiX, out double dpiY);
    void CopyPalette();
    void CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
}

[ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICFormatConverter
{
    void GetSize(out uint width, out uint height);
    void GetPixelFormat(out Guid format);
    void GetResolution(out double dpiX, out double dpiY);
    void CopyPalette();
    void CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
    void Initialize(IWICBitmapSource source, ref Guid dstFormat, int dither, IntPtr palette, double alphaThresholdPercent, int paletteTranslate);
}

[ComImport, Guid("9EDDE9E7-8DEE-47ea-99DF-E6FAF2ED44BF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapDecoder
{
    void QueryCapability();
    void Initialize(IWICStream stream, int cacheOptions);
    void GetContainerFormat(out Guid format);
    void GetDecoderInfo();
    void CopyPalette();
    void GetMetadataQueryReader();
    void GetPreview();
    void GetColorContexts();
    void GetThumbnail();
    void GetFrameCount(out uint count);
    void GetFrame(uint index, out IWICBitmapSource frame);
}

[ComImport, Guid("00000103-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapEncoder
{
    void Initialize(IWICStream stream, int cacheOption);
    void GetContainerFormat(out Guid format);
    void GetEncoderInfo();
    void SetColorContexts();
    void SetPalette();
    void SetThumbnail();
    void SetPreview();
    void CreateNewFrame(out IWICBitmapFrameEncode frame, out IPropertyBag2 options);
    void Commit();
}

[ComImport, Guid("00000105-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapFrameEncode
{
    void Initialize(IPropertyBag2 options);
    void SetSize(uint width, uint height);
    void SetResolution(double dpiX, double dpiY);
    void SetPixelFormat(ref Guid format);
    void SetColorContexts();
    void SetPalette();
    void SetThumbnail();
    void WritePixels(uint lineCount, uint stride, uint bufferSize, IntPtr pixels);
    void WriteSource(IWICBitmapSource source, IntPtr rect);
    void Commit();
}

[ComImport, Guid("22F55882-280B-11d0-A8A9-00A0C90C2004"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyBag2
{
    void Read();
    void Write(uint count, IntPtr propBags, IntPtr values);
}

[ComImport, Guid("135FF860-22B7-4ddf-B0F6-218F4F299A43"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICStream
{
    void Read();
    void Write();
    void Seek(long move, uint origin, out ulong newPosition);
    void SetSize();
    void CopyTo();
    void Commit();
    void Revert();
    void LockRegion();
    void UnlockRegion();
    void Stat();
    void Clone();
    void InitializeFromIStream();
    void InitializeFromFilename();
    void InitializeFromMemory(IntPtr buffer, uint size);
}
