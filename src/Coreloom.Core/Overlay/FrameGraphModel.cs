namespace Coreloom.Core.Overlay;

/// <summary>A frame on the frame-time graph: where it is drawn (pixels) and how long it took (milliseconds).</summary>
public readonly record struct FrameGraphPoint(double X, double Y, double Milliseconds);

/// <summary>Reads the latest frame times (milliseconds, oldest first) and how long ago the newest was shown.</summary>
public delegate int FrameTimeReader(Span<double> buffer, out double newestAgeMilliseconds);

/// <summary>
/// What the overlay's frame-time graph shows: each of the game's frames over the last second at the moment it was shown,
/// newest at the right, its height the time the frame took. The graph glides along with time rather than stepping from
/// read to read. Windows hands frames over in small batches, so the graph runs a little behind real time (a delay that
/// follows how late the batches actually are, rising at once and easing back slowly) and new frames flow in at the right
/// edge instead of popping in. The vertical scale eases toward what the frames need, so it doesn't jump.
/// <para>
/// Platform-neutral: the overlay's own drawing thread and the settings preview both use it, with
/// <see cref="FrameGraphRaster"/> to draw it.
/// </para>
/// </summary>
public sealed class FrameGraphModel
{
    /// <summary>How much time the graph spans (milliseconds).</summary>
    public const double SpanMilliseconds = 1000;

    /// <summary>The most the graph trails real time, even when frames arrive late.</summary>
    public const double MaxDelayMilliseconds = 300;

    public const int MaxFrames = 2048;

    private readonly double[] _times = new double[MaxFrames];
    private readonly double[] _sorted = new double[MaxFrames];
    private int _count;               // frame times read (oldest first)
    private int _wanted = 256;        // how many to ask for: enough to fill the span
    private double _newestAge;        // ms before the read that the newest frame was shown
    private double _delay;            // ms the graph trails real time
    private double _delayTarget;
    private double _scale = 20;       // milliseconds at the top of the graph
    private bool _started;

    /// <summary>Frames to draw (2 or more).</summary>
    public bool HasFrames => _count >= 2;

    public int Count => _count;

    /// <summary>Milliseconds at the top of the graph.</summary>
    public double Scale => _scale;

    /// <summary>How far the graph trails real time (milliseconds).</summary>
    public double Delay => _delay;

    /// <summary>The frame limiter's target frame time (milliseconds), 0 when the game isn't limited.</summary>
    public double Target { get; private set; }

    /// <summary>
    /// Reads the newest frames and moves the graph on by <paramref name="dtSeconds"/>. Returns whether there are frames
    /// to draw; without them the graph is empty (and the next frames start it afresh).
    /// </summary>
    public bool Update(FrameTimeReader read, double target, double dtSeconds)
    {
        int count = read(_times.AsSpan(0, _wanted), out double age);
        Target = target > 0 && double.IsFinite(target) ? target : 0;
        if (count < 2)
        {
            Clear();
            return false;
        }

        _count = count;
        _newestAge = Math.Max(0, age);
        double dt = Math.Clamp(dtSeconds, 0.001, 0.25);
        int visible;
        if (!_started)
        {
            _started = true;
            _delay = _delayTarget = Math.Clamp(_newestAge + 20, 60, MaxDelayMilliseconds);
            visible = Visible();
            _scale = Wanted(visible);
        }
        else
        {
            Follow(dt);
            visible = Visible();
            _scale += (Wanted(visible) - _scale) * (1 - Math.Exp(-dt * 4));
        }

        // Ask for just enough frames to fill the span next time (more if it fell short).
        _wanted = visible >= count && count == _wanted
            ? Math.Min(MaxFrames, _wanted * 2)
            : Math.Clamp(visible + 16 + (visible / 4), 64, MaxFrames);
        return true;
    }

    /// <summary>Nothing to show (the game stopped drawing).</summary>
    public void Clear()
    {
        _count = 0;
        _started = false;
    }

