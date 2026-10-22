using System.Runtime.InteropServices;
using SkiaSharp;

namespace Glance.Core.Rendering;

/// <summary>
/// Builds device frames for an animated layout with reused buffers. Each stored JPEG frame is decoded and, with the
/// overlay, put straight into one device-orientation surface (layout fitted into the canvas and rotated by a single
/// transform), which is then encoded in place. When the layout maps 1:1 onto the device at rotation 0 or 180 the
/// frame is decoded directly into the surface (and reversed in place for 180), so no frame-sized drawing happens at
/// all. Nothing frame-sized is allocated per frame except the encoded JPEG.
/// </summary>
public sealed class FrameCompositor : IDisposable
{
    private readonly int _layoutW, _layoutH, _rotation;
    private readonly SKBitmap _device;
    private readonly SKCanvas _canvas;
    private readonly SKMatrix _layoutToDevice;
    private readonly bool _scaled;   // layout pixels do not map 1:1 onto device pixels
    private readonly float _fitScale = 1;
    private readonly IJpegCodec _codec;
    private SKBitmap? _decoded;      // decode buffer when the frame cannot be decoded straight into the device surface
    private Decoded _state;
    private SKImage? _overlay;
    private SKRectI[] _overlayRects = Array.Empty<SKRectI>();
    private const int Tile = 32;

    private enum Decoded { None, IntoBuffer, IntoDevice }

    /// <param name="codec">Decodes stored frames and encodes device frames; defaults to Skia.</param>
    public FrameCompositor(int layoutW, int layoutH, int deviceW, int deviceH, int rotation, IJpegCodec? codec = null)
    {
        _codec = codec ?? SkiaJpegCodec.Instance;
        _layoutW = layoutW;
        _layoutH = layoutH;
        _rotation = rotation;
        var (cw, ch) = Orientation.Canvas(deviceW, deviceH, rotation);
        _device = new SKBitmap(new SKImageInfo(deviceW, deviceH, SKColorType.Bgra8888, SKAlphaType.Opaque));
        _canvas = new SKCanvas(_device);
        _canvas.Clear(SKColors.Black);

        // layout -> canvas: the same fit as Orientation.FitToCanvas (identity when the sizes match)
        var fit = SKMatrix.Identity;
        if ((layoutW, layoutH) != (cw, ch))
        {
            var scale = Math.Min((float)cw / layoutW, (float)ch / layoutH);
            fit = SKMatrix.CreateScaleTranslation(scale, scale, (cw - layoutW * scale) / 2, (ch - layoutH * scale) / 2);
            _scaled = true;
            _fitScale = scale;
        }
        // canvas -> device: the same clockwise rotation as Orientation.ToDevice
        var rotate = rotation switch
        {
            90 => SKMatrix.CreateTranslation(ch, 0).PreConcat(SKMatrix.CreateRotationDegrees(90)),
            180 => SKMatrix.CreateTranslation(cw, ch).PreConcat(SKMatrix.CreateRotationDegrees(180)),
            270 => SKMatrix.CreateTranslation(0, cw).PreConcat(SKMatrix.CreateRotationDegrees(270)),
            _ => SKMatrix.Identity,
        };
        _layoutToDevice = rotate.PreConcat(fit);
    }

    /// <summary>Sets the layout-size overlay drawn over every frame (null for none). The bitmap is made immutable and shared, not copied.</summary>
    public void SetOverlay(SKBitmap? overlay)
    {
        _overlay?.Dispose();
        _overlay = null;
        if (overlay is null) return;
        _overlayRects = OpaqueRects(overlay);
        overlay.SetImmutable();   // lets the image share the pixels instead of copying them
        _overlay = SKImage.FromBitmap(overlay);
    }

    /// <summary>Decodes <paramref name="jpeg"/> and composes it with the overlay into the device surface.</summary>
    public void Compose(byte[] jpeg)
    {
        Decode(jpeg);
        Draw();
    }

