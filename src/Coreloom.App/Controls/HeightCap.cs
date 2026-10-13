using System.Windows;
using System.Windows.Controls;

namespace Coreloom.App.Controls;

/// <summary>
/// Asks for at most <see cref="Cap"/> height when its container has no limit, but fills whatever height it is given. Lets
/// a scrolling list in a dashboard card start at a sensible height and grow when the card is made taller.
/// </summary>
public sealed class HeightCap : Decorator
{
    public static readonly DependencyProperty CapProperty = DependencyProperty.Register(
        nameof(Cap), typeof(double), typeof(HeightCap), new FrameworkPropertyMetadata(250.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Cap
    {
        get => (double)GetValue(CapProperty);
        set => SetValue(CapProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
        {
            return default;
        }

        double height = double.IsPositiveInfinity(constraint.Height) ? Cap : constraint.Height;
        Child.Measure(new Size(constraint.Width, height));
        return Child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}
