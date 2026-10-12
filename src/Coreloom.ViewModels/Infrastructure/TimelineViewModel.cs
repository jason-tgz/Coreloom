using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Infrastructure;

/// <summary>
/// Shared time context for every chart: the history store, the latest sample time, the selected window,
/// freeze (pause viewing) state and a shared hover time so stacked charts show synchronized crosshairs.
/// </summary>
public sealed class TimelineViewModel : ObservableObject
{
    private long _now;
    private DateTime _nowLocal = DateTime.Now;
    private HistoryWindow _window = HistoryWindow.Seconds60;
    private int _intervalMilliseconds = 1000;
    private bool _isFrozen;
    private long _frozenAt;
    private bool _smoothScrolling = true;
    private bool _animateValues = true;
    private long? _hoverTime;

    public TimelineViewModel(HistoryStore history)
    {
        History = history ?? throw new ArgumentNullException(nameof(history));
    }

    public HistoryStore History { get; }

    /// <summary>Monotonic timestamp (ms) of the latest frame. Charts re-render when this changes.</summary>
    public long Now
    {
        get => _now;
        private set => SetProperty(ref _now, value);
    }

    public HistoryWindow Window
    {
        get => _window;
        set
        {
            if (SetProperty(ref _window, value))
            {
                OnPropertyChanged(nameof(WindowMilliseconds));
                OnPropertyChanged(nameof(WindowLabel));
            }
        }
    }

    public long WindowMilliseconds => (long)Window.ToTimeSpan().TotalMilliseconds;

    public string WindowLabel => Window.ToLongLabel();

    public int IntervalMilliseconds
    {
        get => _intervalMilliseconds;
        set => SetProperty(ref _intervalMilliseconds, Math.Max(50, value));
    }

    /// <summary>When frozen, charts stop scrolling so values can be inspected; telemetry keeps recording.</summary>
    public bool IsFrozen
    {
        get => _isFrozen;
        set
        {
            if (SetProperty(ref _isFrozen, value))
            {
                _frozenAt = _now;
                OnPropertyChanged(nameof(RightEdge));
            }
        }
    }

    /// <summary>Right edge of the visible window (latest sample, or the freeze point).</summary>
    public long RightEdge => _isFrozen ? _frozenAt : _now;

    public bool SmoothScrolling
    {
        get => _smoothScrolling;
        set => SetProperty(ref _smoothScrolling, value);
    }

    public bool AnimateValues
    {
        get => _animateValues;
        set => SetProperty(ref _animateValues, value);
    }

    /// <summary>Hovered timestamp shared between charts (null when not hovering).</summary>
    public long? HoverTime
    {
        get => _hoverTime;
        set => SetProperty(ref _hoverTime, value);
    }

    public void Advance(TelemetryFrame frame)
    {
        _nowLocal = frame.LocalTime;
        Now = frame.Timestamp;
        if (!_isFrozen)
        {
            OnPropertyChanged(nameof(RightEdge));
        }
    }

    /// <summary>Converts a monotonic timestamp to local wall-clock time (for tooltips and exports).</summary>
    public DateTime ToLocalTime(long timestamp) => _nowLocal.AddMilliseconds(timestamp - _now);
}
