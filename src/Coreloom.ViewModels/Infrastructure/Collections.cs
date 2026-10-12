using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Infrastructure;

/// <summary>A label/value pair for specification lists.</summary>
public sealed record KeyValueItem(string Key, string Value) : StaticRecord;

/// <summary>A segment of a donut chart.</summary>
public sealed class DonutSegmentViewModel : ObservableObject
{
    private double _value;
    private string _valueText = string.Empty;

    public DonutSegmentViewModel(string label, string colorKey)
    {
        Label = label;
        ColorKey = colorKey;
    }

    public string Label { get; }

    public string ColorKey { get; }

    public double Value
    {
        get => _value;
        set
        {
            if (!_value.Equals(value))
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }
}

/// <summary>One row of a sensor table.</summary>
public sealed class SensorRowViewModel : ObservableObject
{
    private string _value = "—";
    private bool _hasValue;
    private string? _source;
    private bool _isDerived;
    private string _toolTip = string.Empty;

    public SensorRowViewModel(MetricDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public MetricDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string Name => Descriptor.Name;

    public string Group => Descriptor.Group ?? Descriptor.Kind.ToString();

    public string Device => Descriptor.DeviceName ?? string.Empty;

    public string Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public bool HasValue
    {
        get => _hasValue;
        private set => SetProperty(ref _hasValue, value);
    }

    public string? Source
    {
        get => _source;
        private set => SetProperty(ref _source, value);
    }

    public bool IsDerived
    {
        get => _isDerived;
        private set => SetProperty(ref _isDerived, value);
    }

    public string ToolTip
    {
        get => _toolTip;
        private set => SetProperty(ref _toolTip, value);
    }

    public void Update(TelemetryFrame frame, UnitFormatter formatter)
    {
        if (frame.TryGetSample(Descriptor.Id, out var sample))
        {
            HasValue = true;
            Value = Descriptor.Kind == MetricKind.Voltage && sample.Value < 2.5 && Descriptor.Category == MetricCategory.Gpu
                ? formatter.Millivolts(sample.Value).ToString()
                : formatter.Format(Descriptor.Kind, sample.Value).ToString();
            Source = sample.Source.Provider;
            IsDerived = sample.Source.Provenance == Provenance.Derived;
            ToolTip = $"{Descriptor.Id}\n{sample.Source.Provider} · {sample.Source.Provenance}{(sample.Source.Note is { Length: > 0 } note ? "\n" + note : string.Empty)}";
        }
        else
        {
            HasValue = false;
            Value = "—";
            Source = null;
            IsDerived = false;
            ToolTip = $"{Descriptor.Id}\nUnavailable: {Descriptor.UnavailableReason ?? "no reading in the latest sample"}";
        }
    }
}

/// <summary>A named group of sensor rows (e.g. "Temperatures").</summary>
public sealed class SensorGroupViewModel : StaticItem
{
    public SensorGroupViewModel(string name, IReadOnlyList<SensorRowViewModel> rows)
    {
        Name = name;
        Rows = rows;
    }

    public string Name { get; }

    public IReadOnlyList<SensorRowViewModel> Rows { get; }
}

/// <summary>
/// A filterable, self-refreshing table of sensors (canonical and raw). Rows are rebuilt only when the
/// catalog changes; values are refreshed every frame while the owning page is visible.
/// </summary>
public sealed class SensorTableViewModel : ObservableObject
{
    private readonly Func<MetricDescriptor, bool> _filter;
    private int _catalogVersion = -1;
    private bool _showRaw;

    public SensorTableViewModel(Func<MetricDescriptor, bool> filter, bool showRaw = true)
    {
        _filter = filter;
        _showRaw = showRaw;
    }

    public ObservableCollection<SensorRowViewModel> Rows { get; } = [];

