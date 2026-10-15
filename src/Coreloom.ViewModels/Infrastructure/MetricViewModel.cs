using CommunityToolkit.Mvvm.ComponentModel;
using Coreloom.Core.Formatting;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Infrastructure;

/// <summary>
/// Presentation state of one live metric: formatted number and unit, severity level, gauge fraction,
/// window statistics and provenance. Missing values are shown as unavailable with the reason — never
/// replaced by placeholders.
/// </summary>
public sealed class MetricViewModel : ObservableObject
{
    private string _metricId;
    private string _label;
    private bool _hasValue;
    private double _value = double.NaN;
    private string _number = FormattedValue.Unavailable.Number;
    private string _unit = string.Empty;
    private ThresholdLevel _level;
    private double _fraction;
    private string? _detail;
    private string? _statsText;
    private string? _source;
    private bool _isDerived;
    private string _toolTip = string.Empty;
    private UnitFormatter _formatter = UnitFormatter.Default;
    private Thresholds _thresholds;
    private double _gaugeMaximum;

    public MetricViewModel(string metricId, string label, ValueStyle style, string colorKey = ColorKeys.Accent, Thresholds? thresholds = null, double gaugeMaximum = 100)
    {
        _metricId = metricId;
        _label = label;
        Style = style;
        ColorKey = colorKey;
        Thresholds = thresholds ?? Core.Formatting.Thresholds.None;
        GaugeMaximum = gaugeMaximum;
        NumberFormatter = FormatNumber;
    }

