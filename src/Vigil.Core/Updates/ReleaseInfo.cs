namespace Vigil.Core.Updates;

public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

/// <summary>A published GitHub release.</summary>
public sealed record ReleaseInfo(
    SemanticVersion Version,
    string TagName,
    string Title,
    string Notes,
    Uri HtmlUrl,
    DateTimeOffset? PublishedAt,
    bool IsPreRelease,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>Installer asset (e.g. <c>Vigil-1.2.0-win-x64-Setup.exe</c>).</summary>
    public ReleaseAsset? Installer => Assets.FirstOrDefault(a =>
        a.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) &&
        a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase));

    /// <summary>Portable zip asset (e.g. <c>Vigil-1.2.0-win-x64-Portable.zip</c>).</summary>
    public ReleaseAsset? Portable => Assets.FirstOrDefault(a =>
        a.Name.EndsWith("-Portable.zip", StringComparison.OrdinalIgnoreCase) &&
        a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase));

    /// <summary>Checksum manifest (<c>SHA256SUMS.txt</c>).</summary>
    public ReleaseAsset? Checksums => Assets.FirstOrDefault(a =>
        a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
}

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NotConfigured,
    NoReleases,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release, string Message)
{
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable && Release is not null;
}
