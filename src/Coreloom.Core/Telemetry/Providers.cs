namespace Coreloom.Core.Telemetry;

/// <summary>
/// A source of hardware telemetry. Implementations must be read-only: they never change clocks,
/// voltages, fan curves or any other hardware setting.
/// </summary>
/// <remarks>
/// Threading contract: <see cref="Initialize"/> may run on a thread-pool thread; <see cref="Poll"/>
/// always runs on the telemetry thread and is never called concurrently with Initialize or Shutdown.
/// Initialize may be called again after <see cref="Shutdown"/> (e.g. after a driver reset).
/// </remarks>
public interface ITelemetryProvider : IDisposable
{
    /// <summary>Display name, e.g. "NVIDIA NVML".</summary>
    string Name { get; }

    /// <summary>Section that enables/disables this provider.</summary>
    MonitoringSection Section { get; }

    /// <summary>Providers with a lower stage finish their first initialization before higher stages start.</summary>
    int InitStage { get; }

    /// <summary>Minimum time between polls. <see cref="TimeSpan.Zero"/> means every frame.
    /// Values from the last poll are carried forward in frames where the provider is not due.</summary>
    TimeSpan MinimumPollInterval { get; }

    /// <summary>Discovers hardware and registers metrics. May throw; the engine retries with back-off.</summary>
    ProviderInitResult Initialize(IProviderContext context);

    /// <summary>Reads current values and writes them. May throw; repeated failures trigger re-initialization.</summary>
    void Poll(IMetricWriter writer);

    /// <summary>Releases native resources. Initialize can be called again afterwards.</summary>
    void Shutdown();
}

/// <summary>Services available to providers during initialization.</summary>
public interface IProviderContext
{
    SystemCapabilities Capabilities { get; }

    MetricHandle Register(MetricDescriptor descriptor, Provenance provenance = Provenance.Measured, int priority = 0, string? note = null);

    void SetUnavailableReason(string metricId, string? reason);

    void UpsertDevice(DeviceInfo device);

    void RemoveDevice(string deviceId);

    bool IsSectionEnabled(MonitoringSection section);
}

/// <summary>Receives readings during a poll.</summary>
public interface IMetricWriter
{
    /// <summary>Writes a reading. Non-finite values (NaN/∞) are ignored.</summary>
    void Write(MetricHandle handle, double value);
}

public sealed record ProviderInitResult(ProviderState State, string? Message = null)
{
    public static ProviderInitResult Running(string? message = null) => new(ProviderState.Running, message);

    public static ProviderInitResult Degraded(string message) => new(ProviderState.Degraded, message);

    public static ProviderInitResult Unavailable(string message) => new(ProviderState.Unavailable, message);
}

/// <summary>Immutable status snapshot of a provider, for the UI and diagnostics.</summary>
public sealed record ProviderStatus(
    string Name,
    MonitoringSection Section,
    ProviderState State,
    string? Message,
    string? LastError,
    int ConsecutiveFailures,
    int TotalFailures,
    double LastPollMilliseconds,
    double AveragePollMilliseconds,
    double InitMilliseconds,
    DateTime? LastSuccessLocal,
    int MetricsWritten);

/// <summary>What the current process is allowed to access. Filled in by the Windows layer.</summary>
public sealed record SystemCapabilities
{
    public bool IsElevated { get; init; }

    /// <summary>True when the PawnIO kernel driver (used by LibreHardwareMonitor for MSR/SMN/SuperIO access) is installed.</summary>
    public bool PawnIoInstalled { get; init; }

    public string? PawnIoVersion { get; init; }

    public int OsBuild { get; init; }

    public bool IsWindows11 => OsBuild >= 22000;

    /// <summary>Low-level CPU/motherboard sensors require both administrator rights and PawnIO.</summary>
    public bool LowLevelSensorsAccessible => IsElevated && PawnIoInstalled;

    public static SystemCapabilities Unknown { get; } = new();
}
