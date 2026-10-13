using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Hardware;

/// <summary>One clock bin of the observed curve: the lowest, average and highest voltage seen at that clock.</summary>
public readonly record struct VoltageFrequencyPoint(double ClockMhz, double MinVolts, double MeanVolts, double MaxVolts, int Samples);

/// <summary>
/// The voltage the GPU actually ran at for each core clock while Coreloom was watching. This is
/// <b>measured</b>, not configured: it shows where the card operated, which is how an undervolt becomes visible
/// (the same clock at a lower voltage), but it is not the curve a tuning tool stores. Memory use is fixed
/// (a few arrays); samples outside plausible ranges are ignored. Not thread-safe: use from the UI thread only.
/// </summary>
public sealed class VoltageFrequencyCurve
{
    public const double BinWidthMhz = 15;
    public const int MaxBins = 400;

    /// <summary>A bin needs this many samples before it is drawn (a single reading can be a clock/voltage race).</summary>
    public const int MinSamplesPerPoint = 2;

    private const double MinVolts = 0.2;
    private const double MaxVolts = 2.0;

    private readonly int[] _count = new int[MaxBins];
    private readonly double[] _sum = new double[MaxBins];
    private readonly double[] _min = new double[MaxBins];
    private readonly double[] _max = new double[MaxBins];

    public VoltageFrequencyCurve()
    {
        Array.Fill(_min, double.PositiveInfinity);
        Array.Fill(_max, double.NegativeInfinity);
    }

    public long TotalSamples { get; private set; }

    /// <summary>Latest accepted reading (for the "now" marker); null until one arrives.</summary>
    public (double ClockMhz, double Volts)? Latest { get; private set; }

    /// <summary>Returns false when the pair was rejected (unavailable, zero, or outside plausible limits).</summary>
    public bool Record(double clockMhz, double volts)
    {
        if (!double.IsFinite(clockMhz) || !double.IsFinite(volts) || volts < MinVolts || volts > MaxVolts || clockMhz < BinWidthMhz)
        {
            return false;
        }

        int bin = (int)(clockMhz / BinWidthMhz);
        if (bin >= MaxBins)
        {
            return false;
        }

        _count[bin]++;
        _sum[bin] += volts;
        if (volts < _min[bin])
        {
            _min[bin] = volts;
        }

        if (volts > _max[bin])
        {
            _max[bin] = volts;
        }

        TotalSamples++;
        Latest = (clockMhz, volts);
        return true;
    }

    /// <summary>Bins with enough samples, ordered by clock.</summary>
    public VoltageFrequencyPoint[] Snapshot()
    {
        int n = 0;
        for (int i = 0; i < MaxBins; i++)
        {
            if (_count[i] >= MinSamplesPerPoint)
            {
                n++;
            }
        }

        var points = new VoltageFrequencyPoint[n];
        int k = 0;
        for (int i = 0; i < MaxBins; i++)
        {
            if (_count[i] >= MinSamplesPerPoint)
            {
                points[k++] = new VoltageFrequencyPoint((i + 0.5) * BinWidthMhz, _min[i], _sum[i] / _count[i], _max[i], _count[i]);
            }
        }

        return points;
    }

    public void Clear()
    {
        Array.Clear(_count);
        Array.Clear(_sum);
        Array.Fill(_min, double.PositiveInfinity);
        Array.Fill(_max, double.NegativeInfinity);
        TotalSamples = 0;
        Latest = null;
    }
}

/// <summary>Keeps one <see cref="VoltageFrequencyCurve"/> per GPU slot and feeds it from telemetry frames.</summary>
public sealed class VoltageFrequencyRecorder
{
    private const int MaxSlots = 8;
    private readonly Dictionary<int, Entry> _entries = [];

    private sealed class Entry
    {
        public Entry(int slot)
        {
            ClockId = MetricIds.GpuCoreClock(slot);
            VoltageId = MetricIds.GpuVoltage(slot);
        }

        public string ClockId { get; }

        public string VoltageId { get; }

        public VoltageFrequencyCurve Curve { get; } = new();
    }

    public VoltageFrequencyCurve For(int slot) => Get(slot).Curve;

    /// <summary>Records the core clock / core voltage pair of <paramref name="slot"/> when both are present in the frame.</summary>
    public void Record(TelemetryFrame frame, int slot)
    {
        if (slot < 0)
        {
            return;
        }

        var entry = Get(slot);
        if (frame.TryGetValue(entry.ClockId, out double clock) && frame.TryGetValue(entry.VoltageId, out double volts))
        {
            entry.Curve.Record(clock, volts);
        }
    }

    private Entry Get(int slot)
    {
        if (!_entries.TryGetValue(slot, out var entry))
        {
            if (_entries.Count >= MaxSlots)
            {
                _entries.Clear();
            }

            entry = new Entry(slot);
            _entries[slot] = entry;
        }

        return entry;
    }
}
