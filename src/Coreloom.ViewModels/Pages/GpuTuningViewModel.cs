using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Pages;

/// <summary>Colour intent of a tuning indicator: amber for raised settings, blue for lowered ones.</summary>
public enum TuningTone
{
    None,
    Raised,
    Lowered,
    Mixed,
}

/// <summary>
/// One read-only range bar in the tuning viewer (for example "Core clock offset −200 … +300 MHz" with a marker at the
/// current value and a tick at stock). Nothing here can change a GPU setting: the bar is display only.
/// </summary>
public sealed class TuningMeterViewModel : ObservableObject
{
    private bool _hasValue;
    private bool _hasRange;
    private double _value;
    private double _minimum;
    private double _maximum = 1;
    private double _reference = double.NaN;
    private string _valueText = "—";
    private string _rangeText = string.Empty;
    private string _referenceText = string.Empty;
    private string _statusText = string.Empty;
    private TuningTone _tone;
    private string _unavailableText = string.Empty;

    public TuningMeterViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string Description { get; }

    public bool HasValue
    {
        get => _hasValue;
        private set => SetProperty(ref _hasValue, value);
    }

    /// <summary>True when the driver also reported the allowed range, which positions the marker on the bar.</summary>
    public bool HasRange
    {
        get => _hasRange;
        private set => SetProperty(ref _hasRange, value);
    }

    public double Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public double Minimum
    {
        get => _minimum;
        private set => SetProperty(ref _minimum, value);
    }

    public double Maximum
    {
        get => _maximum;
        private set => SetProperty(ref _maximum, value);
    }

