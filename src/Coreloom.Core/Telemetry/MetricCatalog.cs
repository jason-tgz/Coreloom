namespace Coreloom.Core.Telemetry;

/// <summary>
/// Thread-safe registry of every metric known to the application. Providers register metrics
/// during initialization (or lazily when a sensor appears) and receive a <see cref="MetricHandle"/>.
/// </summary>
public sealed class MetricCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MetricDescriptor> _byId = new(StringComparer.Ordinal);
    private readonly List<MetricDescriptor> _ordered = [];
    private readonly Dictionary<string, string> _unavailableReasons = new(StringComparer.Ordinal);
    private int _version;

    /// <summary>Raised (on the registering thread) after metrics were added or reasons changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Incremented on every change. Cheap way for consumers to detect changes.</summary>
    public int Version => Volatile.Read(ref _version);

    public int Count
    {
        get { lock (_gate) return _ordered.Count; }
    }

    /// <summary>
    /// Registers a metric source. If the id is already known the existing descriptor is reused
    /// (the first registration defines the display name and kind), and a new handle for this
    /// source is returned.
    /// </summary>
    public MetricHandle Register(MetricDescriptor descriptor, string provider, Provenance provenance = Provenance.Measured, int priority = 0, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        MetricDescriptor effective;
        bool added = false;
        lock (_gate)
        {
            if (!_byId.TryGetValue(descriptor.Id, out effective!))
            {
                effective = descriptor;
                _byId.Add(descriptor.Id, descriptor);
                _ordered.Add(descriptor);
                if (_unavailableReasons.TryGetValue(descriptor.Id, out var reason))
                {
                    descriptor.UnavailableReason = reason;
                }
                added = true;
                _version++;
            }
        }

        if (added)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return new MetricHandle(effective, provider, provenance, priority, note);
    }

    public bool TryGet(string id, out MetricDescriptor descriptor)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(id, out descriptor!);
        }
    }

    public MetricDescriptor? Get(string id)
    {
        lock (_gate)
        {
            return _byId.GetValueOrDefault(id);
        }
    }

    public bool Contains(string id)
    {
        lock (_gate)
        {
            return _byId.ContainsKey(id);
        }
    }

    /// <summary>Returns a copy of all descriptors in registration order.</summary>
    public IReadOnlyList<MetricDescriptor> Snapshot()
    {
        lock (_gate)
        {
            return _ordered.ToArray();
        }
    }

    public IReadOnlyList<MetricDescriptor> Where(Func<MetricDescriptor, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_gate)
        {
            return _ordered.Where(predicate).ToArray();
        }
    }

    /// <summary>
    /// Records why a metric is unavailable. Works even before the metric is registered so
    /// that capability checks can explain missing sensors up-front.
    /// </summary>
    public void SetUnavailableReason(string id, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        bool changed;
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                changed = _unavailableReasons.Remove(id);
            }
            else
            {
                changed = !_unavailableReasons.TryGetValue(id, out var existing) || existing != reason;
                _unavailableReasons[id] = reason;
            }

            if (_byId.TryGetValue(id, out var descriptor))
            {
                descriptor.UnavailableReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
            }

            if (changed)
            {
                _version++;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? GetUnavailableReason(string id)
    {
        lock (_gate)
        {
            return _unavailableReasons.GetValueOrDefault(id);
        }
    }

    /// <summary>Called by the engine when the winning source of a metric changes.</summary>
    internal static void SetActiveSource(MetricDescriptor descriptor, MetricHandle handle)
    {
        if (!ReferenceEquals(descriptor.ActiveSource, handle))
        {
            descriptor.ActiveSource = handle;
        }
    }
}
