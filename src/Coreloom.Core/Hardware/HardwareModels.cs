namespace Coreloom.Core.Hardware;

/// <summary>Aggregated per-application resource usage (processes grouped by image name).</summary>
public sealed record ProcessUsage(string Name, int InstanceCount, double CpuPercent, long WorkingSetBytes, long PrivateBytes);

/// <summary>Samples per-process CPU and memory usage on demand (only while a page shows it).</summary>
public interface IProcessSampler
{
    /// <summary>Takes a sample. CPU percentages are relative to the previous call (0 on the first call).</summary>
    IReadOnlyList<ProcessUsage> Sample();
}

/// <summary>Static description of the machine, shown in page headers.</summary>
public sealed record SystemSummary(
    string ComputerName,
    string OperatingSystem,
    string OsVersion,
    string? Motherboard,
    string? Bios,
    string CpuName,
    int PhysicalCores,
    int LogicalProcessors,
    long InstalledMemoryBytes,
    IReadOnlyList<MemoryModuleInfo> MemoryModules)
{
    public static SystemSummary Unknown { get; } = new(Environment.MachineName, "Windows", string.Empty, null, null, "Processor", 0, Environment.ProcessorCount, 0, []);
}

/// <summary>Decodes NVML "clocks event reasons" (formerly throttle reasons) bit masks.</summary>
public static class GpuClockReasons
{
    public static IReadOnlyList<string> Describe(ulong mask)
    {
        var reasons = new List<string>();
        if (mask == 0)
        {
            return reasons;
        }

        if ((mask & 0x1) != 0) reasons.Add("Idle");
        if ((mask & 0x2) != 0) reasons.Add("Application clocks setting");
        if ((mask & 0x4) != 0) reasons.Add("Power limit");
        if ((mask & 0x8) != 0) reasons.Add("Hardware slowdown");
        if ((mask & 0x10) != 0) reasons.Add("Sync boost");
        if ((mask & 0x20) != 0) reasons.Add("Thermal limit");
        if ((mask & 0x40) != 0) reasons.Add("Hardware thermal slowdown");
        if ((mask & 0x80) != 0) reasons.Add("Power brake slowdown");
        if ((mask & 0x100) != 0) reasons.Add("Display clock setting");
        return reasons;
    }

    public static string Summarize(double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            return "—";
        }

        var reasons = Describe((ulong)value);
        return reasons.Count == 0 ? "None (running at full clocks)" : string.Join(", ", reasons);
    }
}
