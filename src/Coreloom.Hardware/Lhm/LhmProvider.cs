using System.Globalization;
using LibreHardwareMonitor.Hardware;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Telemetry;

namespace Coreloom.Hardware.Lhm;

/// <summary>Options for the LibreHardwareMonitor adapter.</summary>
public sealed record LhmOptions(bool ScanUsbControllers = false);

/// <summary>
/// Adapter for LibreHardwareMonitorLib (MPL-2.0). Supplies sensors that Windows APIs and NVML do not:
/// CPU temperatures (Tctl/Tdie, CCDs), package power and per-core/effective clocks; NVIDIA hot spot and
/// memory-junction temperatures, fan RPM and core voltage (via NvAPI); motherboard Super I/O fans and
/// temperatures; drive temperatures and NVMe health; DIMM sensors.
/// Low-level sensors need administrator rights and the PawnIO driver — Coreloom never installs drivers;
/// without them those sensors are simply reported as unavailable.
/// Every LHM sensor is also published verbatim as a raw metric ("lhm/...") for the sensor tables.
/// </summary>
public sealed class LhmProvider : ITelemetryProvider
{
    private const string LogCategory = "LibreHardwareMonitor";
    private static readonly TimeSpan StorageUpdateInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MemoryUpdateInterval = TimeSpan.FromSeconds(5);

    private readonly HardwareServices _services;
    private readonly LhmOptions _options;
    private readonly Dictionary<ISensor, Binding> _bindings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IHardware, HardwareInfo> _hardware = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<int> _claimedDisks = [];
    private Computer? _computer;
    private IProviderContext? _context;
    private DateTime _lastStorageUpdate = DateTime.MinValue;
    private DateTime _lastMemoryUpdate = DateTime.MinValue;
    private MetricHandle? _derivedClockAverage;
    private MetricHandle? _derivedEffectiveAverage;

    public LhmProvider(HardwareServices services, LhmOptions? options = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? new LhmOptions();
    }

    public string Name => "LibreHardwareMonitor";

    /// <summary>Covers several sections; individual metrics are filtered by category.</summary>
    public MonitoringSection Section => MonitoringSection.System;

