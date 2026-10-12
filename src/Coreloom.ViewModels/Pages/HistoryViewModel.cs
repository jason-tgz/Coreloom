using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Formatting;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Pages;

/// <summary>A selectable metric in the Performance History picker.</summary>
public sealed class HistoryMetricOption : ObservableObject
{
    private bool _isSelected;

    public HistoryMetricOption(MetricDescriptor descriptor, Action<HistoryMetricOption> onToggled)
    {
        Descriptor = descriptor;
        _onToggled = onToggled;
    }

    private readonly Action<HistoryMetricOption> _onToggled;

    public MetricDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string Name => Descriptor.DeviceName is { Length: > 0 } device && !Descriptor.Name.Contains(device, StringComparison.OrdinalIgnoreCase)
        ? $"{Descriptor.Name}"
        : Descriptor.Name;

    public string Group => Descriptor.Category switch
    {
        MetricCategory.Cpu => "Processor",
        MetricCategory.Gpu => "Graphics",
        MetricCategory.Memory => "Memory",
        MetricCategory.Storage => "Storage",
        MetricCategory.Network => "Network",
        MetricCategory.Motherboard => "Motherboard",
        MetricCategory.Application => "Coreloom",
        _ => "System",
    };

    public string? Device => Descriptor.DeviceName;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onToggled(this);
            }
        }
    }

    internal void SetSelectedSilently(bool value)
    {
        if (_isSelected != value)
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}

/// <summary>A chart in the stacked comparison view, with window statistics.</summary>
public sealed class HistoryChartViewModel : ObservableObject
{
    public HistoryChartViewModel(HistoryMetricOption option, ChartViewModel chart, MetricViewModel metric)
    {
        Option = option;
        Chart = chart;
        Metric = metric;
    }

    public HistoryMetricOption Option { get; }

    public ChartViewModel Chart { get; }

    public MetricViewModel Metric { get; }
}

/// <summary>
/// Performance History: pick up to six metrics and compare them on stacked, time-aligned charts with
/// synchronized hover. Data can be exported to CSV for the selected window.
/// </summary>
public sealed class HistoryViewModel : PageViewModel
{
    public const int MaxSelected = 6;
    private static readonly string[] DefaultSelection = [MetricIds.CpuLoad, "gpu/{0}/load", MetricIds.CpuTemperature, "gpu/{0}/temp"];

    private readonly IShellService _shell;
    private int _catalogVersion = -1;
    private string _filter = string.Empty;
    private bool _initialized;

    public HistoryViewModel(PageContext context, IShellService shell)
        : base("history", "Performance History", "Icon.History", context)
    {
        _shell = shell;
        ExportCommand = new RelayCommand(Export, () => Charts.Count > 0);
        ClearSelectionCommand = new RelayCommand(() =>
        {
            foreach (var option in Options.Where(o => o.IsSelected).ToArray())
            {
                option.IsSelected = false;
            }
        });
        RemoveChartCommand = new RelayCommand<HistoryChartViewModel>(chart =>
        {
            if (chart is not null)
            {
                chart.Option.IsSelected = false;
            }
        });
    }

    public ObservableCollection<HistoryMetricOption> Options { get; } = [];

    public ObservableCollection<HistoryMetricOption> FilteredOptions { get; } = [];

    public ObservableCollection<HistoryChartViewModel> Charts { get; } = [];

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectionText => $"{Charts.Count} of {MaxSelected} selected";

    public IRelayCommand ExportCommand { get; }

    public IRelayCommand ClearSelectionCommand { get; }

    public IRelayCommand<HistoryChartViewModel> RemoveChartCommand { get; }

    protected override void OnActivated()
    {
        _catalogVersion = -1;
    }

    protected override void OnUpdate(TelemetryFrame frame)
    {
        if (Catalog.Version != _catalogVersion)
        {
            _catalogVersion = Catalog.Version;
            RebuildOptions();
        }

        foreach (var chart in Charts)
        {
            chart.Chart.Update(frame, Formatter);
            UpdateMetric(chart.Metric, frame, withStats: true);
        }
    }

    private void RebuildOptions()
    {
        var selected = Charts.Select(c => c.Option.Id).ToHashSet(StringComparer.Ordinal);
        var tracked = Catalog.Where(d => d.TrackHistory && !d.IsRaw && History.Contains(d.Id))
            .OrderBy(d => d.Category)
            .ThenBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.SortOrder)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (Options.Select(o => o.Id).SequenceEqual(tracked.Select(d => d.Id)))
        {
            return;
        }

        Options.Clear();
        foreach (var descriptor in tracked)
        {
            var option = new HistoryMetricOption(descriptor, OnOptionToggled);
            option.SetSelectedSilently(selected.Contains(descriptor.Id));
            Options.Add(option);
        }

