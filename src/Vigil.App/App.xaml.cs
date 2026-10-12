using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Vigil.App.Services;
using Vigil.App.Themes;
using Vigil.App.Views;
using Vigil.Core;
using Vigil.Core.Alerts;
using Vigil.Core.Diagnostics;
using Vigil.Core.Settings;
using Vigil.Core.Telemetry;
using Vigil.Hardware;
using Vigil.Hardware.Providers;
using Vigil.ViewModels;
using Vigil.ViewModels.Infrastructure;
using Vigil.ViewModels.Services;

namespace Vigil.App;

/// <summary>
/// Composition root. Owns the single-instance lock, logging, settings, the monitoring host, the tray icon,
/// the frame pump (telemetry thread → UI thread) and alert notifications. Exiting always goes through
/// <see cref="ExitApplication"/> so settings are flushed and the sensor libraries are closed cleanly.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan UpdateCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdateCheckPeriod = TimeSpan.FromHours(6);

    private SingleInstance? _instance;
    private FileLogSink? _fileLog;
    private SettingsService? _settings;
    private MonitoringHost? _host;
    private AlertEngine? _alerts;
    private ShellViewModel? _shell;
    private MainWindow? _window;
    private TrayIconService? _tray;
    private HttpClient? _http;
    private DispatcherTimer? _updateTimer;
    private int _framePending;
    private volatile bool _exiting;
    private bool _windowClosed;
    private bool _trayHintShown;
    private DateTime _lastCrashDialog = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = CommandLine.Parse(e.Args);

        // A restart (elevation, settings change) passes our old PID: wait for it so the instance lock is free.
        if (args.WaitForProcessId is int pid)
        {
            SingleInstance.WaitForProcessExit(pid, TimeSpan.FromSeconds(15));
        }

        _instance = new SingleInstance("Vigil");
        if (!_instance.TryAcquire())
        {
            _instance.SignalPrimary();
            _instance.Dispose();
            _instance = null;
            Shutdown(0);
            return;
        }

        var paths = AppPaths.Resolve(AppContext.BaseDirectory);
        var memoryLog = new MemoryLogSink();
        InitializeLogging(paths, memoryLog);
        RegisterExceptionHandlers();

        var app = AppVersionInfo.FromAssembly(typeof(App).Assembly);
        Log.Info("App", $"Vigil {app.InformationalVersion} starting (args: {string.Join(' ', e.Args)}; portable: {paths.IsPortable}; installed: {paths.IsInstalled}; elevated: {Environment.IsPrivilegedProcess}; OS: {Environment.OSVersion.VersionString}).");

        // ---- Settings & theme ---------------------------------------------------------------------
        var store = new SettingsStore(paths.SettingsFile);
        var settings = store.Load();
        _settings = new SettingsService(store, settings);
        Log.MinimumLevel = settings.Diagnostics.VerboseLogging ? LogLevel.Debug : LogLevel.Info;

        var theme = new ThemeService(Resources);
        theme.Initialize(settings.Appearance);

        // ---- Monitoring back-end ------------------------------------------------------------------
        try
        {
            _host = MonitoringHost.Create(new HardwareOptions
            {
                UseLibreHardwareMonitor = settings.Monitoring.UseLibreHardwareMonitor,
                ScanUsbControllers = settings.Monitoring.ScanUsbControllers,
                PollingInterval = TimeSpan.FromMilliseconds(settings.Monitoring.PollingIntervalMs),
                PreferredNetworkAdapter = () => _settings?.Current.Monitoring.PreferredNetworkAdapter,
            });
        }
        catch (Exception ex)
        {
            Log.Error("App", "Creating the monitoring host failed.", ex);
            Log.Flush();
            MessageBox.Show($"Vigil could not start its monitoring engine:\n\n{ex.Message}\n\nDetails were written to {paths.LogDirectory}.",
                "Vigil", MessageBoxButton.OK, MessageBoxImage.Error);
            ReleaseAndShutdown(1);
            return;
        }

        var host = _host;
        var network = host.Providers.OfType<NetworkProvider>().FirstOrDefault();
        var monitor = new MonitorContext
        {
            Engine = host.Engine,
            Summary = host.Summary,
            Processes = host.ProcessSampler,
            PrimaryGpuSlot = () => host.PrimaryGpuSlot,
            Gpus = () => host.Services.Gpus.Entries.Select(g => (g.Slot, g.Name, g.IsIntegrated)).ToArray(),
            NetworkAdapters = () => network?.GetAdapters() ?? [],
            LibreHardwareMonitorIncluded = MonitoringHost.LibreHardwareMonitorIncluded,
        };

        var alerts = new AlertEngine(TimeSpan.FromMinutes(settings.Alerts.CooldownMinutes));
        var settingsService = _settings;
        _alerts = alerts;

        // ---- Services ---------------------------------------------------------------------------
        var shellService = new ShellService();
        var lifetime = new LifetimeService(() => Dispatcher.InvokeAsync(ExitApplication));
        var installer = new UpdateInstaller(paths, () => Dispatcher.InvokeAsync(ExitApplication));
        var diagnostics = new DiagnosticsService(paths.LogDirectory, memoryLog);
        var startup = new StartupService();

        _http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            // Applies until response headers arrive; downloads stream afterwards and are cancellable by the user.
            Timeout = TimeSpan.FromSeconds(60),
        };
        var updates = new UpdatesViewModel(app, settingsService, installer, shellService, _http, paths.DownloadDirectory);

        var shell = new ShellViewModel(new ShellDependencies
        {
            Monitor = monitor,
            Settings = settingsService,
            Alerts = alerts,
            Theme = theme,
            Startup = startup,
            Elevation = lifetime,
            Shell = shellService,
            Lifetime = lifetime,
            Diagnostics = diagnostics,
            Updates = updates,
            App = app,
        });
        _shell = shell;

        // ---- Window & tray ----------------------------------------------------------------------
        var window = new MainWindow(shell)
        {
            DesktopSettingsProvider = () => settingsService.Current.Desktop,
        };
        window.ApplyPlacement(settings.Window);
        window.HiddenToTray += (_, _) => ShowTrayHintOnce();
        window.Closed += (_, _) =>
        {
            _windowClosed = true;
            ExitApplication();  // closing without "close to tray" exits the app
        };
        _window = window;

        void OpenUpdates()
        {
            shell.OpenUpdatesCommand.Execute(null);
            ShowMainWindow();
        }

        var tray = new TrayIconService(
            open: ShowMainWindow,
            togglePause: () => shell.IsMonitoringPaused = !shell.IsMonitoringPaused,
            openSettings: () =>
            {
                shell.Navigate(shell.Settings);
                ShowMainWindow();
            },
            checkUpdates: () =>
            {
                OpenUpdates();
                _ = updates.CheckAsync(userInitiated: true);
            },
            exit: ExitApplication);
        _tray = tray;

        shell.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(ShellViewModel.IsMonitoringPaused))
            {
                tray.SetPaused(shell.IsMonitoringPaused);
            }
        };

        updates.UpdateFound += release => Dispatcher.InvokeAsync(() =>
            _tray?.Notify("Vigil update available", $"Version {release.Version} is ready to install.", onClick: OpenUpdates));

        // ---- Frame pump & alerts ----------------------------------------------------------------
        alerts.AlertRaised += OnAlertRaised;
        host.Engine.FrameProduced += OnFrameProduced;

        _instance.ListenForActivation(() => Dispatcher.InvokeAsync(ShowMainWindow));
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        host.Engine.Start();

        bool startHidden = args.Minimized || (args.FromStartup && settings.Desktop.LaunchMinimized);
        if (startHidden)
        {
            shell.SetWindowVisible(false);
            Log.Info("App", "Started hidden in the notification area.");
        }
        else
        {
            window.Show();
        }

        // ---- Background housekeeping ------------------------------------------------------------
        _ = shell.Settings.ApplyStartupAsync(userInitiated: false);  // Run key only; never a UAC prompt at launch
        ScheduleUpdateChecks(updates, isStartup: true);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Log.Info("App", $"Windows session ending ({e.ReasonSessionEnding}).");
        ExitApplication();
        base.OnSessionEnding(e);
    }

    // ------------------------------------------------------------------------------------------------
    // Frames: produced on the telemetry thread. Alerts are evaluated there (cheap, and works while the
    // window is hidden); UI delivery is coalesced so a slow UI never queues more than one frame.
    // ------------------------------------------------------------------------------------------------

    private void OnFrameProduced(TelemetryFrame frame)
    {
        try
        {
            _alerts?.Evaluate(frame);
        }
        catch (Exception ex)
        {
            Log.WarnThrottled("alerts", TimeSpan.FromMinutes(5), "Alerts", "Evaluating alert rules failed.", ex);
        }

        if (Interlocked.Exchange(ref _framePending, 1) == 1)
        {
            return;
        }

        Dispatcher.InvokeAsync(DeliverLatestFrame, DispatcherPriority.Normal);
    }

    private void DeliverLatestFrame()
    {
        Volatile.Write(ref _framePending, 0);
        if (_exiting || _shell is null || _host is null)
        {
            return;
        }

        try
        {
            _shell.OnFrame(_host.Engine.LatestFrame);
            _tray?.SetTooltip(_shell.TrayText);
        }
        catch (Exception ex)
        {
            // A UI bug must never stop monitoring; log it (throttled) and keep going.
            Log.WarnThrottled("frame-ui", TimeSpan.FromMinutes(1), "UI", "Updating the UI with a new frame failed.", ex);
        }
    }

    private void OnAlertRaised(AlertEvent alert)
    {
        var settings = _settings;
        if (settings is null)
        {
            return;
        }

        string message = DescribeAlert(alert, settings);
        Log.Info("Alerts", $"{alert.Rule.Name}: {message}");
        Dispatcher.InvokeAsync(() => _tray?.Notify(alert.Rule.Name, message, warning: true, onClick: ShowMainWindow));
    }

    internal static string DescribeAlert(AlertEvent alert, SettingsService settings)
    {
        var formatter = settings.Formatter;
        var kind = alert.Metric?.Kind ?? MetricKind.Load;
        var style = ValueStyles.ForKind(kind);
        string value = ValueStyles.Format(style, alert.Value, formatter).ToString();
        string threshold = ValueStyles.Format(style, alert.Rule.Threshold, formatter).ToString();
        string name = alert.Metric?.Name ?? alert.Rule.MetricId;
        string direction = alert.Rule.Comparison == AlertComparison.Above ? "above" : "below";
        string sustained = alert.Rule.SustainSeconds > 0
            ? string.Format(CultureInfo.CurrentCulture, " for {0} s", alert.Rule.SustainSeconds)
            : string.Empty;
        return $"{name} is {value} — {direction} your {threshold} threshold{sustained}.";
    }

    // ------------------------------------------------------------------------------------------------
    // Window / tray
    // ------------------------------------------------------------------------------------------------

    private void ShowMainWindow()
    {
        if (_exiting || _window is null)
        {
            return;
        }

        _window.ShowFromTray();
    }

    private void ShowTrayHintOnce()
    {
        if (_trayHintShown || _settings?.Current.Desktop.TrayHintShown == true)
        {
            return;
        }

        _trayHintShown = true;
        _settings?.Update("Desktop", s => s.Desktop.TrayHintShown = true);
        _tray?.Notify("Vigil is still running", "Monitoring and alerts continue in the notification area. Right-click the icon to exit.");
    }

    // ------------------------------------------------------------------------------------------------
    // Updates & power events
    // ------------------------------------------------------------------------------------------------

    private void ScheduleUpdateChecks(UpdatesViewModel updates, bool isStartup)
    {
        var first = new DispatcherTimer { Interval = UpdateCheckDelay };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            await SafeCheckAsync(updates, isStartup);
        };
        first.Start();

        _updateTimer = new DispatcherTimer { Interval = UpdateCheckPeriod };
        _updateTimer.Tick += async (_, _) => await SafeCheckAsync(updates, isStartup: false);
        _updateTimer.Start();
    }

    private static async Task SafeCheckAsync(UpdatesViewModel updates, bool isStartup)
    {
        try
        {
            await updates.CheckIfDueAsync(isStartup);
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Scheduled update check failed.", ex);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        // Drivers (NVML in particular) can need a moment after resume. Re-initialize providers afterwards.
        Log.Info("App", "System resumed from sleep; re-initializing sensor providers shortly.");
        _ = ReinitializeAfterResumeAsync();
    }

    private async Task ReinitializeAfterResumeAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (!_exiting)
        {
            _host?.Engine.RequestReinitialize();  // thread-safe: sets a flag the telemetry thread picks up
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Exit
    // ------------------------------------------------------------------------------------------------

    /// <summary>Saves state, stops monitoring, removes the tray icon and shuts down. Safe to call twice.</summary>
    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Log.Info("App", "Exiting.");
        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _updateTimer?.Stop();

            if (_window is not null && _settings is not null)
            {
                _window.AllowClose = true;
                if (!_windowClosed)
                {
                    _window.Close();  // captures LastPlacement while closing
                }

                if (_window.LastPlacement is { } placement)
                {
                    _settings.Update("Window", s => s.Window = placement);
                }
            }

            _settings?.Flush();
            _tray?.Dispose();
            _tray = null;

            if (_host is not null)
            {
                _host.Engine.FrameProduced -= OnFrameProduced;
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error("App", "Error during shutdown.", ex);
        }
        finally
        {
            _http?.Dispose();
            _settings?.Dispose();
            ReleaseAndShutdown(0);
        }
    }

    private void ReleaseAndShutdown(int exitCode)
    {
        _instance?.Dispose();
        _instance = null;
        Log.Flush();
        if (_fileLog is not null)
        {
            Log.RemoveSink(_fileLog);
            _fileLog.Dispose();
            _fileLog = null;
        }

        Shutdown(exitCode);
    }

    // ------------------------------------------------------------------------------------------------
    // Logging & crash handling
    // ------------------------------------------------------------------------------------------------

    private void InitializeLogging(AppPaths paths, MemoryLogSink memory)
    {
        Log.AddSink(memory);
        try
        {
            paths.EnsureCreated();
            _fileLog = new FileLogSink(paths.LogDirectory);
            Log.AddSink(_fileLog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging to disk is best-effort (e.g. a read-only portable folder); the in-memory log still works.
            Log.Warn("App", "File logging is unavailable.", ex);
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Crash", "Unhandled UI exception.", e.Exception);
            Log.Flush();
            e.Handled = true;
            if (!_exiting && DateTime.UtcNow - _lastCrashDialog > TimeSpan.FromSeconds(30))
            {
                _lastCrashDialog = DateTime.UtcNow;
                MessageBox.Show($"Something went wrong in Vigil:\n\n{e.Exception.Message}\n\nThe error was logged. Vigil will keep running.",
                    "Vigil", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Crash", $"Unhandled exception (terminating: {e.IsTerminating}).", e.ExceptionObject as Exception);
            Log.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warn("Crash", "Unobserved task exception.", e.Exception);
            e.SetObserved();
        };
    }
}

/// <summary>Command-line options: --minimized, --startup, --wait-for &lt;pid&gt;.</summary>
internal sealed record CommandLine(bool Minimized, bool FromStartup, int? WaitForProcessId)
{
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        bool minimized = false;
        bool startup = false;
        int? waitFor = null;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--minimized" or "/minimized" or "-m":
                    minimized = true;
                    break;
                case "--startup":
                    startup = true;
                    break;
                case "--wait-for" when i + 1 < args.Count && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid):
                    waitFor = pid;
                    i++;
                    break;
            }
        }

        return new CommandLine(minimized, startup, waitFor);
    }
}
