using System.Globalization;
using Vigil.Core.Diagnostics;
using Vigil.Core.Formatting;
using Vigil.Core.Telemetry;
using Vigil.Hardware.Native;

namespace Vigil.Hardware.Providers;

/// <summary>
/// Physical disks (model, bus, size, throughput, active time, temperature where Windows exposes it)
/// and volumes (capacity/used/free). Throughput and activity come from the PhysicalDisk performance
/// counters; identification and temperature from storage IOCTLs. No administrator rights required.
/// </summary>
public sealed class StorageProvider : ITelemetryProvider
{
    public const int Priority = 50;
    private const string LogCategory = "Storage";
    private static readonly TimeSpan VolumeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TemperatureInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(30);

    private readonly HardwareServices _services;
    private readonly List<KeyValuePair<string, double>> _scratch = [];
    private readonly Dictionary<int, DiskHandles> _disks = [];
    private readonly Dictionary<string, VolumeHandles> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private IProviderContext? _context;
    private PdhQuery? _query;
    private PdhCounter? _read;
    private PdhCounter? _write;
    private PdhCounter? _idle;
    private MetricHandle? _totalRead;
    private MetricHandle? _totalWrite;
    private DateTime _lastVolumeUpdate = DateTime.MinValue;
    private DateTime _lastTemperatureUpdate = DateTime.MinValue;
    private DateTime _lastRescan = DateTime.MinValue;
    private bool _elevated;

    public StorageProvider(HardwareServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string Name => "Windows storage";

    public MonitoringSection Section => MonitoringSection.Storage;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _context = context;
        _elevated = context.Capabilities.IsElevated;

        _totalRead = context.Register(new MetricDescriptor(MetricIds.DiskTotalRead, "Total disk read", MetricCategory.Storage, MetricKind.Throughput) { Group = "Throughput" }, Provenance.Measured, Priority);
        _totalWrite = context.Register(new MetricDescriptor(MetricIds.DiskTotalWrite, "Total disk write", MetricCategory.Storage, MetricKind.Throughput) { Group = "Throughput" }, Provenance.Measured, Priority);

        DiscoverDisks();

        try
        {
            _query = PdhQuery.Open();
            _read = _query.TryAddCounter(@"\PhysicalDisk(*)\Disk Read Bytes/sec", out _);
            _write = _query.TryAddCounter(@"\PhysicalDisk(*)\Disk Write Bytes/sec", out _);
            _idle = _query.TryAddCounter(@"\PhysicalDisk(*)\% Idle Time", out _);
            _query.Collect();
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, "PhysicalDisk performance counters are unavailable; throughput will not be shown.", ex);
            _query?.Dispose();
            _query = null;
        }

        DiscoverVolumes();
        _lastRescan = DateTime.UtcNow;

