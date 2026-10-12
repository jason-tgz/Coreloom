namespace Coreloom.Core.Charting;

/// <summary>
/// Monotone cubic interpolation (Fritsch–Carlson). Produces smooth curves that never overshoot
/// the data: a series that sits at 100 % will never be drawn above 100 %, and a flat segment
/// stays flat. Used to render smoothed line and area charts.
/// </summary>
public static class MonotoneCurve
{
    /// <summary>
    /// Computes tangents (dy/dx) for each point. <paramref name="xs"/> must be strictly increasing.
    /// </summary>
    public static void ComputeTangents(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, Span<double> tangents)
    {
        int n = xs.Length;
        if (ys.Length != n || tangents.Length < n)
        {
            throw new ArgumentException("Input lengths do not match.");
        }

        if (n == 0)
        {
            return;
        }

        if (n == 1)
        {
            tangents[0] = 0;
            return;
        }

        Span<double> delta = n <= 512 ? stackalloc double[n - 1] : new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            double h = xs[i + 1] - xs[i];
            delta[i] = h > 0 ? (ys[i + 1] - ys[i]) / h : 0;
        }

        tangents[0] = delta[0];
        tangents[n - 1] = delta[n - 2];
        for (int i = 1; i < n - 1; i++)
        {
            tangents[i] = delta[i - 1] * delta[i] <= 0 ? 0 : (delta[i - 1] + delta[i]) / 2;
        }

        for (int i = 0; i < n - 1; i++)
        {
            if (delta[i] == 0)
            {
                tangents[i] = 0;
                tangents[i + 1] = 0;
                continue;
            }

            double a = tangents[i] / delta[i];
            double b = tangents[i + 1] / delta[i];
            double s = (a * a) + (b * b);
            if (s > 9)
            {
                double t = 3 / Math.Sqrt(s);
                tangents[i] = t * a * delta[i];
                tangents[i + 1] = t * b * delta[i];
            }
        }
    }

    /// <summary>
    /// Bézier control points for the segment between point <paramref name="i"/> and i+1.
    /// </summary>
    public static (double X1, double Y1, double X2, double Y2) ControlPoints(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, ReadOnlySpan<double> tangents, int i)
    {
        double h = xs[i + 1] - xs[i];
        return (
            xs[i] + (h / 3),
            ys[i] + (tangents[i] * h / 3),
            xs[i + 1] - (h / 3),
            ys[i + 1] - (tangents[i + 1] * h / 3));
    }
}
