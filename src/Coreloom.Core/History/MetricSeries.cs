namespace Coreloom.Core.History;

/// <summary>A single historical reading: monotonic timestamp (ms) and value.</summary>
public readonly record struct HistoryPoint(long T, float V);

/// <summary>Aggregate statistics over a time window.</summary>
public readonly record struct WindowStats(int Count, double Min, double Max, double Average)
{
    public bool HasData => Count > 0;

    public static WindowStats Empty => new(0, double.NaN, double.NaN, double.NaN);
}

/// <summary>
/// Fixed-capacity ring buffer of readings for one metric. Writes come from the telemetry thread,
/// reads from the UI thread; all access is guarded by a private lock (uncontended in practice).
/// Memory per point: 12 bytes.
/// </summary>
public sealed class MetricSeries
{
    private readonly object _gate = new();
    private long[] _t;
    private float[] _v;
    private int _head;   // next write position
    private int _count;
    private long _version;

    public MetricSeries(string id, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        Id = id;
        _t = new long[capacity];
        _v = new float[capacity];
    }

    public string Id { get; }

    public int Capacity
    {
        get { lock (_gate) return _t.Length; }
    }

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    /// <summary>Incremented on every append. Lets renderers skip work when nothing changed.</summary>
    public long Version => Interlocked.Read(ref _version);

    public void Append(long timestamp, double value)
    {
        if (!double.IsFinite(value))
        {
            return;
        }

        lock (_gate)
        {
            if (_count > 0)
            {
                long last = _t[(_head - 1 + _t.Length) % _t.Length];
                if (timestamp < last)
                {
                    // Out-of-order sample (should not happen with a monotonic clock): drop it.
                    return;
                }
            }

            _t[_head] = timestamp;
            _v[_head] = (float)value;
            _head = (_head + 1) % _t.Length;
            if (_count < _t.Length)
            {
                _count++;
            }
        }

        Interlocked.Increment(ref _version);
    }

    public bool TryGetLatest(out HistoryPoint point)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                point = default;
                return false;
            }

            int i = (_head - 1 + _t.Length) % _t.Length;
            point = new HistoryPoint(_t[i], _v[i]);
            return true;
        }
    }

    public bool TryGetOldest(out HistoryPoint point)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                point = default;
                return false;
            }

            int i = PhysicalIndex(0);
            point = new HistoryPoint(_t[i], _v[i]);
            return true;
        }
    }

    /// <summary>
    /// Copies points with <paramref name="from"/> ≤ T ≤ <paramref name="to"/> in chronological order into
    /// <paramref name="buffer"/> (grown if necessary). When <paramref name="includeNeighbors"/> is true the
    /// nearest point before <paramref name="from"/> and after <paramref name="to"/> are included too, so
    /// that lines can be drawn continuously to the chart edges.
    /// </summary>
    /// <returns>Number of points written.</returns>
    public int CopyRange(long from, long to, ref HistoryPoint[] buffer, bool includeNeighbors = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        lock (_gate)
        {
            if (_count == 0 || to < from)
            {
                return 0;
            }

            int start = LowerBound(from);              // first logical index with T >= from
            int end = UpperBound(to);                  // first logical index with T > to
            if (includeNeighbors)
            {
                if (start > 0)
                {
                    start--;
                }

                if (end < _count)
                {
                    end++;
                }
            }

            int n = end - start;
            if (n <= 0)
            {
                return 0;
            }

            if (buffer.Length < n)
            {
                buffer = new HistoryPoint[Math.Max(n, buffer.Length * 2)];
            }

            for (int k = 0; k < n; k++)
            {
                int p = PhysicalIndex(start + k);
                buffer[k] = new HistoryPoint(_t[p], _v[p]);
            }

            return n;
        }
    }

    /// <summary>Min/max/average of readings with from ≤ T ≤ to.</summary>
    public WindowStats GetStats(long from, long to)
    {
        lock (_gate)
        {
            if (_count == 0 || to < from)
            {
                return WindowStats.Empty;
            }

            int start = LowerBound(from);
            int end = UpperBound(to);
            if (end <= start)
            {
                return WindowStats.Empty;
            }

            double min = double.MaxValue, max = double.MinValue, sum = 0;
            for (int k = start; k < end; k++)
            {
                double v = _v[PhysicalIndex(k)];
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
            }

            int n = end - start;
            return new WindowStats(n, min, max, sum / n);
        }
    }

    /// <summary>Finds the reading closest in time to <paramref name="timestamp"/>.</summary>
    public bool TryFindNearest(long timestamp, out HistoryPoint point)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                point = default;
                return false;
            }

            int idx = LowerBound(timestamp);
            if (idx >= _count)
            {
                idx = _count - 1;
            }
            else if (idx > 0)
            {
                long after = _t[PhysicalIndex(idx)];
                long before = _t[PhysicalIndex(idx - 1)];
                if (timestamp - before <= after - timestamp)
                {
                    idx--;
                }
            }

            int p = PhysicalIndex(idx);
            point = new HistoryPoint(_t[p], _v[p]);
            return true;
        }
    }

    /// <summary>Changes capacity, keeping the most recent readings.</summary>
    public void Resize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        lock (_gate)
        {
            if (capacity == _t.Length)
            {
                return;
            }

            int keep = Math.Min(_count, capacity);
            var t = new long[capacity];
            var v = new float[capacity];
            int skip = _count - keep;
            for (int k = 0; k < keep; k++)
            {
                int p = PhysicalIndex(skip + k);
                t[k] = _t[p];
                v[k] = _v[p];
            }

            _t = t;
            _v = v;
            _count = keep;
            _head = keep % capacity;
        }

        Interlocked.Increment(ref _version);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _head = 0;
            _count = 0;
        }

        Interlocked.Increment(ref _version);
    }

    // ---- helpers (callers hold the lock) ------------------------------------------------

    private int PhysicalIndex(int logical)
    {
        int start = (_head - _count + _t.Length) % _t.Length;
        return (start + logical) % _t.Length;
    }

    /// <summary>First logical index whose timestamp is ≥ value (or _count).</summary>
    private int LowerBound(long value)
    {
        int lo = 0, hi = _count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_t[PhysicalIndex(mid)] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>First logical index whose timestamp is > value (or _count).</summary>
    private int UpperBound(long value)
    {
        int lo = 0, hi = _count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_t[PhysicalIndex(mid)] <= value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