    /// <summary>Runs after NVML and the storage provider so GPU slots and disk numbers can be matched.</summary>
    public int InitStage => 1;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public int RawSensorCount => _bindings.Count;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _context = context;
        var caps = context.Capabilities;

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = _options.ScanUsbControllers,
            IsPsuEnabled = _options.ScanUsbControllers,
            IsNetworkEnabled = false,
            IsBatteryEnabled = false,
            IsPowerMonitorEnabled = false,
        };

        _computer.Open();

        // First update activates lazily created sensors (e.g. CCD temperatures).
        foreach (var hardware in _computer.Hardware)
        {
            UpdateTree(hardware);
        }

        foreach (var hardware in _computer.Hardware)
        {
            Discover(hardware);
        }

        _derivedClockAverage = context.Register(new MetricDescriptor(MetricIds.CpuClockAverage, "Average core clock", MetricCategory.Cpu, MetricKind.Clock)
        {
            DeviceId = MetricIds.CpuDeviceId,
            Group = "Clocks",
        }, Provenance.Derived, 5, "Mean of the per-core clocks reported by LibreHardwareMonitor");
        _derivedEffectiveAverage = context.Register(new MetricDescriptor(MetricIds.CpuClockEffectiveAverage, "Average effective clock", MetricCategory.Cpu, MetricKind.Clock)
        {
            DeviceId = MetricIds.CpuDeviceId,
            Group = "Clocks",
        }, Provenance.Derived, 5, "Mean of the per-core effective clocks reported by LibreHardwareMonitor");

        bool hasCpuTemperature = _bindings.Values.Any(b => b.Canonical?.Descriptor.Id == MetricIds.CpuTemperature);
        if (!hasCpuTemperature)
        {
            string reason = !caps.IsElevated
                ? "Requires running Coreloom as administrator (LibreHardwareMonitor needs ring-0 access through PawnIO)."
                : !caps.PawnIoInstalled
                    ? "Requires the PawnIO driver, which is not installed. Coreloom does not install drivers; see Settings › Diagnostics."
                    : "LibreHardwareMonitor did not report a temperature for this processor.";
            context.SetUnavailableReason(MetricIds.CpuTemperature, reason);
            context.SetUnavailableReason(MetricIds.CpuPower, reason);
            context.SetUnavailableReason(MetricIds.CpuClockEffectiveAverage, reason);
        }
        else
        {
            context.SetUnavailableReason(MetricIds.CpuTemperature, null);
            context.SetUnavailableReason(MetricIds.CpuPower, null);
            context.SetUnavailableReason(MetricIds.CpuClockEffectiveAverage, null);
        }

        int hardwareCount = _hardware.Count;
        string summary = $"{hardwareCount} device(s), {_bindings.Count} sensors";
        if (!caps.LowLevelSensorsAccessible)
        {
            string missing = !caps.IsElevated ? "not running as administrator" : "PawnIO driver not installed";
            return ProviderInitResult.Degraded($"{summary}. Limited mode ({missing}): CPU temperature/power and motherboard sensors unavailable.");
        }

        return ProviderInitResult.Running(summary);
    }

    private void UpdateTree(IHardware hardware)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception ex)
        {
            Log.WarnThrottled($"lhm-update:{hardware.Identifier}", TimeSpan.FromMinutes(5), LogCategory, $"Updating {hardware.Name} failed.", ex);
        }

        foreach (var sub in hardware.SubHardware)
        {
            UpdateTree(sub);
        }
    }

    private void Discover(IHardware hardware)
    {
        var info = GetInfo(hardware);
        foreach (var sensor in hardware.Sensors)
        {
            if (!_bindings.ContainsKey(sensor))
            {
                _bindings[sensor] = CreateBinding(sensor, info);
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            Discover(sub);
        }
    }

    private HardwareInfo GetInfo(IHardware hardware)
    {
        if (_hardware.TryGetValue(hardware, out var existing))
        {
            return existing;
        }

        var kind = ToKind(hardware.HardwareType);
        var info = new HardwareInfo(kind, hardware.Name);
        var context = _context!;

        if (kind is SensorHardwareKind.GpuNvidia or SensorHardwareKind.GpuAmd or SensorHardwareKind.GpuIntel)
        {
            string vendor = kind switch
            {
                SensorHardwareKind.GpuNvidia => "NVIDIA",
                SensorHardwareKind.GpuAmd => "AMD",
                _ => "Intel",
            };
            bool integrated = GpuDirectory.LooksIntegrated(vendor, hardware.Name);
            info.GpuSlot = _services.Gpus.Register($"lhm:{hardware.Identifier}", vendor, hardware.Name, integrated);
            context.UpsertDevice(new DeviceInfo(MetricIds.GpuDeviceId(info.GpuSlot.Value), MetricCategory.Gpu, hardware.Name)
            {
                Vendor = vendor,
                Kind = integrated ? "Integrated GPU" : "Discrete GPU",
                IsIntegrated = integrated,
                Order = info.GpuSlot.Value,
                Source = "LibreHardwareMonitor",
            });
        }
        else if (kind == SensorHardwareKind.Storage)
        {
            info.DiskNumber = _services.Disks.MatchByName(hardware.Name, _claimedDisks);
            if (info.DiskNumber is int disk)
            {
                _claimedDisks.Add(disk);
            }
        }
        else if (kind == SensorHardwareKind.Motherboard)
        {
            context.UpsertDevice(new DeviceInfo(MetricIds.BoardDeviceId, MetricCategory.Motherboard, hardware.Name) { Kind = "Motherboard", Source = "LibreHardwareMonitor" });
        }

        _hardware[hardware] = info;
        return info;
    }

    private Binding CreateBinding(ISensor sensor, HardwareInfo hardware)
    {
        var context = _context!;
        var valueKind = Enum.TryParse(sensor.SensorType.ToString(), out SensorValueKind parsed) ? parsed : SensorValueKind.Unknown;
        var category = SensorMapping.CategoryOf(hardware.Kind);
        string rawId = "lhm" + sensor.Identifier.ToString();
        string deviceName = hardware.Kind == SensorHardwareKind.SuperIO && sensor.Hardware.Parent is { } parent
            ? $"{parent.Name} ({hardware.Name})"
            : hardware.Name;

        var raw = context.Register(new MetricDescriptor(rawId, sensor.Name, category, SensorMapping.ToKind(valueKind))
        {
            DeviceName = deviceName,
            DeviceId = hardware.GpuSlot is int slot ? MetricIds.GpuDeviceId(slot) : hardware.DiskNumber is int disk ? MetricIds.DiskDeviceId(disk) : null,
            Group = SensorMapping.GroupName(valueKind),
            IsRaw = true,
            TrackHistory = false,
            SortOrder = sensor.Index,
        }, Provenance.Measured, 0);

        CanonicalMapping? mapping = hardware.Kind switch
        {
            SensorHardwareKind.Cpu => SensorMapping.MapCpu(sensor.Name, valueKind),
            SensorHardwareKind.GpuNvidia or SensorHardwareKind.GpuAmd or SensorHardwareKind.GpuIntel when hardware.GpuSlot is int gpuSlot => SensorMapping.MapGpu(gpuSlot, sensor.Name, valueKind),
            SensorHardwareKind.Storage when hardware.DiskNumber is int diskNumber => MapStorageOnce(hardware, diskNumber, sensor.Name, valueKind),
            SensorHardwareKind.SuperIO or SensorHardwareKind.Cooler or SensorHardwareKind.EmbeddedController => SensorMapping.MapBoard(sensor.Name, valueKind),
            _ => null,
        };

        MetricHandle? canonical = null;
        if (mapping is not null)
        {
            string? canonicalDevice = hardware.Kind switch
            {
                SensorHardwareKind.Cpu => MetricIds.CpuDeviceId,
                SensorHardwareKind.GpuNvidia or SensorHardwareKind.GpuAmd or SensorHardwareKind.GpuIntel => hardware.GpuSlot is int s ? MetricIds.GpuDeviceId(s) : null,
                SensorHardwareKind.Storage => hardware.DiskNumber is int d ? MetricIds.DiskDeviceId(d) : null,
                _ => MetricIds.BoardDeviceId,
            };

            canonical = context.Register(new MetricDescriptor(mapping.Id, mapping.Name, mapping.Category, mapping.Kind)
            {
                DeviceId = canonicalDevice,
                DeviceName = deviceName,
                Group = mapping.Group,
                TrackHistory = mapping.Track,
            }, Provenance.Measured, mapping.Priority, $"LibreHardwareMonitor sensor \"{sensor.Name}\"");
        }

        return new Binding(raw, canonical, SensorMapping.ScaleFor(valueKind), hardware);
    }

    private static CanonicalMapping? MapStorageOnce(HardwareInfo hardware, int diskNumber, string name, SensorValueKind kind)
    {
        var mapping = SensorMapping.MapStorage(diskNumber, name, kind);
        if (mapping is null && kind == SensorValueKind.Temperature && !hardware.HasStorageTemperature)
        {
            // Fall back to the first temperature sensor when no composite reading exists.
            mapping = new CanonicalMapping(MetricIds.DiskTemperature(diskNumber), "Temperature", MetricKind.Temperature, SensorMapping.LhmStoragePriority - 5, "Temperatures", MetricCategory.Storage);
        }

        if (mapping is not null && mapping.Kind == MetricKind.Temperature)
        {
            hardware.HasStorageTemperature = true;
        }

        return mapping;
    }

    public void Poll(IMetricWriter writer)
    {
        var computer = _computer;
        var context = _context;
        if (computer is null || context is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        bool updateStorage = now - _lastStorageUpdate >= StorageUpdateInterval;
        bool updateMemory = now - _lastMemoryUpdate >= MemoryUpdateInterval;
        if (updateStorage) _lastStorageUpdate = now;
        if (updateMemory) _lastMemoryUpdate = now;

        foreach (var hardware in computer.Hardware)
        {
            var kind = ToKind(hardware.HardwareType);
            bool due = kind switch
            {
                SensorHardwareKind.Storage => updateStorage && context.IsSectionEnabled(MonitoringSection.Storage),
                SensorHardwareKind.Memory => updateMemory && context.IsSectionEnabled(MonitoringSection.Memory),
                SensorHardwareKind.GpuNvidia or SensorHardwareKind.GpuAmd or SensorHardwareKind.GpuIntel => context.IsSectionEnabled(MonitoringSection.Gpu),
                SensorHardwareKind.Cpu or SensorHardwareKind.Motherboard => context.IsSectionEnabled(MonitoringSection.Cpu),
                _ => true,
            };

            if (due)
            {
                UpdateTree(hardware);
                Discover(hardware); // picks up sensors that appear after updates
            }
        }

        double clockSum = 0, effectiveSum = 0;
        int clockCount = 0, effectiveCount = 0;
        foreach (var (sensor, binding) in _bindings)
        {
            float? value = sensor.Value;
            if (value is not float v || !float.IsFinite(v))
            {
                continue;
            }

            double scaled = v * binding.Scale;
            writer.Write(binding.Raw, scaled);
            if (binding.Canonical is { } canonical)
            {
                writer.Write(canonical, scaled);
                string id = canonical.Descriptor.Id;
                if (binding.Hardware.Kind == SensorHardwareKind.Cpu && id.StartsWith("cpu/core/", StringComparison.Ordinal))
                {
                    if (id.EndsWith("/clock", StringComparison.Ordinal) && scaled > 0)
                    {
                        clockSum += scaled;
                        clockCount++;
                    }
                    else if (id.EndsWith("/clock-eff", StringComparison.Ordinal) && scaled >= 0)
                    {
                        effectiveSum += scaled;
                        effectiveCount++;
                    }
                }
            }
        }

        // Lower-priority derived averages for CPUs where LHM has per-core clocks but no average sensor.
        if (clockCount > 0)
        {
            writer.Write(_derivedClockAverage!, clockSum / clockCount);
        }

        if (effectiveCount > 0)
        {
            writer.Write(_derivedEffectiveAverage!, effectiveSum / effectiveCount);
        }
    }

    /// <summary>LibreHardwareMonitor's own text report (for the diagnostics tool).</summary>
    public string? GetReport()
    {
        try
        {
            return _computer?.GetReport();
        }
        catch (Exception ex)
        {
            return "Report unavailable: " + ex.Message;
        }
    }

    private static SensorHardwareKind ToKind(HardwareType type) =>
        Enum.TryParse(type.ToString(), out SensorHardwareKind kind) ? kind : SensorHardwareKind.Other;

    public void Shutdown()
    {
        _bindings.Clear();
        _hardware.Clear();
        _claimedDisks.Clear();
        var computer = _computer;
        _computer = null;
        if (computer is not null)
        {
            try
            {
                computer.Close();
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory, "Closing LibreHardwareMonitor failed.", ex);
            }
        }
    }

    public void Dispose() => Shutdown();

    private sealed class HardwareInfo
    {
        public HardwareInfo(SensorHardwareKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        public SensorHardwareKind Kind { get; }

        public string Name { get; }

        public int? GpuSlot { get; set; }

        public int? DiskNumber { get; set; }

        public bool HasStorageTemperature { get; set; }
    }

    private sealed record Binding(MetricHandle Raw, MetricHandle? Canonical, double Scale, HardwareInfo Hardware);
}
