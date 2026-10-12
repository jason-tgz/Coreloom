using Vigil.Core.Hardware;
using Vigil.Core.History;
using Vigil.Core.Telemetry;
using Vigil.Hardware.Processes;
using Vigil.Hardware.Providers;
using Vigil.Hardware.SystemInfo;

namespace Vigil.Hardware;

public sealed record HardwareOptions
{
    public bool UseLibreHardwareMonitor { get; init; } = true;

    public bool ScanUsbControllers { get; init; }

    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Returns the preferred network adapter id (or null for automatic). Evaluated on every poll.</summary>
    public Func<string?>? PreferredNetworkAdapter { get; init; }
}

/// <summary>
/// Composition root for the monitoring back-end: detects capabilities, reads static system information,
/// creates the providers and the telemetry engine. Used by both the WPF app and VigilDiag.
/// </summary>
public sealed class MonitoringHost : IDisposable
{
    private MonitoringHost(TelemetryEngine engine, HardwareServices services, SystemSummary summary, SystemCapabilities capabilities, IReadOnlyList<ITelemetryProvider> providers)
    {
        Engine = engine;
        Services = services;
        Summary = summary;
        Capabilities = capabilities;
        Providers = providers;
        ProcessSampler = new ProcessSampler();
    }

    public TelemetryEngine Engine { get; }

    public HardwareServices Services { get; }

    public SystemSummary Summary { get; }

    public SystemCapabilities Capabilities { get; }

    public IReadOnlyList<ITelemetryProvider> Providers { get; }

    public IProcessSampler ProcessSampler { get; }

    /// <summary>True when this build includes the LibreHardwareMonitor adapter.</summary>
    public static bool LibreHardwareMonitorIncluded =>
#if VIGIL_NO_LHM
        false;
#else
        true;
#endif

    public static MonitoringHost Create(HardwareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var capabilities = SystemInfoReader.DetectCapabilities();
        var summary = SystemInfoReader.ReadSummary();
        var services = new HardwareServices();

        var providers = new List<ITelemetryProvider>
        {
            new SelfMonitorProvider(),
            new SystemInfoProvider(summary),
            new CpuProvider(),
            new MemoryProvider(),
            new StorageProvider(services),
            new NetworkProvider(options.PreferredNetworkAdapter),
            new NvmlGpuProvider(services),
        };

#if !VIGIL_NO_LHM
        if (options.UseLibreHardwareMonitor)
        {
            providers.Add(new Lhm.LhmProvider(services, new Lhm.LhmOptions(options.ScanUsbControllers)));
        }
#endif

        var engine = new TelemetryEngine(providers, new MetricCatalog(), new DeviceRegistry(), HistoryStore.ForInterval(options.PollingInterval), capabilities)
        {
            Interval = options.PollingInterval,
        };

        return new MonitoringHost(engine, services, summary, capabilities, providers);
    }

    /// <summary>Primary GPU slot (first discrete GPU), or 0 when none was detected.</summary>
    public int PrimaryGpuSlot => Services.Gpus.PrimarySlot ?? 0;

    /// <summary>LibreHardwareMonitor's own text report, when the adapter is active.</summary>
    public string? GetLibreHardwareMonitorReport()
    {
#if VIGIL_NO_LHM
        return null;
#else
        return Providers.OfType<Lhm.LhmProvider>().FirstOrDefault()?.GetReport();
#endif
    }

    public void Dispose() => Engine.Dispose();
}
