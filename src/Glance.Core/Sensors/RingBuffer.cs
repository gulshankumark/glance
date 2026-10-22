namespace Glance.Core.Sensors;

/// <summary>Fixed-capacity buffer; oldest samples are overwritten. Not thread-safe.</summary>
internal sealed class RingBuffer
{
    private readonly double?[] _data;
    private int _next;

    public RingBuffer(int capacity) => _data = new double?[capacity];

    public int Count { get; private set; }
    public int Capacity => _data.Length;

    public void Add(double? v)
    {
        _data[_next] = v;
        _next = (_next + 1) % _data.Length;
        if (Count < _data.Length) Count++;
    }

    /// <summary>Last <paramref name="n"/> samples, oldest first, left-padded with null.</summary>
    public double?[] Last(int n)
    {
        var result = new double?[n];
        var take = Math.Min(n, Count);
        for (int i = 0; i < take; i++)
            result[n - take + i] = _data[((_next - take + i) % _data.Length + _data.Length) % _data.Length];
        return result;
    }
}
