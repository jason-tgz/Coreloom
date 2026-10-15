using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Coreloom.App.Controls;

namespace Coreloom.App.Behaviors;

/// <summary>How a dropdown lines up with the control it opens from.</summary>
public enum PopupAlign
{
    /// <summary>Left as it was (WPF's own placement).</summary>
    None,

    /// <summary>Its left edge on the control's left edge (a combo box's list).</summary>
    Start,

    /// <summary>Centered under the control (the quick search's results).</summary>
    Center,
}

/// <summary>
/// Keeps a dropdown attached to the control it belongs to. Set <c>b:PopupAnchor.Align</c> on the template's Popup and it is
/// placed directly under the control (or above it when there's no room below) at <c>b:PopupAnchor.Gap</c>, aligned the
/// same way whatever Windows' "right-handed" menu setting says (WPF's own Bottom placement lines the popup up with the
/// control's right edge under that setting, and ignores the margin left around the panel for its shadow). While it is
/// open, scrolling the page moves it with the control, and it closes once the control scrolls out of view.
/// </summary>
public static class PopupAnchor
{
    public static readonly DependencyProperty AlignProperty = DependencyProperty.RegisterAttached(
        "Align", typeof(PopupAlign), typeof(PopupAnchor), new PropertyMetadata(PopupAlign.None, OnAlignChanged));

    public static readonly DependencyProperty GapProperty = DependencyProperty.RegisterAttached(
        "Gap", typeof(double), typeof(PopupAnchor), new PropertyMetadata(4.0));

    private static readonly List<WeakReference<Popup>> Open = [];

    public static PopupAlign GetAlign(DependencyObject element) => (PopupAlign)element.GetValue(AlignProperty);

    public static void SetAlign(DependencyObject element, PopupAlign value) => element.SetValue(AlignProperty, value);

    /// <summary>Space between the control and the panel, in DIPs.</summary>
    public static double GetGap(DependencyObject element) => (double)element.GetValue(GapProperty);

    public static void SetGap(DependencyObject element, double value) => element.SetValue(GapProperty, value);

    private static void OnAlignChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup)
        {
            return;
        }

        popup.Opened -= OnOpened;
        popup.Closed -= OnClosed;
        if ((PopupAlign)e.NewValue == PopupAlign.None)
        {
            return;
        }

        popup.Placement = PlacementMode.Custom;
        popup.CustomPopupPlacementCallback = (popupSize, targetSize, _) => Place(popup, popupSize, targetSize);
        popup.Opened += OnOpened;
        popup.Closed += OnClosed;
    }

    /// <summary>
    /// Where the popup goes, below first and above as the fallback. WPF passes the panel's and the control's sizes in screen
    /// pixels and moves the popup window's top-left corner by the point returned, measured from the control's top-left;
    /// the panel sits inside that window at its margin.
    /// </summary>
    internal static CustomPopupPlacement[] Place(Popup popup, Size popupSize, Size targetSize)
    {
        double scale = popup.Child is Visual child ? VisualTreeHelper.GetDpi(child).DpiScaleX : 1;
        var margin = popup.Child is FrameworkElement element ? element.Margin : default;
        double gap = GetGap(popup) * scale;
        double x = GetAlign(popup) == PopupAlign.Center ? (targetSize.Width - popupSize.Width) / 2 : 0;
        x -= margin.Left * scale;
        double top = margin.Top * scale;
        return
        [
            new CustomPopupPlacement(new Point(x, targetSize.Height + gap - top), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, -gap - popupSize.Height - top), PopupPrimaryAxis.Horizontal),
        ];
    }

    private static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is Popup popup)
        {
            Open.RemoveAll(r => !r.TryGetTarget(out var p) || ReferenceEquals(p, popup));
            Open.Add(new WeakReference<Popup>(popup));
        }
    }

    private static void OnClosed(object? sender, EventArgs e) =>
        Open.RemoveAll(r => !r.TryGetTarget(out var p) || ReferenceEquals(p, sender));

    /// <summary>
    /// The page in <paramref name="viewer"/> scrolled: open dropdowns of controls on it follow their control, or close when
    /// it has scrolled out of view.
    /// </summary>
    internal static void OnScrolled(ScrollViewer viewer)
    {
        if (Open.Count == 0)
        {
            return;
        }

        foreach (var reference in Open.ToArray())
        {
            if (!reference.TryGetTarget(out var popup) || !popup.IsOpen || !viewer.IsAncestorOf(popup))
            {
                continue;
            }

            if (InView(viewer, (popup.PlacementTarget ?? VisualTreeHelper.GetParent(popup)) as FrameworkElement))
            {
                // Changing an offset makes WPF place the popup again, next to where its control is now.
                double offset = popup.HorizontalOffset;
                popup.HorizontalOffset = offset + 0.01;
                popup.HorizontalOffset = offset;
            }
            else
            {
                Close(popup);
            }
        }
    }

    private static bool InView(ScrollViewer viewer, FrameworkElement? anchor)
    {
        if (anchor is null || !viewer.IsAncestorOf(anchor))
        {
            return true;
        }

        try
        {
            var bounds = anchor.TransformToAncestor(viewer).TransformBounds(new Rect(anchor.RenderSize));
            return bounds.Bottom > 0 && bounds.Top < viewer.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void Close(Popup popup)
    {
        switch (popup.TemplatedParent)
        {
            case ComboBox combo:
                combo.IsDropDownOpen = false;
                break;
            case QuickSearch search:
                search.IsDropDownOpen = false;
                break;
            default:
                popup.IsOpen = false;
                break;
        }
    }
}
