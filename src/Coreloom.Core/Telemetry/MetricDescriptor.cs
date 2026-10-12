namespace Coreloom.Core.Telemetry;

/// <summary>
/// Describes a metric (a time series of numeric readings). One descriptor exists per metric id.
/// Several providers may report the same canonical id; the highest-priority source that produced
/// a value in a given frame wins, and <see cref="ActiveSource"/> reflects that source.
/// </summary>
public sealed class MetricDescriptor
{
    public MetricDescriptor(string id, string name, MetricCategory category, MetricKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        Category = category;
        Kind = kind;
    }

    /// <summary>Stable identifier, e.g. <c>gpu/0/temp</c> or <c>lhm/amdcpu/0/temperature/2</c>.</summary>
    public string Id { get; }

    /// <summary>Human readable sensor label.</summary>
    public string Name { get; init; }

    public MetricCategory Category { get; init; }

    public MetricKind Kind { get; init; }

    /// <summary>Id of the device this metric belongs to (see <see cref="DeviceInfo"/>), e.g. <c>gpu/0</c>.</summary>
    public string? DeviceId { get; init; }

    /// <summary>Display name of the device, e.g. "NVIDIA GeForce RTX 5090".</summary>
    public string? DeviceName { get; init; }

    /// <summary>Optional grouping label used in sensor tables (e.g. "Temperatures").</summary>
    public string? Group { get; init; }

    /// <summary>When true, readings are kept in the rolling history buffer.</summary>
    public bool TrackHistory { get; init; } = true;

    /// <summary>True for raw sensors listed verbatim from a library (not canonical metrics).</summary>
    public bool IsRaw { get; init; }

    /// <summary>Sort order within a group.</summary>
    public int SortOrder { get; init; }

    /// <summary>Provider currently supplying values (updated when the winning source changes).</summary>
    public MetricHandle? ActiveSource { get; internal set; }

    /// <summary>
    /// Explanation shown when no value is available (e.g. "Requires administrator rights and the PawnIO driver").
    /// </summary>
    public string? UnavailableReason { get; internal set; }

    public override string ToString() => $"{Id} ({Name})";
}

/// <summary>
/// A provider's registration for a metric. Providers keep the handle and write values through it.
/// </summary>
public sealed class MetricHandle
{
    internal MetricHandle(MetricDescriptor descriptor, string provider, Provenance provenance, int priority, string? note)
    {
        Descriptor = descriptor;
        Provider = provider;
        Provenance = provenance;
        Priority = priority;
        Note = note;
    }

    public MetricDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    /// <summary>Name of the provider/API, e.g. "NVML", "LibreHardwareMonitor", "Windows PDH".</summary>
    public string Provider { get; }

    public Provenance Provenance { get; }

    /// <summary>Higher priority sources win when several report the same metric in one frame.</summary>
    public int Priority { get; }

    /// <summary>Optional note about the source, e.g. how a derived value is computed.</summary>
    public string? Note { get; }

    public override string ToString() => $"{Id} via {Provider} ({Provenance}, p{Priority})";
}
