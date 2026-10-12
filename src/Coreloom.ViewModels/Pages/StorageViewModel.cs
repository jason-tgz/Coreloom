using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.ViewModels.Pages;

/// <summary>A physical disk with its volumes, live throughput, activity, temperature and health.</summary>
public sealed class DiskViewModel : ObservableObject
{
    private string _name;
    private string? _kind;
    private string? _capacity;
    private string? _health;

    public DiskViewModel(int number, string name)
    {
        Number = number;
        _name = name;
        Read = new MetricViewModel(MetricIds.DiskRead(number), "Read", ValueStyle.DiskRate, ColorKeys.Read);
        Write = new MetricViewModel(MetricIds.DiskWrite(number), "Write", ValueStyle.DiskRate, ColorKeys.Write);
        Activity = new MetricViewModel(MetricIds.DiskActivity(number), "Active time", ValueStyle.Percent, ColorKeys.Storage, Thresholds.Load);
        Temperature = new MetricViewModel(MetricIds.DiskTemperature(number), "Temperature", ValueStyle.Temperature, ColorKeys.Temperature, new Thresholds(60, 70));
        LifeUsed = new MetricViewModel(MetricIds.DiskLifeUsed(number), "Endurance used", ValueStyle.Percent, ColorKeys.Storage, new Thresholds(80, 95));
        Spare = new MetricViewModel(MetricIds.DiskAvailableSpare(number), "Available spare", ValueStyle.Percent, ColorKeys.Storage);
        Chart = new ChartViewModel("Throughput", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.DiskRead(number), "Read", ColorKeys.Read, ValueStyle.DiskRate),
            new ChartSeriesViewModel(MetricIds.DiskWrite(number), "Write", ColorKeys.Write, ValueStyle.DiskRate));
    }

    public int Number { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string? Kind
    {
        get => _kind;
        set => SetProperty(ref _kind, value);
    }

    public string? Capacity
    {
        get => _capacity;
        set => SetProperty(ref _capacity, value);
    }

    public string? Health
    {
        get => _health;
        private set => SetProperty(ref _health, value);
    }

    public MetricViewModel Read { get; }

    public MetricViewModel Write { get; }

    public MetricViewModel Activity { get; }

    public MetricViewModel Temperature { get; }

    public MetricViewModel LifeUsed { get; }

    public MetricViewModel Spare { get; }

    public ChartViewModel Chart { get; }

    public ObservableCollection<VolumeViewModel> Volumes { get; } = [];

    public ObservableCollection<KeyValueItem> Specifications { get; } = [];

    public void Update(TelemetryFrame frame, UnitFormatter f, MetricCatalog catalog)
    {
        foreach (var m in new[] { Read, Write, Activity, Temperature, LifeUsed, Spare })
        {
            m.Update(frame, f, catalog);
        }

        Health = LifeUsed.HasValue
            ? $"{LifeUsed.Number}% endurance used{(Spare.HasValue ? $" · {Spare.Number}% spare" : string.Empty)}"
            : null;
        Chart.Update(frame, f);
        foreach (var volume in Volumes)
        {
            volume.Update(frame, f);
        }
    }
}

/// <summary>Storage page: per-disk cards with volume donuts, throughput charts and health.</summary>
public sealed class StorageViewModel : PageViewModel
{
    private int _deviceVersion = -1;

    public StorageViewModel(PageContext context)
        : base("storage", "Storage", "Icon.Storage", context, MonitoringSection.Storage)
    {
        TotalRead = new MetricViewModel(MetricIds.DiskTotalRead, "Read", ValueStyle.DiskRate, ColorKeys.Read);
        TotalWrite = new MetricViewModel(MetricIds.DiskTotalWrite, "Write", ValueStyle.DiskRate, ColorKeys.Write);
        TotalChart = new ChartViewModel("All disks", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.DiskTotalRead, "Read", ColorKeys.Read, ValueStyle.DiskRate),
            new ChartSeriesViewModel(MetricIds.DiskTotalWrite, "Write", ColorKeys.Write, ValueStyle.DiskRate));
    }

    public MetricViewModel TotalRead { get; }

    public MetricViewModel TotalWrite { get; }

    public ChartViewModel TotalChart { get; }

    public ObservableCollection<DiskViewModel> Disks { get; } = [];

    protected override void OnActivated() => _deviceVersion = -1;

    protected override void OnUpdate(TelemetryFrame frame)
    {
        if (Devices.Version != _deviceVersion)
        {
            _deviceVersion = Devices.Version;
            RebuildDisks();
        }

        UpdateMetrics(frame, true, TotalRead, TotalWrite);
        TotalChart.Update(frame, Formatter);
        foreach (var disk in Disks)
        {
            disk.Update(frame, Formatter, Catalog);
        }
    }

    private void RebuildDisks()
    {
        var devices = Devices.GetByCategory(MetricCategory.Storage);
        var disks = devices.Where(d => d.Id.StartsWith("disk/", StringComparison.Ordinal))
            .Select(d => (Number: int.TryParse(d.Id[5..], out int n) ? n : -1, Device: d))
            .Where(x => x.Number >= 0)
            .OrderBy(x => x.Number)
            .ToArray();

        if (!Disks.Select(d => d.Number).SequenceEqual(disks.Select(d => d.Number)))
        {
            Disks.Clear();
            foreach (var (number, device) in disks)
            {
                Disks.Add(new DiskViewModel(number, device.Name));
            }
        }

        var volumes = devices.Where(d => d.Kind == "Volume").ToArray();
        foreach (var disk in Disks)
        {
            var device = disks.First(d => d.Number == disk.Number).Device;
            disk.Name = device.Name;
            disk.Kind = device.Kind;
            disk.Capacity = device.GetProperty("Capacity");
            var specs = ToItems(device);
            if (!disk.Specifications.SequenceEqual(specs))
            {
                disk.Specifications.Clear();
                foreach (var item in specs)
                {
                    disk.Specifications.Add(item);
                }
            }

            var mine = volumes.Where(v => v.GetProperty("Disk number") == disk.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Select(v => (Letter: v.Id[4..], v.Name))
                .OrderBy(v => v.Letter, StringComparer.Ordinal)
                .ToArray();
            if (!disk.Volumes.Select(v => v.Letter).SequenceEqual(mine.Select(v => v.Letter)))
            {
                disk.Volumes.Clear();
                foreach (var (letter, name) in mine)
                {
                    disk.Volumes.Add(new VolumeViewModel(letter, name));
                }
            }
        }
    }
}
