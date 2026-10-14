using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Coreloom.App.Behaviors;

/// <summary>
/// Glides a progress bar or slider to a new value instead of jumping (Value is animated; the binding target is this
/// attached property). Instant with reduced motion.
/// </summary>
public static class Smooth
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(double), typeof(Smooth), new PropertyMetadata(0.0, OnValueChanged));

    public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);

    public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RangeBase range)
        {
            return;
        }

        double target = Math.Clamp(double.IsFinite((double)e.NewValue) ? (double)e.NewValue : 0, range.Minimum, range.Maximum);
        bool backwards = target < range.Value - ((range.Maximum - range.Minimum) * 0.001);
        if (Motion.ReducedMotion || !AnimatedText.IsGloballyEnabled || !range.IsVisible || backwards)
        {
            range.BeginAnimation(RangeBase.ValueProperty, null); // resets drop straight back
            range.Value = target;
            return;
        }

        range.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }
}

/// <summary>Turns an element round and round while <c>IsSpinning</c> is true (the scanning ring), easing to a stop.</summary>
public static class Spin
{
    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.RegisterAttached(
        "IsSpinning", typeof(bool), typeof(Spin), new PropertyMetadata(false, OnChanged));

    public static bool GetIsSpinning(DependencyObject element) => (bool)element.GetValue(IsSpinningProperty);

    public static void SetIsSpinning(DependencyObject element, bool value) => element.SetValue(IsSpinningProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (element.RenderTransform is not RotateTransform rotate || rotate.IsFrozen)
        {
            rotate = new RotateTransform();
            element.RenderTransform = rotate;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        if ((bool)e.NewValue && !Motion.ReducedMotion)
        {
            double from = rotate.Angle % 360;
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(from, from + 360, TimeSpan.FromSeconds(1.3))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });
        }
        else
        {
            double at = rotate.Angle % 360;
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = at;
            if (!Motion.ReducedMotion)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(at, at + 90, TimeSpan.FromMilliseconds(500))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd,
                });
            }
        }
    }
}

/// <summary>Breathes an element's opacity while <c>IsPulsing</c> is true (the test running now); back to full when it stops.</summary>
public static class Pulse
{
    public static readonly DependencyProperty IsPulsingProperty = DependencyProperty.RegisterAttached(
        "IsPulsing", typeof(bool), typeof(Pulse), new PropertyMetadata(false, OnChanged));

    public static bool GetIsPulsing(DependencyObject element) => (bool)element.GetValue(IsPulsingProperty);

    public static void SetIsPulsing(DependencyObject element, bool value) => element.SetValue(IsPulsingProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue && !Motion.ReducedMotion)
        {
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(900))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
        else
        {
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { FillBehavior = FillBehavior.Stop });
            element.Opacity = 1;
        }
    }
}

/// <summary>A small "pop" (scale up from 85 % with a soft overshoot and fade in) whenever <c>Trigger</c> changes to true.</summary>
public static class Pop
{
    public static readonly DependencyProperty TriggerProperty = DependencyProperty.RegisterAttached(
        "Trigger", typeof(bool), typeof(Pop), new PropertyMetadata(false, OnChanged));

    public static bool GetTrigger(DependencyObject element) => (bool)element.GetValue(TriggerProperty);

    public static void SetTrigger(DependencyObject element, bool value) => element.SetValue(TriggerProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || !(bool)e.NewValue || Motion.ReducedMotion)
        {
            return;
        }

        if (element.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransform = scale;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        var grow = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { FillBehavior = FillBehavior.Stop });
    }
}
