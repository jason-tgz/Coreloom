using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Pages;

public enum CardVisual
{
    Gauge,
    Sparkline,
}

/// <summary>A summary card on the Overview (value + gauge + sparkline + stats).</summary>
public sealed class SummaryCardViewModel : ObservableObject
{
    public SummaryCardViewModel(string id, string title, string iconKey, MetricViewModel metric)
    {
        Id = id;
        Title = title;
        IconKey = iconKey;
        Metric = metric;
    }

    public string Id { get; }

    public string Title { get; }

    public string IconKey { get; }

    public MetricViewModel Metric { get; }

    public string ColorKey => Metric.ColorKey;
}

/// <summary>Base type for larger Overview panels (charts, donuts, network).</summary>
public abstract class DashboardPanelViewModel : ObservableObject
{
    protected DashboardPanelViewModel(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }
}

public sealed class ChartPanelViewModel : DashboardPanelViewModel
{
    public ChartPanelViewModel(string id, ChartViewModel chart)
        : base(id, chart.Title)
    {
        Chart = chart;
    }

    public ChartViewModel Chart { get; }
}

public sealed class MemoryPanelViewModel : DashboardPanelViewModel
{
    private string _centerText = "—";
    private string _caption = string.Empty;

    public MemoryPanelViewModel()
        : base(DashboardCatalog.MemoryDonut, "Memory")
    {
        Segments = [InUse, Available];
    }

    public DonutSegmentViewModel InUse { get; } = new("In use", ColorKeys.Memory);

    public DonutSegmentViewModel Available { get; } = new("Available", ColorKeys.Neutral);

    public ObservableCollection<DonutSegmentViewModel> Segments { get; }

    public ObservableCollection<KeyValueItem> Details { get; } = [];

    public string CenterText
    {
        get => _centerText;
        set => SetProperty(ref _centerText, value);
    }

    public string Caption
    {
        get => _caption;
        set => SetProperty(ref _caption, value);
    }
}

/// <summary>A volume row/donut.</summary>
public sealed class VolumeViewModel : ObservableObject
{
    private string _usedText = "—";
    private string _freeText = "—";
    private string _capacityText = "—";
    private string _percentText = "—";
    private double _fraction;

    public VolumeViewModel(string letter, string name)
    {
        Letter = letter;
        Name = name;
        Segments = [Used, Free];
    }

    public string Letter { get; }

    public string Name { get; }

    public DonutSegmentViewModel Used { get; } = new("Used", ColorKeys.Storage);

    public DonutSegmentViewModel Free { get; } = new("Free", ColorKeys.Neutral);

    public ObservableCollection<DonutSegmentViewModel> Segments { get; }

    public string UsedText
    {
        get => _usedText;
        private set => SetProperty(ref _usedText, value);
    }

    public string FreeText
    {
        get => _freeText;
        private set => SetProperty(ref _freeText, value);
    }

    public string CapacityText
    {
        get => _capacityText;
        private set => SetProperty(ref _capacityText, value);
    }

    public string PercentText
    {
        get => _percentText;
        private set => SetProperty(ref _percentText, value);
    }

    public double Fraction
    {
        get => _fraction;
        private set
        {
            if (!_fraction.Equals(value))
            {
                _fraction = value;
                OnPropertyChanged();
            }
        }
    }

    public void Update(TelemetryFrame frame, UnitFormatter f)
    {
        if (frame.TryGetValue(MetricIds.VolumeTotal(Letter), out double total) && total > 0
            && frame.TryGetValue(MetricIds.VolumeFree(Letter), out double free))
        {
            double used = Math.Max(0, total - free);
            Used.Value = used;
            Free.Value = free;
            Used.ValueText = f.Bytes(used).ToString();
            Free.ValueText = f.Bytes(free).ToString();
            UsedText = Used.ValueText;
            FreeText = Free.ValueText;
            CapacityText = f.Bytes(total, 0).ToString();
            Fraction = used / total;
            PercentText = f.Percent(Fraction * 100).ToString();
        }
    }
}

public sealed class StoragePanelViewModel : DashboardPanelViewModel
{
    public StoragePanelViewModel()
        : base(DashboardCatalog.StorageDonut, "Storage")
    {
    }

