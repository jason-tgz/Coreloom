using System.Windows;
using System.Windows.Media;
using Coreloom.App.Themes;
using Coreloom.ViewModels.Pages;

namespace Coreloom.App.Controls;

/// <summary>
/// Read-only range bar: a track from <see cref="Minimum"/> to <see cref="Maximum"/>, a tick at the stock value
/// (<see cref="Reference"/>), a tinted span from stock to the current value and a marker at the current value.
/// It has no input handling at all (it is not hit-testable), so it can only display a setting, never change one.
/// </summary>
public sealed class RangeMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0.0);
    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0.0);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 1.0);
    public static readonly DependencyProperty ReferenceProperty = Register(nameof(Reference), double.NaN);

    public static readonly DependencyProperty HasRangeProperty = DependencyProperty.Register(
        nameof(HasRange), typeof(bool), typeof(RangeMeter), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HasValueProperty = DependencyProperty.Register(
        nameof(HasValue), typeof(bool), typeof(RangeMeter), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(TuningTone), typeof(RangeMeter), new FrameworkPropertyMetadata(TuningTone.None, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double TrackHeight = 8;
    private const double MarkerRadius = 7;
    private bool _attached;

    public RangeMeter()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Stock value; NaN hides the tick.</summary>
    public double Reference
    {
        get => (double)GetValue(ReferenceProperty);
        set => SetValue(ReferenceProperty, value);
    }

    public bool HasRange
    {
        get => (bool)GetValue(HasRangeProperty);
        set => SetValue(HasRangeProperty, value);
    }

    public bool HasValue
    {
        get => (bool)GetValue(HasValueProperty);
        set => SetValue(HasValueProperty, value);
    }

    public TuningTone Tone
    {
        get => (TuningTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, (MarkerRadius * 2) + 6);

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 2 * MarkerRadius || height < TrackHeight)
        {
            return;
        }

        double left = MarkerRadius;
        double right = width - MarkerRadius;
        double centerY = height / 2;
        var track = new Rect(left - (TrackHeight / 2), centerY - (TrackHeight / 2), (right - left) + TrackHeight, TrackHeight);
        dc.DrawRoundedRectangle(ChartColors.Solid(ChartColors.Theme("Track")), null, track, TrackHeight / 2, TrackHeight / 2);

        double min = Minimum;
        double max = Maximum;
        if (!HasValue || !HasRange || !(max > min) || !double.IsFinite(Value))
        {
            return;
        }

        double X(double v) => left + (Math.Clamp((v - min) / (max - min), 0, 1) * (right - left));

        var tone = Tone;
        var toneColor = tone switch
        {
            TuningTone.Raised or TuningTone.Mixed => ChartColors.Theme("Elevated"),
            TuningTone.Lowered => ChartColors.Theme("Info"),
            _ => ChartColors.Theme("Text"),
        };

        double xValue = X(Value);
        bool hasReference = double.IsFinite(Reference);
        double xReference = hasReference ? X(Reference) : left;

        if (hasReference && tone != TuningTone.None && Math.Abs(xValue - xReference) > 0.5)
        {
            var span = new Rect(Math.Min(xValue, xReference), centerY - (TrackHeight / 2), Math.Abs(xValue - xReference), TrackHeight);
            var fill = Color.FromArgb(0x99, toneColor.R, toneColor.G, toneColor.B);
            dc.DrawRectangle(ChartColors.Solid(fill), null, span);
        }

        if (hasReference)
        {
            dc.DrawLine(ChartColors.Pen(ChartColors.Theme("TextSecondary"), 1.5, round: false), new Point(xReference, centerY - 9), new Point(xReference, centerY + 9));
        }

        // Marker: a filled dot with a ring in the card colour so it reads on the track and on the tinted span.
        dc.DrawEllipse(ChartColors.Solid(toneColor), ChartColors.Pen(ChartColors.Theme("Card"), 2), new Point(xValue, centerY), MarkerRadius - 1, MarkerRadius - 1);
    }

    private static DependencyProperty Register(string name, double defaultValue) =>
        DependencyProperty.Register(name, typeof(double), typeof(RangeMeter), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged += OnThemeChanged;
        }
    }

    private void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        if (ThemeService.Instance is { } theme)
        {
            theme.ThemeChanged -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(InvalidateVisual);
}