    /// <summary>A believable still trace for the preview: ~7 ms frames with a little jitter and a stutter now and then.</summary>
    public void LoadSample()
    {
        const int Count = 160;
        for (int i = 0; i < Count; i++)
        {
            double t = i;
            double jitter = (Math.Sin(t * 0.9) * 0.35) + (Math.Sin((t * 2.3) + 1) * 0.2);
            double stutter = (i % 97) == 0 ? 9 : (i % 53) == 0 ? 4 : 0;
            _times[i] = 6.9 + jitter + stutter;
        }

        _count = Count;
        _newestAge = 0;
        _delay = _delayTarget = 0;
        Target = 0;
        _started = false;
        _scale = Wanted(Count);
    }

    /// <summary>
    /// Places the frames on a graph <paramref name="width"/> × <paramref name="height"/> pixels: newest first, each at
    /// the moment it was shown, stopping at the first one left of the graph. Returns how many points were written.
    /// </summary>
    public int Layout(double width, double height, Span<FrameGraphPoint> points)
    {
        if (_count < 2 || width < 1 || height < 1)
        {
            return 0;
        }

        double perMs = width / SpanMilliseconds;
        int written = 0;
        double age = _newestAge - _delay;
        for (int i = _count - 1; i >= 0 && written < points.Length; i--)
        {
            double x = width - (age * perMs);
            points[written++] = new FrameGraphPoint(x, Y(_times[i], height), _times[i]);
            if (x < 0)
            {
                break;
            }

            age += _times[i];
        }

        return written;
    }

    /// <summary>The height on the graph for a frame time (pixels from the top), inside a graph of <paramref name="height"/>.</summary>
    public double Y(double milliseconds, double height) =>
        Math.Clamp(height - (milliseconds / Math.Max(_scale, 1) * (height - 1.5)), 1, height);

    /// <summary>The typical (median) frame time of the newest <paramref name="newest"/> frames.</summary>
    public double Typical(int newest) => Percentile(newest, 0.5);

    /// <summary>
    /// A stutter: a frame well above the typical one. Hot (red) when it is far above, warm (amber) otherwise.
    /// </summary>
    public static (bool Stutter, bool Hot) Classify(double milliseconds, double typical) =>
        milliseconds > typical * 1.6 && milliseconds > typical + 2 ? (true, milliseconds > typical * 2.5) : (false, false);

    /// <summary>
    /// How often the graph is redrawn for a display: the refresh rate, or an even fraction of it when that is over
    /// 144 Hz (120 on a 240 Hz display), so each redraw lands on the same number of display refreshes.
    /// </summary>
    public static int RedrawRate(int refreshHz)
    {
        if (refreshHz <= 1)
        {
            return 60;
        }

        int divisor = (int)Math.Ceiling(refreshHz / 144.0);
        return Math.Max(30, refreshHz / Math.Max(1, divisor));
    }

    /// <summary>
    /// The delay follows how late the newest frame is when it is read: it rises at once to the latest batch's lateness
    /// (so the right edge never runs dry) and lets go slowly, and the graph eases toward it so it never jumps.
    /// </summary>
    private void Follow(double dt)
    {
        _delayTarget = _newestAge > _delayTarget
            ? Math.Min(_newestAge, MaxDelayMilliseconds)
            : Math.Max(_newestAge, _delayTarget - (dt * 30));
        _delay += (_delayTarget - _delay) * (1 - Math.Exp(-dt * 10));
    }

    /// <summary>How many of the newest frames fall within the span (and the delay).</summary>
    private int Visible()
    {
        double covered = _newestAge - _delay;
        int visible = 0;
        for (int i = _count - 1; i >= 0 && covered < SpanMilliseconds; i--)
        {
            covered += _times[i];
            visible++;
        }

        return Math.Max(visible, Math.Min(2, _count));
    }

    /// <summary>The top of the graph: room above the typical frame and the target.</summary>
    private double Wanted(int visible) =>
        Math.Max(Math.Max(Percentile(visible, 0.5) * 2, Percentile(visible, 0.95) * 1.25), Math.Max(Target * 1.6, 8));

    private double Percentile(int newest, double p)
    {
        int count = Math.Min(newest, _count);
        if (count == 0)
        {
            return 0;
        }

        Array.Copy(_times, _count - count, _sorted, 0, count);
        Array.Sort(_sorted, 0, count);
        return _sorted[Math.Clamp((int)(p * (count - 1)), 0, count - 1)];
    }
}
