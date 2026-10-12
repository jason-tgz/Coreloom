using System.Windows.Controls;
using System.Windows.Input;

namespace Vigil.App.Views;

// Code-behind for page views. Pages contain no logic beyond small input conveniences: everything is bound to the view models.

public partial class OverviewPage : UserControl
{
    public OverviewPage() => InitializeComponent();
}

public partial class GpuPage : UserControl
{
    public GpuPage() => InitializeComponent();
}

public partial class CpuPage : UserControl
{
    public CpuPage() => InitializeComponent();
}

public partial class MemoryPage : UserControl
{
    public MemoryPage() => InitializeComponent();
}

public partial class StoragePage : UserControl
{
    public StoragePage() => InitializeComponent();
}

public partial class NetworkPage : UserControl
{
    public NetworkPage() => InitializeComponent();
}

public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();
}

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    /// <summary>Text boxes commit on focus loss; Enter commits immediately too.</summary>
    private void OnCommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.SelectAll();
            e.Handled = true;
        }
    }
}