    /// <summary>Decodes a stored frame; an undecodable frame leaves the layout area black.</summary>
    public void Decode(byte[] jpeg)
    {
        _state = Decoded.None;
        if (!JpegHeader.TryReadSize(jpeg, out var w, out var h)) return;
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var direct = !_scaled && _rotation is 0 or 180 && (info.Width, info.Height) == (_device.Width, _device.Height);
        if (!direct && (_decoded is null || _decoded.Width != info.Width || _decoded.Height != info.Height))
        {
            _decoded?.Dispose();
            _decoded = new SKBitmap(info);
        }
        var target = direct ? _device : _decoded!;
        using (var pixels = target.PeekPixels())
            if (!_codec.TryDecode(jpeg, pixels)) return;
        if (direct && _rotation == 180) MemoryMarshal.Cast<byte, uint>(_device.GetPixelSpan()).Reverse();   // contiguous: RowBytes == Width * 4
        _state = direct ? Decoded.IntoDevice : Decoded.IntoBuffer;
    }

    /// <summary>Puts the decoded frame (stretched to the layout) and then the overlay into the device surface.</summary>
    public void Draw()
    {
        if (_state == Decoded.IntoDevice) _device.NotifyPixelsChanged();
        _canvas.SetMatrix(_layoutToDevice);
        var layoutRect = SKRect.Create(_layoutW, _layoutH);
        if (_state == Decoded.IntoBuffer)
        {
            using var pixels = _decoded!.PeekPixels();
            using var frame = SKImage.FromPixels(pixels);   // wraps the buffer, no copy
            if (!_scaled && (_decoded.Width, _decoded.Height) == (_layoutW, _layoutH)) _canvas.DrawImage(frame, 0, 0);
            else _canvas.DrawImage(frame, layoutRect, Resampling(_decoded.Width, _decoded.Height));
        }
        else if (_state == Decoded.None)
        {
            using var black = new SKPaint { Color = SKColors.Black };
            _canvas.DrawRect(layoutRect, black);
        }
        if (_overlay is not null)
            foreach (var r in _overlayRects) _canvas.DrawImage(_overlay, r, r, _scaled ? Resampling(_layoutW, _layoutH) : SKSamplingOptions.Default);
        _canvas.ResetMatrix();
    }

    /// <summary>The composed device surface before encoding (tests compare it exactly).</summary>
    internal SKBitmap DeviceSurface => _device;

    /// <summary>The device surface encoded within the device size limit.</summary>
    public EncodedJpeg Encode()
    {
        _canvas.Flush();
        using var pixels = _device.PeekPixels();
        return JpegEncoder.Encode(pixels, codec: _codec);
    }

    public void Dispose()
    {
        _overlay?.Dispose();
        _decoded?.Dispose();
        _canvas.Dispose();
        _device.Dispose();
    }

    /// <summary>Bilinear for a source of <paramref name="w"/> x <paramref name="h"/> drawn over the layout area; mipmapped when it shrinks.</summary>
    private SKSamplingOptions Resampling(int w, int h)
    {
        var shrinks = w > _layoutW * _fitScale + 0.5f || h > _layoutH * _fitScale + 0.5f;   // in device pixels
        return new SKSamplingOptions(SKFilterMode.Linear, shrinks ? SKMipmapMode.Linear : SKMipmapMode.None);
    }

    /// <summary>
    /// The overlay's non-transparent area as horizontal runs of 32x32 tiles, so only pixels near items are blended
    /// each frame (items are usually small and spread over the screen).
    /// </summary>
    private static SKRectI[] OpaqueRects(SKBitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        if (bmp.ColorType is not (SKColorType.Bgra8888 or SKColorType.Rgba8888)) return new[] { SKRectI.Create(w, h) };
        int cols = (w + Tile - 1) / Tile, rows = (h + Tile - 1) / Tile, rowPixels = bmp.RowBytes / 4;
        var used = new bool[rows, cols];
        var pixels = MemoryMarshal.Cast<byte, uint>(bmp.GetPixelSpan());
        for (var y = 0; y < h; y++)
        {
            var row = pixels.Slice(y * rowPixels, w);
            for (var x = 0; x < w; x++)
                if ((row[x] & 0xFF000000u) != 0) used[y / Tile, x / Tile] = true;   // alpha is the high byte in both colour types
        }
        var rects = new List<SKRectI>();
        for (var ty = 0; ty < rows; ty++)
            for (var tx = 0; tx < cols; tx++)
            {
                if (!used[ty, tx]) continue;
                var start = tx;
                while (tx + 1 < cols && used[ty, tx + 1]) tx++;
                rects.Add(new SKRectI(start * Tile, ty * Tile, Math.Min((tx + 1) * Tile, w), Math.Min((ty + 1) * Tile, h)));
            }
        return rects.ToArray();
    }
}
