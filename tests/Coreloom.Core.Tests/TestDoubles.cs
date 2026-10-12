using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Tests;

internal sealed class FakeClock : IMonotonicClock
{
    public long NowMilliseconds { get; set; } = 1_000_000;

    public DateTime LocalNow { get; set; } = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Local);

    public void Advance(long milliseconds)
    {
        NowMilliseconds += milliseconds;
        LocalNow = LocalNow.AddMilliseconds(milliseconds);
    }
}

/// <summary>Scriptable provider for engine tests.</summary>
internal sealed class FakeProvider : ITelemetryProvider
{
    private readonly List<(MetricDescriptor Descriptor, int Priority, bool Track)> _definitions = [];
    private readonly Dictionary<string, MetricHandle> _handles = new();

    public FakeProvider(string name, MonitoringSection section = MonitoringSection.System, int stage = 0)
    {
        Name = name;
        Section = section;
        InitStage = stage;
    }

    public string Name { get; }

    public MonitoringSection Section { get; }

    public int InitStage { get; }

    public TimeSpan MinimumPollInterval { get; set; } = TimeSpan.Zero;

    public Dictionary<string, double> Values { get; } = new();

    public int InitializeCalls { get; private set; }

    public int PollCalls { get; private set; }

    public int ShutdownCalls { get; private set; }

    public bool ThrowOnPoll { get; set; }

    public bool ThrowOnInitialize { get; set; }

    public ProviderInitResult InitResult { get; set; } = ProviderInitResult.Running();

    public List<int> InitOrderLog { get; set; } = [];

    public FakeProvider Define(string id, MetricCategory category = MetricCategory.System, MetricKind kind = MetricKind.Load, int priority = 0, bool trackHistory = true)
    {
        _definitions.Add((new MetricDescriptor(id, id, category, kind) { TrackHistory = trackHistory }, priority, trackHistory));
        return this;
    }

    public ProviderInitResult Initialize(IProviderContext context)
    {
        InitializeCalls++;
        InitOrderLog.Add(InitStage);
        if (ThrowOnInitialize)
        {
            throw new InvalidOperationException("init failed");
        }

        foreach (var (descriptor, priority, _) in _definitions)
        {
            _handles[descriptor.Id] = context.Register(descriptor, Provenance.Measured, priority);
        }

        return InitResult;
    }

    public void Poll(IMetricWriter writer)
    {
        PollCalls++;
        if (ThrowOnPoll)
        {
            throw new InvalidOperationException("poll failed");
        }

        foreach (var (id, value) in Values)
        {
            if (_handles.TryGetValue(id, out var handle))
            {
                writer.Write(handle, value);
            }
        }
    }

    public void Shutdown() => ShutdownCalls++;

    public void Dispose()
    {
    }
}
