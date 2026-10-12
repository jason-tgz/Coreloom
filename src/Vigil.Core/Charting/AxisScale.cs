namespace Vigil.Core.Charting;

/// <summary>"Nice" axis ranges and tick steps (1, 2, 2.5, 5 × 10ⁿ).</summary>
public static class AxisScale
{
    private static readonly double[] Steps = [1, 2, 2.5, 5, 10];
    private static readonly double[] CeilingSteps = [1, 2, 2.5, 4, 5, 10];

    /// <summary>Smallest nice number ≥ <paramref name="value"/> (e.g. 37 → 40, 412 → 500, 0.7 → 1).</summary>
    public static double NiceCeiling(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return 1;
        }

        double exponent = Math.Floor(Math.Log10(value));
        double magnitude = Math.Pow(10, exponent);
        foreach (double step in CeilingSteps)
        {
            double candidate = step * magnitude;
            if (candidate >= value - (magnitude * 1e-9))
            {
                return candidate;
            }
        }

        return 10 * magnitude;
    }

    /// <summary>A nice tick step that divides [0, max] into at most <paramref name="maxTicks"/> intervals.</summary>
    public static double NiceStep(double max, int maxTicks)
    {
        if (maxTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTicks));
        }

        if (!double.IsFinite(max) || max <= 0)
        {
            return 1;
        }

        double raw = max / maxTicks;
        double exponent = Math.Floor(Math.Log10(raw));
        double magnitude = Math.Pow(10, exponent);
        foreach (double step in Steps)
        {
            double candidate = step * magnitude;
            if (candidate >= raw - (magnitude * 1e-9))
            {
                return candidate;
            }
        }

        return 10 * magnitude;
    }

    /// <summary>
    /// Computes a nice [min, max] range and step for the given data extent. Ranges always include zero
    /// when <paramref name="includeZero"/> is set (appropriate for utilization, power and throughput).
    /// </summary>
    public static (double Min, double Max, double Step) NiceRange(double dataMin, double dataMax, int maxTicks = 4, bool includeZero = true)
    {
        if (!double.IsFinite(dataMin) || !double.IsFinite(dataMax))
        {
            return (0, 1, 0.25);
        }

        if (dataMin > dataMax)
        {
            (dataMin, dataMax) = (dataMax, dataMin);
        }

        if (includeZero)
        {
            dataMin = Math.Min(0, dataMin);
            dataMax = Math.Max(0, dataMax);
        }

        if (dataMax - dataMin < 1e-9)
        {
            dataMax = dataMin + Math.Max(1, Math.Abs(dataMin) * 0.1);
        }

        double step = NiceStep(dataMax - dataMin, maxTicks);
        double min = Math.Floor(dataMin / step) * step;
        double max = Math.Ceiling(dataMax / step) * step;
        if (max - min < step)
        {
            max = min + step;
        }

        return (min, max, step);
    }
}