    public ObservableCollection<VolumeViewModel> Volumes { get; } = [];

    public MetricViewModel Read { get; } = new(MetricIds.DiskTotalRead, "Read", ValueStyle.DiskRate, ColorKeys.Read);

    public MetricViewModel Write { get; } = new(MetricIds.DiskTotalWrite, "Write", ValueStyle.DiskRate, ColorKeys.Write);
}

public sealed class NetworkPanelViewModel : DashboardPanelViewModel
{
    private string _adapterName = "No active connection";

    public NetworkPanelViewModel()
        : base(DashboardCatalog.Network, "Network")
    {
        Chart = new ChartViewModel("Network", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.NetworkActiveDown, "Download", ColorKeys.NetworkDown, ValueStyle.NetworkRate),
            new ChartSeriesViewModel(MetricIds.NetworkActiveUp, "Upload", ColorKeys.NetworkUp, ValueStyle.NetworkRate));
    }

    public MetricViewModel Download { get; } = new(MetricIds.NetworkActiveDown, "Download", ValueStyle.NetworkRate, ColorKeys.NetworkDown);

    public MetricViewModel Upload { get; } = new(MetricIds.NetworkActiveUp, "Upload", ValueStyle.NetworkRate, ColorKeys.NetworkUp);

    public ChartViewModel Chart { get; }

    public string AdapterName
    {
        get => _adapterName;
        set => SetProperty(ref _adapterName, value);
    }
}

/// <summary>The Overview dashboard: summary cards and larger panels, ordered and filtered by user settings.</summary>
public sealed class OverviewViewModel : PageViewModel
{
    private readonly Dictionary<string, SummaryCardViewModel> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DashboardPanelViewModel> _panels = new(StringComparer.Ordinal);
    private readonly ChartViewModel _loadChart;
    private readonly ChartViewModel _temperatureChart;
    private readonly ChartViewModel _powerChart;
    private readonly MemoryPanelViewModel _memory = new();
    private readonly StoragePanelViewModel _storage = new();
    private readonly NetworkPanelViewModel _network = new();
    private readonly MetricViewModel _hotSpot;
    private readonly MetricViewModel _maxThread;
    private readonly MetricViewModel _memoryUsed;
    private readonly MetricViewModel _memoryTotal;
    private readonly MetricViewModel _powerLimit;
    private readonly MetricViewModel _ccd1;
    private readonly MetricViewModel _ccd2;
    private int _gpuSlot = -1;
    private int _deviceVersion = -1;
    private IReadOnlyList<KeyValueItem> _leftSpecs = [];
    private IReadOnlyList<KeyValueItem> _rightSpecs = [];
    private int _driveCount;

