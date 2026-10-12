namespace Vigil.Core.Telemetry;

/// <summary>A single reading together with the source that produced it.</summary>
public readonly record struct MetricSample(double Value, MetricHandle Source);

/// <summary>
/// An immutable snapshot of every metric value produced by one polling cycle.
/// </summary>
public sealed class TelemetryFrame
{
    private readonly IReadOnlyDictionary<string, MetricSample> _samples;

    public TelemetryFrame(long sequence, long timestamp, DateTime localTime, TimeSpan pollDuration, IReadOnlyDictionary<string, MetricSample> samples)
    {
        Sequence = sequence;
        Timestamp = timestamp;
        LocalTime = localTime;
        PollDuration = pollDuration;
        _samples = samples ?? throw new ArgumentNullException(nameof(samples));
    }

    public static TelemetryFrame Empty { get; } = new(0, 0, DateTime.MinValue, TimeSpan.Zero, new Dictionary<string, MetricSample>());

    /// <summary>Monotonically increasing frame number (starts at 1).</summary>
    public long Sequence { get; }

    /// <summary>Monotonic timestamp in milliseconds (see <see cref="IMonotonicClock"/>).</summary>
    public long Timestamp { get; }

    /// <summary>Wall-clock time of the frame (local time zone).</summary>
    public DateTime LocalTime { get; }

    /// <summary>Total time spent polling all providers for this frame.</summary>
    public TimeSpan PollDuration { get; }

    public IReadOnlyDictionary<string, MetricSample> Samples => _samples;

    public int Count => _samples.Count;

    public bool Contains(string id) => _samples.ContainsKey(id);

    public bool TryGetValue(string id, out double value)
    {
        if (_samples.TryGetValue(id, out var sample))
        {
            value = sample.Value;
            return true;
        }

        value = double.NaN;
        return false;
    }

    public double? GetValue(string id) => _samples.TryGetValue(id, out var sample) ? sample.Value : null;

    public bool TryGetSample(string id, out MetricSample sample) => _samples.TryGetValue(id, out sample);
}

/// <summary>Monotonic millisecond clock plus wall-clock time. Abstracted for tests.</summary>
public interface IMonotonicClock
{
    /// <summary>Milliseconds from an arbitrary origin. Never goes backwards; includes time spent in sleep.</summary>
    long NowMilliseconds { get; }

    DateTime LocalNow { get; }
}

public sealed class SystemMonotonicClock : IMonotonicClock
{
    public static SystemMonotonicClock Instance { get; } = new();

    public long NowMilliseconds => Environment.TickCount64;

    public DateTime LocalNow => DateTime.Now;
}

/// <summary>Collects writes for one frame, resolving conflicts by source priority.</summary>
internal sealed class FrameBuilder : IMetricWriter
{
    private readonly Dictionary<string, MetricSample> _samples = new(256, StringComparer.Ordinal);
    private readonly Func<MetricCategory, bool>? _categoryFilter;

    public FrameBuilder(Func<MetricCategory, bool>? categoryFilter = null)
    {
        _categoryFilter = categoryFilter;
    }

    public int Count => _samples.Count;

    public void Write(MetricHandle handle, double value)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!double.IsFinite(value))
        {
            return;
        }

        if (_categoryFilter is not null && !_categoryFilter(handle.Descriptor.Category))
        {
            return;
        }

        if (_samples.TryGetValue(handle.Id, out var existing) && existing.Source.Priority > handle.Priority)
        {
            return;
        }

        _samples[handle.Id] = new MetricSample(value, handle);
    }

    public TelemetryFrame Build(long sequence, long timestamp, DateTime localTime, TimeSpan pollDuration)
    {
        foreach (var sample in _samples.Values)
        {
            MetricCatalog.SetActiveSource(sample.Source.Descriptor, sample.Source);
        }

        // A builder is used for exactly one frame, so the dictionary can be handed over without copying.
        return new TelemetryFrame(sequence, timestamp, localTime, pollDuration, _samples);
    }
}
