namespace Coreloom.Core.Formatting;

/// <summary>Visual severity used to tint values and gauges.</summary>
public enum ThresholdLevel
{
    Normal,
    Elevated,
    Critical,
}

/// <summary>Warning thresholds for colouring UI elements (not safety limits).</summary>
public readonly record struct Thresholds(double Elevated, double Critical)
{
    public static Thresholds None { get; } = new(double.PositiveInfinity, double.PositiveInfinity);

    public static Thresholds Load { get; } = new(85, 97);

    public static Thresholds GpuTemperature { get; } = new(75, 83);

    public static Thresholds CpuTemperature { get; } = new(80, 90);

    public static Thresholds Memory { get; } = new(80, 92);

    public ThresholdLevel Evaluate(double value)
    {
        if (!double.IsFinite(value))
        {
            return ThresholdLevel.Normal;
        }

        if (value >= Critical)
        {
            return ThresholdLevel.Critical;
        }

        return value >= Elevated ? ThresholdLevel.Elevated : ThresholdLevel.Normal;
    }
}
