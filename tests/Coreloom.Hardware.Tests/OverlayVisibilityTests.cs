using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Coreloom.Hardware.Native;

namespace Coreloom.Hardware.Tests;

/// <summary>
/// The frame-time graph's window seen on screen over a WPF window set up like the in-game overlay (see-through,
/// topmost, click-through, drawn in software, moved with SetWindowPos as the overlay service does), with its pixels
/// handed over from another thread. Checked by reading the screen; skipped where the screen can't be read.
/// </summary>
public class OverlayVisibilityTests
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const int GwlExStyle = -20;
    private const uint GwHwndPrev = 3;
    private static readonly IntPtr HwndTopmost = new(-1);

    [Fact]
    public void GraphWindow_IsSeenOverTheOverlayWindow()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        (bool OwnerSeen, bool GraphSeen, string Report) result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = Check();
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the check hung");
        Assert.Null(error);
        Assert.SkipUnless(result.OwnerSeen, $"The screen can't be read here ({result.Report}).");
        Assert.True(result.GraphSeen, result.Report);
    }

    private static (bool OwnerSeen, bool GraphSeen, string Report) Check()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x40, 0xC0)),
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Width = 200,
            Height = 100,
            Left = -32000,
            Top = -32000,
        };
        window.SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | 0x20 | 0x80 | 0x80000 | 0x08000000);
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            {
                target.RenderMode = RenderMode.SoftwareOnly;
            }
        };
        window.Show();
        Pump(300);
        IntPtr owner = new WindowInteropHelper(window).Handle;
        SetWindowPos(owner, HwndTopmost, 100, 100, 0, 0, SwpNoSize | SwpNoActivate);
        Pump(300);

        var surface = LayeredSurface.Create(owner);
        var red = new uint[100 * 30];
        Array.Fill(red, 0xFFFF0000u);
        try
        {
            surface.Place(120, 120, 100, 30);
            surface.SetVisible(true);
            bool presented = PresentFromAnotherThread(surface, red);
            Pump(400);
            uint ownerColor = Pixel(110, 185);
            uint graphColor = Pixel(170, 135);

            // The overlay moves (and claims the top again); the graph's window follows.
            SetWindowPos(owner, HwndTopmost, 400, 100, 0, 0, SwpNoSize | SwpNoActivate);
            surface.Place(420, 120, 100, 30);
            bool presentedAgain = PresentFromAnotherThread(surface, red);
            Pump(400);
            uint ownerColor2 = Pixel(410, 185);
            uint graphColor2 = Pixel(470, 135);

            bool above = false;
            for (IntPtr h = GetWindow(owner, GwHwndPrev); h != IntPtr.Zero; h = GetWindow(h, GwHwndPrev))
            {
                if (h == surface.Handle)
                {
                    above = true;
                    break;
                }
            }

            GetWindowRect(surface.Handle, out var rect);
            string report = $"presented {presented}/{presentedAgain}; overlay pixel {ownerColor:X6} then {ownerColor2:X6}; graph pixel {graphColor:X6} then {graphColor2:X6}; "
                + $"graph window visible {IsWindowVisible(surface.Handle)}, at {rect.Left},{rect.Top}-{rect.Right},{rect.Bottom}, above the overlay {above}, "
                + $"ex style {GetWindowLongPtr(surface.Handle, GwlExStyle):X}";
            return (IsBlue(ownerColor) && IsBlue(ownerColor2), IsRed(graphColor) && IsRed(graphColor2), report);
        }
        finally
        {
            surface.Dispose();
            window.Close();
        }
    }

    private static bool PresentFromAnotherThread(LayeredSurface surface, uint[] pixels)
    {
        bool ok = false;
        var presenter = new Thread(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                ok = surface.Present(pixels, 100, 30) || ok;
                Thread.Sleep(15);
            }
        });
        presenter.Start();
        while (!presenter.Join(5))
        {
            Pump(5);
        }

        return ok;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(milliseconds), DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    /// <summary>The screen's color at a point (0xBBGGRR), or 0xFFFFFFFF when it can't be read.</summary>
    private static uint Pixel(int x, int y)
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            return GetPixel(dc, x, y);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private static bool IsRed(uint bgr) => bgr != 0xFFFFFFFF && (bgr & 0xFF) > 0xC0 && ((bgr >> 8) & 0xFF) < 0x40 && ((bgr >> 16) & 0xFF) < 0x40;

    private static bool IsBlue(uint bgr) => bgr != 0xFFFFFFFF && ((bgr >> 16) & 0xFF) > 0x90 && (bgr & 0xFF) < 0x60;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);
}
