using Coreloom.Core.Diagnostics;

namespace Coreloom.Core.Updates;

/// <summary>Compares the running version with the newest GitHub release.</summary>
public sealed class UpdateChecker
{
    private readonly Func<bool, CancellationToken, Task<ReleaseInfo?>> _fetchLatest;

    public UpdateChecker(SemanticVersion currentVersion, string? repository, Func<bool, CancellationToken, Task<ReleaseInfo?>> fetchLatest)
    {
        CurrentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        Repository = repository;
        _fetchLatest = fetchLatest ?? throw new ArgumentNullException(nameof(fetchLatest));
    }

    public SemanticVersion CurrentVersion { get; }

    public string? Repository { get; }

    public bool IsConfigured => GitHubReleaseClient.IsValidRepository(Repository);

    public async Task<UpdateCheckResult> CheckAsync(bool includePreReleases, string? skippedVersion = null, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NotConfigured, null,
                "Update checks are not configured for this build (no GitHub repository set).");
        }

        try
        {
            var latest = await _fetchLatest(includePreReleases, cancellationToken).ConfigureAwait(false);
            if (latest is null)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NoReleases, null, "No releases have been published yet.");
            }

            if (latest.Version > CurrentVersion)
            {
                if (skippedVersion is not null && SemanticVersion.TryParse(skippedVersion, out var skipped) && skipped.Equals(latest.Version))
                {
                    return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latest, $"Version {latest.Version} was skipped.");
                }

                return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, latest, $"Version {latest.Version} is available.");
            }

            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, latest, $"You're up to date ({CurrentVersion}).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn("Updates", "Update check failed.", ex);
            string message = ex is UpdateException ? ex.Message : $"Couldn't check for updates: {ex.Message}";
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null, message);
        }
    }
}
