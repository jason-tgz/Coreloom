using Vigil.Core.Formatting;
using Vigil.Core.Telemetry;

namespace Vigil.ViewModels.Infrastructure;

/// <summary>How a numeric metric is presented.</summary>
public enum ValueStyle
{
    Percent,
    PercentPrecise,
    Temperature,
    ClockMegahertz,
    ClockGigahertz,
    Power,
    Volts,
    Millivolts,
    Bytes,
    DiskRate,
    NetworkRate,
    Fan,
    PerformanceState,
    Count,
    Duration,
    Auto,
}

/// <summary>Semantic color keys. The view maps them to theme brushes (e.g. "Cpu" → accent blue).</summary>
public static class ColorKeys
{
    public const string Accent = "Accent";
    public const string Cpu = "Cpu";
    public const string Gpu = "Gpu";
    public const string Memory = "Memory";
    public const string Storage = "Storage";
    public const string Temperature = "Temperature";
    public const string Power = "Power";
    public const string Fan = "Fan";
    public const string NetworkDown = "NetworkDown";
    public const string NetworkUp = "NetworkUp";
    public const string Read = "Read";
    public const string Write = "Write";
    public const string Neutral = "Neutral";

    /// <summary>Palette used for user-composed charts (Performance History).</summary>
    public static IReadOnlyList<string> Sequence { get; } = [Cpu, Gpu, Temperature, Memory, Storage, Power, NetworkDown, Fan];
}

public static class ValueStyles
{
    public static FormattedValue Format(ValueStyle style, double value, UnitFormatter f) => style switch
    {
        ValueStyle.Percent => f.Percent(value),
        ValueStyle.PercentPrecise => f.Percent(value, 1),
        ValueStyle.Temperature => f.Temperature(value),
        ValueStyle.ClockMegahertz => f.Clock(value),
        ValueStyle.ClockGigahertz => f.ClockGigahertz(value),
        ValueStyle.Power => f.Power(value),
        ValueStyle.Volts => f.Voltage(value),
        ValueStyle.Millivolts => f.Millivolts(value),
        ValueStyle.Bytes => f.Bytes(value),
        ValueStyle.DiskRate => f.DiskRate(value),
        ValueStyle.NetworkRate => f.NetworkRate(value),
        ValueStyle.Fan => f.Fan(value),
        ValueStyle.PerformanceState => new FormattedValue(UnitFormatter.PerformanceState(value), string.Empty),
        ValueStyle.Count => new FormattedValue(f.Number(value, 0), string.Empty),
        ValueStyle.Duration => f.Duration(value),
        _ => FormattedValue.Unavailable,
    };

    /// <summary>Default style for a metric kind.</summary>
    public static ValueStyle ForKind(MetricKind kind) => kind switch
    {
        MetricKind.Load or MetricKind.Control or MetricKind.Level => ValueStyle.Percent,
        MetricKind.Temperature => ValueStyle.Temperature,
        MetricKind.Clock => ValueStyle.ClockMegahertz,
        MetricKind.Power => ValueStyle.Power,
        MetricKind.Voltage => ValueStyle.Volts,
        MetricKind.Fan => ValueStyle.Fan,
        MetricKind.Data => ValueStyle.Bytes,
        MetricKind.Throughput => ValueStyle.DiskRate,
        MetricKind.Duration => ValueStyle.Duration,
        MetricKind.State or MetricKind.Count => ValueStyle.Count,
        _ => ValueStyle.Auto,
    };

    /// <summary>Formats with <paramref name="style"/>, or by kind when the style is <see cref="ValueStyle.Auto"/>.</summary>
    public static FormattedValue Format(ValueStyle style, MetricKind kind, double value, UnitFormatter f) =>
        style == ValueStyle.Auto ? f.Format(kind, value) : Format(style, value, f);
}
