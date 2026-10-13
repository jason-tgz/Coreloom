namespace Coreloom.Core.Startup;

/// <summary>
/// Rules and timings for the startup splash (platform-neutral so they can be unit-tested).
/// The splash never decides by itself when to go away: the app hands off when its main window has painted, and
/// these values only prevent a flash on very fast starts and keep the motion consistent.
/// </summary>
public static class SplashPolicy
{
    /// <summary>Shortest time the splash stays up, so an instant start doesn't flash it on and off.</summary>
    public static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(850);

    /// <summary>Delay before the content fades in, then the fade/rise itself.</summary>
    public static readonly TimeSpan EntranceDelay = TimeSpan.FromMilliseconds(90);

    public static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(620);

    /// <summary>How far (DIPs) the content rises while it fades in.</summary>
    public const double EntranceRise = 12;

    /// <summary>Splash fade-out (and main window fade-in) during the handoff.</summary>
    public static readonly TimeSpan HandoffDuration = TimeSpan.FromMilliseconds(360);

    /// <summary>One sweep of the indeterminate loading line (travel, then a pause off-track).</summary>
    public static readonly TimeSpan IndicatorCycle = TimeSpan.FromMilliseconds(1450);

    /// <summary>Fraction of <see cref="IndicatorCycle"/> spent travelling across the track.</summary>
    public const double IndicatorTravelFraction = 0.6;

    /// <summary>
    /// Safety net only: if the main window was shown but never reported its first paint within this time, it is
    /// revealed anyway so a rendering problem can never leave an invisible window behind a splash. This runs
    /// after all startup work has finished, so it never cuts a slow start short.
    /// </summary>
    public static readonly TimeSpan RevealWatchdog = TimeSpan.FromSeconds(10);

    /// <summary>
    /// True when this launch should go straight to the notification area (no window, so no splash):
    /// <c>--minimized</c>, or the sign-in task with "Start minimized" turned on.
    /// </summary>
    public static bool StartsHidden(bool minimizedArgument, bool fromStartupTask, bool launchMinimizedSetting) =>
        minimizedArgument || (fromStartupTask && launchMinimizedSetting);

    /// <summary>How much longer to keep the splash up once the main window is ready.</summary>
    public static TimeSpan RemainingBeforeHandoff(TimeSpan splashVisibleFor)
    {
        if (splashVisibleFor < TimeSpan.Zero)
        {
            splashVisibleFor = TimeSpan.Zero;
        }

        return splashVisibleFor >= MinimumVisible ? TimeSpan.Zero : MinimumVisible - splashVisibleFor;
    }

    /// <summary>Motion is reduced when Coreloom's own setting asks for it or Windows animation effects are off.</summary>
    public static bool UseReducedMotion(bool appReducedMotion, bool windowsAnimationsEnabled) =>
        appReducedMotion || !windowsAnimationsEnabled;
}
