using System.Globalization;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Native;

namespace Coreloom.Hardware.Providers;

/// <summary>
/// NVIDIA GPUs via NVML (the same library nvidia-smi uses). No administrator rights required.
/// Covers utilization, temperature, clocks, VRAM, power and power limit, fan duty, P-state,
/// encoder/decoder load, PCIe link and clock-limit reasons. Hot spot / memory junction temperatures,
/// fan RPM and core voltage are not exposed by NVML; those come from LibreHardwareMonitor (NvAPI) when available.
/// </summary>
public sealed class NvmlGpuProvider : ITelemetryProvider
{
    public const int Priority = 100;
    private const string Source = "NVML";
    private const string LogCategory = "NVML";

    private readonly HardwareServices _services;
    private readonly List<Device> _devices = [];
    private NvmlLibrary? _nvml;

    public NvmlGpuProvider(HardwareServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string Name => "NVIDIA NVML";

    public MonitoringSection Section => MonitoringSection.Gpu;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _nvml = NvmlLibrary.TryLoad(out string? loadError);
        if (_nvml is null)
        {
            return ProviderInitResult.Unavailable(loadError ?? "NVML not available.");
        }

        var init = _nvml.Initialize();
        if (init != NvmlReturn.Success)
        {
            string message = $"nvmlInit failed: {_nvml.ErrorString(init)} ({init}).";
            _nvml.Dispose();
            _nvml = null;
            if (init is NvmlReturn.DriverNotLoaded or NvmlReturn.LibraryNotFound or NvmlReturn.NoPermission)
            {
                return ProviderInitResult.Unavailable(message);
            }

            throw new InvalidOperationException(message);
        }

        _nvml.GetDriverVersion(out string driver);
        var count = _nvml.GetDeviceCount(out uint deviceCount);
        if (count != NvmlReturn.Success || deviceCount == 0)
        {
            return ProviderInitResult.Unavailable(count == NvmlReturn.Success ? "No NVIDIA GPUs found." : $"nvmlDeviceGetCount failed: {count}");
        }

        for (uint i = 0; i < deviceCount; i++)
        {
            if (_nvml.GetDeviceHandle(i, out IntPtr handle) != NvmlReturn.Success)
            {
                continue;
            }

            _devices.Add(CreateDevice(context, i, handle, driver));
        }

        if (_devices.Count == 0)
        {
            return ProviderInitResult.Unavailable("NVIDIA GPUs were found but could not be opened.");
        }

        string names = string.Join(", ", _devices.Select(d => d.Name));
        return ProviderInitResult.Running($"{names} — driver {driver}");
    }

    private Device CreateDevice(IProviderContext context, uint index, IntPtr handle, string driver)
    {
        var nvml = _nvml!;
        nvml.GetName(handle, out string name);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"NVIDIA GPU {index}";
        }

        nvml.GetPciBusId(handle, out string busId);
        int slot = _services.Gpus.Register($"nvml:{index}", "NVIDIA", name, isIntegrated: false, NormalizeBusId(busId));
        var device = new Device(handle, slot, name);
        string deviceId = MetricIds.GpuDeviceId(slot);

        MetricHandle Reg(string id, string label, MetricKind kind, string group, bool track = true, Provenance provenance = Provenance.Measured, string? note = null) =>
            context.Register(new MetricDescriptor(id, label, MetricCategory.Gpu, kind)
            {
                DeviceId = deviceId,
                DeviceName = name,
                Group = group,
                TrackHistory = track,
            }, provenance, Priority, note);

        // Probe each query once; unsupported ones are never registered or polled.
        if (nvml.GetUtilization(handle, out _) == NvmlReturn.Success)
        {
            device.Load = Reg(MetricIds.GpuLoad(slot), "GPU utilization", MetricKind.Load, "Load");
            device.MemoryControllerLoad = Reg(MetricIds.GpuMemoryControllerLoad(slot), "Memory controller load", MetricKind.Load, "Load");
        }

        if (nvml.GetTemperature(handle, out _) == NvmlReturn.Success)
        {
            device.Temperature = Reg(MetricIds.GpuTemperature(slot), "GPU temperature", MetricKind.Temperature, "Temperatures");
        }

