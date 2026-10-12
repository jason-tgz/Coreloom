using System.Collections.Concurrent;

namespace Vigil.Core.History;

/// <summary>
/// Rolling, bounded in-memory history for every tracked metric. Retention is fixed to one hour of
/// samples at the current polling interval, so memory use is bounded regardless of uptime.
/// </summary>
public sealed class HistoryStore
{
    /// <summary>Longest selectable chart window.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    public const int MinimumCapacity = 128;
    public const int MaximumCapacity = 14_432;

    private readonly ConcurrentDictionary<string, MetricSeries> _series = new(StringComparer.Ordinal);
    private int _capacity;

    public HistoryStore(int capacity)
    {
        _capacity = Math.Clamp(capacity, MinimumCapacity, MaximumCapacity);
    }

    public static HistoryStore ForInterval(TimeSpan interval) => new(CapacityFor(interval));

    /// <summary>Capacity needed to keep <see cref="Retention"/> worth of samples at the given interval.</summary>
    public static int CapacityFor(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromSeconds(1);
        }

        double samples = Math.Ceiling(Retention.TotalMilliseconds / interval.TotalMilliseconds);
        return (int)Math.Clamp(samples + 32, MinimumCapacity, MaximumCapacity);
    }

    public int Capacity => Volatile.Read(ref _capacity);

    public int SeriesCount => _series.Count;

    public MetricSeries GetOrCreate(string id) => _series.GetOrAdd(id, static (key, cap) => new MetricSeries(key, cap), Capacity);

    public MetricSeries? Get(string id) => _series.GetValueOrDefault(id);

    public bool Contains(string id) => _series.ContainsKey(id);

    public void Append(string id, long timestamp, double value) => GetOrCreate(id).Append(timestamp, value);

    public IReadOnlyCollection<string> Ids => _series.Keys.ToArray();

    /// <summary>Resizes every series, keeping the newest samples.</summary>
    public void SetCapacity(int capacity)
    {
        capacity = Math.Clamp(capacity, MinimumCapacity, MaximumCapacity);
        Volatile.Write(ref _capacity, capacity);
        foreach (var series in _series.Values)
        {
            series.Resize(capacity);
        }
    }

    public void Clear()
    {
        foreach (var series in _series.Values)
        {
            series.Clear();
        }
    }

    /// <summary>Approximate memory footprint of the stored samples in bytes.</summary>
    public long EstimatedBytes => (long)_series.Count * Capacity * (sizeof(long) + sizeof(float));
}
