namespace Coreloom.Core.Telemetry;

/// <summary>The physical quantity a metric represents. Determines units and formatting.</summary>
public enum MetricKind
{
    /// <summary>Utilization / load in percent (0–100).</summary>
    Load,
    /// <summary>Temperature in degrees Celsius.</summary>
    Temperature,
    /// <summary>Clock frequency in MHz.</summary>
    Clock,
    /// <summary>Power in watts.</summary>
    Power,
    /// <summary>Voltage in volts.</summary>
    Voltage,
    /// <summary>Current in amperes.</summary>
    Current,
    /// <summary>Fan speed in RPM.</summary>
    Fan,
    /// <summary>Fan / pump duty or target in percent.</summary>
    Control,
    /// <summary>A percentage level that is not a utilization (e.g. SSD life remaining).</summary>
    Level,
    /// <summary>Amount of data in bytes.</summary>
    Data,
    /// <summary>Data rate in bytes per second.</summary>
    Throughput,
    /// <summary>A unitless factor (e.g. CPU multiplier).</summary>
    Factor,
    /// <summary>A discrete state such as a GPU performance state (P0..P15) or a bit mask.</summary>
    State,
    /// <summary>Duration in seconds.</summary>
    Duration,
    /// <summary>A plain count.</summary>
    Count,
    /// <summary>Frequency in hertz (non-clock frequencies).</summary>
    Frequency,
}

/// <summary>High level grouping used for navigation and section toggles.</summary>
public enum MetricCategory
{
    System,
    Cpu,
    Gpu,
    Memory,
    Storage,
    Network,
    Motherboard,
    Application,
}

/// <summary>
/// How a value was obtained. Coreloom never invents sensor readings: values are either
/// read directly from a sensor/API (<see cref="Measured"/>) or computed from measured
/// inputs with a documented formula (<see cref="Derived"/>).
/// </summary>
public enum Provenance
{
    Measured,
    Derived,
}

/// <summary>Monitoring sections that the user can enable or disable.</summary>
public enum MonitoringSection
{
    System,
    Cpu,
    Gpu,
    Memory,
    Storage,
    Network,
}

/// <summary>Lifecycle state of a telemetry provider.</summary>
public enum ProviderState
{
    NotInitialized,
    Initializing,
    Running,
    /// <summary>Running, but some sensors are unavailable (e.g. missing privileges).</summary>
    Degraded,
    /// <summary>The provider's API is not present on this system (e.g. no NVIDIA driver).</summary>
    Unavailable,
    /// <summary>The provider failed and will be retried after a back-off delay.</summary>
    Faulted,
    /// <summary>Disabled by the user.</summary>
    Disabled,
}
