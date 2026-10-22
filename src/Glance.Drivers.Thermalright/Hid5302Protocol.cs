using System.Buffers.Binary;

namespace Glance.Drivers.Thermalright;

public record HandshakeReply(byte SubModel, byte Model, string? Serial);

/// <summary>Wire format for Thermalright HID 0416:5302. See docs/protocols/thermalright-hid-5302.md.</summary>
public static class Hid5302Protocol
{
    public const int ChunkSize = 512;
    const int HeaderLength = 20;
    static readonly byte[] Magic = { 0xDA, 0xDB, 0xDC, 0xDD };

    public static byte[] BuildHandshake()
    {
        var b = new byte[HeaderLength];
        Magic.CopyTo(b, 0);
        b[12] = 0x01;
        return b;
    }

    public static HandshakeReply? ParseHandshakeReply(ReadOnlySpan<byte> report)
    {
        if (report.Length < 14 || !report.Slice(1, 4).SequenceEqual(Magic) || report[13] != 0x01)
            return null;
        string? serial = report.Length >= 37 && report[17] == 0x10
            ? Convert.ToHexString(report.Slice(21, 16))
            : null;
        return new HandshakeReply(report[5], report[6], serial);
    }

    public static (int Width, int Height)? ResolvePanel(byte model, byte subModel) =>
        model == 0x80 && subModel <= 2 ? (1280, 480) : null;

    public static byte[] BuildJpegFrame(ReadOnlySpan<byte> jpeg, int width, int height)
    {
        var b = new byte[HeaderLength + jpeg.Length];
        Magic.CopyTo(b, 0);
        b[4] = 0x02;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), checked((ushort)width));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(10), checked((ushort)height));
        b[12] = 0x02;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), (uint)jpeg.Length);
        jpeg.CopyTo(b.AsSpan(HeaderLength));
        return b;
    }

    public static IReadOnlyList<byte[]> ToReports(ReadOnlySpan<byte> payload)
    {
        var reports = new List<byte[]>((payload.Length + ChunkSize - 1) / ChunkSize);
        for (int off = 0; off < payload.Length; off += ChunkSize)
        {
            var r = new byte[ChunkSize + 1]; // r[0] = report ID 0
            payload.Slice(off, Math.Min(ChunkSize, payload.Length - off)).CopyTo(r.AsSpan(1));
            reports.Add(r);
        }
        return reports;
    }
}
