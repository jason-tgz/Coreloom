using System.Windows;
using System.Windows.Controls;

namespace Vigil.App.Controls;

/// <summary>
/// Responsive grid: fits as many equal-width columns as possible with each at least
/// <see cref="MinColumnWidth"/> wide (capped at <see cref="MaxColumns"/>), and stretches items to fill
/// the row. Row height is the tallest item in the row. Used for all card grids.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty UniformRowHeightProperty = DependencyProperty.Register(
        nameof(UniformRowHeight), typeof(bool), typeof(AdaptiveGrid),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private double[] _rowHeights = [];
    private int _columns = 1;
    private double _columnWidth;

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>When true every row uses the height of the tallest item overall.</summary>
    public bool UniformRowHeight
    {
        get => (bool)GetValue(UniformRowHeightProperty);
        set => SetValue(UniformRowHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double width = double.IsInfinity(availableSize.Width) ? (MinColumnWidth * Math.Max(1, Math.Min(MaxColumns, visible.Count))) + (Spacing * Math.Max(0, Math.Min(MaxColumns, visible.Count) - 1)) : availableSize.Width;
        double spacing = Spacing;

        int columns = (int)Math.Floor((width + spacing) / (Math.Max(1, MinColumnWidth) + spacing));
        columns = Math.Clamp(columns, 1, Math.Max(1, MaxColumns));
        if (visible.Count > 0)
        {
            columns = Math.Min(columns, visible.Count);
        }

        _columns = columns;
        _columnWidth = Math.Max(0, (width - (spacing * (columns - 1))) / columns);

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_columnWidth, double.PositiveInfinity));
        }

        int rows = visible.Count == 0 ? 0 : (int)Math.Ceiling(visible.Count / (double)columns);
        _rowHeights = new double[rows];
        for (int i = 0; i < visible.Count; i++)
        {
            int row = i / columns;
            _rowHeights[row] = Math.Max(_rowHeights[row], visible[i].DesiredSize.Height);
        }

        if (UniformRowHeight && rows > 0)
        {
            double max = _rowHeights.Max();
            Array.Fill(_rowHeights, max);
        }

        double height = _rowHeights.Sum() + (spacing * Math.Max(0, rows - 1));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double spacing = Spacing;
        double columnWidth = Math.Max(0, (finalSize.Width - (spacing * (_columns - 1))) / _columns);
        double y = 0;
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            int row = index / _columns;
            int column = index % _columns;
            if (column == 0 && row > 0)
            {
                y += _rowHeights[row - 1] + spacing;
            }

            double height = row < _rowHeights.Length ? _rowHeights[row] : child.DesiredSize.Height;
            child.Arrange(new Rect(column * (columnWidth + spacing), y, columnWidth, height));
            index++;
        }

        return finalSize;
    }
}
