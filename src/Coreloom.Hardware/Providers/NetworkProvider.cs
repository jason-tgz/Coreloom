using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Telemetry;

namespace Coreloom.Hardware.Providers;

/// <summary>
/// Network throughput per adapter from interface byte counters, plus the "active" adapter (the one carrying
/// the default route) for the dashboard. Adapters are re-enumerated on address/availability changes so
/// plugging in Ethernet, switching Wi-Fi or connecting a VPN is picked up automatically.
/// </summary>
public sealed class NetworkProvider : ITelemetryProvider
{
    public const int Priority = 50;
    private const string LogCategory = "Network";
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(15);
    private static readonly string[] VirtualHints = ["virtual", "hyper-v", "vmware", "virtualbox", "loopback", "tap-", "wan miniport", "teredo", "isatap", "bluetooth", "pseudo"];

    private readonly Func<string?> _preferredAdapter;
    private readonly Dictionary<string, AdapterState> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private IProviderContext? _context;
    private MetricHandle? _activeDown;
    private MetricHandle? _activeUp;
    private MetricHandle? _activeRx;
    private MetricHandle? _activeTx;
    private volatile bool _rescanRequested = true;
    private DateTime _lastRescan = DateTime.MinValue;
    private string? _activeKey;
    private IReadOnlyList<(string Key, string Name, bool IsActive)> _snapshot = [];

    /// <param name="preferredAdapter">Returns the user's preferred adapter id, or null for automatic.</param>
    public NetworkProvider(Func<string?>? preferredAdapter = null)
    {
        _preferredAdapter = preferredAdapter ?? (() => null);
    }

    public string Name => "Windows network";

    public MonitoringSection Section => MonitoringSection.Network;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    /// <summary>Key of the adapter currently shown as "active" (null if none).</summary>
    public string? ActiveAdapterKey => _activeKey;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _context = context;
        MetricHandle Reg(string id, string name, MetricKind kind, bool track = true) =>
            context.Register(new MetricDescriptor(id, name, MetricCategory.Network, kind)
            {
                Group = "Active connection",
                TrackHistory = track,
            }, Provenance.Measured, Priority);

        _activeDown = Reg(MetricIds.NetworkActiveDown, "Download", MetricKind.Throughput);
        _activeUp = Reg(MetricIds.NetworkActiveUp, "Upload", MetricKind.Throughput);
        _activeRx = Reg(MetricIds.NetworkActiveReceivedSession, "Received this session", MetricKind.Data, track: false);
        _activeTx = Reg(MetricIds.NetworkActiveSentSession, "Sent this session", MetricKind.Data, track: false);

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        Rescan();
        return ProviderInitResult.Running($"{_adapters.Count} adapter(s)");
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _rescanRequested = true;

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => _rescanRequested = true;

    private void Rescan()
    {
        var context = _context!;
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn(LogCategory, "Enumerating network adapters failed.", ex);
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nic in interfaces)
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            string key = AdapterKey(nic.Id);
            seen.Add(key);
            if (!_adapters.TryGetValue(key, out var state))
            {
                string name = string.IsNullOrWhiteSpace(nic.Name) ? nic.Description : nic.Name;
                string deviceId = MetricIds.NetworkDeviceId(key);
                MetricHandle Reg(string id, string label, MetricKind kind, bool track) =>
                    context.Register(new MetricDescriptor(id, label, MetricCategory.Network, kind)
                    {
                        DeviceId = deviceId,
                        DeviceName = name,
                        Group = "Adapters",
                        TrackHistory = track,
                    }, Provenance.Measured, Priority);

                state = new AdapterState(nic.Id, key)
                {
                    Down = Reg(MetricIds.NetworkDown(key), $"{name} download", MetricKind.Throughput, true),
                    Up = Reg(MetricIds.NetworkUp(key), $"{name} upload", MetricKind.Throughput, true),
                    Received = Reg(MetricIds.NetworkReceivedSession(key), $"{name} received", MetricKind.Data, false),
                    Sent = Reg(MetricIds.NetworkSentSession(key), $"{name} sent", MetricKind.Data, false),
                };
                _adapters[key] = state;
            }

