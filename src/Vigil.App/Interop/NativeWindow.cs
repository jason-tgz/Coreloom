using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Vigil.App.Interop;

/// <summary>DWM integration: immersive dark title bar, rounded corners and the Windows 11 Mica backdrop.</summary>
internal static partial class NativeWindow
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2; // Mica

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    /// <summary>System backdrop types (Mica) require Windows 11 22H2 (build 22621) or later.</summary>
    public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22621;

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static void ApplyBackdrop(Window window, bool dark, bool mica)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return; // applied again from SourceInitialized
        }

        try
        {
            int darkValue = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkValue, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref darkValue, sizeof(int));
            }

            if (IsWindows11)
            {
                int corner = DwmwcpRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
            }

            // Mica needs the frame extended over the whole client area (GlassFrameThickness = -1) and
            // transparent client pixels where it should show through. Without Mica, no frame extension is
            // used, so DWM never draws its own caption buttons behind Vigil's.
            if (WindowChrome.GetWindowChrome(window) is { } chrome)
            {
                var desired = mica ? new Thickness(-1) : new Thickness(0);
                if (chrome.GlassFrameThickness != desired)
                {
                    var updated = (WindowChrome)chrome.Clone();
                    updated.GlassFrameThickness = desired;
                    WindowChrome.SetWindowChrome(window, updated);
                }
            }

            var source = HwndSource.FromHwnd(hwnd);
            if (mica)
            {
                if (source?.CompositionTarget is { } target)
                {
                    target.BackgroundColor = Colors.Transparent;
                }

                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                int type = DwmsbtMainWindow;
                DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
            }
            else if (IsMicaSupported)
            {
                int type = DwmsbtNone;
                DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // DWM attributes are cosmetic; ignore on unsupported systems.
        }
    }

    /// <summary>
    /// Margin that keeps content on-screen when a WindowChrome window is maximized (Windows extends the
    /// window by the resize border on every side).
    /// </summary>
    public static Thickness MaximizedInset(Window window)
    {
        var resize = SystemParameters.WindowResizeBorderThickness;
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        double padded = GetSystemMetrics(92 /* SM_CXPADDEDBORDER */) / (scale <= 0 ? 1 : scale);
        return new Thickness(resize.Left + padded, resize.Top + padded, resize.Right + padded, resize.Bottom + padded);
    }
}
