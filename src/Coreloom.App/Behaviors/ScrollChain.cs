using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Coreloom.App.Behaviors;

/// <summary>
/// For a scroll area inside the page (e.g. the Specifications card): once it reaches its top or bottom, the mouse
/// wheel scrolls the page instead of doing nothing. Set <c>b:ScrollChain.Enabled="True"</c> on the inner ScrollViewer.
/// </summary>
public static class ScrollChain
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollChain), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        if (e.NewValue is true)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer viewer)
        {
            return;
        }

        bool canScrollUp = viewer.VerticalOffset > 0.5;
        bool canScrollDown = viewer.VerticalOffset < viewer.ScrollableHeight - 0.5;
        if ((e.Delta > 0 && canScrollUp) || (e.Delta < 0 && canScrollDown))
        {
            return; // the inner area still has room: let it scroll
        }

        // Hand the wheel to whatever contains this scroll area (normally the page's ScrollViewer).
        e.Handled = true;
        if (VisualTreeHelper.GetParent(viewer) is UIElement parent)
        {
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = viewer,
            });
        }
    }
}