        if (!_initialized && Options.Count > 0)
        {
            _initialized = true;
            int slot = Context.GpuSlot;
            foreach (string pattern in DefaultSelection)
            {
                string id = string.Format(CultureInfo.InvariantCulture, pattern, slot);
                if (Options.FirstOrDefault(o => o.Id == id) is { } option)
                {
                    option.IsSelected = true;
                }
            }
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string f = _filter.Trim();
        FilteredOptions.Clear();
        foreach (var option in Options)
        {
            if (f.Length == 0 || option.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || option.Group.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (option.Device?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                FilteredOptions.Add(option);
            }
        }
    }

    private void OnOptionToggled(HistoryMetricOption option)
    {
        if (option.IsSelected)
        {
            if (Charts.Count >= MaxSelected)
            {
                option.SetSelectedSilently(false);
                return;
            }

            var d = option.Descriptor;
            var style = ValueStyles.ForKind(d.Kind);
            string color = ColorKeys.Sequence[Charts.Count % ColorKeys.Sequence.Count];
            var scale = d.Kind switch
            {
                MetricKind.Load or MetricKind.Control or MetricKind.Level => ChartScale.Percent,
                MetricKind.Temperature => ChartScale.Temperature,
                _ => ChartScale.Auto,
            };
            string title = d.DeviceName is { Length: > 0 } device ? $"{d.Name} — {device}" : d.Name;
            var chart = new ChartViewModel(title, scale, new ChartSeriesViewModel(d.Id, d.Name, color, style));
            var metric = new MetricViewModel(d.Id, d.Name, style, color);
            Charts.Add(new HistoryChartViewModel(option, chart, metric));
        }
        else
        {
            var existing = Charts.FirstOrDefault(c => ReferenceEquals(c.Option, option));
            if (existing is not null)
            {
                Charts.Remove(existing);
            }
        }

        OnPropertyChanged(nameof(SelectionText));
        ExportCommand.NotifyCanExecuteChanged();
        var frame = Monitor.Engine.LatestFrame;
        if (frame.Sequence > 0)
        {
            OnUpdate(frame);
        }
    }

    private void Export()
    {
        string? path = _shell.PickSaveFile($"coreloom-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv", "CSV file (*.csv)|*.csv");
        if (path is null)
        {
            return;
        }

        long to = Timeline.RightEdge;
        long from = to - Timeline.WindowMilliseconds;
        string csv = CsvExporter.Build(History, Charts.Select(c => c.Option.Descriptor).ToArray(), from, to, Timeline.ToLocalTime);
        try
        {
            File.WriteAllText(path, csv, new UTF8Encoding(true));
            _shell.OpenFolder(Path.GetDirectoryName(path)!);
        }
        catch (Exception ex)
        {
            _shell.ShowMessage("Export failed", ex.Message);
        }
    }
}

/// <summary>Builds a wide CSV (one row per timestamp, one column per metric) from history.</summary>
public static class CsvExporter
{
    public static string Build(HistoryStore history, IReadOnlyList<MetricDescriptor> metrics, long from, long to, Func<long, DateTime> toLocalTime)
    {
        var columns = new List<SortedDictionary<long, float>>();
        var allTimes = new SortedSet<long>();
        var buffer = new HistoryPoint[256];
        foreach (var metric in metrics)
        {
            var map = new SortedDictionary<long, float>();
            var series = history.Get(metric.Id);
            if (series is not null)
            {
                int n = series.CopyRange(from, to, ref buffer);
                for (int i = 0; i < n; i++)
                {
                    map[buffer[i].T] = buffer[i].V;
                    allTimes.Add(buffer[i].T);
                }
            }

            columns.Add(map);
        }

        var sb = new StringBuilder();
        sb.Append("timestamp");
        foreach (var metric in metrics)
        {
            sb.Append(',').Append(Escape($"{metric.Name} ({Unit(metric.Kind)})"));
        }

        sb.AppendLine();
        foreach (long t in allTimes)
        {
            sb.Append(toLocalTime(t).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            foreach (var column in columns)
            {
                sb.Append(',');
                if (column.TryGetValue(t, out float v))
                {
                    sb.Append(v.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Unit(MetricKind kind) => kind switch
    {
        MetricKind.Load or MetricKind.Control or MetricKind.Level => "%",
        MetricKind.Temperature => "°C",
        MetricKind.Clock => "MHz",
        MetricKind.Power => "W",
        MetricKind.Voltage => "V",
        MetricKind.Current => "A",
        MetricKind.Fan => "RPM",
        MetricKind.Data => "bytes",
        MetricKind.Throughput => "bytes/s",
        MetricKind.Duration => "s",
        _ => "value",
    };

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
