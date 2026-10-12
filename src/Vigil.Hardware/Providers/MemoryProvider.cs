using System.Globalization;
using System.Runtime.InteropServices;
using Vigil.Core.Formatting;
using Vigil.Core.Telemetry;
using Vigil.Hardware.Native;
using Vigil.Hardware.SystemInfo;

namespace Vigil.Hardware.Providers;

/// <summary>Physical memory and commit charge via GlobalMemoryStatusEx / GetPerformanceInfo.</summary>
public sealed class MemoryProvider : ITelemetryProvider
{
    private MetricHandle? _total;
    private MetricHandle? _used;
    private MetricHandle? _available;
    private MetricHandle? _load;
    private MetricHandle? _commitUsed;
    private MetricHandle? _commitLimit;
    private MetricHandle? _cache;

    public string Name => "Windows memory";

    public MonitoringSection Section => MonitoringSection.Memory;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!Win32.GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException($"GlobalMemoryStatusEx failed (error {Marshal.GetLastPInvokeError()}).");
        }

        MetricHandle Reg(string id, string name, MetricKind kind, bool track = true, Provenance provenance = Provenance.Measured, string? note = null) =>
            context.Register(new MetricDescriptor(id, name, MetricCategory.Memory, kind)
            {
                DeviceId = MetricIds.MemoryDeviceId,
                DeviceName = "Memory",
                Group = "Memory",
                TrackHistory = track,
            }, provenance, 50, note);

        _total = Reg(MetricIds.MemoryTotal, "Usable memory", MetricKind.Data, track: false);
        _used = Reg(MetricIds.MemoryUsed, "In use", MetricKind.Data, note: "Total physical memory − available memory");
        _available = Reg(MetricIds.MemoryAvailable, "Available", MetricKind.Data);
        _load = Reg(MetricIds.MemoryLoad, "Memory utilization", MetricKind.Load, provenance: Provenance.Derived, note: "In use ÷ usable memory");
        _commitUsed = Reg(MetricIds.MemoryCommitUsed, "Committed", MetricKind.Data);
        _commitLimit = Reg(MetricIds.MemoryCommitLimit, "Commit limit", MetricKind.Data, track: false);
        _cache = Reg(MetricIds.MemoryCache, "System cache", MetricKind.Data);

        RegisterDevice(context, (long)status.TotalPhys);
        return ProviderInitResult.Running();
    }

    private static void RegisterDevice(IProviderContext context, long usable)
    {
        var f = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var props = new List<KeyValuePair<string, string>>();
        Win32.GetPhysicallyInstalledSystemMemory(out ulong installedKb);
        if (installedKb > 0)
        {
            props.Add(new("Installed", f.Bytes(installedKb * 1024.0, 0).ToString()));
        }

        props.Add(new("Usable", f.Bytes(usable, 1).ToString()));

        var modules = SystemInfoReader.Smbios.MemoryModules;
        if (modules.Count > 0)
        {
            var first = modules[0];
            bool uniform = modules.All(m => m.SizeBytes == first.SizeBytes && m.MemoryType == first.MemoryType);
            int speed = modules.Max(m => m.ConfiguredSpeedMtps > 0 ? m.ConfiguredSpeedMtps : m.SpeedMtps);
            string config = uniform
                ? $"{modules.Count} × {f.Bytes(first.SizeBytes, 0)} {first.MemoryType}"
                : string.Join(", ", modules.Select(m => $"{f.Bytes(m.SizeBytes, 0)} {m.MemoryType}"));
            props.Add(new("Modules", config));
            if (speed > 0)
            {
                props.Add(new("Speed", $"{speed} MT/s"));
            }

            string? manufacturer = modules.Select(m => m.Manufacturer).FirstOrDefault(m => m is not null);
            string? part = modules.Select(m => m.PartNumber).FirstOrDefault(p => p is not null);
            if (manufacturer is not null) props.Add(new("Manufacturer", manufacturer));
            if (part is not null) props.Add(new("Part number", part));
            props.Add(new("Slots used", string.Join(", ", modules.Select(m => m.Locator))));
        }

        context.UpsertDevice(new DeviceInfo(MetricIds.MemoryDeviceId, MetricCategory.Memory, "Memory")
        {
            Kind = modules.Count > 0 ? modules[0].MemoryType : "RAM",
            Source = "Windows, SMBIOS",
            Properties = props,
        });
    }

    public void Poll(IMetricWriter writer)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!Win32.GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException($"GlobalMemoryStatusEx failed (error {Marshal.GetLastPInvokeError()}).");
        }

        double total = status.TotalPhys;
        double available = status.AvailPhys;
        double used = Math.Max(0, total - available);
        writer.Write(_total!, total);
        writer.Write(_available!, available);
        writer.Write(_used!, used);
        if (total > 0)
        {
            writer.Write(_load!, used * 100.0 / total);
        }

        var perf = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (Win32.GetPerformanceInfo(ref perf, perf.Size))
        {
            double page = perf.PageSize;
            writer.Write(_commitUsed!, perf.CommitTotal * page);
            writer.Write(_commitLimit!, perf.CommitLimit * page);
            writer.Write(_cache!, perf.SystemCache * page);
        }
    }

    public void Shutdown()
    {
    }

    public void Dispose()
    {
    }
}