    public OverviewViewModel(PageContext context)
        : base("overview", "Overview", "Icon.Overview", context)
    {
        var cpuTemp = AlertThresholds("cpu-temp", Thresholds.CpuTemperature, 10);
        var gpuTemp = AlertThresholds("gpu-temp", Thresholds.GpuTemperature, 8);

        AddCard(DashboardCatalog.GpuLoad, "GPU", "Icon.Gpu", new MetricViewModel(MetricIds.GpuLoad(0), "GPU utilization", ValueStyle.Percent, ColorKeys.Gpu, Thresholds.Load));
        AddCard(DashboardCatalog.GpuTemperature, "GPU temperature", "Icon.Temperature", new MetricViewModel(MetricIds.GpuTemperature(0), "GPU temperature", ValueStyle.Temperature, ColorKeys.Gpu, gpuTemp));
        AddCard(DashboardCatalog.CpuLoad, "CPU", "Icon.Cpu", new MetricViewModel(MetricIds.CpuLoad, "CPU utilization", ValueStyle.Percent, ColorKeys.Cpu, Thresholds.Load));
        AddCard(DashboardCatalog.CpuTemperature, "CPU temperature", "Icon.Temperature", new MetricViewModel(MetricIds.CpuTemperature, "CPU temperature", ValueStyle.Temperature, ColorKeys.Cpu, cpuTemp));
        AddCard(DashboardCatalog.Memory, "Memory", "Icon.Memory", new MetricViewModel(MetricIds.MemoryLoad, "Memory utilization", ValueStyle.Percent, ColorKeys.Memory, Thresholds.Memory));
        AddCard(DashboardCatalog.GpuPower, "GPU power", "Icon.Power", new MetricViewModel(MetricIds.GpuPower(0), "GPU power draw", ValueStyle.Power, ColorKeys.Power, gaugeMaximum: 600));

        _hotSpot = new MetricViewModel(MetricIds.GpuHotSpotTemperature(0), "Hot spot", ValueStyle.Temperature);
        _maxThread = new MetricViewModel(MetricIds.CpuLoadMaxThread, "Busiest thread", ValueStyle.Percent);
        _memoryUsed = new MetricViewModel(MetricIds.MemoryUsed, "In use", ValueStyle.Bytes);
        _memoryTotal = new MetricViewModel(MetricIds.MemoryTotal, "Usable", ValueStyle.Bytes);
        _powerLimit = new MetricViewModel(MetricIds.GpuPowerLimit(0), "Power limit", ValueStyle.Power);
        _ccd1 = new MetricViewModel(MetricIds.CpuCcdTemperature(1), "CCD1", ValueStyle.Temperature);
        _ccd2 = new MetricViewModel(MetricIds.CpuCcdTemperature(2), "CCD2", ValueStyle.Temperature);

        _loadChart = new ChartViewModel("Processor & graphics load", ChartScale.Percent,
            new ChartSeriesViewModel(MetricIds.CpuLoad, "CPU", ColorKeys.Cpu, ValueStyle.Percent),
            new ChartSeriesViewModel(MetricIds.GpuLoad(0), "GPU", ColorKeys.Gpu, ValueStyle.Percent));
        _temperatureChart = new ChartViewModel("Temperatures", ChartScale.Temperature,
            new ChartSeriesViewModel(MetricIds.CpuTemperature, "CPU", ColorKeys.Cpu, ValueStyle.Temperature, fill: false),
            new ChartSeriesViewModel(MetricIds.GpuTemperature(0), "GPU", ColorKeys.Gpu, ValueStyle.Temperature, fill: false),
            new ChartSeriesViewModel(MetricIds.GpuHotSpotTemperature(0), "GPU hot spot", ColorKeys.Temperature, ValueStyle.Temperature, fill: false));
        _powerChart = new ChartViewModel("Power", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.GpuPower(0), "GPU board", ColorKeys.Gpu, ValueStyle.Power),
            new ChartSeriesViewModel(MetricIds.CpuPower, "CPU package", ColorKeys.Cpu, ValueStyle.Power));

        _panels[DashboardCatalog.LoadChart] = new ChartPanelViewModel(DashboardCatalog.LoadChart, _loadChart);
        _panels[DashboardCatalog.TemperatureChart] = new ChartPanelViewModel(DashboardCatalog.TemperatureChart, _temperatureChart);
        _panels[DashboardCatalog.PowerChart] = new ChartPanelViewModel(DashboardCatalog.PowerChart, _powerChart);
        _panels[DashboardCatalog.MemoryDonut] = _memory;
        _panels[DashboardCatalog.StorageDonut] = _storage;
        _panels[DashboardCatalog.Network] = _network;

