using Coreloom.Core.Hardware;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;

namespace Coreloom.ViewModels.Services;

/// <summary>Everything the view models need from the monitoring back-end.</summary>
public sealed class MonitorContext
{
    public required TelemetryEngine Engine { get; init; }

    public required SystemSummary Summary { get; init; }

    public required IProcessSampler Processes { get; init; }

    /// <summary>Returns the automatically selected primary GPU slot.</summary>
    public required Func<int> PrimaryGpuSlot { get; init; }

    /// <summary>Lists GPU slots (slot, name, integrated).</summary>
    public required Func<IReadOnlyList<(int Slot, string Name, bool IsIntegrated)>> Gpus { get; init; }

    /// <summary>Lists network adapters (key, name, isActive).</summary>
    public required Func<IReadOnlyList<(string Key, string Name, bool IsActive)>> NetworkAdapters { get; init; }

    public required bool LibreHardwareMonitorIncluded { get; init; }
}

/// <summary>Applies appearance settings (theme, accent, motion, backdrop) to the UI.</summary>
public interface IThemeService
{
    void Apply(AppearanceSettings appearance);

    bool IsMicaSupported { get; }
}

/// <summary>
/// Registers the app to start at sign-in. The app always runs as administrator, so this is a Task Scheduler
/// logon task with highest privileges: it starts the app elevated without a UAC prompt each time.
/// </summary>
public interface IStartupService
{
    /// <summary>True when the sign-in task exists.</summary>
    bool IsStartAtLogonRegistered { get; }

    /// <summary>Creates (or refreshes) the sign-in task, or removes it. Returns false when the change failed.</summary>
    Task<bool> SetStartAtLogonAsync(bool enabled, bool minimized);
}

public interface IElevationService
{
    /// <summary>True when the process holds an elevated administrator token (always the case once started).</summary>
    bool IsElevated { get; }
}

/// <summary>Shell interactions: links, folders, files, clipboard and simple dialogs.</summary>
public interface IShellService
{
    void OpenUrl(string url);

    void OpenFolder(string path);

    void OpenFile(string path);

    void CopyToClipboard(string text);

    /// <summary>Shows a save dialog. Returns the chosen path or null if cancelled.</summary>
    string? PickSaveFile(string suggestedName, string filter);

    void ShowMessage(string title, string message);

    bool Confirm(string title, string message);
}

/// <summary>Hands a verified installer to Windows and exits the app so files can be replaced safely.</summary>
public interface IUpdateInstaller
{
    /// <summary>True when Coreloom was installed with the installer (portable copies cannot self-update).</summary>
    bool CanInstallUpdates { get; }

    /// <summary>
    /// Starts the installer (still held open by <paramref name="installer"/>, so it can't change before it runs) with
    /// the arguments for <paramref name="options"/>, then exits Coreloom. Returns false when it could not be started.
    /// </summary>
    bool LaunchInstallerAndExit(Coreloom.Core.Updates.VerifiedInstaller installer, Coreloom.Core.Updates.UpdateInstallOptions options);
}

/// <summary>Application lifetime operations.</summary>
public interface IAppLifetime
{
    /// <summary>Restarts Coreloom (same privilege level).</summary>
    void Restart();

    void Exit();
}

/// <summary>Logging level and log locations controlled from Settings.</summary>
public interface IDiagnosticsService
{
    string LogDirectory { get; }

    void SetVerboseLogging(bool enabled);

    IReadOnlyList<Coreloom.Core.Diagnostics.LogEntry> RecentLog();
}
