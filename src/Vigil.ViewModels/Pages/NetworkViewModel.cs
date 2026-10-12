using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vigil.Core.Formatting;
using Vigil.Core.Telemetry;
using Vigil.ViewModels.Infrastructure;

namespace Vigil.ViewModels.Pages;

public sealed class AdapterViewModel : ObservableObject
{
    private string _name;
    private string? _kind;
    private string? _status;
    private string? _linkSpeed;
    private string? _address;
    private bool _isActive;

    public AdapterViewModel(string key, string name)
    {
        Key = key;
        _name = name;
        Download = new MetricViewModel(MetricIds.NetworkDown(key), "Download", ValueStyle.NetworkRate, ColorKeys.NetworkDown);
        Upload = new MetricViewModel(MetricIds.NetworkUp(key), "Upload", ValueStyle.NetworkRate, ColorKeys.NetworkUp);
    }

    public string Key { get; }

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

    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string? LinkSpeed
    {
        get => _linkSpeed;
        set => SetProperty(ref _linkSpeed, value);
    }

    public string? Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public MetricViewModel Download { get; }

    public MetricViewModel Upload { get; }
}

/// <summary>Network page: active connection throughput, session totals and every adapter.</summary>
public sealed class NetworkViewModel : PageViewModel
{
    private int _deviceVersion = -1;
    private string _activeName = "No active connection";
    private string? _activeDetails;

    public NetworkViewModel(PageContext context)
        : base("network", "Network", "Icon.Network", context, MonitoringSection.Network)
    {
        Download = new MetricViewModel(MetricIds.NetworkActiveDown, "Download", ValueStyle.NetworkRate, ColorKeys.NetworkDown);
        Upload = new MetricViewModel(MetricIds.NetworkActiveUp, "Upload", ValueStyle.NetworkRate, ColorKeys.NetworkUp);
        Received = new MetricViewModel(MetricIds.NetworkActiveReceivedSession, "Received this session", ValueStyle.Bytes, ColorKeys.NetworkDown);
        Sent = new MetricViewModel(MetricIds.NetworkActiveSentSession, "Sent this session", ValueStyle.Bytes, ColorKeys.NetworkUp);
        Chart = new ChartViewModel("Traffic", ChartScale.Auto,
            new ChartSeriesViewModel(MetricIds.NetworkActiveDown, "Download", ColorKeys.NetworkDown, ValueStyle.NetworkRate),
            new ChartSeriesViewModel(MetricIds.NetworkActiveUp, "Upload", ColorKeys.NetworkUp, ValueStyle.NetworkRate));
        UseAdapterCommand = new RelayCommand<AdapterViewModel>(adapter =>
            Settings.Update("Monitoring", s => s.Monitoring.PreferredNetworkAdapter = adapter?.Key));
        UseAutomaticCommand = new RelayCommand(() => Settings.Update("Monitoring", s => s.Monitoring.PreferredNetworkAdapter = null));
    }

    public MetricViewModel Download { get; }

    public MetricViewModel Upload { get; }

    public MetricViewModel Received { get; }

    public MetricViewModel Sent { get; }

    public ChartViewModel Chart { get; }

    public ObservableCollection<AdapterViewModel> Adapters { get; } = [];

    public string ActiveName
    {
        get => _activeName;
        private set => SetProperty(ref _activeName, value);
    }

    public string? ActiveDetails
    {
        get => _activeDetails;
        private set => SetProperty(ref _activeDetails, value);
    }

    public bool IsAutomaticSelection => Settings.Current.Monitoring.PreferredNetworkAdapter is null;

    public IRelayCommand<AdapterViewModel> UseAdapterCommand { get; }

    public IRelayCommand UseAutomaticCommand { get; }

    protected override void OnActivated() => _deviceVersion = -1;

    public override void OnSettingsChanged(string section) => OnPropertyChanged(nameof(IsAutomaticSelection));

    protected override void OnUpdate(TelemetryFrame frame)
    {
        var adapters = Monitor.NetworkAdapters();
        if (Devices.Version != _deviceVersion)
        {
            _deviceVersion = Devices.Version;
            RebuildAdapters(adapters);
        }

        string? activeKey = adapters.FirstOrDefault(a => a.IsActive).Key;
        foreach (var adapter in Adapters)
        {
            adapter.IsActive = adapter.Key == activeKey;
            adapter.Download.Update(frame, Formatter, Catalog);
            adapter.Upload.Update(frame, Formatter, Catalog);
        }

        var active = Adapters.FirstOrDefault(a => a.IsActive);
        ActiveName = active?.Name ?? "No active connection";
        ActiveDetails = active is null ? null : string.Join(" · ", new[] { active.Kind, active.LinkSpeed, active.Address }.Where(s => !string.IsNullOrEmpty(s)));
        Subtitle = ActiveDetails is null ? ActiveName : $"{ActiveName} · {ActiveDetails}";

        UpdateMetrics(frame, true, Download, Upload);
        UpdateMetrics(frame, false, Received, Sent);
        Chart.Update(frame, Formatter);
    }

    private void RebuildAdapters(IReadOnlyList<(string Key, string Name, bool IsActive)> adapters)
    {
        var devices = Devices.GetByCategory(MetricCategory.Network);
        var ordered = devices
            .Where(d => d.Id.StartsWith("net/", StringComparison.Ordinal))
            .OrderBy(d => d.Order)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!Adapters.Select(a => a.Key).SequenceEqual(ordered.Select(d => d.Id[4..])))
        {
            Adapters.Clear();
            foreach (var d in ordered)
            {
                Adapters.Add(new AdapterViewModel(d.Id[4..], d.Name));
            }
        }

        foreach (var adapter in Adapters)
        {
            var d = ordered.First(x => x.Id[4..] == adapter.Key);
            adapter.Name = d.Name;
            adapter.Kind = d.Kind;
            adapter.Status = d.GetProperty("Status");
            adapter.LinkSpeed = d.GetProperty("Link speed");
            adapter.Address = d.GetProperty("IPv4 address");
        }
    }
}
