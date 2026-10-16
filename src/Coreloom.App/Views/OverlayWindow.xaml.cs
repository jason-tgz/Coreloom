using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Coreloom.ViewModels.Overlay;

namespace Coreloom.App.Views;

/// <summary>
/// The in-game overlay: a small topmost window that clicks pass through, that never takes focus and that isn't in the
/// taskbar or Alt+Tab. Shown over windowed and borderless games (and over exclusive-fullscreen games where Windows'
/// fullscreen optimizations apply).
/// <para>
/// It is drawn on the processor, not the graphics card: a see-through WPF window drawn on the graphics card has every
/// changed frame copied back from the card for Windows to blend, which makes the app wait on a card the game is keeping
/// busy. Drawn on the processor, the overlay never queues work on the graphics card or waits for it; Windows only
/// blends the finished picture over the game. The window is small and changes only twice a second (and the frame-time
/// graph while a game draws), so this costs little.
/// </para>
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExAppWindow = 0x00040000;

    public OverlayWindow(OverlayViewModel overlay)
    {
        InitializeComponent();
        DataContext = overlay;
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = Handle;
        nint style = GetWindowLongPtr(hwnd, GwlExStyle);
        style |= (nint)(WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate);
        style &= ~(nint)WsExAppWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, style);

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.RenderMode = RenderMode.SoftwareOnly;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);
}