    /// <summary>Rows grouped by <see cref="SensorRowViewModel.Group"/>, canonical metrics first.</summary>
    public ObservableCollection<SensorGroupViewModel> Groups { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public bool ShowRaw
    {
        get => _showRaw;
        set
        {
            if (SetProperty(ref _showRaw, value))
            {
                _catalogVersion = -1;
            }
        }
    }

    public int Count => Rows.Count;

    public void Invalidate() => _catalogVersion = -1;

    public void Update(TelemetryFrame frame, UnitFormatter formatter, MetricCatalog catalog)
    {
        if (catalog.Version != _catalogVersion)
        {
            _catalogVersion = catalog.Version;
            var wanted = catalog.Where(d => _filter(d) && (_showRaw || !d.IsRaw))
                .OrderBy(d => d.IsRaw)
                .ThenBy(d => d.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.SortOrder)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (wanted.Length != Rows.Count || wanted.Where((d, i) => !ReferenceEquals(d, Rows[i].Descriptor)).Any())
            {
                Rows.Clear();
                foreach (var d in wanted)
                {
                    Rows.Add(new SensorRowViewModel(d));
                }

                Groups.Clear();
                foreach (var group in Rows.GroupBy(r => r.Descriptor.IsRaw ? $"{r.Group} · raw" : r.Group))
                {
                    Groups.Add(new SensorGroupViewModel(group.Key, group.ToArray()));
                }

                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        foreach (var row in Rows)
        {
            row.Update(frame, formatter);
        }
    }
}

/// <summary>A row in a "top processes" list.</summary>
public sealed class ProcessRowViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _primary = string.Empty;
    private string _secondary = string.Empty;
    private double _fraction;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Primary
    {
        get => _primary;
        set => SetProperty(ref _primary, value);
    }

    public string Secondary
    {
        get => _secondary;
        set => SetProperty(ref _secondary, value);
    }

    public double Fraction
    {
        get => _fraction;
        set
        {
            if (!_fraction.Equals(value))
            {
                _fraction = value;
                OnPropertyChanged();
            }
        }
    }
}

public enum ProcessSort
{
    Cpu,
    Memory,
}

/// <summary>Top-N processes, sampled on demand at a slow cadence while visible.</summary>
public sealed class ProcessListViewModel : ObservableObject
{
    private readonly IProcessSampler _sampler;
    private readonly ProcessSort _sort;
    private readonly int _count;
    private DateTime _lastSample = DateTime.MinValue;

    public ProcessListViewModel(IProcessSampler sampler, ProcessSort sort, int count = 6)
    {
        _sampler = sampler;
        _sort = sort;
        _count = count;
        for (int i = 0; i < count; i++)
        {
            Rows.Add(new ProcessRowViewModel());
        }
    }

    public ObservableCollection<ProcessRowViewModel> Rows { get; }  = [];

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    public void Reset() => _lastSample = DateTime.MinValue;

    public void Update(UnitFormatter formatter, double totalMemoryBytes)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSample < Interval)
        {
            return;
        }

        _lastSample = now;
        IReadOnlyList<ProcessUsage> sample;
        try
        {
            sample = _sampler.Sample();
        }
        catch
        {
            return;
        }

        var ordered = (_sort == ProcessSort.Cpu
                ? sample.Where(p => !p.Name.Equals("System", StringComparison.OrdinalIgnoreCase) || p.CpuPercent > 0).OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes)
                : sample.OrderByDescending(p => p.WorkingSetBytes))
            .Take(_count)
            .ToArray();

        for (int i = 0; i < _count; i++)
        {
            var row = Rows[i];
            if (i >= ordered.Length)
            {
                row.Name = string.Empty;
                row.Primary = string.Empty;
                row.Secondary = string.Empty;
                row.Fraction = 0;
                continue;
            }

            var p = ordered[i];
            row.Name = p.InstanceCount > 1 ? $"{TrimExe(p.Name)}  ×{p.InstanceCount}" : TrimExe(p.Name);
            if (_sort == ProcessSort.Cpu)
            {
                row.Primary = formatter.Percent(p.CpuPercent, 1).ToString();
                row.Secondary = formatter.Bytes(p.WorkingSetBytes).ToString();
                row.Fraction = Math.Clamp(p.CpuPercent / 100.0, 0, 1);
            }
            else
            {
                row.Primary = formatter.Bytes(p.WorkingSetBytes).ToString();
                row.Secondary = formatter.Percent(p.CpuPercent, 1).ToString() + " CPU";
                row.Fraction = totalMemoryBytes > 0 ? Math.Clamp(p.WorkingSetBytes / totalMemoryBytes, 0, 1) : 0;
            }
        }
    }

    private static string TrimExe(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
