namespace Glance.Core.Media;

public record Animation(int Width, int Height, IReadOnlyList<byte[]> JpegFrames, IReadOnlyList<int> DelaysMs);