        if (nvml.GetTemperatureThreshold(handle, NvmlTemperatureThreshold.Slowdown, out uint slowdown) == NvmlReturn.Success && slowdown > 0)
        {
            device.SlowdownTemperature = slowdown;
            device.Slowdown = Reg(MetricIds.GpuSlowdownTemperature(slot), "Slowdown temperature (driver)", MetricKind.Temperature, "Limits", track: false);
        }

        nvml.GetTemperatureThreshold(handle, NvmlTemperatureThreshold.Shutdown, out uint shutdown);
        nvml.GetTemperatureThreshold(handle, NvmlTemperatureThreshold.GpuMax, out uint gpuMax);

        if (nvml.GetClock(handle, NvmlClockType.Graphics, out _) == NvmlReturn.Success)
        {
            device.CoreClock = Reg(MetricIds.GpuCoreClock(slot), "Core clock", MetricKind.Clock, "Clocks");
        }

        if (nvml.GetClock(handle, NvmlClockType.Memory, out _) == NvmlReturn.Success)
        {
            device.MemoryClock = Reg(MetricIds.GpuMemoryClock(slot), "Memory clock", MetricKind.Clock, "Clocks");
        }

        if (nvml.GetClock(handle, NvmlClockType.Sm, out _) == NvmlReturn.Success)
        {
            device.ShaderClock = Reg(MetricIds.GpuShaderClock(slot), "SM clock", MetricKind.Clock, "Clocks");
        }

        if (nvml.GetClock(handle, NvmlClockType.Video, out _) == NvmlReturn.Success)
        {
            device.VideoClock = Reg(MetricIds.GpuVideoClock(slot), "Video clock", MetricKind.Clock, "Clocks");
        }

        ulong totalVram = 0;
        if (nvml.GetMemoryInfo(handle, out var memory) == NvmlReturn.Success)
        {
            totalVram = memory.Total;
            device.MemoryUsed = Reg(MetricIds.GpuMemoryUsed(slot), "VRAM used", MetricKind.Data, "Memory");
            device.MemoryTotal = Reg(MetricIds.GpuMemoryTotal(slot), "VRAM total", MetricKind.Data, "Memory", track: false);
            device.MemoryLoad = Reg(MetricIds.GpuMemoryLoad(slot), "VRAM utilization", MetricKind.Load, "Memory", provenance: Provenance.Derived, note: "VRAM used ÷ total");
        }

        if (nvml.GetPowerUsage(handle, out _) == NvmlReturn.Success)
        {
            device.Power = Reg(MetricIds.GpuPower(slot), "Board power draw", MetricKind.Power, "Power");
        }
        else
        {
            context.SetUnavailableReason(MetricIds.GpuPower(slot), "Power readings are not exposed by this GPU/driver.");
        }

        if (nvml.GetEnforcedPowerLimit(handle, out uint limitMw) == NvmlReturn.Success && limitMw > 0)
        {
            device.PowerLimit = Reg(MetricIds.GpuPowerLimit(slot), "Power limit (enforced)", MetricKind.Power, "Power", track: false);
        }

        if (nvml.GetFanCount(handle, out uint fans) != NvmlReturn.Success)
        {
            fans = nvml.GetFanSpeed(handle, 0, out _) == NvmlReturn.Success ? 1u : 0u;
        }

        for (uint f = 0; f < fans; f++)
        {
            if (nvml.GetFanSpeed(handle, f, out _) == NvmlReturn.Success)
            {
                device.Fans.Add((f, Reg(MetricIds.GpuFanPercent(slot, (int)f + 1), fans > 1 ? $"Fan {f + 1} speed" : "Fan speed", MetricKind.Control, "Cooling")));
            }
        }

        if (device.Fans.Count == 0)
        {
            context.SetUnavailableReason(MetricIds.GpuFanPercent(slot, 1), "Fan speed is not reported by this GPU (e.g. passive cooling or unsupported board).");
        }

        if (nvml.GetPerformanceState(handle, out _) == NvmlReturn.Success)
        {
            device.PerformanceState = Reg(MetricIds.GpuPerformanceState(slot), "Performance state", MetricKind.State, "State", track: false);
        }

