using Coreloom.Core.History;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware.Processes;
using Coreloom.Hardware.Providers;
using Coreloom.Hardware.SystemInfo;

namespace Coreloom.Hardware.Tests;

/// <summary>
/// Runs the real Windows providers. These execute in CI on GitHub's Windows runners (which have no NVIDIA
/// GPU, so NVML must report "Unavailable" gracefully) and on a developer PC. Skipped on other platforms.
/// </summary>
public class WindowsIntegrationTests
{
    private static TelemetryEngine CreateEngine(params ITelemetryProvider[] providers) =>
        new(providers, new MetricCatalog(), new DeviceRegistry(), new HistoryStore(256), SystemInfoReader.DetectCapabilities());

    private static TelemetryFrame PollTwice(TelemetryEngine engine)
    {
        engine.InitializeAll();
        engine.PollOnce();
        Thread.Sleep(1100);
        return engine.PollOnce();
    }

    [Fact]
    public void CpuProvider_ReportsTotalAndPerThreadLoad()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var engine = CreateEngine(new CpuProvider());
        var frame = PollTwice(engine);

        Assert.True(frame.TryGetValue(MetricIds.CpuLoad, out double total), "CPU load missing");
        Assert.InRange(total, 0, 100);
        Assert.True(frame.Contains(MetricIds.CpuThreadLoad(0)));
        Assert.NotNull(engine.Devices.Get(MetricIds.CpuDeviceId));
    }

    [Fact]
    public void MemoryProvider_ReportsConsistentValues()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var engine = CreateEngine(new MemoryProvider());
        var frame = PollTwice(engine);

        double total = frame.GetValue(MetricIds.MemoryTotal)!.Value;
        double used = frame.GetValue(MetricIds.MemoryUsed)!.Value;
        double available = frame.GetValue(MetricIds.MemoryAvailable)!.Value;
        Assert.True(total > 512L * 1024 * 1024);
        Assert.Equal(total, used + available, 0);
        Assert.InRange(frame.GetValue(MetricIds.MemoryLoad)!.Value, 0, 100);
    }

    [Fact]
    public void StorageProvider_FindsAtLeastOneDiskAndVolume()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var services = new HardwareServices();
        using var engine = CreateEngine(new StorageProvider(services));
        var frame = PollTwice(engine);

        Assert.NotEmpty(services.Disks.Disks);
        Assert.True(frame.Contains(MetricIds.DiskTotalRead));
        string system = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\', ':');
        Assert.True(frame.TryGetValue(MetricIds.VolumeTotal(system), out double capacity));
        Assert.True(capacity > 0);
    }

    [Fact]
    public void NetworkProvider_InitializesWithoutErrors()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var engine = CreateEngine(new NetworkProvider());
        PollTwice(engine);
        Assert.Equal(ProviderState.Running, engine.ProviderStatuses[0].State);
    }

    [Fact]
    public void NvmlProvider_IsRunningOrGracefullyUnavailable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var services = new HardwareServices();
        using var engine = CreateEngine(new NvmlGpuProvider(services));
        var frame = PollTwice(engine);
        var state = engine.ProviderStatuses[0].State;

        Assert.True(state is ProviderState.Running or ProviderState.Unavailable, $"Unexpected NVML state {state}: {engine.ProviderStatuses[0].Message}");
        if (state == ProviderState.Running)
        {
            Assert.True(frame.Contains(MetricIds.GpuLoad(0)) || frame.Contains(MetricIds.GpuTemperature(0)));
        }
    }

    [Fact]
    public void ProcessSampler_ReturnsProcessesIncludingThisOne()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var sampler = new ProcessSampler();
        sampler.Sample();
        Thread.Sleep(200);
        var processes = sampler.Sample();
        Assert.NotEmpty(processes);
        Assert.Contains(processes, p => p.WorkingSetBytes > 0);
    }

    [Fact]
    public void SystemSummary_HasCpuAndOs()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var summary = SystemInfoReader.ReadSummary();
        Assert.False(string.IsNullOrWhiteSpace(summary.CpuName));
        Assert.StartsWith("Windows", summary.OperatingSystem);
        Assert.True(summary.LogicalProcessors > 0);
    }

    [Fact]
    public void MonitoringHost_RunsAllProvidersTogether()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var host = MonitoringHost.Create(new HardwareOptions { UseLibreHardwareMonitor = true });
        host.Engine.InitializeAll();
        host.Engine.PollOnce();
        Thread.Sleep(1100);
        var frame = host.Engine.PollOnce();

        Assert.True(frame.Contains(MetricIds.CpuLoad));
        Assert.True(frame.Contains(MetricIds.MemoryLoad));
        Assert.DoesNotContain(host.Engine.ProviderStatuses, s => s.State == ProviderState.Faulted);
    }
}
