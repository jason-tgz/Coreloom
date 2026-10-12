using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Vigil.Core.Diagnostics;

namespace Vigil.App.Services;

/// <summary>
/// Notification-area icon with a themed WPF context menu (Open, Pause/Resume, Settings, Check for updates,
/// Exit) and balloon notifications (shown as toasts on Windows 10/11). Uses WinForms' NotifyIcon, which
/// handles Explorer restarts and DPI changes reliably.
/// </summary>
public sealed partial class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _pauseItem;
    private Action? _balloonAction;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    public TrayIconService(Action open, Action togglePause, Action openSettings, Action checkUpdates, Action exit)
    {
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Vigil",
            Visible = true,
        };

        _menu = new ContextMenu();
        _menu.Items.Add(CreateItem("Open dashboard", open, bold: true));
        _pauseItem = CreateItem("Pause monitoring", togglePause);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CreateItem("Settings", openSettings));
        _menu.Items.Add(CreateItem("Check for updates", checkUpdates));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(CreateItem("Exit Vigil", exit));

        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                ShowMenu();
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                open();
            }
        };
        _icon.BalloonTipClicked += (_, _) => (_balloonAction ?? open)();
    }

    public void SetTooltip(string text)
    {
        // NotifyIcon.Text is limited to 127 characters.
        string trimmed = string.IsNullOrWhiteSpace(text) ? "Vigil" : text.Length > 127 ? text[..127] : text;
        if (_icon.Text != trimmed)
        {
            _icon.Text = trimmed;
        }
    }

    public void SetPaused(bool paused) => _pauseItem.Header = paused ? "Resume monitoring" : "Pause monitoring";

    public void Notify(string title, string message, bool warning = false, Action? onClick = null)
    {
        try
        {
            _balloonAction = onClick;
            _icon.ShowBalloonTip(8000, title, message, warning ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warn("Tray", "Showing a notification failed.", ex);
        }
    }

    private void ShowMenu()
    {
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;
        // Bring the menu's popup to the foreground so clicking elsewhere closes it.
        if (PresentationSource.FromVisual(_menu) is HwndSource source)
        {
            SetForegroundWindow(source.Handle);
        }
    }

    private static MenuItem CreateItem(string header, Action action, bool bold = false)
    {
        var item = new MenuItem { Header = header };
        if (bold)
        {
            item.FontWeight = FontWeights.SemiBold;
        }

        item.Click += (_, _) => action();
        return item;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/vigil.ico", UriKind.Absolute));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Tray", "Loading the tray icon failed; using the application icon.", ex);
        }

        return System.Drawing.Icon.ExtractAssociatedIcon(LifetimeService.ExecutablePath) ?? System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