        if (nvml.GetClockEventReasons(handle, out _) == NvmlReturn.Success)
        {
            device.ClockReasons = Reg(MetricIds.GpuClockEventReasons(slot), "Clock limit reasons", MetricKind.State, "State", track: false);
        }

        if (nvml.GetEncoderUtilization(handle, out _) == NvmlReturn.Success)
        {
            device.EncoderLoad = Reg(MetricIds.GpuEncoderLoad(slot), "Video encoder load", MetricKind.Load, "Load");
        }

        if (nvml.GetDecoderUtilization(handle, out _) == NvmlReturn.Success)
        {
            device.DecoderLoad = Reg(MetricIds.GpuDecoderLoad(slot), "Video decoder load", MetricKind.Load, "Load");
        }

        if (nvml.GetCurrentPcieGeneration(handle, out _) == NvmlReturn.Success)
        {
            device.PcieGeneration = Reg(MetricIds.GpuPcieGeneration(slot), "PCIe link generation", MetricKind.State, "Bus", track: false);
        }

        if (nvml.GetCurrentPcieWidth(handle, out _) == NvmlReturn.Success)
        {
            device.PcieWidth = Reg(MetricIds.GpuPcieWidth(slot), "PCIe link width", MetricKind.State, "Bus", track: false);
        }

        // Hot spot, memory junction, fan RPM and voltage are not part of NVML's public API.
        const string lhmNote = "Not exposed by NVML; provided by LibreHardwareMonitor (NvAPI) when enabled.";
        context.SetUnavailableReason(MetricIds.GpuHotSpotTemperature(slot), lhmNote);
        context.SetUnavailableReason(MetricIds.GpuMemoryJunctionTemperature(slot), lhmNote);
        context.SetUnavailableReason(MetricIds.GpuVoltage(slot), lhmNote);
        context.SetUnavailableReason(MetricIds.GpuFanRpm(slot, 1), lhmNote);

