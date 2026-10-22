using Glance.Core.Devices;

namespace Glance.Testing;

/// <summary>Matches every device. <see cref="Next"/> is consumed by the next connect; <see cref="AlwaysFail"/> wins over it.</summary>
public sealed class FakeDriver : IDisplayDriver
{
    private readonly object _gate = new();
    private ConnectResult? _next;
    private int _connectCalls;

    public string Id => "fake";
    public string DisplayName => "Fake driver";

    public ConnectResult? Next { set { lock (_gate) _next = value; } }
    public string? AlwaysFail { get; set; }
    public int ConnectCalls => Volatile.Read(ref _connectCalls);
    public FakeConnection? LastConnection { get; private set; }

    /// <summary>Matches throws for the device with this path.</summary>
    public string? ThrowOnMatchPath { get; set; }
    /// <summary>When set, every connect returns what this makes (wins over <see cref="Next"/>).</summary>
    public Func<ConnectResult>? Always { get; set; }

    public bool Matches(UsbDeviceInfo dev) =>
        dev.Path == ThrowOnMatchPath ? throw new InvalidOperationException("match failed") : true;

    public Task<ConnectResult> ConnectAsync(UsbDeviceInfo dev, IHidPortFactory ports, CancellationToken ct)
    {
        Interlocked.Increment(ref _connectCalls);
        ConnectResult result;
        lock (_gate)
        {
            if (Always is { } make) result = make();
            else if (AlwaysFail is { } reason) result = new ConnectResult.Failed(reason);
            else
            {
                result = _next ?? new ConnectResult.Failed("no result queued");
                _next = null;
            }
            if (result is ConnectResult.Connected { Connection: FakeConnection c }) LastConnection = c;
        }
        return Task.FromResult(result);
    }
}