        string summary = _disks.Count == 0 ? "No physical disks could be queried." : $"{_disks.Count} disk(s), {_volumes.Count} volume(s)";
        return _query is null ? ProviderInitResult.Degraded(summary + " Throughput counters unavailable.") : ProviderInitResult.Running(summary);
    }

    private void DiscoverDisks()
    {
        var context = _context!;
        var entries = new List<DiskDirectory.DiskEntry>();
        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        int misses = 0;
        for (int number = 0; number < 32 && misses < 8; number++)
        {
            PhysicalDiskInfo? info;
            try
            {
                info = StorageInterop.TryQueryDisk(number);
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory, $"Querying PhysicalDrive{number} failed.", ex);
                info = null;
            }

            if (info is null)
            {
                misses++;
                continue;
            }

            misses = 0;
            entries.Add(new DiskDirectory.DiskEntry(info.Number, info.Model, null, info.BusType, info.IsSsd, info.SizeBytes));
            if (_disks.ContainsKey(number))
            {
                continue;
            }

            string deviceId = MetricIds.DiskDeviceId(number);
            MetricHandle Reg(string id, string name, MetricKind kind, string group) =>
                context.Register(new MetricDescriptor(id, name, MetricCategory.Storage, kind)
                {
                    DeviceId = deviceId,
                    DeviceName = info.Model,
                    Group = group,
                }, Provenance.Measured, Priority);

            var handles = new DiskHandles(number)
            {
                Read = Reg(MetricIds.DiskRead(number), "Read speed", MetricKind.Throughput, "Throughput"),
                Write = Reg(MetricIds.DiskWrite(number), "Write speed", MetricKind.Throughput, "Throughput"),
                Activity = Reg(MetricIds.DiskActivity(number), "Active time", MetricKind.Load, "Activity"),
            };

            double? temperature = SafeTemperature(number);
            if (temperature is not null)
            {
                handles.Temperature = Reg(MetricIds.DiskTemperature(number), "Temperature", MetricKind.Temperature, "Temperatures");
                handles.LastTemperature = temperature;
            }
            else
            {
                context.SetUnavailableReason(MetricIds.DiskTemperature(number), _elevated
                    ? "This drive does not report its temperature to Windows."
                    : "Not exposed to standard users by this drive; may be available when running as administrator.");
            }

            _disks[number] = handles;

            string kind = info.BusType == "NVMe" ? "NVMe SSD" : info.IsSsd == true ? $"{info.BusType} SSD" : info.IsSsd == false ? $"{info.BusType} HDD" : info.BusType;
            var props = new List<KeyValuePair<string, string>>
            {
                new("Capacity", info.SizeBytes > 0 ? f.Bytes(info.SizeBytes, 0).ToString() : "Unknown"),
                new("Interface", info.BusType),
                new("Type", info.IsSsd switch { true => "Solid state", false => "Rotational", _ => "Unknown" }),
                new("Disk number", number.ToString(CultureInfo.InvariantCulture)),
            };
            if (info.Removable)
            {
                props.Add(new("Removable", "Yes"));
            }

            context.UpsertDevice(new DeviceInfo(deviceId, MetricCategory.Storage, info.Model)
            {
                Kind = kind,
                Order = number,
                Source = "Windows storage API",
                Properties = props,
            });
        }

        _services.Disks.Set(entries);
    }

    private double? SafeTemperature(int number)
    {
        try
        {
            return StorageInterop.TryReadTemperature(number, _elevated);
        }
        catch
        {
            return null;
        }
    }

    private void DiscoverVolumes()
    {
        var context = _context!;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, "Enumerating drives failed.", ex);
            return;
        }

        foreach (var drive in drives)
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            string letter = drive.Name.TrimEnd('\\', ':').ToUpperInvariant();
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                seen.Add(letter);
                if (_volumes.ContainsKey(letter))
                {
                    continue;
                }

                int? diskNumber = StorageInterop.TryGetVolumeDiskNumber(letter);
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? (drive.DriveType == DriveType.Removable ? "Removable" : "Local Disk") : drive.VolumeLabel;
                string displayName = $"{label} ({letter}:)";
                string deviceId = $"vol/{letter}";
                MetricHandle Reg(string id, string name, MetricKind kind, bool track) =>
                    context.Register(new MetricDescriptor(id, name, MetricCategory.Storage, kind)
                    {
                        DeviceId = deviceId,
                        DeviceName = displayName,
                        Group = "Volumes",
                        TrackHistory = track,
                    }, Provenance.Measured, Priority);

                _volumes[letter] = new VolumeHandles(drive.Name)
                {
                    Used = Reg(MetricIds.VolumeUsed(letter), $"{letter}: used", MetricKind.Data, false),
                    Free = Reg(MetricIds.VolumeFree(letter), $"{letter}: free", MetricKind.Data, false),
                    Total = Reg(MetricIds.VolumeTotal(letter), $"{letter}: capacity", MetricKind.Data, false),
                    Load = Reg(MetricIds.VolumeLoad(letter), $"{letter}: used %", MetricKind.Load, false),
                };

                var props = new List<KeyValuePair<string, string>>
                {
                    new("Drive", $"{letter}:"),
                    new("File system", drive.DriveFormat),
                };
                if (diskNumber is int n)
                {
                    props.Add(new("Disk number", n.ToString(CultureInfo.InvariantCulture)));
                }

                context.UpsertDevice(new DeviceInfo(deviceId, MetricCategory.Storage, displayName)
                {
                    Kind = "Volume",
                    Order = 100 + letter[0],
                    Source = "Windows",
                    Properties = props,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Drive vanished or is locked (e.g. BitLocker): skip quietly.
            }
        }

        foreach (string gone in _volumes.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            _volumes.Remove(gone);
            context.RemoveDevice($"vol/{gone}");
        }
    }

    public void Poll(IMetricWriter writer)
    {
        var now = DateTime.UtcNow;
        if (now - _lastRescan >= RescanInterval)
        {
            _lastRescan = now;
            DiscoverDisks();
            DiscoverVolumes();
        }

        if (_query is not null && _query.Collect() == PdhNative.ErrorSuccess)
        {
            ReadCounter(_read, writer, (d, v) => d.Read, _totalRead);
            ReadCounter(_write, writer, (d, v) => d.Write, _totalWrite);
            if (_idle is not null && _idle.TryGetArray(_scratch, noCap100: false))
            {
                foreach (var (instance, idle) in _scratch)
                {
                    if (TryParseDiskNumber(instance, out int number) && _disks.TryGetValue(number, out var disk))
                    {
                        writer.Write(disk.Activity!, Math.Clamp(100 - idle, 0, 100));
                    }
                }
            }
        }

        if (now - _lastTemperatureUpdate >= TemperatureInterval)
        {
            _lastTemperatureUpdate = now;
            foreach (var disk in _disks.Values.Where(d => d.Temperature is not null))
            {
                disk.LastTemperature = SafeTemperature(disk.Number);
            }
        }

        foreach (var disk in _disks.Values)
        {
            if (disk.Temperature is not null && disk.LastTemperature is double t)
            {
                writer.Write(disk.Temperature, t);
            }
        }

        if (now - _lastVolumeUpdate >= VolumeInterval)
        {
            _lastVolumeUpdate = now;
            foreach (var volume in _volumes.Values)
            {
                try
                {
                    var drive = new DriveInfo(volume.Root);
                    if (!drive.IsReady)
                    {
                        volume.Valid = false;
                        continue;
                    }

                    volume.TotalBytes = drive.TotalSize;
                    volume.FreeBytes = drive.TotalFreeSpace;
                    volume.Valid = volume.TotalBytes > 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    volume.Valid = false;
                }
            }
        }

        foreach (var volume in _volumes.Values.Where(v => v.Valid))
        {
            double used = volume.TotalBytes - volume.FreeBytes;
            writer.Write(volume.Total!, volume.TotalBytes);
            writer.Write(volume.Free!, volume.FreeBytes);
            writer.Write(volume.Used!, used);
            writer.Write(volume.Load!, used * 100.0 / volume.TotalBytes);
        }
    }

    private void ReadCounter(PdhCounter? counter, IMetricWriter writer, Func<DiskHandles, double, MetricHandle?> select, MetricHandle? total)
    {
        if (counter is null || !counter.TryGetArray(_scratch))
        {
            return;
        }

        foreach (var (instance, value) in _scratch)
        {
            if (instance == "_Total")
            {
                if (total is not null)
                {
                    writer.Write(total, Math.Max(0, value));
                }

                continue;
            }

            if (TryParseDiskNumber(instance, out int number) && _disks.TryGetValue(number, out var disk) && select(disk, value) is { } handle)
            {
                writer.Write(handle, Math.Max(0, value));
            }
        }
    }

    /// <summary>PhysicalDisk instances are named "&lt;number&gt; &lt;letters&gt;", e.g. "0 C: D:".</summary>
    internal static bool TryParseDiskNumber(string instance, out int number)
    {
        int space = instance.IndexOf(' ');
        string head = space < 0 ? instance : instance[..space];
        return int.TryParse(head, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    public void Shutdown()
    {
        _query?.Dispose();
        _query = null;
        _read = _write = _idle = null;
        _disks.Clear();
        _volumes.Clear();
    }

    public void Dispose() => Shutdown();

    private sealed class DiskHandles
    {
        public DiskHandles(int number)
        {
            Number = number;
        }

        public int Number { get; }

        public MetricHandle? Read { get; init; }

        public MetricHandle? Write { get; init; }

        public MetricHandle? Activity { get; init; }

        public MetricHandle? Temperature { get; set; }

        public double? LastTemperature { get; set; }
    }

    private sealed class VolumeHandles
    {
        public VolumeHandles(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public MetricHandle? Used { get; init; }

        public MetricHandle? Free { get; init; }

        public MetricHandle? Total { get; init; }

        public MetricHandle? Load { get; init; }

        public double TotalBytes { get; set; }

        public double FreeBytes { get; set; }

        public bool Valid { get; set; }
    }
}
