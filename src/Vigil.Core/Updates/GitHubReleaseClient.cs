using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Vigil.Core.Updates;

/// <summary>
/// Reads releases from the public GitHub REST API. No access token is required for public repositories
/// (unauthenticated requests are limited to 60/hour per IP, far more than the updater needs).
/// </summary>
public sealed class GitHubReleaseClient
{
    private readonly HttpClient _http;
    private readonly string _repository;

    public GitHubReleaseClient(HttpClient http, string repository, string userAgent)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (!IsValidRepository(repository))
        {
            throw new ArgumentException("Repository must be in the form 'owner/name'.", nameof(repository));
        }

        _repository = repository.Trim();
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? "Vigil-Updater" : userAgent;
    }

    public string UserAgent { get; }

    public Uri ApiBase { get; init; } = new("https://api.github.com/");

    public static bool IsValidRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return false;
        }

        string[] parts = repository.Trim().Split('/');
        return parts.Length == 2
            && parts.All(p => p.Length > 0 && p.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
            && !repository.Trim().Equals("OWNER/Vigil", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the newest release, or null when the repository has no (matching) releases.
    /// Drafts are always ignored; pre-releases only when <paramref name="includePreReleases"/> is set.
    /// </summary>
    public async Task<ReleaseInfo?> GetLatestAsync(bool includePreReleases, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(ApiBase, $"repos/{_repository}/releases?per_page=20");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new UpdateException($"Repository '{_repository}' was not found or is private.");
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new UpdateException("GitHub rate limit reached. Try again later.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return SelectLatest(ParseReleases(document.RootElement), includePreReleases);
    }

    public static ReleaseInfo? SelectLatest(IEnumerable<ReleaseInfo> releases, bool includePreReleases) =>
        releases
            .Where(r => includePreReleases || !r.IsPreRelease)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();

    /// <summary>Parses the JSON array returned by <c>GET /repos/{owner}/{repo}/releases</c>.</summary>
    public static IReadOnlyList<ReleaseInfo> ParseReleases(JsonElement root)
    {
        var list = new List<ReleaseInfo>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var element in root.EnumerateArray())
        {
            var release = ParseRelease(element);
            if (release is not null)
            {
                list.Add(release);
            }
        }

        return list;
    }

    public static ReleaseInfo? ParseRelease(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (GetBool(element, "draft"))
        {
            return null;
        }

        string tag = GetString(element, "tag_name") ?? string.Empty;
        if (!SemanticVersion.TryParse(tag, out var version))
        {
            return null;
        }

        var assets = new List<ReleaseAsset>();
        if (element.TryGetProperty("assets", out var assetArray) && assetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetArray.EnumerateArray())
            {
                string? name = GetString(asset, "name");
                string? url = GetString(asset, "browser_download_url");
                if (name is null || url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                long size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out long s) ? s : 0;
                assets.Add(new ReleaseAsset(name, uri, size));
            }
        }

        string htmlUrl = GetString(element, "html_url") ?? "https://github.com/";
        DateTimeOffset? published = null;
        if (GetString(element, "published_at") is { } publishedText && DateTimeOffset.TryParse(publishedText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var p))
        {
            published = p;
        }

        return new ReleaseInfo(
            version,
            tag,
            GetString(element, "name") is { Length: > 0 } title ? title : tag,
            GetString(element, "body") ?? string.Empty,
            Uri.TryCreate(htmlUrl, UriKind.Absolute, out var html) ? html : new Uri("https://github.com/"),
            published,
            GetBool(element, "prerelease") || version.IsPreRelease,
            assets);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message)
        : base(message)
    {
    }

    public UpdateException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
