using System.Globalization;

namespace Coreloom.Core.Hardware;

/// <summary>What the read-only tuning check concluded.</summary>
public enum GpuTuningKind
{
    /// <summary>None of the settings Coreloom can read were reported by this GPU/driver.</summary>
    Unavailable,

    /// <summary>The readable settings are at their stock values. Voltage-curve edits cannot be read, so this is not proof of a stock GPU.</summary>
    NotDetected,

    /// <summary>At least one readable setting is above stock (clock offset or power limit) and none is below.</summary>
    Overclock,

    /// <summary>At least one readable setting is below stock (negative clock offset or reduced power limit) and none is above.</summary>
    Reduced,

    /// <summary>Some settings are raised and others lowered.</summary>
    Mixed,
}

public enum TuningDirection
{
    Increased,
    Decreased,
}

/// <summary>One setting that differs from stock, with a ready-to-display description.</summary>
public sealed record TuningFinding(string Setting, TuningDirection Direction, string Description);

/// <summary>
/// The tuning-related values NVML can report, all read-only. A null value means "not reported".
/// Offsets are in MHz as the driver reports them; power values are in watts.
/// </summary>
public readonly record struct GpuTuningReading(
    double? CoreOffsetMhz,
    double? MemoryOffsetMhz,
    double? PowerLimitWatts,
    double? DefaultPowerLimitWatts);

public sealed record GpuTuningStatus(GpuTuningKind Kind, IReadOnlyList<TuningFinding> Findings, int ReadableSettings)
{
    public bool IsTuned => Kind is GpuTuningKind.Overclock or GpuTuningKind.Reduced or GpuTuningKind.Mixed;

    public string Headline => Kind switch
    {
        GpuTuningKind.Overclock => "Overclock detected",
        GpuTuningKind.Reduced => "Reduced clocks or power limit detected",
        GpuTuningKind.Mixed => "Custom tuning detected",
        GpuTuningKind.NotDetected => "No overclock or reduced limits detected",
        _ => "Tuning settings can't be read on this GPU",
    };

    /// <summary>Short text for the badge next to the GPU name.</summary>
    public string BadgeText => Kind switch
    {
        GpuTuningKind.Overclock => "Overclock",
        GpuTuningKind.Reduced => "Reduced limits",
        GpuTuningKind.Mixed => "Custom tuning",
        _ => string.Empty,
    };
}

/// <summary>
/// Decides, from values the NVIDIA driver reports, whether a clock offset or power-limit change is in effect.
/// This never guesses: a setting that is not reported is simply not counted, and a custom voltage/frequency curve
/// (for example one made with MSI Afterburner) is not readable through NVIDIA's public API, so it can never be
/// detected here.
/// </summary>
public static class GpuTuningAnalyzer
{
    /// <summary>Offsets are whole MHz; anything under this is treated as "no offset".</summary>
    public const double OffsetThresholdMhz = 1;

    /// <summary>Power limits within this many watts of the default are treated as the default (rounding, 1 W steps).</summary>
    public const double PowerToleranceWatts = 1;

    /// <summary>...or this fraction of the default, whichever is larger.</summary>
    public const double PowerToleranceFraction = 0.005;

    public static GpuTuningStatus Analyze(GpuTuningReading reading)
    {
        var findings = new List<TuningFinding>(3);
        int readable = 0;

        if (Valid(reading.CoreOffsetMhz) is double core)
        {
            readable++;
            if (ClassifyOffset(core) is TuningDirection direction)
            {
                findings.Add(new TuningFinding("Core clock offset", direction, $"Core clock offset {SignedMhz(core)}"));
            }
        }

        if (Valid(reading.MemoryOffsetMhz) is double memory)
        {
            readable++;
            if (ClassifyOffset(memory) is TuningDirection direction)
            {
                findings.Add(new TuningFinding("Memory clock offset", direction, $"Memory clock offset {SignedMhz(memory)}"));
            }
        }

        if (Valid(reading.PowerLimitWatts) is double limit && Valid(reading.DefaultPowerLimitWatts) is double stock && stock > 0)
        {
            readable++;
            if (ClassifyPower(limit, stock) is TuningDirection direction)
            {
                double percent = limit / stock * 100.0;
                findings.Add(new TuningFinding(
                    "Power limit",
                    direction,
                    string.Create(CultureInfo.InvariantCulture, $"Power limit {limit:0} W ({percent:0}% of the {stock:0} W default)")));
            }
        }

        if (readable == 0)
        {
            return new GpuTuningStatus(GpuTuningKind.Unavailable, findings, 0);
        }

        bool up = findings.Any(f => f.Direction == TuningDirection.Increased);
        bool down = findings.Any(f => f.Direction == TuningDirection.Decreased);
        var kind = (up, down) switch
        {
            (true, true) => GpuTuningKind.Mixed,
            (true, false) => GpuTuningKind.Overclock,
            (false, true) => GpuTuningKind.Reduced,
            _ => GpuTuningKind.NotDetected,
        };
        return new GpuTuningStatus(kind, findings, readable);
    }

    /// <summary>Direction of a clock offset, or null when it is zero (within <see cref="OffsetThresholdMhz"/>).</summary>
    public static TuningDirection? ClassifyOffset(double offsetMhz)
    {
        if (!double.IsFinite(offsetMhz) || Math.Abs(offsetMhz) < OffsetThresholdMhz)
        {
            return null;
        }

        return offsetMhz > 0 ? TuningDirection.Increased : TuningDirection.Decreased;
    }

    /// <summary>Direction of the power limit relative to the stock limit, or null when they match within tolerance.</summary>
    public static TuningDirection? ClassifyPower(double limitWatts, double defaultWatts)
    {
        if (!double.IsFinite(limitWatts) || !double.IsFinite(defaultWatts) || defaultWatts <= 0)
        {
            return null;
        }

        double tolerance = Math.Max(PowerToleranceWatts, defaultWatts * PowerToleranceFraction);
        if (Math.Abs(limitWatts - defaultWatts) <= tolerance)
        {
            return null;
        }

        return limitWatts > defaultWatts ? TuningDirection.Increased : TuningDirection.Decreased;
    }

    /// <summary>"+150 MHz", "-75 MHz", "0 MHz".</summary>
    public static string SignedMhz(double mhz)
    {
        double rounded = Math.Round(mhz, MidpointRounding.AwayFromZero);
        string sign = rounded > 0 ? "+" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{rounded:0} MHz");
    }

    private static double? Valid(double? value) => value is double v && double.IsFinite(v) ? v : null;
}
