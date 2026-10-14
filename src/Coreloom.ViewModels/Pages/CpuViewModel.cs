using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Pages;

/// <summary>A tile in the per-logical-processor grid.</summary>
public sealed class ThreadTileViewModel : ObservableObject
{
    private string _valueText = "—";
    private double _value;

    public ThreadTileViewModel(int index)
    {
        Index = index;
        MetricId = MetricIds.CpuThreadLoad(index);
        Label = $"CPU {index}";
    }

    public int Index { get; }

    public string MetricId { get; }

    public string Label { get; }

    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }

    public double Value
    {
        get => _value;
        private set
        {
            if (!_value.Equals(value))
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    public void Update(TelemetryFrame frame, UnitFormatter f)
    {
        if (frame.TryGetValue(MetricId, out double v))
        {
            Value = v;
            ValueText = f.Percent(v).ToString();
        }
        else
        {
            ValueText = "—";
        }
    }
}

/// <summary>A row in the per-core table (clock, effective clock, temperature, power).</summary>
public sealed class CoreRowViewModel : ObservableObject
{
    private string _clock = "—";
    private string _effective = "—";
    private string _temperature = "—";
    private string _power = "—";

    public CoreRowViewModel(int core)
    {
        Core = core;
        Label = $"Core {core}";
    }

    public int Core { get; }

    public string Label { get; }

    public string Clock
    {
        get => _clock;
        private set => SetProperty(ref _clock, value);
    }

    public string Effective
    {
        get => _effective;
        private set => SetProperty(ref _effective, value);
    }

    public string Temperature
    {
        get => _temperature;
        private set => SetProperty(ref _temperature, value);
    }

    public string Power
    {
        get => _power;
        private set => SetProperty(ref _power, value);
    }

    public void Update(TelemetryFrame frame, UnitFormatter f)
    {
        Clock = frame.TryGetValue(MetricIds.CpuCoreClock(Core), out double c) ? f.ClockGigahertz(c).ToString() : "—";
        Effective = frame.TryGetValue(MetricIds.CpuCoreEffectiveClock(Core), out double e) ? f.ClockGigahertz(e).ToString() : "—";
        Temperature = frame.TryGetValue(MetricIds.CpuCoreTemperature(Core), out double t) ? f.Temperature(t).ToString() : "—";
        Power = frame.TryGetValue(MetricIds.CpuCorePower(Core), out double p) ? f.Power(p).ToString() : "—";
    }
}

/// <summary>Processor page.</summary>
public sealed class CpuViewModel : PageViewModel
{
    private int _deviceVersion = -1;
    private int _catalogVersion = -1;
    private string _cpuName = "Processor";
    private HardwareVendor _vendor;
    private bool _showThreads;
    private bool _hasCoreTable;
    private bool _lowLevelUnavailable;
    private string? _lowLevelMessage;

    public CpuViewModel(PageContext context)
        : base("cpu", "Processor", "Icon.Cpu", context, MonitoringSection.Cpu)
    {
        Load = new MetricViewModel(MetricIds.CpuLoad, "Utilization", ValueStyle.Percent, ColorKeys.Cpu, Thresholds.Load);
        Temperature = new MetricViewModel(MetricIds.CpuTemperature, "Temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.CpuTemperature);
        Power = new MetricViewModel(MetricIds.CpuPower, "Package power", ValueStyle.Power, ColorKeys.Power, gaugeMaximum: 200);
        Clock = new MetricViewModel(MetricIds.CpuClockEffectiveAverage, "Effective clock", ValueStyle.ClockGigahertz, ColorKeys.Cpu, gaugeMaximum: 6000);
        DerivedClock = new MetricViewModel(MetricIds.CpuClockDerived, "Speed (estimated)", ValueStyle.ClockGigahertz, ColorKeys.Cpu);
        AverageClock = new MetricViewModel(MetricIds.CpuClockAverage, "Average core clock", ValueStyle.ClockGigahertz, ColorKeys.Cpu);
        MaxThread = new MetricViewModel(MetricIds.CpuLoadMaxThread, "Busiest thread", ValueStyle.Percent, ColorKeys.Cpu);
        Voltage = new MetricViewModel(MetricIds.CpuVoltage, "Core voltage", ValueStyle.Volts, ColorKeys.Power);
        Ccd1 = new MetricViewModel(MetricIds.CpuCcdTemperature(1), "CCD1 temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.CpuTemperature);
        Ccd2 = new MetricViewModel(MetricIds.CpuCcdTemperature(2), "CCD2 temperature", ValueStyle.Temperature, ColorKeys.Temperature, Thresholds.CpuTemperature);

        Details = [DerivedClock, AverageClock, MaxThread, Voltage, Ccd1, Ccd2];

        LoadChart = new ChartViewModel("Utilization", ChartScale.Percent,
            new ChartSeriesViewModel(MetricIds.CpuLoad, "Total", ColorKeys.Cpu, ValueStyle.Percent),
            new ChartSeriesViewModel(MetricIds.CpuLoadMaxThread, "Busiest thread", ColorKeys.Temperature, ValueStyle.Percent, fill: false));
        TemperatureChart = new ChartViewModel("Temperature", ChartScale.Temperature,
            new ChartSeriesViewModel(MetricIds.CpuTemperature, "Package (Tctl/Tdie)", ColorKeys.Temperature, ValueStyle.Temperature, fill: false),
            new ChartSeriesViewModel(MetricIds.CpuCcdTemperature(1), "CCD1", ColorKeys.Cpu, ValueStyle.Temperature, fill: false),
            new ChartSeriesViewModel(MetricIds.CpuCcdTemperature(2), "CCD2", ColorKeys.Memory, ValueStyle.Temperature, fill: false));
        PowerChart = new ChartViewModel("Package power", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.CpuPower, "Package", ColorKeys.Power, ValueStyle.Power));
        ClockChart = new ChartViewModel("Clocks", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.CpuClockEffectiveAverage, "Effective (avg)", ColorKeys.Cpu, ValueStyle.ClockMegahertz, fill: false),
            new ChartSeriesViewModel(MetricIds.CpuClockDerived, "Speed (estimated)", ColorKeys.Neutral, ValueStyle.ClockMegahertz, fill: false));

        for (int i = 0; i < Monitor.Summary.LogicalProcessors; i++)
        {
            Threads.Add(new ThreadTileViewModel(i));
        }

        Processes = new ProcessListViewModel(Monitor.Processes, ProcessSort.Cpu);
        BoardSensors = new SensorTableViewModel(d => d.Category == MetricCategory.Motherboard && d.Kind is MetricKind.Fan or MetricKind.Temperature or MetricKind.Control, showRaw: false);
        Sensors = new SensorTableViewModel(d => d.Category is MetricCategory.Cpu or MetricCategory.Motherboard && !d.Id.StartsWith("cpu/thread/", StringComparison.Ordinal));
        _showThreads = Settings.Current.Monitoring.ShowPerThreadGraphs;
        _vendor = HardwareVendors.Detect(null, Monitor.Summary.CpuName);
    }

    /// <summary>"amd", "intel" or "nvidia"-style key for the brand mark next to the CPU name; empty when unknown.</summary>
    public string VendorKey => HardwareVendors.Key(_vendor);

    public string CpuName
    {
        get => _cpuName;
        private set => SetProperty(ref _cpuName, value);
    }

    public ObservableCollection<KeyValueItem> Specifications { get; } = [];

    /// <summary>The processor's health card.</summary>
    public HealthReportViewModel Health => Context.Health.Cpu;

    public MetricViewModel Load { get; }

    public MetricViewModel Temperature { get; }

    public MetricViewModel Power { get; }

    /// <summary>Effective clock (LHM) — falls back to the derived estimate in the view when unavailable.</summary>
    public MetricViewModel Clock { get; }

    public MetricViewModel DerivedClock { get; }

    public MetricViewModel AverageClock { get; }

    public MetricViewModel MaxThread { get; }

    public MetricViewModel Voltage { get; }

    public MetricViewModel Ccd1 { get; }

    public MetricViewModel Ccd2 { get; }

    public IReadOnlyList<MetricViewModel> Details { get; }

    public ChartViewModel LoadChart { get; }

    public ChartViewModel TemperatureChart { get; }

    public ChartViewModel PowerChart { get; }

    public ChartViewModel ClockChart { get; }

    public ObservableCollection<ThreadTileViewModel> Threads { get; } = [];

    public ObservableCollection<CoreRowViewModel> Cores { get; } = [];

    public ProcessListViewModel Processes { get; }

    public SensorTableViewModel BoardSensors { get; }

    public SensorTableViewModel Sensors { get; }

    public bool ShowThreads
    {
        get => _showThreads;
        set
        {
            if (SetProperty(ref _showThreads, value))
            {
                Settings.Update("Monitoring", s => s.Monitoring.ShowPerThreadGraphs = value);
            }
        }
    }

    public bool HasCoreTable
    {
        get => _hasCoreTable;
        private set => SetProperty(ref _hasCoreTable, value);
    }

    /// <summary>True when temperature/power/clocks are missing because of privileges or the PawnIO driver.</summary>
    public bool LowLevelUnavailable
    {
        get => _lowLevelUnavailable;
        private set => SetProperty(ref _lowLevelUnavailable, value);
    }

    public string? LowLevelMessage
    {
        get => _lowLevelMessage;
        private set => SetProperty(ref _lowLevelMessage, value);
    }

    protected override void OnActivated()
    {
        _deviceVersion = -1;
        Processes.Reset();
    }

    protected override void OnUpdate(TelemetryFrame frame)
    {
        if (Devices.Version != _deviceVersion)
        {
            _deviceVersion = Devices.Version;
            var device = Devices.Get(MetricIds.CpuDeviceId);
            CpuName = ShortCpuName(device?.Name ?? Monitor.Summary.CpuName);
            var vendor = HardwareVendors.Detect(device?.Vendor, device?.Name ?? Monitor.Summary.CpuName);
            if (vendor != _vendor)
            {
                _vendor = vendor;
                OnPropertyChanged(nameof(VendorKey));
            }

            Subtitle = string.Join(" · ", new[] { device?.GetProperty("Cores") is { } c ? $"{c} cores" : null, device?.GetProperty("Threads") is { } t ? $"{t} threads" : null, device?.GetProperty("L3 cache") is { } l3 ? $"{l3} L3" : null }.Where(s => s is not null));
            var items = ToItems(device);
            if (!Specifications.SequenceEqual(items))
            {
                Specifications.Clear();
                foreach (var item in items)
                {
                    Specifications.Add(item);
                }
            }
        }

        if (Catalog.Version != _catalogVersion)
        {
            _catalogVersion = Catalog.Version;
            RebuildCores();
        }

        UpdateMetrics(frame, true, Load, Temperature, Power, Clock);
        UpdateMetrics(frame, false, DerivedClock, AverageClock, MaxThread, Voltage, Ccd1, Ccd2);
        Load.Detail = MaxThread.HasValue ? $"Busiest thread {MaxThread.Number}%" : null;
        Clock.Detail = Clock.HasValue ? null : DerivedClock.HasValue ? $"Estimated {DerivedClock.Number} {DerivedClock.Unit}" : null;
        Temperature.Detail = Ccd1.HasValue ? $"CCD1 {Ccd1.Number}° · CCD2 {(Ccd2.HasValue ? Ccd2.Number + "°" : "—")}" : null;

        LowLevelUnavailable = !Temperature.HasValue;
        LowLevelMessage = LowLevelUnavailable
            ? Catalog.Get(MetricIds.CpuTemperature)?.UnavailableReason ?? Catalog.GetUnavailableReason(MetricIds.CpuTemperature)
            : null;

        UpdateCharts(frame, LoadChart, TemperatureChart, PowerChart, ClockChart);

        if (ShowThreads)
        {
            foreach (var tile in Threads)
            {
                tile.Update(frame, Formatter);
            }
        }

        foreach (var core in Cores)
        {
            core.Update(frame, Formatter);
        }

        Processes.Update(Formatter, frame.GetValue(MetricIds.MemoryTotal) ?? 0);
        BoardSensors.Update(frame, Formatter, Catalog);
        Sensors.Update(frame, Formatter, Catalog);
    }

    private void RebuildCores()
    {
        var coreNumbers = Catalog.Where(d => d.Id.StartsWith("cpu/core/", StringComparison.Ordinal))
            .Select(d => int.TryParse(d.Id.Split('/')[2], out int n) ? n : -1)
            .Where(n => n >= 0)
            .Distinct()
            .Order()
            .ToArray();
        if (!Cores.Select(c => c.Core).SequenceEqual(coreNumbers))
        {
            Cores.Clear();
            foreach (int n in coreNumbers)
            {
                Cores.Add(new CoreRowViewModel(n));
            }
        }

        HasCoreTable = Cores.Count > 0;
    }
}
