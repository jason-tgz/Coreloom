using System.Windows;
using System.Windows.Controls;

namespace Coreloom.App.Controls;

/// <summary>
/// Hosts the current page view and keeps one view instance per page view model, so navigating back to a
/// page is instant (no template re-instantiation, no re-creation of dozens of charts). Views are created from
/// the DataTemplate registered for the view model's type, exactly as a ContentControl would; they are simply
/// not thrown away. Removing a view from the tree still raises Unloaded, so charts detach their event handlers
/// while hidden and re-attach on Loaded.
/// </summary>
public sealed class PageHost : ContentControl
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(object), typeof(PageHost), new PropertyMetadata(null, OnPageChanged));

    private readonly Dictionary<object, FrameworkElement> _views = new(ReferenceEqualityComparer.Instance);

    public PageHost()
    {
        Focusable = false;
        IsTabStop = false;
    }

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (PageHost)d;
        object? page = e.NewValue;
        if (page is null)
        {
            host.Content = null;
            return;
        }

        if (!host._views.TryGetValue(page, out var view))
        {
            view = host.CreateView(page);
            if (view is null)
            {
                host.Content = page; // no template: let the ContentControl show whatever it can
                return;
            }

            view.DataContext = page;
            host._views[page] = view;
        }

        host.Content = view;
    }

    private FrameworkElement? CreateView(object page)
    {
        var template = TryFindResource(new DataTemplateKey(page.GetType())) as DataTemplate;
        return template?.LoadContent() as FrameworkElement;
    }
}
