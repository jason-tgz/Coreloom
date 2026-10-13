using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Coreloom.App.Themes;
using Coreloom.Core.Hardware;

namespace Coreloom.App.Controls;

/// <summary>
/// Read-only plot of the voltage the GPU actually ran at (millivolts, vertical) for each core clock (MHz,
/// horizontal): a shaded band from the lowest to the highest voltage seen at that clock, the average as a line,
/// and a marker for the current reading. It is built from measurements, so it shows where the card operated,
/// not the curve a tuning tool stores. Not hit-testable: it cannot edit anything.
/// </summary>
public sealed class VoltageFrequencyChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(VoltageFrequencyPoint[]), typeof(VoltageFrequencyChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentClockProperty = DependencyProperty.Register(
        nameof(CurrentClock), typeof(double), typeof(VoltageFrequencyChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentVoltsProperty = DependencyProperty.Register(
        nameof(CurrentVolts), typeof(double), typeof(VoltageFrequencyChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColorKeyProperty = DependencyProperty.Register(
        nameof(ColorKey), typeof(string), typeof(VoltageFrequencyChart),
        new FrameworkPropertyMetadata("Gpu", FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftMargin = 66;
    private const double RightMargin = 16;
    private const double TopMargin = 12;
    private const double BottomMargin = 26;
    private const double LabelSize = 10.5;

    private static readonly double[] ClockSteps = [50, 100, 200, 250, 500, 1000];
    private static readonly double[] VoltSteps = [10, 20, 25, 50, 100, 200];

    private bool _attached;

    public VoltageFrequencyChart()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public VoltageFrequencyPoint[]? Points
    {
        get => (VoltageFrequencyPoint[]?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>Current core clock in MHz (NaN hides the marker).</summary>
    public double CurrentClock
    {
        get => (double)GetValue(CurrentClockProperty);
        set => SetValue(CurrentClockProperty, value);
    }

    /// <summary>Current core voltage in volts (NaN hides the marker).</summary>
    public double CurrentVolts
    {
        get => (double)GetValue(CurrentVoltsProperty);
        set => SetValue(CurrentVoltsProperty, value);
    }

    public string ColorKey
    {
        get => (string)GetValue(ColorKeyProperty);
        set => SetValue(ColorKeyProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var points = Points;
        double width = ActualWidth;
        double height = ActualHeight;
        if (points is null || points.Length == 0 || width < 160 || height < 100)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var color = ChartColors.Series(ColorKey);

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.ClockMhz);
            maxX = Math.Max(maxX, p.ClockMhz);
            minY = Math.Min(minY, p.MinVolts * 1000);
            maxY = Math.Max(maxY, p.MaxVolts * 1000);
        }

        bool hasCurrent = double.IsFinite(CurrentClock) && double.IsFinite(CurrentVolts);
        if (hasCurrent)
        {
            minX = Math.Min(minX, CurrentClock);
            maxX = Math.Max(maxX, CurrentClock);
            minY = Math.Min(minY, CurrentVolts * 1000);
            maxY = Math.Max(maxY, CurrentVolts * 1000);
        }

        var (x0, x1, xStep) = Axis(minX - 10, maxX + 10, ClockSteps, 7, 300);
        var (y0, y1, yStep) = Axis(minY - 5, maxY + 5, VoltSteps, 6, 100);

        double plotLeft = LeftMargin;
        double plotRight = width - RightMargin;
        double plotTop = TopMargin;
        double plotBottom = height - BottomMargin;
        double X(double mhz) => plotLeft + ((mhz - x0) / (x1 - x0) * (plotRight - plotLeft));
        double Y(double millivolts) => plotBottom - ((millivolts - y0) / (y1 - y0) * (plotBottom - plotTop));

        var gridPen = ChartColors.Pen(ChartColors.Theme("ChartGrid"), 1, round: false);
        var axisBrush = ChartColors.Solid(ChartColors.Theme("ChartAxis"));

        for (double v = y0; v <= y1 + (yStep / 1000); v += yStep)
        {
            double y = Y(v);
            dc.DrawLine(gridPen, new Point(plotLeft, y), new Point(plotRight, y));
            bool top = v + yStep > y1 + (yStep / 1000);
            string text = top ? $"{v.ToString("0", CultureInfo.InvariantCulture)} mV" : v.ToString("0", CultureInfo.InvariantCulture);
            var label = Label(text, axisBrush, pixelsPerDip);
            dc.DrawText(label, new Point(plotLeft - 8 - label.Width, y - (label.Height / 2)));
        }

        for (double c = x0; c <= x1 + (xStep / 1000); c += xStep)
        {
            double x = X(c);
            dc.DrawLine(gridPen, new Point(x, plotTop), new Point(x, plotBottom));
            bool last = c + xStep > x1 + (xStep / 1000);
            string text = last ? $"{c.ToString("0", CultureInfo.InvariantCulture)} MHz" : c.ToString("0", CultureInfo.InvariantCulture);
            var label = Label(text, axisBrush, pixelsPerDip);
            double labelX = Math.Min(x - (label.Width / 2), width - label.Width - 2);
            dc.DrawText(label, new Point(Math.Max(labelX, 0), plotBottom + 6));
        }

        // Band between the lowest and highest voltage seen at each clock.
        if (points.Length > 1)
        {
            var band = new StreamGeometry();
            using (var ctx = band.Open())
            {
                ctx.BeginFigure(new Point(X(points[0].ClockMhz), Y(points[0].MaxVolts * 1000)), isFilled: true, isClosed: true);
                for (int i = 1; i < points.Length; i++)
                {
                    ctx.LineTo(new Point(X(points[i].ClockMhz), Y(points[i].MaxVolts * 1000)), isStroked: false, isSmoothJoin: false);
                }

                for (int i = points.Length - 1; i >= 0; i--)
                {
                    ctx.LineTo(new Point(X(points[i].ClockMhz), Y(points[i].MinVolts * 1000)), isStroked: false, isSmoothJoin: false);
                }
            }

            band.Freeze();
            dc.DrawGeometry(ChartColors.Solid(Color.FromArgb(0x38, color.R, color.G, color.B)), null, band);

            var mean = new StreamGeometry();
            using (var ctx = mean.Open())
            {
                ctx.BeginFigure(new Point(X(points[0].ClockMhz), Y(points[0].MeanVolts * 1000)), isFilled: false, isClosed: false);
                for (int i = 1; i < points.Length; i++)
                {
                    ctx.LineTo(new Point(X(points[i].ClockMhz), Y(points[i].MeanVolts * 1000)), isStroked: true, isSmoothJoin: false);
                }
            }

            mean.Freeze();
            dc.DrawGeometry(null, ChartColors.Pen(color, 2), mean);
        }

        if (points.Length <= 90)
        {
            var fill = ChartColors.Solid(color);
            foreach (var p in points)
            {
                dc.DrawEllipse(fill, null, new Point(X(p.ClockMhz), Y(p.MeanVolts * 1000)), 2.4, 2.4);
            }
        }

        if (hasCurrent)
        {
            var center = new Point(X(CurrentClock), Y(CurrentVolts * 1000));
            dc.DrawEllipse(ChartColors.Solid(ChartColors.Theme("Text")), ChartColors.Pen(color, 2.5), center, 5, 5);
            var now = Label("now", ChartColors.Solid(ChartColors.Theme("TextSecondary")), pixelsPerDip);
            double nowX = center.X + 9 + now.Width > plotRight ? center.X - 9 - now.Width : center.X + 9;
            dc.DrawText(now, new Point(nowX, center.Y - (now.Height / 2)));
        }
    }

    private static FormattedText Label(string text, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ChartFonts.MonoRegular, LabelSize, brush, null, TextFormattingMode.Display, pixelsPerDip);

    /// <summary>Rounds a range out to whole steps and picks the smallest step that keeps the tick count readable.</summary>
    private static (double Low, double High, double Step) Axis(double low, double high, double[] steps, int maxTicks, double minSpan)
    {
        if (high - low < minSpan)
        {
            double middle = (high + low) / 2;
            low = middle - (minSpan / 2);
            high = middle + (minSpan / 2);
        }

        double step = steps[^1];
        foreach (double candidate in steps)
        {
            if ((high - low) / candidate <= maxTicks)
            {
                step = candidate;
                break;
            }
        }

        return (Math.Floor(low / step) * step, Math.Ceiling(high / step) * step, step);
    }

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