    /// <summary>The stock value (0 for offsets, the default limit for power); NaN when unknown.</summary>
    public double Reference
    {
        get => _reference;
        private set
        {
            if (!_reference.Equals(value))
            {
                _reference = value;
                OnPropertyChanged();
            }
        }
    }

    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }

    public string RangeText
    {
        get => _rangeText;
        private set => SetProperty(ref _rangeText, value);
    }

    public string ReferenceText
    {
        get => _referenceText;
        private set => SetProperty(ref _referenceText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public TuningTone Tone
    {
        get => _tone;
        private set => SetProperty(ref _tone, value);
    }

    public string UnavailableText
    {
        get => _unavailableText;
        private set => SetProperty(ref _unavailableText, value);
    }

    public void SetUnavailable(string reason)
    {
        HasValue = false;
        HasRange = false;
        ValueText = "—";
        RangeText = string.Empty;
        ReferenceText = string.Empty;
        StatusText = string.Empty;
        Tone = TuningTone.None;
        UnavailableText = reason;
    }

    public void Set(double value, (double Min, double Max)? range, double reference, string valueText, string rangeText, string referenceText, string statusText, TuningTone tone)
    {
        UnavailableText = string.Empty;
        HasValue = true;
        Value = value;
        bool hasRange = range is { } r && r.Max > r.Min;
        HasRange = hasRange;
        if (hasRange)
        {
            Minimum = range!.Value.Min;
            Maximum = range.Value.Max;
        }

        Reference = reference;
        ValueText = valueText;
        RangeText = hasRange ? rangeText : string.Empty;
        ReferenceText = referenceText;
        StatusText = statusText;
        Tone = tone;
    }
}

/// <summary>
/// The GPU page's read-only tuning section: an indicator that appears when a clock offset or power-limit change is
/// detected, and a viewer (offset/limit range bars plus the voltage the GPU actually ran at for each clock).
/// Coreloom never writes a GPU setting. What cannot be read is said so rather than guessed:
/// an edited voltage/frequency curve is invisible to NVIDIA's public API.
/// </summary>
public sealed class GpuTuningViewModel : ObservableObject
{
    private const long CurveRefreshMilliseconds = 1000;

    private readonly VoltageFrequencyRecorder _recorder;
    private GpuTuningStatus _status = GpuTuningAnalyzer.Analyze(default);
    private GpuTuningReading? _lastReading;
    private int _lastSlot = -1;
    private HardwareVendor _vendor;
    private bool _isOpen;
    private long? _lastCurveRefresh;
    private VoltageFrequencyPoint[] _curvePoints = [];
    private double _currentClock = double.NaN;
    private double _currentVolts = double.NaN;
    private string _curveSummary = string.Empty;
    private string _curveMessage = string.Empty;
    private string _unavailableMessage = string.Empty;
    private IReadOnlyList<string> _findingLines = [];

    public GpuTuningViewModel(VoltageFrequencyRecorder recorder)
    {
        _recorder = recorder;
        CoreOffset = new TuningMeterViewModel("Core clock offset", "Shifts the clock the GPU reaches at every voltage step. Positive is an overclock.");
        MemoryOffset = new TuningMeterViewModel("Memory clock offset", "Shifts the video-memory clock, in the units the NVIDIA driver reports.");
        PowerLimit = new TuningMeterViewModel("Power limit", "The most power the board may draw. Above the default is a raised limit; below it is a cap.");
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
        ResetCurveCommand = new RelayCommand(ResetCurve);
    }

    /// <summary>Raised when the viewer is opened or closed so the page can refresh it immediately.</summary>
    public event Action? OpenChanged;

    public IRelayCommand ToggleCommand { get; }

    public IRelayCommand ResetCurveCommand { get; }

    public TuningMeterViewModel CoreOffset { get; }

    public TuningMeterViewModel MemoryOffset { get; }

    public TuningMeterViewModel PowerLimit { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (SetProperty(ref _isOpen, value))
            {
                OpenChanged?.Invoke();
            }
        }
    }

    public GpuTuningKind Kind => _status.Kind;

    /// <summary>True when a change from stock was detected (shows the badge next to the GPU name).</summary>
    public bool HasBadge => _status.IsTuned;

    public string BadgeText => _status.BadgeText;

    public string Headline => _status.Headline;

    public TuningTone BadgeTone => _status.Kind switch
    {
        GpuTuningKind.Overclock => TuningTone.Raised,
        GpuTuningKind.Reduced => TuningTone.Lowered,
        GpuTuningKind.Mixed => TuningTone.Mixed,
        _ => TuningTone.None,
    };

    /// <summary>Tooltip for the badge: the headline followed by each detected setting.</summary>
    public string BadgeToolTip => _status.IsTuned
        ? $"{_status.Headline}\n{string.Join('\n', _status.Findings.Select(f => f.Description))}\n\nRead-only: Coreloom never changes GPU settings."
        : _status.Headline;

    /// <summary>One line per setting that differs from stock; empty when none.</summary>
    public IReadOnlyList<string> FindingLines
    {
        get => _findingLines;
        private set => SetProperty(ref _findingLines, value);
    }

    /// <summary>True when at least one tuning value could be read (the bars show data).</summary>
    public bool IsReadable => _status.Kind != GpuTuningKind.Unavailable;

    /// <summary>Why nothing can be shown, when <see cref="IsReadable"/> is false.</summary>
    public string UnavailableMessage
    {
        get => _unavailableMessage;
        private set => SetProperty(ref _unavailableMessage, value);
    }

    public VoltageFrequencyPoint[] CurvePoints
    {
        get => _curvePoints;
        private set => SetProperty(ref _curvePoints, value);
    }

    public bool HasCurve => _curvePoints.Length > 0;

    /// <summary>Latest clock (MHz) for the "now" marker; NaN when unknown.</summary>
    public double CurrentClockMhz
    {
        get => _currentClock;
        private set
        {
            if (!_currentClock.Equals(value))
            {
                _currentClock = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Latest core voltage (V) for the "now" marker; NaN when unknown.</summary>
    public double CurrentVolts
    {
        get => _currentVolts;
        private set
        {
            if (!_currentVolts.Equals(value))
            {
                _currentVolts = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurveSummary
    {
        get => _curveSummary;
        private set => SetProperty(ref _curveSummary, value);
    }

    /// <summary>Shown instead of the chart when there is nothing to plot yet (and why).</summary>
    public string CurveMessage
    {
        get => _curveMessage;
        private set => SetProperty(ref _curveMessage, value);
    }

    /// <summary>
    /// Refreshes the badge (cheap, only recomputed when a reading changes) and, while the viewer is open, the bars and
    /// the observed curve.
    /// </summary>
    public void Update(TelemetryFrame frame, int slot, HardwareVendor vendor, UnitFormatter formatter, MetricCatalog catalog, bool force = false)
    {
        _vendor = vendor;
        var reading = new GpuTuningReading(
            Get(frame, MetricIds.GpuCoreClockOffset(slot)),
            Get(frame, MetricIds.GpuMemoryClockOffset(slot)),
            Get(frame, MetricIds.GpuPowerLimit(slot)),
            Get(frame, MetricIds.GpuPowerLimitDefault(slot)));

        if (slot != _lastSlot || _lastReading is null || _lastReading.Value != reading)
        {
            _lastSlot = slot;
            _lastReading = reading;
            ApplyStatus(GpuTuningAnalyzer.Analyze(reading), slot, catalog);
        }

        if (!IsOpen)
        {
            return;
        }

        UpdateMeters(frame, slot, formatter, catalog);
        UpdateCurve(frame, slot, formatter, catalog, force);
    }

    private static double? Get(TelemetryFrame frame, string id) => frame.TryGetValue(id, out double v) ? v : null;

    private void ApplyStatus(GpuTuningStatus status, int slot, MetricCatalog catalog)
    {
        _status = status;
        FindingLines = status.Findings.Select(f => f.Description).ToArray();
        if (status.Kind == GpuTuningKind.Unavailable)
        {
            UnavailableMessage = _vendor == HardwareVendor.Nvidia
                ? "The NVIDIA driver did not report clock offsets or a default power limit for this GPU, so its tuning can't be checked. "
                    + (catalog.Get(MetricIds.GpuCoreClockOffset(slot))?.UnavailableReason ?? catalog.GetUnavailableReason(MetricIds.GpuCoreClockOffset(slot)) ?? string.Empty)
                : "Reading tuning settings is supported for NVIDIA GPUs only (through the NVIDIA driver). This GPU's overclock and power-limit state isn't read.";
            UnavailableMessage = UnavailableMessage.Trim();
        }
        else
        {
            UnavailableMessage = string.Empty;
        }

        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(BadgeTone));
        OnPropertyChanged(nameof(BadgeToolTip));
        OnPropertyChanged(nameof(IsReadable));
    }

    private void UpdateMeters(TelemetryFrame frame, int slot, UnitFormatter f, MetricCatalog catalog)
    {
        UpdateOffsetMeter(CoreOffset, frame, f, catalog, MetricIds.GpuCoreClockOffset(slot), MetricIds.GpuCoreClockOffsetMin(slot), MetricIds.GpuCoreClockOffsetMax(slot));
        UpdateOffsetMeter(MemoryOffset, frame, f, catalog, MetricIds.GpuMemoryClockOffset(slot), MetricIds.GpuMemoryClockOffsetMin(slot), MetricIds.GpuMemoryClockOffsetMax(slot));

        if (frame.TryGetValue(MetricIds.GpuPowerLimit(slot), out double limit))
        {
            double? stock = Get(frame, MetricIds.GpuPowerLimitDefault(slot));
            (double, double)? range = frame.TryGetValue(MetricIds.GpuPowerLimitMin(slot), out double lo) && frame.TryGetValue(MetricIds.GpuPowerLimitMax(slot), out double hi) ? (lo, hi) : null;
            var direction = stock is double s ? GpuTuningAnalyzer.ClassifyPower(limit, s) : null;
            string status = stock is null ? "Default not reported" : direction switch
            {
                TuningDirection.Increased => $"Raised: {limit / stock.Value * 100:0}% of default",
                TuningDirection.Decreased => $"Capped: {limit / stock.Value * 100:0}% of default",
                _ => "At default",
            };
            PowerLimit.Set(
                limit,
                range,
                stock ?? double.NaN,
                f.Power(limit).ToString(),
                range is { } r ? $"{f.Power(r.Item1)} – {f.Power(r.Item2)} allowed" : string.Empty,
                stock is double d ? $"Default {f.Power(d)}" : string.Empty,
                status,
                ToneOf(direction));
        }
        else
        {
            PowerLimit.SetUnavailable(catalog.Get(MetricIds.GpuPowerLimit(slot))?.UnavailableReason ?? "The driver did not report a power limit for this GPU.");
        }
    }

    private static void UpdateOffsetMeter(TuningMeterViewModel meter, TelemetryFrame frame, UnitFormatter f, MetricCatalog catalog, string valueId, string minId, string maxId)
    {
        if (!frame.TryGetValue(valueId, out double value))
        {
            meter.SetUnavailable(catalog.Get(valueId)?.UnavailableReason ?? catalog.GetUnavailableReason(valueId) ?? "The driver did not report this offset for the GPU.");
            return;
        }

        (double, double)? range = frame.TryGetValue(minId, out double lo) && frame.TryGetValue(maxId, out double hi) ? (lo, hi) : null;
        var direction = GpuTuningAnalyzer.ClassifyOffset(value);
        string status = direction switch
        {
            TuningDirection.Increased => "Raised above stock",
            TuningDirection.Decreased => "Lowered below stock",
            _ => "Stock (no offset)",
        };
        meter.Set(
            value,
            range,
            0,
            f.ClockOffset(value).ToString(),
            range is { } r ? $"{f.ClockOffset(r.Item1)} to {f.ClockOffset(r.Item2)} allowed" : string.Empty,
            "Stock 0",
            status,
            ToneOf(direction));
    }

    private static TuningTone ToneOf(TuningDirection? direction) => direction switch
    {
        TuningDirection.Increased => TuningTone.Raised,
        TuningDirection.Decreased => TuningTone.Lowered,
        _ => TuningTone.None,
    };

    private void UpdateCurve(TelemetryFrame frame, int slot, UnitFormatter f, MetricCatalog catalog, bool force)
    {
        bool hasVoltage = frame.TryGetValue(MetricIds.GpuVoltage(slot), out double volts);
        bool hasClock = frame.TryGetValue(MetricIds.GpuCoreClock(slot), out double clock);
        CurrentClockMhz = hasVoltage && hasClock ? clock : double.NaN;
        CurrentVolts = hasVoltage && hasClock ? volts : double.NaN;

        if (!force && _lastCurveRefresh is long last && frame.Timestamp - last < CurveRefreshMilliseconds)
        {
            return;
        }

        _lastCurveRefresh = frame.Timestamp;
        var curve = _recorder.For(slot);
        CurvePoints = curve.Snapshot();
        OnPropertyChanged(nameof(HasCurve));
        CurveSummary = HasCurve
            ? $"{curve.TotalSamples:N0} readings across {CurvePoints.Length} clock steps since Coreloom started (or since cleared)"
            : string.Empty;

        if (HasCurve)
        {
            CurveMessage = string.Empty;
        }
        else if (!hasVoltage)
        {
            CurveMessage = "Core voltage isn't being reported for this GPU, so the voltage curve can't be drawn. "
                + (catalog.Get(MetricIds.GpuVoltage(slot))?.UnavailableReason ?? catalog.GetUnavailableReason(MetricIds.GpuVoltage(slot)) ?? string.Empty);
            CurveMessage = CurveMessage.Trim();
        }
        else
        {
            CurveMessage = "Collecting readings… the curve fills in as the GPU runs at different clock speeds. Play a game or run a GPU workload to see the upper end.";
        }
    }

    private void ResetCurve()
    {
        if (_lastSlot >= 0)
        {
            _recorder.For(_lastSlot).Clear();
        }

        CurvePoints = [];
        OnPropertyChanged(nameof(HasCurve));
        CurveSummary = string.Empty;
        CurveMessage = "Cleared. The curve fills in again as the GPU runs.";
        _lastCurveRefresh = null;
    }
}
