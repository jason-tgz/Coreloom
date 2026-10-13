using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Coreloom.App.Interop;
using Coreloom.Core.Startup;

namespace Coreloom.App.Views;

/// <summary>
/// Borderless startup splash: the approved icon and wordmark, a status line and an indeterminate loading line.
/// Created and driven by <see cref="Services.SplashScreenHost"/> on its own UI thread, so it keeps moving while the
/// main thread builds the dashboard. It never decides when to close; the app hands off when its window has painted.
/// </summary>
public partial class SplashWindow : Window
{
    private static readonly SolidColorBrush ErrorBrush = Frozen(Color.FromRgb(0xDD, 0x74, 0x6C));

    private readonly bool _reducedMotion;
    private bool _closing;
    private bool _closed;
    private bool _failed;

    public SplashWindow(bool reducedMotion, string status)
    {
        // Resource lookups and inherited values stop at this window and continue at the system theme. App.xaml's
        // dictionaries belong to the main UI thread (and are being filled while this window is up), so nothing on
        // this thread may read them.
        InheritanceBehavior = InheritanceBehavior.SkipToThemeNext;
        _reducedMotion = reducedMotion;
        InitializeComponent();
        StatusText.Text = status;

        SourceInitialized += (_, _) =>
        {
            SplashHandle = new WindowInteropHelper(this).Handle;
            NativeWindow.MakeSplash(SplashHandle);  // no taskbar button, not in Alt+Tab, never takes focus
        };
        Loaded += (_, _) => BeginEntrance();
    }

    /// <summary>Native handle once created; the main thread uses it to keep the splash above the app during the handoff.</summary>
    public IntPtr SplashHandle { get; private set; }

    public void SetStatus(string text)
    {
        if (!_failed && !_closed && !string.IsNullOrWhiteSpace(text))
        {
            StatusText.Text = text;
            Announce(StatusText);
        }
    }

    /// <summary>Closes the window unless it has already closed (Close() on a closed window throws).</summary>
    public void CloseOnce()
    {
        if (!_closed)
        {
            _closing = true;
            Close();
        }
    }

    /// <summary>Stops the loading line and shows a short message. The app closes the splash after its error dialog.</summary>
    public void ShowError(string message)
    {
        if (_closing || _closed)
        {
            return;
        }

        _failed = true;
        Body.BeginAnimation(OpacityProperty, null);
        Body.Opacity = 1;
        BodyOffset.BeginAnimation(TranslateTransform.YProperty, null);
        BodyOffset.Y = 0;

        IndicatorOffset.BeginAnimation(TranslateTransform.XProperty, null);
        IndicatorOffset.X = 0;
        Indicator.Width = IndicatorTrack.Width;
        Indicator.Background = ErrorBrush;
        Indicator.Opacity = 0.85;

        StatusText.Text = "Startup interrupted";
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        Announce(ErrorText);
    }

    /// <summary>Fades the whole splash out over the app window, then closes it (instantly with reduced motion).</summary>
    public void FadeOutAndClose()
    {
        if (_closing || _closed)
        {
            return;
        }

        _closing = true;
        if (_reducedMotion)
        {
            CloseOnce();
            return;
        }

        var fade = Animate(1, 0, SplashPolicy.HandoffDuration, new KeySpline(0.22, 0.6, 0.32, 1));
        fade.Completed += (_, _) => CloseOnce();
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    private void BeginEntrance()
    {
        if (_failed || _closing || _closed)
        {
            return;
        }

        if (_reducedMotion)
        {
            // No fade or rise, and a still, dimmed line instead of a moving one.
            Body.Opacity = 1;
            BodyOffset.Y = 0;
            Indicator.Width = IndicatorTrack.Width;
            IndicatorOffset.X = 0;
            Indicator.Opacity = 0.65;
            return;
        }

        var entrance = new KeySpline(0.16, 1, 0.3, 1);
        var fade = Animate(0, 1, SplashPolicy.EntranceDuration, entrance);
        var rise = Animate(SplashPolicy.EntranceRise, 0, SplashPolicy.EntranceDuration, entrance);
        fade.BeginTime = SplashPolicy.EntranceDelay;
        rise.BeginTime = SplashPolicy.EntranceDelay;
        BodyOffset.Y = SplashPolicy.EntranceRise;
        Body.BeginAnimation(OpacityProperty, fade);
        BodyOffset.BeginAnimation(TranslateTransform.YProperty, rise);

        // One sweep: from just off the left end to just off the right end in the first 60 % of the cycle, then a
        // pause off-track (the kit's -140 % → 370 % of the line's own width, ease-in-out).
        double width = Indicator.Width;
        var cycle = SplashPolicy.IndicatorCycle;
        var sweep = new DoubleAnimationUsingKeyFrames { Duration = cycle, RepeatBehavior = RepeatBehavior.Forever };
        sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.4 * width, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sweep.KeyFrames.Add(new SplineDoubleKeyFrame(3.7 * width, KeyTime.FromTimeSpan(cycle * SplashPolicy.IndicatorTravelFraction), new KeySpline(0.42, 0, 0.58, 1)));
        IndicatorOffset.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    /// <summary>Tells screen readers that a live-region text changed (WPF doesn't raise this by itself).</summary>
    private static void Announce(UIElement element) =>
        UIElementAutomationPeer.FromElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

    private static DoubleAnimationUsingKeyFrames Animate(double from, double to, TimeSpan duration, KeySpline spline)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = duration, FillBehavior = FillBehavior.HoldEnd };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
        return animation;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
