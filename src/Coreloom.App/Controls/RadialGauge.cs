using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coreloom.App.Behaviors;
using Coreloom.App.Themes;
using Coreloom.Core.Formatting;

namespace Coreloom.App.Controls;

/// <summary>How a <see cref="RadialGauge"/>'s arc is colored along its length.</summary>
public enum GaugeGradient
{
    /// <summary>One color (<see cref="RadialGauge.Fill"/> or the scale's).</summary>
    None,

    /// <summary>The accent sweeping from its cooler, lighter neighbour through the accent to its warmer one.</summary>
    Accent,

    /// <summary>The arc's color, starting a shade lighter.</summary>
    Shade,
}

/// <summary>
/// 270° ring gauge with rounded caps. <see cref="Value"/> is a 0–1 fraction; changes ease over ~450 ms
/// at the display refresh rate unless reduced motion is on. With <see cref="ToneScale"/> the arc takes its color from the
/// shared four-color scale (green idle or cool, blue normal, orange heavy, red at the limit; see <see cref="GaugeTone"/>),
/// worked out from the animated value so the color glides along with the arc; otherwise it is drawn with <see cref="Fill"/>.
/// </summary>
public sealed class RadialGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RadialGauge), new PropertyMetadata(0.0, OnValueChanged));

    private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        "DisplayValue", typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RadialGauge), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(RadialGauge), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SweepAngleProperty = DependencyProperty.Register(
        nameof(SweepAngle), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(270.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsUnavailableProperty = DependencyProperty.Register(
        nameof(IsUnavailable), typeof(bool), typeof(RadialGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ToneScaleProperty = DependencyProperty.Register(
        nameof(ToneScale), typeof(bool), typeof(RadialGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CoolAtProperty = DependencyProperty.Register(
        nameof(CoolAt), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WarmAtProperty = DependencyProperty.Register(
        nameof(WarmAt), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HotAtProperty = DependencyProperty.Register(
        nameof(HotAt), typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>A fixed position on the color scale (0 green … 3 red) instead of one worked out from the value.</summary>
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(double), typeof(RadialGauge), new PropertyMetadata(double.NaN, OnToneChanged));

    private static readonly DependencyProperty DisplayToneProperty = DependencyProperty.Register(
        "DisplayTone", typeof(double), typeof(RadialGauge), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GradientProperty = DependencyProperty.Register(
        nameof(Gradient), typeof(GaugeGradient), typeof(RadialGauge), new FrameworkPropertyMetadata(GaugeGradient.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FadeTailProperty = DependencyProperty.Register(
        nameof(FadeTail), typeof(bool), typeof(RadialGauge), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _themeHooked;

    public RadialGauge()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => HookTheme(true);
        Unloaded += (_, _) => HookTheme(false);
    }

    /// <summary>
    /// Colors the arc along its length. The colors are fixed to positions on the ring (not to the arc's end), so the
    /// gradient stays put while the arc grows.
    /// </summary>
    public GaugeGradient Gradient
    {
        get => (GaugeGradient)GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
    }

    /// <summary>The arc fades out toward its start, like a comet's tail (for a short spinning arc).</summary>
    public bool FadeTail
    {
        get => (bool)GetValue(FadeTailProperty);
        set => SetValue(FadeTailProperty, value);
    }

    /// <summary>Color the arc from the four-color scale instead of <see cref="Fill"/>.</summary>
    public bool ToneScale
    {
        get => (bool)GetValue(ToneScaleProperty);
        set => SetValue(ToneScaleProperty, value);
    }

    /// <summary>
    /// Sets the color directly (for ratings where more is better, like a speed): 0 green, 1 blue, 2 orange, 3 red.
    /// Changes glide like the value does. NaN (the default) colors by the value and the stops instead.
    /// </summary>
    public double Tone
    {
        get => (double)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>Gauge fraction where green turns blue (default 0.30).</summary>
    public double CoolAt
    {
        get => (double)GetValue(CoolAtProperty);
        set => SetValue(CoolAtProperty, value);
    }

    /// <summary>Gauge fraction where blue turns orange (default 0.80).</summary>
    public double WarmAt
    {
        get => (double)GetValue(WarmAtProperty);
        set => SetValue(WarmAtProperty, value);
    }

    /// <summary>Gauge fraction where the arc is fully red (default 0.95).</summary>
    public double HotAt
    {
        get => (double)GetValue(HotAtProperty);
        set => SetValue(HotAtProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double SweepAngle
    {
        get => (double)GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    /// <summary>Draws only the track (value unavailable).</summary>
    public bool IsUnavailable
    {
        get => (bool)GetValue(IsUnavailableProperty);
        set => SetValue(IsUnavailableProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (RadialGauge)d;
        double target = Math.Clamp(double.IsFinite((double)e.NewValue) ? (double)e.NewValue : 0, 0, 1);
        if (!AnimatedText.IsGloballyEnabled || !gauge.IsVisible)
        {
            gauge.BeginAnimation(DisplayValueProperty, null);
            gauge.SetValue(DisplayValueProperty, target);
            return;
        }

        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        gauge.BeginAnimation(DisplayValueProperty, animation);
    }

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (RadialGauge)d;
        double target = (double)e.NewValue;
        double current = (double)gauge.GetValue(DisplayToneProperty);
        if (!double.IsFinite(target) || !double.IsFinite(current) || !AnimatedText.IsGloballyEnabled || !gauge.IsVisible)
        {
            gauge.BeginAnimation(DisplayToneProperty, null);
            gauge.SetValue(DisplayToneProperty, target);
            return;
        }

        gauge.BeginAnimation(DisplayToneProperty, new DoubleAnimation(Math.Clamp(target, 0, 3), TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8)
        {
            return;
        }

        double thickness = Math.Min(Thickness, size / 4);
        double radius = (size - thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double sweep = Math.Clamp(SweepAngle, 10, 359);
        double start = 90 + ((360 - sweep) / 2); // opening centered at the bottom

        dc.DrawGeometry(null, RoundPen(TrackBrush, thickness), Arc(center, radius, start, sweep));

        double value = (double)GetValue(DisplayValueProperty);
        if (IsUnavailable || value <= 0.002)
        {
            return;
        }

        double tone = (double)GetValue(DisplayToneProperty);
        double arcSweep = sweep * Math.Clamp(value, 0, 1);
        var gradient = Gradient;
        if (gradient == GaugeGradient.None && !FadeTail)
        {
            var brush = !ToneScale ? Fill
                : double.IsFinite(tone) ? ChartColors.Solid(ScaleColor(tone))
                : ChartColors.Solid(ToneColor(value));
            dc.DrawGeometry(null, RoundPen(brush, thickness), Arc(center, radius, start, arcSweep));
            return;
        }

        Color color = ToneScale ? (double.IsFinite(tone) ? ScaleColor(tone) : ToneColor(value))
            : Fill is SolidColorBrush solid ? solid.Color
            : Colors.DodgerBlue;
        if (FadeTail)
        {
            DrawTail(dc, center, radius, thickness, start, arcSweep, gradient == GaugeGradient.None ? color : Lighter(color));
            return;
        }

        Func<double, Color> colorAt;
        if (gradient == GaugeGradient.Accent)
        {
            var accent = ThemeService.Instance?.AccentColor ?? color;
            var (cool, warm) = ThemeService.AccentNeighbours(accent);
            colorAt = t => t < 0.5 ? Lerp(cool, accent, t * 2) : Lerp(accent, warm, (t - 0.5) * 2);
        }
        else
        {
            var light = Lighter(color);
            colorAt = t => Lerp(light, color, t);
        }

        DrawSweep(dc, center, radius, thickness, start, sweep, arcSweep, colorAt);
    }

    private static Color Lighter(Color color) => ThemePalette.Shift(color, 0, 0.14);

    /// <summary>
    /// Draws the arc as short pieces, each in the color for its place on the ring (a sweep gradient, which WPF doesn't
    /// have). Neighbouring pieces overlap a hair so no seam shows; only the two ends are rounded.
    /// </summary>
    private static void DrawSweep(DrawingContext dc, Point center, double radius, double thickness, double start, double fullSweep, double arcSweep, Func<double, Color> colorAt)
    {
        int pieces = Math.Clamp((int)Math.Ceiling(arcSweep / 3.0), 1, 120);
        double step = arcSweep / pieces;
        for (int i = 0; i < pieces; i++)
        {
            bool last = i == pieces - 1;
            var color = colorAt(Math.Clamp(((i + 0.5) * step) / fullSweep, 0, 1));
            var pen = new Pen(new SolidColorBrush(color), thickness)
            {
                StartLineCap = i == 0 ? PenLineCap.Round : PenLineCap.Flat,
                EndLineCap = last ? PenLineCap.Round : PenLineCap.Flat,
            };
            pen.Freeze();
            double overlap = !last && color.A == 255 ? 0.6 : 0;
            dc.DrawGeometry(null, pen, Arc(center, radius, start + (i * step), step + overlap));
        }
    }

    /// <summary>A short arc fading from clear at its start to <paramref name="color"/> at its head, in one piece.</summary>
    private static void DrawTail(DrawingContext dc, Point center, double radius, double thickness, double start, double arcSweep, Color color)
    {
        double a0 = start * Math.PI / 180;
        double a1 = (start + arcSweep) * Math.PI / 180;
        var tail = new Point(center.X + (radius * Math.Cos(a0)), center.Y + (radius * Math.Sin(a0)));
        var head = new Point(center.X + (radius * Math.Cos(a1)), center.Y + (radius * Math.Sin(a1)));
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = tail,
            EndPoint = head,
            GradientStops = { new GradientStop(ThemePalette.WithAlpha(color, 0), 0), new GradientStop(color, 1) },
        };
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, Arc(center, radius, start, arcSweep));
    }

    /// <summary>The scale's color for a gauge fraction, blended between the theme's success, info, elevated and critical colors.</summary>
    private Color ToneColor(double fraction)
    {
        double hot = Valid(HotAt, 0.95);
        double warm = Math.Min(Valid(WarmAt, 0.80), hot);
        double cool = Math.Min(Valid(CoolAt, Math.Min(0.30, warm * 0.5)), warm);
        return ScaleColor(GaugeTone.At(fraction, cool, warm, hot));

        static double Valid(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
    }

    /// <summary>The color at a position on the scale (0 green … 3 red), blended between neighbours.</summary>
    internal static Color ScaleColor(double position)
    {
        position = Math.Clamp(position, 0, 3);
        int band = Math.Clamp((int)Math.Floor(position), 0, 2);
        return Lerp(Stop(band), Stop(band + 1), Math.Clamp(position - band, 0, 1));
    }

    private static Color Stop(int index) => ChartColors.Theme(index switch
    {
        0 => "Success",
        1 => "Info",
        2 => "Elevated",
        _ => "Critical",
    });

    private static Color Lerp(Color a, Color b, double t)
    {
        if (t <= 0)
        {
            return a;
        }

        if (t >= 1)
        {
            return b;
        }

        return Color.FromArgb(
            (byte)Math.Round(a.A + ((b.A - a.A) * t)),
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    /// <summary>Redraws with the new palette when the theme changes; subscribed only while the gauge is on screen.</summary>
    private void HookTheme(bool hook)
    {
        if (hook == _themeHooked || ThemeService.Instance is not { } theme)
        {
            return;
        }

        _themeHooked = hook;
        if (hook)
        {
            theme.ThemeChanged += OnThemeChanged;
        }
        else
        {
            theme.ThemeChanged -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>
    /// A round-capped pen, frozen when its brush allows it. Frozen pens are cheaper to render and do not register
    /// change handlers on the (shared, theme-owned) brush.
    /// </summary>
    private static Pen RoundPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    internal static Geometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        double a0 = startDegrees * Math.PI / 180;
        double a1 = (startDegrees + sweepDegrees) * Math.PI / 180;
        var p0 = new Point(center.X + (radius * Math.Cos(a0)), center.Y + (radius * Math.Sin(a0)));
        var p1 = new Point(center.X + (radius * Math.Cos(a1)), center.Y + (radius * Math.Sin(a1)));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(p0, false, false);
            ctx.ArcTo(p1, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }
}