        context.UpsertDevice(BuildDeviceInfo(nvml, handle, deviceId, name, driver, totalVram, slowdown, shutdown, gpuMax, busId, index));
        return device;
    }

    private static DeviceInfo BuildDeviceInfo(NvmlLibrary nvml, IntPtr handle, string deviceId, string name, string driver, ulong totalVram, uint slowdown, uint shutdown, uint gpuMax, string busId, uint index)
    {
        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var props = new List<KeyValuePair<string, string>>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                props.Add(new(key, value));
            }
        }

        Add("Driver", driver);
        if (totalVram > 0)
        {
            Add("VRAM", f.Bytes(totalVram, 0).ToString());
        }

        if (nvml.GetMemoryBusWidth(handle, out uint bus) == NvmlReturn.Success && bus > 0)
        {
            Add("Memory bus", $"{bus}-bit");
        }

        if (nvml.GetGpuCoreCount(handle, out uint cores) == NvmlReturn.Success && cores > 0)
        {
            Add("CUDA cores", cores.ToString("N0", CultureInfo.InvariantCulture));
        }

        if (nvml.GetMaxClock(handle, NvmlClockType.Graphics, out uint maxCore) == NvmlReturn.Success && maxCore > 0)
        {
            Add("Max core clock", $"{maxCore} MHz");
        }

        if (nvml.GetMaxClock(handle, NvmlClockType.Memory, out uint maxMem) == NvmlReturn.Success && maxMem > 0)
        {
            Add("Max memory clock", $"{maxMem} MHz");
        }

        if (nvml.GetDefaultPowerLimit(handle, out uint defaultLimit) == NvmlReturn.Success && defaultLimit > 0)
        {
            Add("Default power limit", $"{defaultLimit / 1000.0:0} W");
        }

        if (nvml.GetPowerLimitConstraints(handle, out uint minLimit, out uint maxLimit) == NvmlReturn.Success && maxLimit > 0)
        {
            Add("Power limit range", $"{minLimit / 1000.0:0}–{maxLimit / 1000.0:0} W");
        }

        if (slowdown > 0) Add("Slowdown temperature", $"{slowdown} °C (driver)");
        if (gpuMax > 0) Add("Max operating temperature", $"{gpuMax} °C (driver)");
        if (shutdown > 0) Add("Shutdown temperature", $"{shutdown} °C (driver)");

        if (nvml.GetMaxPcieGeneration(handle, out uint maxGen) == NvmlReturn.Success && nvml.GetMaxPcieWidth(handle, out uint maxWidth) == NvmlReturn.Success && maxGen > 0)
        {
            Add("PCIe (max)", $"Gen {maxGen} ×{maxWidth}");
        }

        if (nvml.GetVbiosVersion(handle, out string vbios) == NvmlReturn.Success)
        {
            Add("VBIOS", vbios);
        }

        Add("PCI bus", NormalizeBusId(busId));
        Add("NVML index", index.ToString(CultureInfo.InvariantCulture));

        return new DeviceInfo(deviceId, MetricCategory.Gpu, name)
        {
            Vendor = "NVIDIA",
            Kind = "Discrete GPU",
            IsIntegrated = false,
            Order = (int)index,
            Source = Source,
            Properties = props,
        };
    }

    public void Poll(IMetricWriter writer)
    {
        var nvml = _nvml;
        if (nvml is null)
        {
            return;
        }

        foreach (var d in _devices)
        {
            IntPtr h = d.Handle;

            if (d.Load is not null && Check(nvml, nvml.GetUtilization(h, out var util), "utilization", ref d.Load))
            {
                writer.Write(d.Load!, util.Gpu);
                if (d.MemoryControllerLoad is not null)
                {
                    writer.Write(d.MemoryControllerLoad, util.Memory);
                }
            }

            if (d.Temperature is not null && Check(nvml, nvml.GetTemperature(h, out uint temp), "temperature", ref d.Temperature))
            {
                writer.Write(d.Temperature!, temp);
            }

            if (d.Slowdown is not null)
            {
                writer.Write(d.Slowdown, d.SlowdownTemperature);
            }

            if (d.CoreClock is not null && Check(nvml, nvml.GetClock(h, NvmlClockType.Graphics, out uint core), "graphics clock", ref d.CoreClock))
            {
                writer.Write(d.CoreClock!, core);
            }

            if (d.MemoryClock is not null && Check(nvml, nvml.GetClock(h, NvmlClockType.Memory, out uint mem), "memory clock", ref d.MemoryClock))
            {
                writer.Write(d.MemoryClock!, mem);
            }

            if (d.ShaderClock is not null && Check(nvml, nvml.GetClock(h, NvmlClockType.Sm, out uint sm), "SM clock", ref d.ShaderClock))
            {
                writer.Write(d.ShaderClock!, sm);
            }

            if (d.VideoClock is not null && Check(nvml, nvml.GetClock(h, NvmlClockType.Video, out uint video), "video clock", ref d.VideoClock))
            {
                writer.Write(d.VideoClock!, video);
            }

            if (d.MemoryUsed is not null && Check(nvml, nvml.GetMemoryInfo(h, out var memory), "memory info", ref d.MemoryUsed))
            {
                writer.Write(d.MemoryUsed!, memory.Used);
                writer.Write(d.MemoryTotal!, memory.Total);
                if (memory.Total > 0)
                {
                    writer.Write(d.MemoryLoad!, memory.Used * 100.0 / memory.Total);
                }
            }

            if (d.Power is not null && Check(nvml, nvml.GetPowerUsage(h, out uint mw), "power", ref d.Power))
            {
                writer.Write(d.Power!, mw / 1000.0);
            }

            if (d.PowerLimit is not null && Check(nvml, nvml.GetEnforcedPowerLimit(h, out uint limit), "power limit", ref d.PowerLimit))
            {
                writer.Write(d.PowerLimit!, limit / 1000.0);
            }

            for (int i = d.Fans.Count - 1; i >= 0; i--)
            {
                var (fan, handle) = d.Fans[i];
                var r = nvml.GetFanSpeed(h, fan, out uint pct);
                if (r == NvmlReturn.Success)
                {
                    writer.Write(handle, pct);
                }
                else
                {
                    HandleError(nvml, r, "fan speed");
                    if (r == NvmlReturn.NotSupported)
                    {
                        d.Fans.RemoveAt(i);
                    }
                }
            }

            if (d.PerformanceState is not null && Check(nvml, nvml.GetPerformanceState(h, out int pstate), "performance state", ref d.PerformanceState) && pstate is >= 0 and <= 15)
            {
                writer.Write(d.PerformanceState!, pstate);
            }

            if (d.ClockReasons is not null && Check(nvml, nvml.GetClockEventReasons(h, out ulong reasons), "clock reasons", ref d.ClockReasons))
            {
                writer.Write(d.ClockReasons!, reasons);
            }

            if (d.EncoderLoad is not null && Check(nvml, nvml.GetEncoderUtilization(h, out uint enc), "encoder", ref d.EncoderLoad))
            {
                writer.Write(d.EncoderLoad!, enc);
            }

            if (d.DecoderLoad is not null && Check(nvml, nvml.GetDecoderUtilization(h, out uint dec), "decoder", ref d.DecoderLoad))
            {
                writer.Write(d.DecoderLoad!, dec);
            }

            if (d.PcieGeneration is not null && Check(nvml, nvml.GetCurrentPcieGeneration(h, out uint gen), "PCIe generation", ref d.PcieGeneration))
            {
                writer.Write(d.PcieGeneration!, gen);
            }

            if (d.PcieWidth is not null && Check(nvml, nvml.GetCurrentPcieWidth(h, out uint width), "PCIe width", ref d.PcieWidth))
            {
                writer.Write(d.PcieWidth!, width);
            }
        }
    }

    /// <summary>
    /// Returns true on success. Unsupported queries are disabled; fatal codes throw so the engine re-initializes NVML.
    /// </summary>
    private static bool Check(NvmlLibrary nvml, NvmlReturn result, string what, ref MetricHandle? handle)
    {
        if (result == NvmlReturn.Success)
        {
            return true;
        }

        HandleError(nvml, result, what);
        if (result is NvmlReturn.NotSupported or NvmlReturn.FunctionNotFound)
        {
            handle = null;
        }

        return false;
    }

    private static void HandleError(NvmlLibrary nvml, NvmlReturn result, string what)
    {
        if (result is NvmlReturn.Uninitialized or NvmlReturn.DriverNotLoaded or NvmlReturn.GpuIsLost or NvmlReturn.ResetRequired
            or NvmlReturn.LibRmVersionMismatch or NvmlReturn.GpuNotFound)
        {
            throw new NvmlFatalException(what, result, nvml.ErrorString(result));
        }

        if (result is not (NvmlReturn.NotSupported or NvmlReturn.FunctionNotFound))
        {
            Log.WarnThrottled($"nvml:{what}", TimeSpan.FromMinutes(5), LogCategory, $"Reading {what} failed: {nvml.ErrorString(result)} ({result})");
        }
    }

    private static string NormalizeBusId(string busId)
    {
        // NVML returns "00000000:01:00.0"; reduce to the conventional "01:00.0" form.
        string trimmed = busId.Trim();
        int colon = trimmed.IndexOf(':');
        return colon == 8 ? trimmed[(colon + 1)..] : trimmed;
    }

    public void Shutdown()
    {
        _devices.Clear();
        _nvml?.Dispose();
        _nvml = null;
    }

    public void Dispose() => Shutdown();

    private sealed class Device
    {
        public Device(IntPtr handle, int slot, string name)
        {
            Handle = handle;
            Slot = slot;
            Name = name;
        }

        public IntPtr Handle { get; }

        public int Slot { get; }

        public string Name { get; }

        public uint SlowdownTemperature { get; set; }

        public MetricHandle? Load;
        public MetricHandle? MemoryControllerLoad;
        public MetricHandle? Temperature;
        public MetricHandle? Slowdown;
        public MetricHandle? CoreClock;
        public MetricHandle? MemoryClock;
        public MetricHandle? ShaderClock;
        public MetricHandle? VideoClock;
        public MetricHandle? MemoryUsed;
        public MetricHandle? MemoryTotal;
        public MetricHandle? MemoryLoad;
        public MetricHandle? Power;
        public MetricHandle? PowerLimit;
        public MetricHandle? PerformanceState;
        public MetricHandle? ClockReasons;
        public MetricHandle? EncoderLoad;
        public MetricHandle? DecoderLoad;
        public MetricHandle? PcieGeneration;
        public MetricHandle? PcieWidth;

        public List<(uint Fan, MetricHandle Handle)> Fans { get; } = [];
    }
}
