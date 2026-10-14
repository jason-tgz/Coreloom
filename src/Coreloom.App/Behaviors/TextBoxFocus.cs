using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Coreloom.App.Behaviors;

/// <summary>
/// Lets people leave a text box the way they expect. On its own, WPF keeps the keyboard in a text box until another
/// control that takes focus is clicked, so clicking on a card or empty space leaves the caret blinking and typing still
/// goes into the box. With this attached to a window, clicking anywhere outside the box, or pressing Esc in it, moves the
/// focus to the window: the caret goes away and what was typed is kept (and committed to its binding).
/// </summary>
public static class TextBoxFocus
{
    public static void Attach(Window window)
    {
        window.PreviewMouseDown += OnPreviewMouseDown;
        window.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Window window || Keyboard.FocusedElement is not TextBoxBase box || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        // Clicks in the box itself, and in pop-ups (its right-click menu, a drop-down), are left alone.
        if (IsWithin(source, box) || (PresentationSource.FromDependencyObject(source) is { } popup && popup != PresentationSource.FromVisual(window)))
        {
            return;
        }

        Leave(window, box);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is Window window && Keyboard.FocusedElement is TextBoxBase box)
        {
            Leave(window, box);
            e.Handled = true;
        }
    }

    private static void Leave(Window window, TextBoxBase box)
    {
        // The window takes the focus (so keyboard shortcuts keep working); if it can't, nothing has it, and the box isn't
        // remembered as the place to return to when the window is activated again.
        if (!window.Focus())
        {
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(box), null);
            Keyboard.ClearFocus();
        }
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor))
            {
                return true;
            }

            element = element is Visual or Visual3D
                ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element) ?? (element is ContentElement content ? ContentOperations.GetParent(content) : null);
        }

        return false;
    }
}
