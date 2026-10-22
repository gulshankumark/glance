using System.Buffers.Binary;

namespace Glance.Core.Media;

/// <summary>
/// Reads the .zt animation container: magic byte 0xDC, int32 frame count n, n int32 cumulative end times in ms,
/// then n frames each stored as int32 length + JPEG bytes (all little-endian).
/// Every count and length is checked against the bytes actually remaining before anything is allocated.
/// </summary>
public static class ZtReader
{
    public const byte Magic = 0xDC;
    private const int DefaultDelayMs = 40;   // .zt rule: a non-positive delay becomes 40 ms (GIF import uses 100 ms instead)

    public static (IReadOnlyList<int> DelaysMs, IReadOnlyList<byte[]> Frames) Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5 || data[0] != Magic) throw new InvalidMediaException("Not a .zt animation file.");
        var n = BinaryPrimitives.ReadInt32LittleEndian(data[1..]);
        if (n < 1 || n > MediaImporter.MaxFrames)
            throw new InvalidMediaException($"A .zt animation must have between 1 and {MediaImporter.MaxFrames} frames.");

        var pos = 5;
        if ((long)n * 4 > data.Length - pos) throw new InvalidMediaException("The .zt file is truncated.");
        var delays = new int[n];
        long previous = 0;
        for (var i = 0; i < n; i++, pos += 4)
        {
            long cumulative = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
            var delta = cumulative - previous;
            delays[i] = delta <= 0 ? DefaultDelayMs : (int)Math.Min(delta, int.MaxValue);
            previous = cumulative;
        }

        var frames = new byte[n][];
        for (var i = 0; i < n; i++)
        {
            if (data.Length - pos < 4) throw new InvalidMediaException("The .zt file is truncated.");
            var len = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
            pos += 4;
            if (len < 0 || len > data.Length - pos) throw new InvalidMediaException("The .zt file has an invalid frame length.");
            frames[i] = data.Slice(pos, len).ToArray();
            pos += len;
        }
        return (delays, frames);
    }
}
