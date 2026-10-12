using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Vigil.App.Interop;

/// <summary>
/// Makes Vigil's custom maximize button behave like a native one on Windows 11: hovering it shows the
/// Snap Layouts flyout. Implemented by reporting HTMAXBUTTON for the button's bounds and handling the
/// resulting non-client mouse messages. If anything here fails, the button keeps working as a normal
/// WPF button (no snap flyout).
/// </summary>
internal sealed partial class SnapLayoutSupport
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcMouseLeave = 0x02A2;
    private const int WmMouseMove = 0x0200;
    private const int HtMaxButton = 9;
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;

    private readonly Window _window;
    private readonly Button _button;
    private bool _pressed;

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr HwndTrack;
        public uint HoverTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TrackMouseEvent(ref TrackMouseEventInfo info);

    private SnapLayoutSupport(Window window, Button button)
    {
        _window = window;
        _button = button;
    }

    /// <summary>Attaches to the window (call after the window's handle exists, e.g. on Loaded).</summary>
    public static void Attach(Window window, Button maximizeButton)
    {
        if (!NativeWindow.IsWindows11)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || HwndSource.FromHwnd(hwnd) is not { } source)
        {
            return;
        }

        var support = new SnapLayoutSupport(window, maximizeButton);
        source.AddHook(support.WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            switch (msg)
            {
                case WmNcHitTest:
                    if (IsOverButton(lParam))
                    {
                        handled = true;
                        return new IntPtr(HtMaxButton);
                    }

                    break;

                case WmNcMouseMove:
                    if (wParam.ToInt32() == HtMaxButton)
                    {
                        SetHover(true);
                        var info = new TrackMouseEventInfo { Size = (uint)Marshal.SizeOf<TrackMouseEventInfo>(), Flags = TmeLeave | TmeNonClient, HwndTrack = hwnd };
                        TrackMouseEvent(ref info);
                    }
                    else
                    {
                        SetHover(false);
                    }

                    break;

                case WmNcMouseLeave:
                case WmMouseMove:
                    SetHover(false);
                    _pressed = false;
                    CaptionButton.SetIsPressed(_button, false);
                    break;

                case WmNcLButtonDown:
                    if (wParam.ToInt32() == HtMaxButton)
                    {
                        _pressed = true;
                        CaptionButton.SetIsPressed(_button, true);
                        handled = true;
                        return IntPtr.Zero;
                    }

                    break;

                case WmNcLButtonUp:
                    if (wParam.ToInt32() == HtMaxButton)
                    {
                        if (_pressed)
                        {
                            _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                        }

                        _pressed = false;
                        CaptionButton.SetIsPressed(_button, false);
                        handled = true;
                        return IntPtr.Zero;
                    }

                    break;
            }
        }
        catch
        {
            // Never let cosmetic hit-testing break the window.
        }

        return IntPtr.Zero;
    }

    private bool IsOverButton(IntPtr lParam)
    {
        if (!_button.IsVisible || PresentationSource.FromVisual(_button) is null)
        {
            return false;
        }

        long value = lParam.ToInt64();
        var screen = new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
        var local = _button.PointFromScreen(screen);
        return local.X >= 0 && local.Y >= 0 && local.X < _button.ActualWidth && local.Y < _button.ActualHeight;
    }

    private void SetHover(bool value) => CaptionButton.SetIsHovered(_button, value);
}

/// <summary>Attached state for caption buttons whose input arrives as non-client messages.</summary>
public static class CaptionButton
{
    public static readonly DependencyProperty IsHoveredProperty =
        DependencyProperty.RegisterAttached("IsHovered", typeof(bool), typeof(CaptionButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsPressedProperty =
        DependencyProperty.RegisterAttached("IsPressed", typeof(bool), typeof(CaptionButton), new PropertyMetadata(false));

    public static bool GetIsHovered(DependencyObject element) => (bool)element.GetValue(IsHoveredProperty);

    public static void SetIsHovered(DependencyObject element, bool value) => element.SetValue(IsHoveredProperty, value);

    public static bool GetIsPressed(DependencyObject element) => (bool)element.GetValue(IsPressedProperty);

    public static void SetIsPressed(DependencyObject element, bool value) => element.SetValue(IsPressedProperty, value);
}