        CustomizeCommand = new RelayCommand(() => CustomizeRequested?.Invoke());
        ApplyLayout();
    }

    /// <summary>Raised when the user wants to edit the dashboard (the shell opens Settings › Dashboard).</summary>
    public event Action? CustomizeRequested;

    public ObservableCollection<SummaryCardViewModel> SummaryCards { get; } = [];

    public ObservableCollection<DashboardPanelViewModel> Panels { get; } = [];

    public IRelayCommand CustomizeCommand { get; }

    public bool ShowStatisticsOnCards => ShowStatistics;

    /// <summary>The header at the top of the page: board, graphics card, key specs and the PC illustration.</summary>
    public SystemSummaryViewModel Specs { get; } = new();

    public override void OnSettingsChanged(string section)
    {
        _deviceVersion = -1; // header text follows unit settings (GB vs GiB)
        if (section is "Dashboard" or "Alerts" or "All")
        {
            var cpuTemp = AlertThresholds("cpu-temp", Thresholds.CpuTemperature, 10);
            var gpuTemp = AlertThresholds("gpu-temp", Thresholds.GpuTemperature, 8);
            _cards[DashboardCatalog.CpuTemperature].Metric.Thresholds = cpuTemp;
            _cards[DashboardCatalog.GpuTemperature].Metric.Thresholds = gpuTemp;
            ApplyLayout();
            OnPropertyChanged(nameof(ShowStatisticsOnCards));
        }
    }

    private void AddCard(string id, string title, string icon, MetricViewModel metric) =>
        _cards[id] = new SummaryCardViewModel(id, title, icon, metric);

    /// <summary>Derives colour thresholds from the user's alert rule (critical = alert threshold).</summary>
    private Thresholds AlertThresholds(string ruleId, Thresholds fallback, double elevatedOffset)
    {
        var rule = Settings.Current.Alerts.Rules.FirstOrDefault(r => r.Id == ruleId);
        return rule is { Enabled: true } ? new Thresholds(rule.Threshold - elevatedOffset, rule.Threshold) : fallback;
    }

    private void ApplyLayout()
    {
        var layout = Settings.Current.Dashboard.Cards;
        var summary = layout.Where(c => c.Visible && _cards.ContainsKey(c.Id)).Select(c => _cards[c.Id]).ToList();
        var panels = layout.Where(c => c.Visible && _panels.ContainsKey(c.Id)).Select(c => _panels[c.Id]).ToList();
        Sync(SummaryCards, summary);
        Sync(Panels, panels);
    }

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        if (target.SequenceEqual(desired))
        {
            return;
        }

        target.Clear();
        foreach (var item in desired)
        {
            target.Add(item);
        }
    }

    private void Retarget(int slot)
    {
        _gpuSlot = slot;
        _deviceVersion = -1; // the header names the selected graphics card
        _cards[DashboardCatalog.GpuLoad].Metric.Retarget(MetricIds.GpuLoad(slot));
        _cards[DashboardCatalog.GpuTemperature].Metric.Retarget(MetricIds.GpuTemperature(slot));
        _cards[DashboardCatalog.GpuPower].Metric.Retarget(MetricIds.GpuPower(slot));
        _hotSpot.Retarget(MetricIds.GpuHotSpotTemperature(slot));
        _powerLimit.Retarget(MetricIds.GpuPowerLimit(slot));
        _loadChart.Series[1].MetricId = MetricIds.GpuLoad(slot);
        _temperatureChart.Series[1].MetricId = MetricIds.GpuTemperature(slot);
        _temperatureChart.Series[2].MetricId = MetricIds.GpuHotSpotTemperature(slot);
        _powerChart.Series[0].MetricId = MetricIds.GpuPower(slot);
    }

    protected override void OnActivated()
    {
        _deviceVersion = -1;
    }

    protected override void OnUpdate(TelemetryFrame frame)
    {
        int slot = Context.GpuSlot;
        if (slot != _gpuSlot)
        {
            Retarget(slot);
        }

        if (Devices.Version != _deviceVersion)
        {
            _deviceVersion = Devices.Version;
            RefreshStaticInfo();
        }

        var f = Formatter;
        foreach (var card in SummaryCards)
        {
            UpdateMetric(card.Metric, frame, withStats: true);
        }

        UpdateMetrics(frame, false, _hotSpot, _maxThread, _memoryUsed, _memoryTotal, _powerLimit, _ccd1, _ccd2);
        UpdateSpecs(frame, f);

        // Secondary lines.
        _cards[DashboardCatalog.GpuTemperature].Metric.Detail = _hotSpot.HasValue ? $"Hot spot {_hotSpot.Number}{_hotSpot.Unit}" : null;
        _cards[DashboardCatalog.CpuLoad].Metric.Detail = _maxThread.HasValue ? $"Busiest thread {_maxThread.Number}{_maxThread.Unit}" : null;
        var cpuTempCard = _cards[DashboardCatalog.CpuTemperature].Metric;
        cpuTempCard.Detail = cpuTempCard.HasValue
            ? (_ccd1.HasValue ? $"CCD1 {_ccd1.Number}° · CCD2 {(_ccd2.HasValue ? _ccd2.Number + "°" : "—")}" : null)
            : "Needs administrator + PawnIO";
        _cards[DashboardCatalog.Memory].Metric.Detail = _memoryUsed.HasValue && _memoryTotal.HasValue
            ? $"{f.BytesAs(_memoryUsed.Value, f.ByteUnitIndex(_memoryTotal.Value), 1).Number} of {f.Bytes(_memoryTotal.Value, 1)}"
            : null;
        var power = _cards[DashboardCatalog.GpuPower].Metric;
        if (_powerLimit.HasValue && _powerLimit.Value > 0)
        {
            power.GaugeMaximum = _powerLimit.Value;
            power.Thresholds = new Thresholds(_powerLimit.Value * 0.9, _powerLimit.Value * 0.99);
            power.Detail = $"of {_powerLimit.Number} {_powerLimit.Unit} limit";
        }

        foreach (var panel in Panels)
        {
            switch (panel)
            {
                case ChartPanelViewModel chart:
                    chart.Chart.Update(frame, f);
                    break;
                case MemoryPanelViewModel memory:
                    UpdateMemory(memory, frame, f);
                    break;
                case StoragePanelViewModel storage:
                    foreach (var volume in storage.Volumes)
                    {
                        volume.Update(frame, f);
                    }

                    UpdateMetrics(frame, false, storage.Read, storage.Write);
                    break;
                case NetworkPanelViewModel network:
                    UpdateMetrics(frame, false, network.Download, network.Upload);
                    network.Chart.Update(frame, f);
                    network.AdapterName = Monitor.NetworkAdapters().FirstOrDefault(a => a.IsActive).Name ?? "No active connection";
                    break;
            }
        }
    }

    private static void UpdateMemory(MemoryPanelViewModel memory, TelemetryFrame frame, UnitFormatter f)
    {
        if (frame.TryGetValue(MetricIds.MemoryUsed, out double used) && frame.TryGetValue(MetricIds.MemoryAvailable, out double available))
        {
            double total = used + available;
            memory.InUse.Value = used;
            memory.Available.Value = available;
            memory.InUse.ValueText = f.Bytes(used).ToString();
            memory.Available.ValueText = f.Bytes(available).ToString();
            memory.CenterText = total > 0 ? f.Percent(used * 100 / total).ToString() : "—";
            memory.Caption = $"{f.Bytes(used, 1)} of {f.Bytes(total, 1)}";

            var details = new List<KeyValueItem>
            {
                new("In use", f.Bytes(used).ToString()),
                new("Available", f.Bytes(available).ToString()),
            };
            if (frame.TryGetValue(MetricIds.MemoryCommitUsed, out double commit) && frame.TryGetValue(MetricIds.MemoryCommitLimit, out double limit))
            {
                details.Add(new("Committed", $"{f.Bytes(commit).Number} / {f.Bytes(limit)}"));
            }

            if (frame.TryGetValue(MetricIds.MemoryCache, out double cache))
            {
                details.Add(new("Cached", f.Bytes(cache).ToString()));
            }

            if (!memory.Details.SequenceEqual(details))
            {
                memory.Details.Clear();
                foreach (var item in details)
                {
                    memory.Details.Add(item);
                }
            }
        }
    }

    private void RefreshStaticInfo()
    {
        var summary = Monitor.Summary;
        var cpu = Devices.Get(MetricIds.CpuDeviceId);
        string cpuName = ShortCpuName(cpu?.Name ?? summary.CpuName);
        var gpu = Monitor.Gpus().FirstOrDefault(g => g.Slot == _gpuSlot);
        var gpuDevice = Devices.Get(MetricIds.GpuDeviceId(_gpuSlot));
        string? gpuName = Clean(gpuDevice?.Name ?? gpu.Name);
        bool integrated = gpuDevice?.IsIntegrated ?? gpu.IsIntegrated;

        Specs.SetIdentity(
            SystemDescriptions.BoardName(summary.Motherboard) ?? summary.ComputerName,
            gpuName ?? cpuName,
            SystemDescriptions.HasFourMemorySticks(summary.MemoryModules),
            SystemDescriptions.GpuSizeFor(gpuName, integrated),
            SystemDescriptions.GpuBadge(gpuName));

        _leftSpecs = Rows(
            ("Processor", cpuName),
            ("Cores", summary.LogicalProcessors > 0 ? SystemDescriptions.CoresLine(summary.PhysicalCores, summary.LogicalProcessors) : null),
            ("Memory", SystemDescriptions.MemoryLine(summary.InstalledMemoryBytes, summary.MemoryModules, Formatter)));
        _rightSpecs = Rows(
            ("Graphics memory", gpuDevice?.GetProperty("VRAM")),
            ("Driver", gpuDevice?.GetProperty("Driver")),
            ("System", SystemDescriptions.WindowsLine(summary.OperatingSystem, summary.OsVersion)),
            ("BIOS", summary.Bios));
        _driveCount = Devices.GetByCategory(MetricCategory.Storage)
            .Count(d => d.Id.StartsWith("disk/", StringComparison.Ordinal) && !string.Equals(d.GetProperty("Removable"), "Yes", StringComparison.OrdinalIgnoreCase));

        // The header carries the board and the operating system now.
        Subtitle = null;

        // Volumes for the storage panel (system drive first).
        string systemLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':', '/') is { Length: 1 } l ? l.ToUpperInvariant() : "C";
        var volumes = Devices.GetByCategory(MetricCategory.Storage)
            .Where(d => d.Kind == "Volume" && d.Id.StartsWith("vol/", StringComparison.Ordinal))
            .Select(d => (Letter: d.Id[4..], d.Name))
            .OrderBy(v => v.Letter == systemLetter ? 0 : 1)
            .ThenBy(v => v.Letter, StringComparer.Ordinal)
            .ToArray();
        if (!_storage.Volumes.Select(v => v.Letter).SequenceEqual(volumes.Select(v => v.Letter)))
        {
            _storage.Volumes.Clear();
            foreach (var (letter, name) in volumes)
            {
                _storage.Volumes.Add(new VolumeViewModel(letter, name));
            }
        }
    }

    /// <summary>The live parts of the header: storage total (volumes report their size with the first frame) and the pump screen.</summary>
    private void UpdateSpecs(TelemetryFrame frame, UnitFormatter f)
    {
        double total = 0;
        foreach (var volume in _storage.Volumes)
        {
            if (frame.TryGetValue(MetricIds.VolumeTotal(volume.Letter), out double size) && size > 0)
            {
                total += size;
            }
        }

        string? storage = (_driveCount, total) switch
        {
            (0, <= 0) => null,
            (0, _) => f.Bytes(total, 1).ToString(),
            (_, <= 0) => DriveCount(_driveCount),
            _ => $"{DriveCount(_driveCount)} · {f.Bytes(total, 1)}",
        };
        IReadOnlyList<KeyValueItem> left = storage is null ? _leftSpecs : [.. _leftSpecs, new KeyValueItem("Storage", storage)];
        Specs.SetSpecs(left, _rightSpecs);

        if (frame.TryGetValue(MetricIds.CpuTemperature, out double cpuTemp))
        {
            Specs.SetScreen(f.Temperature(cpuTemp).Number + "°", "CPU");
        }
        else if (frame.TryGetValue(MetricIds.GpuTemperature(_gpuSlot), out double gpuTemp))
        {
            Specs.SetScreen(f.Temperature(gpuTemp).Number + "°", "GPU");
        }
        else if (frame.TryGetValue(MetricIds.CpuLoad, out double load))
        {
            Specs.SetScreen(f.Percent(load).Number + "%", "CPU");
        }
        else
        {
            Specs.SetScreen("—", "CPU");
        }
    }

    private static string DriveCount(int count) => count == 1 ? "1 drive" : $"{count} drives";

    private static KeyValueItem[] Rows(params (string Key, string? Value)[] rows) =>
        rows.Where(r => !string.IsNullOrWhiteSpace(r.Value) && r.Value != "—").Select(r => new KeyValueItem(r.Key, r.Value!.Trim())).ToArray();

    private static string? Clean(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : string.Join(' ', name.Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
