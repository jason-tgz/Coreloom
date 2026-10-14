using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coreloom.App.Behaviors;
using Coreloom.App.Themes;
using Coreloom.ViewModels.Infrastructure;

namespace Coreloom.App.Controls;

/// <summary>
/// Animated donut chart for <see cref="DonutSegmentViewModel"/> items. Segments sweep in on first display
/// and ease between proportions when values change.
/// </summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IEnumerable), typeof(DonutChart), new PropertyMetadata(null, OnSegmentsChanged));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(DonutChart), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapDegreesProperty = DependencyProperty.Register(
        nameof(GapDegrees), typeof(double), typeof(DonutChart), new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(DonutChart), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<DonutSegmentViewModel> _items = [];
    private double[] _from = [];
    private double[] _to = [];
    private INotifyCollectionChanged? _collection;
    private bool _hasShown;
    private bool _attached;

    public DonutChart()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public IEnumerable? Segments
    {
        get => (IEnumerable?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public double GapDegrees
    {
        get => (double)GetValue(GapDegreesProperty);
        set => SetValue(GapDegreesProperty, value);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var donut = (DonutChart)d;
        if (donut._attached)
        {
            donut.Detach();
            donut.Attach();
        }
    }

    /// <summary>
    /// Hooks the data source and theme events. Subscriptions exist only while the chart is in the visual tree, so a
    /// cached page that is not on screen holds no event handlers from (or to) its charts.
    /// </summary>
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

        _collection = Segments as INotifyCollectionChanged;
        if (_collection is not null)
        {
            _collection.CollectionChanged += OnCollectionChanged;
        }

        ResubscribeItems();
        Retarget();
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

        if (_collection is not null)
        {
            _collection.CollectionChanged -= OnCollectionChanged;
            _collection = null;
        }

        foreach (var item in _items)
        {
            item.PropertyChanged -= OnItemChanged;
        }

        _items.Clear();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResubscribeItems();
        Retarget();
    }

    private void ResubscribeItems()
    {
        foreach (var item in _items)
        {
            item.PropertyChanged -= OnItemChanged;
        }

        _items.Clear();
        if (Segments is not null)
        {
            foreach (var item in Segments.OfType<DonutSegmentViewModel>())
            {
                item.PropertyChanged += OnItemChanged;
                _items.Add(item);
            }
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DonutSegmentViewModel.Value))
        {
            Retarget();
        }
        else if (e.PropertyName == nameof(DonutSegmentViewModel.ColorKey))
        {
            InvalidateVisual();
        }
    }

    private void Retarget()
    {
        int count = _items.Count;
        double total = _items.Sum(i => Math.Max(0, i.Value));
        var target = new double[count];
        for (int i = 0; i < count; i++)
        {
            target[i] = total > 0 ? Math.Max(0, _items[i].Value) / total : 0;
        }

        var current = CurrentFractions(count);
        if (target.SequenceEqual(_to) && _hasShown)
        {
            return;
        }

        _from = current;
        _to = target;
        bool animate = AnimatedText.IsGloballyEnabled && IsVisible && total > 0;
        if (!animate)
        {
            BeginAnimation(ProgressProperty, null);
            SetValue(ProgressProperty, 1.0);
        }
        else
        {
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(_hasShown ? 450 : 700))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            BeginAnimation(ProgressProperty, animation);
        }

        _hasShown |= total > 0;
        InvalidateVisual();
    }

    private double[] CurrentFractions(int count)
    {
        double p = (double)GetValue(ProgressProperty);
        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            double from = i < _from.Length ? _from[i] : 0;
            double to = i < _to.Length ? _to[i] : 0;
            result[i] = from + ((to - from) * p);
        }

        return result;
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
        var trackColor = ChartColors.Theme("Track");
        dc.DrawEllipse(null, ChartColors.Pen(trackColor, thickness, round: false), center, radius, radius);

        var fractions = CurrentFractions(_items.Count);
        int nonZero = fractions.Count(f => f > 0.0005);
        double gap = nonZero > 1 ? GapDegrees : 0;
        double angle = -90;
        for (int i = 0; i < _items.Count; i++)
        {
            double sweep = fractions[i] * 360;
            if (sweep <= 0.05)
            {
                continue;
            }

            var pen = ChartColors.Pen(ChartColors.Series(_items[i].ColorKey), thickness, round: false);
            if (sweep >= 359.9)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
            }
            else
            {
                double drawn = Math.Max(0.1, sweep - gap);
                dc.DrawGeometry(null, pen, RadialGauge.Arc(center, radius, angle + (gap / 2), drawn));
            }

            angle += sweep;
        }
    }
}
