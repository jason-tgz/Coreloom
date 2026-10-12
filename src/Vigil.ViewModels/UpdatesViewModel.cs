using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vigil.Core;
using Vigil.Core.Diagnostics;
using Vigil.Core.Updates;
using Vigil.ViewModels.Services;

namespace Vigil.ViewModels;

/// <summary>
/// Update checks against GitHub Releases and the verified download/install flow:
/// download installer (resumable) → verify SHA-256 against SHA256SUMS.txt → user confirms →
/// Vigil exits and the installer replaces files. Running executables are never overwritten in place.
/// </summary>
public sealed class UpdatesViewModel : ObservableObject
{
    private readonly AppVersionInfo _app;
    private readonly SettingsService _settings;
    private readonly IUpdateInstaller _installer;
    private readonly IShellService _shell;
    private readonly HttpClient _http;
    private readonly string _downloadDirectory;
    private CancellationTokenSource? _downloadCts;
    private ReleaseInfo? _release;
    private bool _isChecking;
    private bool _isDownloading;
    private double _progress;
    private string _statusText = "Not checked yet.";
    private string? _downloadText;
    private UpdateCheckStatus? _status;

    public UpdatesViewModel(AppVersionInfo app, SettingsService settings, IUpdateInstaller installer, IShellService shell, HttpClient http, string downloadDirectory)
    {
        _app = app;
        _settings = settings;
        _installer = installer;
        _shell = shell;
        _http = http;
        _downloadDirectory = downloadDirectory;
        CheckCommand = new AsyncRelayCommand(() => CheckAsync(userInitiated: true), () => !IsChecking && !IsDownloading);
        InstallCommand = new AsyncRelayCommand(DownloadAndInstallAsync, () => IsUpdateAvailable && !IsDownloading && !IsChecking);
        CancelDownloadCommand = new RelayCommand(() => _downloadCts?.Cancel(), () => IsDownloading);
        SkipCommand = new RelayCommand(Skip, () => IsUpdateAvailable && !IsDownloading);
        OpenReleaseCommand = new RelayCommand(() => _shell.OpenUrl(_release?.HtmlUrl.ToString() ?? RepositoryUrl ?? "https://github.com"), () => _release is not null || RepositoryUrl is not null);
        if (settings.Current.Updates.LastCheckUtc is DateTime last)
        {
            _statusText = $"Last checked {last.ToLocalTime():g}.";
        }
    }

    /// <summary>Raised when a check finds a newer version (used for the tray notification).</summary>
    public event Action<ReleaseInfo>? UpdateFound;

    public string CurrentVersion => _app.DisplayVersion;

    public string InformationalVersion => _app.InformationalVersion;

    public string? Repository => string.IsNullOrWhiteSpace(_settings.Current.Updates.RepositoryOverride) ? _app.Repository : _settings.Current.Updates.RepositoryOverride;

    public string? RepositoryUrl => GitHubReleaseClient.IsValidRepository(Repository) ? $"https://github.com/{Repository}" : null;

    public bool IsConfigured => GitHubReleaseClient.IsValidRepository(Repository);