    public string MetricId
    {
        get => _metricId;
        private set => SetProperty(ref _metricId, value);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public ValueStyle Style { get; }

    public string ColorKey { get; }

    /// <summary>A temperature: its gauge and card take the temperature's colors (blue when cool … red when hot).</summary>
    public bool IsTemperature => Style == ValueStyle.Temperature;

    /// <summary>
    /// The color the metric's card glows with (and its gauge, sparkline and icon for a temperature): the series color,
    /// or for a temperature the color of how warm it is ("Temp.Cool" … "Temp.Hot"), changing as it warms up or cools down.
    /// </summary>
    public string GlowKey => IsTemperature ? ColorKeys.ForTemperature(HasValue ? Level : ThresholdLevel.Normal) : ColorKey;

    public Thresholds Thresholds
    {
        get => _thresholds;
        set
        {
            if (_thresholds != value)
            {
                _thresholds = value;
                Level = HasValue ? value.Evaluate(Value) : ThresholdLevel.Normal;
                OnToneStopsChanged();
            }
        }
    }

    /// <summary>Value that maps to a full gauge (e.g. 100 for %, the power limit for watts).</summary>
    public double GaugeMaximum
    {
        get => _gaugeMaximum;
        set
        {
            if (!_gaugeMaximum.Equals(value))
            {
                _gaugeMaximum = value;
                OnToneStopsChanged();
            }
        }
    }

    /// <summary>Where the gauge turns from green to blue (a fraction of the gauge; see <see cref="GaugeTone"/>).</summary>
    public double GaugeCoolAt => GaugeTone.Stops(_thresholds, _gaugeMaximum).Cool;

    /// <summary>Where the gauge turns from blue to orange.</summary>
    public double GaugeWarmAt => GaugeTone.Stops(_thresholds, _gaugeMaximum).Warm;

    /// <summary>Where the gauge turns red.</summary>
    public double GaugeHotAt => GaugeTone.Stops(_thresholds, _gaugeMaximum).Hot;

    private void OnToneStopsChanged()
    {
        OnPropertyChanged(nameof(GaugeCoolAt));
        OnPropertyChanged(nameof(GaugeWarmAt));
        OnPropertyChanged(nameof(GaugeHotAt));
    }

    public bool HasValue
    {
        get => _hasValue;
        private set
        {
            if (SetProperty(ref _hasValue, value) && IsTemperature)
            {
                OnPropertyChanged(nameof(GlowKey));
            }
        }
    }

    /// <summary>Raw value in base units (NaN when unavailable). Animated by the view.</summary>
    public double Value
    {
        get => _value;
        private set
        {
            if (!_value.Equals(value))
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    public string Number
    {
        get => _number;
        private set => SetProperty(ref _number, value);
    }

    public string Unit
    {
        get => _unit;
        private set => SetProperty(ref _unit, value);
    }

    public ThresholdLevel Level
    {
        get => _level;
        private set
        {
            if (SetProperty(ref _level, value) && IsTemperature)
            {
                OnPropertyChanged(nameof(GlowKey));
            }
        }
    }

    /// <summary>0..1 for gauges and bars.</summary>
    public double Fraction
    {
        get => _fraction;
        private set
        {
            if (!_fraction.Equals(value))
            {
                _fraction = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Optional secondary line, e.g. "of 575 W limit".</summary>
    public string? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    /// <summary>"Min 31 · Avg 34 · Max 41" for the selected window.</summary>
    public string? StatsText
    {
        get => _statsText;
        private set => SetProperty(ref _statsText, value);
    }

    /// <summary>Provider name of the current value, e.g. "NVML".</summary>
    public string? Source
    {
        get => _source;
        private set => SetProperty(ref _source, value);
    }

    public bool IsDerived
    {
        get => _isDerived;
        private set => SetProperty(ref _isDerived, value);
    }

    /// <summary>Explains where the value comes from, or why it is unavailable.</summary>
    public string ToolTip
    {
        get => _toolTip;
        private set => SetProperty(ref _toolTip, value);
    }

    /// <summary>Formats an (animated) value as a number string with the current unit preferences.</summary>
    public Func<double, string> NumberFormatter { get; }

    public string FormatNumber(double value) => ValueStyles.Format(Style, value, _formatter).Number;

    public string FormatWithUnit(double value) => ValueStyles.Format(Style, value, _formatter).ToString();

    /// <summary>Points this view model at a different metric (e.g. when another GPU is selected).</summary>
    public void Retarget(string metricId)
    {
        if (metricId == _metricId)
        {
            return;
        }

        MetricId = metricId;
        Clear();
    }

    public void Clear()
    {
        HasValue = false;
        Value = double.NaN;
        Number = FormattedValue.Unavailable.Number;
        Unit = string.Empty;
        Fraction = 0;
        Level = ThresholdLevel.Normal;
        StatsText = null;
    }

    /// <summary>Refreshes from a frame. When <paramref name="history"/> is given, window statistics are computed.</summary>
    public void Update(TelemetryFrame frame, UnitFormatter formatter, MetricCatalog catalog, HistoryStore? history = null, long windowMilliseconds = 0)
    {
        _formatter = formatter;
        if (frame.TryGetSample(_metricId, out var sample))
        {
            double v = sample.Value;
            var formatted = ValueStyles.Format(Style, sample.Source.Descriptor.Kind, v, formatter);
            HasValue = true;
            Value = v;
            Number = formatted.Number;
            Unit = formatted.Unit;
            Level = Thresholds.Evaluate(v);
            Fraction = GaugeMaximum > 0 ? Math.Clamp(v / GaugeMaximum, 0, 1) : 0;
            Source = sample.Source.Provider;
            IsDerived = sample.Source.Provenance == Provenance.Derived;
            string note = sample.Source.Note is { Length: > 0 } n ? $"\n{n}" : string.Empty;
            ToolTip = $"{Label}\nSource: {sample.Source.Provider} · {sample.Source.Provenance}{note}";
        }
        else
        {
            if (HasValue || Number != FormattedValue.Unavailable.Number)
            {
                Clear();
            }

            Source = null;
            IsDerived = false;
            string reason = catalog.Get(_metricId)?.UnavailableReason ?? catalog.GetUnavailableReason(_metricId) ?? "Not reported by this system.";
            ToolTip = $"{Label}\nUnavailable: {reason}";
        }

        if (history is not null && windowMilliseconds > 0)
        {
            var series = history.Get(_metricId);
            var stats = series?.GetStats(frame.Timestamp - windowMilliseconds, frame.Timestamp) ?? WindowStats.Empty;
            StatsText = stats.HasData
                ? $"Min {FormatNumber(stats.Min)} · Avg {FormatNumber(stats.Average)} · Max {FormatNumber(stats.Max)}"
                : null;
        }
    }

    public override string ToString() => $"{Label}: {Number} {Unit}";
}
