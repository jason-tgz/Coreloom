using System.Windows;
using System.Windows.Controls;
using Coreloom.Core.Dashboard;

namespace Coreloom.App.Controls;

/// <summary>
/// Columns of cards that stack: items of different heights (such as groups of devices) go under whichever column is
/// shortest so far, so they pack tightly instead of each starting a new row. Columns are worked out exactly like
/// <see cref="AdaptiveGrid"/>'s, so with the same widths and spacing the two line up on one grid.
/// </summary>
public sealed class StackedColumns : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(StackedColumns),
        new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnWidthProperty = DependencyProperty.Register(
        nameof(MaxColumnWidth), typeof(double), typeof(StackedColumns),
        new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(StackedColumns),
        new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(StackedColumns),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing), typeof(double), typeof(StackedColumns),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private IReadOnlyList<StackPlacement> _placements = [];
    private int _columns = 1;

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>Widest a column may get; columns keep this width (or less) and line up from the left.</summary>
    public double MaxColumnWidth
    {
        get => (double)GetValue(MaxColumnWidthProperty);
        set => SetValue(MaxColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>Gap between columns.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Gap between items stacked in a column.</summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Visible();
        double width = double.IsInfinity(availableSize.Width)
            ? (MinColumnWidth * Math.Max(1, Math.Min(MaxColumns, visible.Count))) + (Spacing * Math.Max(0, Math.Min(MaxColumns, visible.Count) - 1))
            : availableSize.Width;

        var (columns, columnWidth) = CardColumns.Fit(width, Spacing, MinColumnWidth, MaxColumnWidth, MaxColumns, visible.Count);
        _columns = columns;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
        }

        _placements = CardColumns.Stack(visible.Select(c => c.DesiredSize.Height).ToList(), columns, RowSpacing, out double height);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double columnWidth = CardColumns.ColumnWidth(finalSize.Width, Spacing, _columns, MaxColumnWidth);
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed || index >= _placements.Count)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var place = _placements[index++];
            child.Arrange(new Rect(place.Column * (columnWidth + Spacing), place.Top, columnWidth, child.DesiredSize.Height));
        }

        return finalSize;
    }

    private List<UIElement> Visible() => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
}
