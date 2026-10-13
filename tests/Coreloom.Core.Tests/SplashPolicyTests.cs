using Coreloom.Core.Startup;

namespace Coreloom.Core.Tests;

public class SplashPolicyTests
{
    [Theory]
    [InlineData(false, false, false, false)]  // double-click: window (and splash)
    [InlineData(false, false, true, false)]   // "Start minimized" only applies to the sign-in task
    [InlineData(false, true, false, false)]   // sign-in task, window wanted
    [InlineData(false, true, true, true)]     // sign-in task with "Start minimized"
    [InlineData(true, false, false, true)]    // --minimized always starts in the tray
    [InlineData(true, true, false, true)]
    public void StartsHidden_FollowsTheLaunchArgumentsAndSetting(bool minimized, bool fromStartup, bool launchMinimized, bool expected) =>
        Assert.Equal(expected, SplashPolicy.StartsHidden(minimized, fromStartup, launchMinimized));

    [Theory]
    [InlineData(0, 850)]
    [InlineData(300, 550)]
    [InlineData(849, 1)]
    [InlineData(850, 0)]
    [InlineData(5000, 0)]
    [InlineData(-200, 850)]
    public void RemainingBeforeHandoff_OnlyPadsVeryFastStarts(int visibleMs, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), SplashPolicy.RemainingBeforeHandoff(TimeSpan.FromMilliseconds(visibleMs)));

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void ReducedMotion_HonoursCoreloomAndWindows(bool appSetting, bool windowsAnimations, bool expected) =>
        Assert.Equal(expected, SplashPolicy.UseReducedMotion(appSetting, windowsAnimations));

    [Fact]
    public void Timings_MatchTheApprovedMotionSpec()
    {
        // Brief: soft ~0.6 s entrance with a 6–12 px rise, 0.36 s handoff fade, ~850 ms minimum on instant starts.
        Assert.InRange(SplashPolicy.EntranceDuration.TotalMilliseconds, 550, 700);
        Assert.InRange(SplashPolicy.EntranceRise, 6, 12);
        Assert.Equal(360, SplashPolicy.HandoffDuration.TotalMilliseconds);
        Assert.Equal(850, SplashPolicy.MinimumVisible.TotalMilliseconds);

        // The safety net must be far longer than any normal first paint, so it never shortens a slow start.
        Assert.True(SplashPolicy.RevealWatchdog >= TimeSpan.FromSeconds(5));
    }
}
