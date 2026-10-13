namespace Coreloom.Core.Updates;

/// <summary>How a downloaded installer is run.</summary>
/// <param name="ShowProgress">Show the installer's small progress window (/SILENT) instead of nothing at all (/VERYSILENT).</param>
/// <param name="RelaunchMinimized">Start the new version in the notification area instead of opening its window.</param>
public readonly record struct UpdateInstallOptions(bool ShowProgress, bool RelaunchMinimized)
{
    /// <summary>The user asked for it ("Install update"): show progress, reopen the window.</summary>
    public static UpdateInstallOptions Interactive => new(ShowProgress: true, RelaunchMinimized: false);

    /// <summary>Installed while nobody was using Coreloom: no windows, back to the notification area.</summary>
    public static UpdateInstallOptions Background => new(ShowProgress: false, RelaunchMinimized: true);

    /// <summary>
    /// Command line for the Inno Setup installer (see installer/Coreloom.iss). /UPDATE=1 makes it start Coreloom
    /// again when it finishes, or start the old version with --update-failed if it could not finish;
    /// /MINIMIZED=1 passes --minimized to that relaunch. /CLOSEAPPLICATIONS lets it close a Coreloom process that is
    /// still exiting.
    /// </summary>
    public string ToInstallerArguments() =>
        (ShowProgress ? "/SILENT" : "/VERYSILENT")
        + " /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1"
        + (RelaunchMinimized ? " /MINIMIZED=1" : string.Empty);
}

/// <summary>What Coreloom knows when deciding whether to install a ready update by itself.</summary>
public readonly record struct BackgroundInstallSignals
{
    /// <summary>The main window is open and not minimized (someone may be looking at it).</summary>
    public bool WindowInUse { get; init; }

    /// <summary>Time since the last keyboard or mouse input anywhere in the session.</summary>
    public TimeSpan UserIdle { get; init; }

    /// <summary>Time since the CPU or GPU was last busy (see <see cref="UpdateInstallPolicy.IsSystemBusy"/>).</summary>
    public TimeSpan SystemQuiet { get; init; }

    public bool MonitoringPaused { get; init; }
}

/// <summary>
/// When Coreloom installs an update without being asked. A downloaded update is installed:
/// <list type="bullet">
/// <item>when Coreloom starts (once, or twice if the first attempt didn't finish), or</item>
/// <item>while it runs in the notification area and the PC is idle: nobody has touched the keyboard or mouse
/// for <see cref="UserIdleThreshold"/>, and the CPU and GPU have been quiet for <see cref="SystemQuietThreshold"/>,
/// so no game, render or benchmark is being watched and the restart loses nothing anyone needs.</item>
/// </list>
/// Nothing is installed automatically when automatic updates are off, when the copy can't update itself (portable),
/// or after the installer reported a failure for that version.
/// </summary>
public static class UpdateInstallPolicy
{
    public const int MaxAutomaticAttempts = 2;

    public static readonly TimeSpan UserIdleThreshold = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan SystemQuietThreshold = TimeSpan.FromMinutes(10);

    /// <summary>A just-downloaded update waits a little before a background install (lets the notification be seen).</summary>
    public static readonly TimeSpan MinimumReadyTime = TimeSpan.FromMinutes(5);

    /// <summary>No background install in the first minutes after Coreloom started.</summary>
    public static readonly TimeSpan MinimumUptime = TimeSpan.FromMinutes(10);

    public const double BusyCpuPercent = 25;

    public const double BusyGpuPercent = 20;

    /// <summary>True when the CPU or GPU load says something is running that someone may be monitoring.</summary>
    public static bool IsSystemBusy(double? cpuLoadPercent, double? gpuLoadPercent) =>
        cpuLoadPercent is >= BusyCpuPercent || gpuLoadPercent is >= BusyGpuPercent;

    public static bool AllowsAutomaticAttempt(PendingUpdate pending) =>
        !pending.InstallFailed && pending.InstallAttempts < MaxAutomaticAttempts;

    public static bool ShouldInstallAtStartup(bool autoInstall, bool canInstall, PendingUpdate? pending) =>
        autoInstall && canInstall && pending is not null && AllowsAutomaticAttempt(pending);

    public static bool ShouldInstallInBackground(
        bool autoInstall,
        bool canInstall,
        PendingUpdate? pending,
        DateTime readySinceUtc,
        DateTime appStartedUtc,
        DateTime nowUtc,
        BackgroundInstallSignals signals)
    {
        if (!ShouldInstallAtStartup(autoInstall, canInstall, pending))
        {
            return false;
        }

        return !signals.WindowInUse
            && !signals.MonitoringPaused
            && signals.UserIdle >= UserIdleThreshold
            && signals.SystemQuiet >= SystemQuietThreshold
            && nowUtc - readySinceUtc >= MinimumReadyTime
            && nowUtc - appStartedUtc >= MinimumUptime;
    }
}
