using System.Diagnostics;
using System.IO;
using System.Windows;
using Vigil.Core;
using Vigil.Core.Diagnostics;
using Vigil.ViewModels.Services;

namespace Vigil.App.Services;

/// <summary>Shell integration: URLs, folders, files, clipboard and simple dialogs.</summary>
public sealed class ShellService : IShellService
{
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    public void CopyToClipboard(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50); // clipboard briefly locked by another app
            }
        }
    }

    public string? PickSaveFile(string suggestedName, string filter)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggestedName,
            Filter = filter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public void ShowMessage(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true })
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public bool Confirm(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is { IsVisible: true }
            ? MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.OK;
    }

    private static void Start(ProcessStartInfo info)
    {
        try
        {
            using var _ = Process.Start(info);
        }
        catch (Exception ex)
        {
            Log.Warn("Shell", $"Could not open {info.FileName}.", ex);
        }
    }
}

/// <summary>Restart / exit / elevation.</summary>
public sealed class LifetimeService : IAppLifetime, IElevationService
{
    private readonly Action _exit;

    public LifetimeService(Action exit)
    {
        _exit = exit;
    }

    public bool IsElevated => Environment.IsPrivilegedProcess;

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Vigil.exe");

    public void Restart()
    {
        if (Launch(elevated: false))
        {
            _exit();
        }
    }

    public void Exit() => _exit();

    public bool RestartAsAdministrator()
    {
        if (IsElevated)
        {
            return true;
        }

        if (Launch(elevated: true))
        {
            _exit();
            return true;
        }

        return false;
    }

    private static bool Launch(bool elevated)
    {
        try
        {
            var info = new ProcessStartInfo(ExecutablePath, $"--wait-for {Environment.ProcessId}")
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            if (elevated)
            {
                info.Verb = "runas";
            }

            using var process = Process.Start(info);
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user cancelled the UAC prompt.
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("Lifetime", "Relaunching Vigil failed.", ex);
            return false;
        }
    }
}

/// <summary>Hands the verified installer to Windows and exits so the installer can replace files.</summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    private readonly AppPaths _paths;
    private readonly Action _exit;

    public UpdateInstaller(AppPaths paths, Action exit)
    {
        _paths = paths;
        _exit = exit;
    }

    public bool CanInstallUpdates => _paths.IsInstalled && !_paths.IsPortable;

    public void LaunchInstallerAndExit(string installerPath)
    {
        // /SILENT shows a progress window without questions. Vigil exits right after starting the installer;
        // /CLOSEAPPLICATIONS lets the installer wait for/close any Vigil process still holding files, and
        // /UPDATE=1 relaunches Vigil when finished (see installer/Vigil.iss). Files are only ever replaced by
        // the installer, never by the running app.
        var info = new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory,
        };

        using var process = Process.Start(info);
        if (process is not null)
        {
            _exit();
        }
    }
}

/// <summary>Log locations and verbosity.</summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly MemoryLogSink _memory;

    public DiagnosticsService(string logDirectory, MemoryLogSink memory)
    {
        LogDirectory = logDirectory;
        _memory = memory;
    }

    public string LogDirectory { get; }

    public void SetVerboseLogging(bool enabled) => Log.MinimumLevel = enabled ? LogLevel.Debug : LogLevel.Info;

    public IReadOnlyList<LogEntry> RecentLog() => _memory.Snapshot();
}
