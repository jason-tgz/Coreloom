using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Coreloom.App.Interop;
using Coreloom.Core.Diagnostics;
using Coreloom.Core.Startup;

namespace Coreloom.App.Services;

/// <summary>
/// Shows the main window for the first time and hands over from the splash. Runs on the main UI thread.
/// <list type="number">
/// <item>The window is shown <em>cloaked</em>: Windows lays it out and renders it but doesn't display it yet.</item>
/// <item>Its <see cref="Window.ContentRendered"/> event (the first frame is drawn) is the readiness signal. No timer
/// decides that the app is ready.</item>
/// <item>If that happened very quickly, the splash stays up to <see cref="SplashPolicy.MinimumVisible"/> so it
/// doesn't flash.</item>
/// <item>The splash is kept above the window and the window is uncloaked with its frame and background already
/// drawn; the splash then fades out while the window's content fades in (both instant with reduced motion, in which
/// case the window appears fully painted at once).</item>
/// </list>
/// If the splash never reached the screen (a very slow splash thread), it is cancelled rather than shown late.
/// A watchdog only covers a window that never reports a first frame, so nothing can be left invisible. Where
/// cloaking isn't available the window is simply shown as before and the splash fades away once it has painted.
/// While cloaked (normally well under a second) the window already has keyboard focus; that is harmless.
/// </summary>
internal sealed class StartupHandoff
{
    private readonly Window _window;
    private readonly SplashScreenHost _splash;
    private readonly bool _reducedMotion;
    private IntPtr _hwnd;
    private bool _cloaked;
    private bool _finished;
    private int _framesToWait;
    private UIElement? _content;
    private DispatcherTimer? _watchdog;
    private DispatcherTimer? _minimumDelay;

    private StartupHandoff(Window window, SplashScreenHost splash, bool reducedMotion)
    {
        _window = window;
        _splash = splash;
        _reducedMotion = reducedMotion;
    }

    /// <summary>Shows <paramref name="window"/>, handing over from <paramref name="splash"/> when there is one.</summary>
    public static void ShowMainWindow(Window window, SplashScreenHost? splash, bool reducedMotion)
    {
        if (splash is null || !splash.IsActive)
        {
            splash?.Close();
            window.Show();
            return;
        }

        new StartupHandoff(window, splash, reducedMotion).Begin();
    }

    private void Begin()
    {
        try
        {
            // Creating the handle now runs SourceInitialized (dark title bar, Mica) before anything is visible.
            _hwnd = new WindowInteropHelper(_window).EnsureHandle();
            _cloaked = NativeWindow.SetCloaked(_hwnd, true);
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", "Could not prepare the main window off-screen; showing it directly.", ex);
            _cloaked = false;
        }

        if (!_cloaked)
        {
            Log.Info("Startup", "Window cloaking is unavailable; the main window is shown directly.");
        }
        else if (!_reducedMotion && _window.Content is UIElement content)
        {
            // Rendered transparent while cloaked, then faded in as the splash fades out.
            _content = content;
            content.Opacity = 0;
        }

        _window.ContentRendered += OnContentRendered;
        _window.Closed += OnWindowClosed;
        // Runs until the reveal (it is not stopped by the first frame), so a stalled handoff can't hide the window.
        // Background priority: if the UI thread is merely busy, the first-frame notification (Input) still wins.
        // This DispatcherTimer constructor starts the timer.
        _watchdog = new DispatcherTimer(SplashPolicy.RevealWatchdog, DispatcherPriority.Background, OnWatchdog, _window.Dispatcher);
        _window.Show();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        _window.ContentRendered -= OnContentRendered;

        // Let the compositor put that first frame on screen (two more frames) before revealing the window.
        _framesToWait = 2;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (--_framesToWait > 0)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        // The minimum time counts from the splash's own first frame. A splash that isn't on screen yet is skipped
        // (cancelled in Reveal) rather than holding the dashboard back.
        var remaining = _splash.IsActive && _splash.HasAppeared ? SplashPolicy.RemainingBeforeHandoff(_splash.VisibleFor) : TimeSpan.Zero;
        if (remaining <= TimeSpan.Zero)
        {
            Reveal("first frame painted");
            return;
        }

        _minimumDelay = new DispatcherTimer(remaining, DispatcherPriority.Normal, (_, _) => Reveal("first frame painted; minimum splash time reached"), _window.Dispatcher);
    }

    private void OnWatchdog(object? sender, EventArgs e)
    {
        Log.Warn("Startup", $"The main window did not report its first frame within {SplashPolicy.RevealWatchdog.TotalSeconds:0} s; revealing it anyway.");
        Reveal("watchdog");
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // Exiting before the handoff (for example "Exit" from the tray icon): just remove the splash.
        if (Stop())
        {
            _splash.Close();
        }
    }

    private void Reveal(string reason)
    {
        if (!Stop())
        {
            return;
        }

        try
        {
            // Keep the splash above the window while it fades. Only when the window really is in front: if the user
            // switched to another app meanwhile, the splash must not jump on top of it.
            if (_cloaked && _splash.HasAppeared && NativeWindow.IsForeground(_hwnd))
            {
                _splash.BringToFront();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Startup", "Could not order the splash above the main window.", ex);
        }
        finally
        {
            if (_cloaked)
            {
                Uncloak();
            }

            FadeInContent();
            _splash.FadeOutAndClose();
            Log.Info("Startup", $"Main window revealed ({reason}) {SplashScreenHost.SinceProcessStart().TotalMilliseconds:0} ms after the process started; splash shown for {_splash.VisibleFor.TotalMilliseconds:0} ms.");
        }
    }

    private void Uncloak()
    {
        _cloaked = false;
        if (!NativeWindow.SetCloaked(_hwnd, false) && !NativeWindow.SetCloaked(_hwnd, false))
        {
            // Not expected (cloaking the same window just worked); logged so it can be diagnosed if it ever happens.
            Log.Error("Startup", "Windows refused to uncloak the main window.");
        }
    }

    /// <summary>Ends the handoff once; false when it had already ended.</summary>
    private bool Stop()
    {
        if (_finished)
        {
            return false;
        }

        _finished = true;
        _watchdog?.Stop();
        _minimumDelay?.Stop();
        CompositionTarget.Rendering -= OnRendering;
        _window.ContentRendered -= OnContentRendered;
        _window.Closed -= OnWindowClosed;
        return true;
    }

    private void FadeInContent()
    {
        if (_content is not { } content)
        {
            return;
        }

        _content = null;
        content.Opacity = 1;  // final value; the animation below runs from 0 and is then removed
        var fade = new DoubleAnimationUsingKeyFrames { Duration = SplashPolicy.HandoffDuration, FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(SplashPolicy.HandoffDuration), new KeySpline(0.22, 0.6, 0.32, 1)));
        content.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