            state.Interface = nic;
            state.IsVirtual = IsVirtual(nic);
            state.IsUp = nic.OperationalStatus == OperationalStatus.Up;
            state.HasGateway = HasDefaultGateway(nic);
            context.UpsertDevice(BuildDeviceInfo(nic, key, state));
        }

        foreach (string gone in _adapters.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            _adapters.Remove(gone);
            context.RemoveDevice(MetricIds.NetworkDeviceId(gone));
        }

        _lastRescan = DateTime.UtcNow;
        _rescanRequested = false;
    }

    private static DeviceInfo BuildDeviceInfo(NetworkInterface nic, string key, AdapterState state)
    {
        var props = new List<KeyValuePair<string, string>>
        {
            new("Description", nic.Description),
            new("Type", TypeName(nic.NetworkInterfaceType)),
            new("Status", nic.OperationalStatus.ToString()),
        };

        if (nic.OperationalStatus == OperationalStatus.Up && nic.Speed > 0)
        {
            props.Add(new("Link speed", FormatLinkSpeed(nic.Speed)));
        }

        try
        {
            var ipv4 = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 is not null)
            {
                props.Add(new("IPv4 address", ipv4.Address.ToString()));
            }
        }
        catch (NetworkInformationException)
        {
            // ignore
        }

        return new DeviceInfo(MetricIds.NetworkDeviceId(key), MetricCategory.Network, string.IsNullOrWhiteSpace(nic.Name) ? nic.Description : nic.Name)
        {
            Kind = TypeName(nic.NetworkInterfaceType) + (state.IsVirtual ? " (virtual)" : string.Empty),
            Order = state.IsVirtual ? 100 : state.IsUp ? 0 : 50,
            Source = "Windows",
            Properties = props,
        };
    }

    public void Poll(IMetricWriter writer)
    {
        if (_rescanRequested || DateTime.UtcNow - _lastRescan >= RescanInterval)
        {
            Rescan();
        }

        long now = Environment.TickCount64;
        foreach (var state in _adapters.Values)
        {
            if (state.Interface is null)
            {
                continue;
            }

            long rx, tx;
            try
            {
                var stats = state.Interface.GetIPStatistics();
                rx = stats.BytesReceived;
                tx = stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            if (state.LastTimestamp is long last && now > last)
            {
                double seconds = (now - last) / 1000.0;
                long dRx = rx - state.LastReceived;
                long dTx = tx - state.LastSent;
                if (dRx < 0 || dTx < 0)
                {
                    // Counter reset (adapter re-initialized): skip this interval.
                    dRx = Math.Max(0, dRx);
                    dTx = Math.Max(0, dTx);
                    state.LastTimestamp = now;
                    state.LastReceived = rx;
                    state.LastSent = tx;
                    continue;
                }

                state.DownRate = dRx / seconds;
                state.UpRate = dTx / seconds;
                state.SessionReceived += dRx;
                state.SessionSent += dTx;
                state.RecentBytes = (state.RecentBytes * 0.8) + dRx + dTx;
                writer.Write(state.Down!, state.DownRate);
                writer.Write(state.Up!, state.UpRate);
                writer.Write(state.Received!, state.SessionReceived);
                writer.Write(state.Sent!, state.SessionSent);
            }

            state.LastTimestamp = now;
            state.LastReceived = rx;
            state.LastSent = tx;
        }

        var active = SelectActive();
        _activeKey = active?.Key;
        PublishSnapshot();
        if (active is not null && active.LastTimestamp is not null)
        {
            writer.Write(_activeDown!, active.DownRate);
            writer.Write(_activeUp!, active.UpRate);
            writer.Write(_activeRx!, active.SessionReceived);
            writer.Write(_activeTx!, active.SessionSent);
        }
    }

    private AdapterState? SelectActive()
    {
        string? preferred = _preferredAdapter();
        if (!string.IsNullOrEmpty(preferred))
        {
            var match = _adapters.Values.FirstOrDefault(a => string.Equals(a.Id, preferred, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Key, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return _adapters.Values
            .Where(a => a.IsUp)
            .OrderByDescending(a => a.HasGateway)
            .ThenBy(a => a.IsVirtual)
            .ThenByDescending(a => a.RecentBytes)
            .FirstOrDefault();
    }

    /// <summary>Adapters seen in the last poll. Safe to call from any thread (returns an immutable snapshot).</summary>
    public IReadOnlyList<(string Key, string Name, bool IsActive)> GetAdapters() => Volatile.Read(ref _snapshot);

    private void PublishSnapshot()
    {
        var current = Volatile.Read(ref _snapshot);
        var next = _adapters.Values.Select(a => (a.Key, a.Interface?.Name ?? a.Key, a.Key == _activeKey)).ToArray();
        if (!current.SequenceEqual(next))
        {
            Volatile.Write(ref _snapshot, next);
        }
    }

    internal static string AdapterKey(string id) =>
        new string(id.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() is { Length: > 0 } key ? key : "adapter";

    private static bool IsVirtual(NetworkInterface nic)
    {
        string text = (nic.Description + " " + nic.Name).ToLowerInvariant();
        return VirtualHints.Any(text.Contains);
    }

    private static bool HasDefaultGateway(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().GatewayAddresses.Any(g => g.Address is not null && !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    private static string TypeName(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "Ethernet",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ppp => "PPP",
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Mobile broadband",
        _ => type.ToString(),
    };

    internal static string FormatLinkSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond >= 1_000_000_000)
        {
            return (bitsPerSecond / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + " Gbps";
        }

        return (bitsPerSecond / 1e6).ToString("0", CultureInfo.InvariantCulture) + " Mbps";
    }

    public void Shutdown()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _adapters.Clear();
        _activeKey = null;
        Volatile.Write(ref _snapshot, Array.Empty<(string Key, string Name, bool IsActive)>());
    }

    public void Dispose() => Shutdown();

    private sealed class AdapterState
    {
        public AdapterState(string id, string key)
        {
            Id = id;
            Key = key;
        }

        public string Id { get; }

        public string Key { get; }

        public NetworkInterface? Interface { get; set; }

        public bool IsVirtual { get; set; }

        public bool IsUp { get; set; }

        public bool HasGateway { get; set; }

        public MetricHandle? Down { get; init; }

        public MetricHandle? Up { get; init; }

        public MetricHandle? Received { get; init; }

        public MetricHandle? Sent { get; init; }

        public long? LastTimestamp { get; set; }

        public long LastReceived { get; set; }

        public long LastSent { get; set; }

        public double DownRate { get; set; }

        public double UpRate { get; set; }

        public double SessionReceived { get; set; }

        public double SessionSent { get; set; }

        public double RecentBytes { get; set; }
    }
}
