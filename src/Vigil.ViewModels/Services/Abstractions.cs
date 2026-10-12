using Vigil.Core.Hardware;
using Vigil.Core.Settings;
using Vigil.Core.Telemetry;

namespace Vigil.ViewModels.Services;

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

/// <summary>Registers Vigil to start at logon (Run key) or elevated (Task Scheduler).</summary>
public interface IStartupService
{
    bool IsRunAtLogonEnabled { get; }

    void SetRunAtLogon(bool enabled, bool minimized);

    bool IsElevatedTaskRegistered { get; }

    /// <summary>Creates or removes the elevated logon task. Shows a UAC prompt. Returns false if cancelled/failed.</summary>
    Task<bool> SetElevatedTaskAsync(bool enabled, bool minimized);
}

public interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>Relaunches Vigil with administrator rights (UAC prompt) and exits this instance on success.</summary>
    bool RestartAsAdministrator();
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
    /// <summary>True when Vigil was installed with the installer (portable copies cannot self-update).</summary>
    bool CanInstallUpdates { get; }

    void LaunchInstallerAndExit(string installerPath);
}

/// <summary>Application lifetime operations.</summary>
public interface IAppLifetime
{
    /// <summary>Restarts Vigil (same privilege level).</summary>
    void Restart();

    void Exit();
}

/// <summary>Logging level and log locations controlled from Settings.</summary>
public interface IDiagnosticsService
{
    string LogDirectory { get; }

    void SetVerboseLogging(bool enabled);

    IReadOnlyList<Vigil.Core.Diagnostics.LogEntry> RecentLog();
}