    public bool CanInstall => _installer.CanInstallUpdates;

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                NotifyCommands();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string? DownloadText
    {
        get => _downloadText;
        private set => SetProperty(ref _downloadText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public UpdateCheckStatus? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsUpdateAvailable => _status == UpdateCheckStatus.UpdateAvailable && _release is not null;

    public string? AvailableVersion => _release?.Version.ToString();

    public string? ReleaseTitle => _release?.Title;

    public string? ReleaseNotes => _release is null ? null : string.IsNullOrWhiteSpace(_release.Notes) ? "No release notes were published." : _release.Notes.Trim();

    public string? PublishedText => _release?.PublishedAt is { } p ? $"Published {p.LocalDateTime:D}" : null;

    public IAsyncRelayCommand CheckCommand { get; }

    public IAsyncRelayCommand InstallCommand { get; }

    public IRelayCommand CancelDownloadCommand { get; }

    public IRelayCommand SkipCommand { get; }

    public IRelayCommand OpenReleaseCommand { get; }

    /// <summary>Checks if the configured frequency says a check is due (called at startup and periodically).</summary>
    public Task CheckIfDueAsync(bool isStartup)
    {
        return _settings.Current.Updates.IsCheckDue(DateTime.UtcNow, isStartup) ? CheckAsync(userInitiated: false) : Task.CompletedTask;
    }

    public async Task CheckAsync(bool userInitiated)
    {
        if (IsChecking || IsDownloading)
        {
            return;
        }

        IsChecking = true;
        StatusText = "Checking for updates…";
        try
        {
            string? repository = Repository;
            var checker = new UpdateChecker(_app.Version, repository, async (pre, ct) =>
            {
                var client = new GitHubReleaseClient(_http, repository!, $"Vigil/{_app.DisplayVersion}");
                return await client.GetLatestAsync(pre, ct).ConfigureAwait(false);
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await checker.CheckAsync(_settings.Current.Updates.IncludePrereleases, userInitiated ? null : _settings.Current.Updates.SkippedVersion, cts.Token);
            _release = result.Release;
            Status = result.Status;
            StatusText = result.Message;
            if (result.Status is UpdateCheckStatus.UpToDate or UpdateCheckStatus.UpdateAvailable or UpdateCheckStatus.NoReleases)
            {
                _settings.Update("Updates", s => s.Updates.LastCheckUtc = DateTime.UtcNow);
            }

            if (result.IsUpdateAvailable)
            {
                UpdateFound?.Invoke(result.Release!);
            }
        }
        catch (OperationCanceledException)
        {
            Status = UpdateCheckStatus.Failed;
            StatusText = "The update check timed out.";
        }
        finally
        {
            IsChecking = false;
            RaiseReleaseProperties();
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        var release = _release;
        if (release is null)
        {
            return;
        }

        if (!CanInstall)
        {
            // Portable copies cannot run the installer over themselves; send the user to the release page.
            _shell.OpenUrl(release.HtmlUrl.ToString());
            return;
        }

        var installer = release.Installer;
        var checksums = release.Checksums;
        if (installer is null || checksums is null)
        {
            _shell.ShowMessage("Update unavailable", "This release does not include a Windows installer and checksum file. Download it manually from the release page.");
            return;
        }

        _downloadCts = new CancellationTokenSource();
        IsDownloading = true;
        Progress = 0;
        DownloadText = "Downloading checksums…";
        try
        {
            string sums = await _http.GetStringAsync(checksums.DownloadUrl, _downloadCts.Token);
            var map = Checksums.Parse(sums);
            if (!map.TryGetValue(installer.Name, out string? expected))
            {
                throw new UpdateException($"{checksums.Name} does not list {installer.Name}; the download cannot be verified.");
            }

            string path = Path.Combine(_downloadDirectory, installer.Name);
            var downloader = new ResumableDownloader(_http) { UserAgent = $"Vigil/{_app.DisplayVersion}" };
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress = p.Fraction ?? 0;
                DownloadText = p.TotalBytes is long total
                    ? $"Downloading… {p.BytesReceived / 1048576.0:0.0} of {total / 1048576.0:0.0} MB"
                    : $"Downloading… {p.BytesReceived / 1048576.0:0.0} MB";
            });
            await downloader.DownloadAsync(installer.DownloadUrl, path, installer.Size > 0 ? installer.Size : null, progress, _downloadCts.Token);

            DownloadText = "Verifying integrity…";
            if (!await Checksums.VerifyAsync(path, expected, _downloadCts.Token))
            {
                TryDelete(path);
                throw new UpdateException("The downloaded installer failed SHA-256 verification and was deleted. Please try again.");
            }

            DownloadText = "Ready to install.";
            if (_shell.Confirm("Install update", $"Vigil {release.Version} has been downloaded and verified.\n\nVigil will close and the installer will update it, then restart Vigil."))
            {
                Log.Info("Updates", $"Launching installer {path}");
                _installer.LaunchInstallerAndExit(path);
            }
        }
        catch (OperationCanceledException)
        {
            DownloadText = "Download cancelled. It will resume where it stopped next time.";
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Update download failed.", ex);
            DownloadText = ex is UpdateException ? ex.Message : $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void Skip()
    {
        if (_release is null)
        {
            return;
        }

        string version = _release.Version.ToString();
        _settings.Update("Updates", s => s.Updates.SkippedVersion = version);
        Status = UpdateCheckStatus.UpToDate;
        StatusText = $"Version {version} will be skipped.";
        RaiseReleaseProperties();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private void RaiseReleaseProperties()
    {
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(AvailableVersion));
        OnPropertyChanged(nameof(ReleaseTitle));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(PublishedText));
        NotifyCommands();
    }

    public void NotifyRepositoryChanged()
    {
        OnPropertyChanged(nameof(Repository));
        OnPropertyChanged(nameof(RepositoryUrl));
        OnPropertyChanged(nameof(IsConfigured));
    }

    private void NotifyCommands()
    {
        CheckCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        CancelDownloadCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        OpenReleaseCommand.NotifyCanExecuteChanged();
    }
}
