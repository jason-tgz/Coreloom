namespace Coreloom.Core.Telemetry;

/// <summary>
/// Static, descriptive information about a detected device (CPU, GPU, disk, adapter, board...).
/// Properties are ordered key/value pairs displayed in "Specifications" panels.
/// </summary>
public sealed class DeviceInfo
{
    public DeviceInfo(string id, MetricCategory category, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Category = category;
        Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim();
    }

    /// <summary>Stable id such as <c>cpu</c>, <c>gpu/0</c>, <c>disk/1</c>, <c>net/{guid}</c>, <c>board</c>, <c>system</c>.</summary>
    public string Id { get; }

    public MetricCategory Category { get; }

    public string Name { get; init; }

    public string? Vendor { get; init; }

    /// <summary>Short classification, e.g. "Discrete GPU", "NVMe SSD", "Ethernet".</summary>
    public string? Kind { get; init; }

    /// <summary>For GPUs: true when the device is an integrated GPU. Null when unknown.</summary>
    public bool? IsIntegrated { get; init; }

    /// <summary>Sort order among devices of the same category.</summary>
    public int Order { get; init; }

    /// <summary>Which provider(s) contributed the information.</summary>
    public string? Source { get; init; }

    public IReadOnlyList<KeyValuePair<string, string>> Properties { get; init; } = [];

    public string? GetProperty(string key)
    {
        foreach (var pair in Properties)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Merges <paramref name="update"/> into this instance: non-empty scalar values win and
    /// properties are merged by key (existing keys keep their position and get the new value).
    /// </summary>
    public DeviceInfo MergeWith(DeviceInfo update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var props = new List<KeyValuePair<string, string>>(Properties);
        foreach (var pair in update.Properties)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            int index = props.FindIndex(p => string.Equals(p.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                props[index] = pair;
            }
            else
            {
                props.Add(pair);
            }
        }

        string? source = Source;
        if (!string.IsNullOrWhiteSpace(update.Source) && (source is null || !source.Contains(update.Source, StringComparison.OrdinalIgnoreCase)))
        {
            source = source is null ? update.Source : $"{source}, {update.Source}";
        }

        return new DeviceInfo(Id, Category, string.IsNullOrWhiteSpace(update.Name) || update.Name == update.Id ? Name : update.Name)
        {
            Vendor = update.Vendor ?? Vendor,
            Kind = update.Kind ?? Kind,
            IsIntegrated = update.IsIntegrated ?? IsIntegrated,
            Order = update.Order != 0 ? update.Order : Order,
            Source = source,
            Properties = props,
        };
    }

    public override string ToString() => $"{Id}: {Name}";
}

/// <summary>Thread-safe collection of <see cref="DeviceInfo"/> keyed by id.</summary>
public sealed class DeviceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceInfo> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];
    private int _version;

    public event EventHandler? Changed;

    public int Version => Volatile.Read(ref _version);

    public void Upsert(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_gate)
        {
            if (_devices.TryGetValue(device.Id, out var existing))
            {
                _devices[device.Id] = existing.MergeWith(device);
            }
            else
            {
                _devices[device.Id] = device;
                _order.Add(device.Id);
            }

            _version++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _devices.Remove(id);
            if (removed)
            {
                _order.Remove(id);
                _version++;
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public DeviceInfo? Get(string id)
    {
        lock (_gate)
        {
            return _devices.GetValueOrDefault(id);
        }
    }

    public IReadOnlyList<DeviceInfo> GetAll()
    {
        lock (_gate)
        {
            return _order.Select(id => _devices[id]).ToArray();
        }
    }

    public IReadOnlyList<DeviceInfo> GetByCategory(MetricCategory category)
    {
        lock (_gate)
        {
            return _order.Select(id => _devices[id])
                .Where(d => d.Category == category)
                .OrderBy(d => d.Order)
                .ToArray();
        }
    }
}
