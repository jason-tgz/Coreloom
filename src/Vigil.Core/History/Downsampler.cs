namespace Vigil.Core.History;

/// <summary>
/// Reduces dense series to roughly two points per horizontal pixel while preserving peaks.
/// Uses min/max bucketing (the M4 family of algorithms), which is visually lossless for
/// line charts: every spike that would be visible at full resolution is still drawn.
/// </summary>
public static class Downsampler
{
    /// <summary>
    /// Downsamples <paramref name="source"/> (chronological) into at most ~2 × <paramref name="buckets"/> points.
    /// Points before <paramref name="from"/> or after <paramref name="to"/> are passed through unchanged.
    /// </summary>
    /// <returns>Number of points written to <paramref name="destination"/>.</returns>
    public static int MinMax(ReadOnlySpan<HistoryPoint> source, long from, long to, int buckets, Span<HistoryPoint> destination)
    {
        if (buckets < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(buckets));
        }

        if (source.Length <= buckets * 2 || to <= from)
        {
            int n = Math.Min(source.Length, destination.Length);
            source[..n].CopyTo(destination);
            return n;
        }

        int written = 0;
        double width = (to - from) / (double)buckets;

        int i = 0;
        // Leading points outside the window.
        while (i < source.Length && source[i].T < from)
        {
            if (!Emit(source[i], destination, ref written)) return written;
            i++;
        }

        int currentBucket = -1;
        HistoryPoint min = default, max = default;
        bool hasBucket = false;

        for (; i < source.Length && source[i].T <= to; i++)
        {
            var p = source[i];
            int bucket = (int)Math.Min(buckets - 1, (p.T - from) / width);
            if (bucket != currentBucket)
            {
                if (hasBucket && !FlushBucket(min, max, destination, ref written)) return written;
                currentBucket = bucket;
                min = p;
                max = p;
                hasBucket = true;
            }
            else
            {
                if (p.V < min.V) min = p;
                if (p.V > max.V) max = p;
            }
        }

        if (hasBucket && !FlushBucket(min, max, destination, ref written)) return written;

        // Trailing points outside the window.
        for (; i < source.Length; i++)
        {
            if (!Emit(source[i], destination, ref written)) return written;
        }

        return written;
    }

    /// <summary>Maximum number of points <see cref="MinMax"/> can produce for the given input.</summary>
    public static int MaxOutput(int sourceLength, int buckets) => Math.Min(sourceLength, (buckets * 2) + 4) + 4;

    private static bool FlushBucket(HistoryPoint min, HistoryPoint max, Span<HistoryPoint> destination, ref int written)
    {
        if (min.T == max.T)
        {
            return Emit(min, destination, ref written);
        }

        if (min.T < max.T)
        {
            return Emit(min, destination, ref written) && Emit(max, destination, ref written);
        }

        return Emit(max, destination, ref written) && Emit(min, destination, ref written);
    }

    private static bool Emit(HistoryPoint p, Span<HistoryPoint> destination, ref int written)
    {
        if (written >= destination.Length)
        {
            return false;
        }

        destination[written++] = p;
        return true;
    }
}
