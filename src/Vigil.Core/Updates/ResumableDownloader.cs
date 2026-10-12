using System.Net;
using System.Net.Http.Headers;
using Vigil.Core.Diagnostics;

namespace Vigil.Core.Updates;

public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

/// <summary>
/// Downloads a file to <c>destination.partial</c> and renames it when complete. Interrupted downloads
/// resume with an HTTP Range request; servers that ignore Range cause a clean restart. Transient
/// network failures are retried with back-off.
/// </summary>
public sealed class ResumableDownloader
{
    private readonly HttpClient _http;

    public ResumableDownloader(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public int MaxAttempts { get; init; } = 4;

    public string UserAgent { get; init; } = "Vigil-Updater";

    public async Task DownloadAsync(Uri url, string destination, long? expectedSize, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        string partial = destination + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadOnceAsync(url, partial, expectedSize, progress, cancellationToken).ConfigureAwait(false);
                File.Move(partial, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex) && !cancellationToken.IsCancellationRequested)
            {
                Log.Warn("Updates", $"Download attempt {attempt} failed; retrying.", ex);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DownloadOnceAsync(Uri url, string partial, long? expectedSize, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        long existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (expectedSize is > 0 && existing > expectedSize)
        {
            File.Delete(partial);
            existing = 0;
        }

        if (expectedSize is > 0 && existing == expectedSize)
        {
            progress?.Report(new DownloadProgress(existing, expectedSize));
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file is stale or already complete; start over.
            File.Delete(partial);
            throw new IOException("Server rejected the resume request; restarting download.");
        }

        response.EnsureSuccessStatusCode();
        bool resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
        {
            existing = 0;
        }

        long? total = expectedSize;
        if (total is null or <= 0)
        {
            total = response.Content.Headers.ContentLength is long length ? length + existing : null;
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(partial, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        byte[] buffer = new byte[1 << 16];
        long received = existing;
        long lastReport = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (received - lastReport >= 256 * 1024)
            {
                lastReport = received;
                progress?.Report(new DownloadProgress(received, total));
            }
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new DownloadProgress(received, total));

        if (total is > 0 && received != total)
        {
            throw new IOException($"Download incomplete ({received} of {total} bytes).");
        }
    }

    private static bool IsTransient(Exception ex) => ex is HttpRequestException or IOException or TaskCanceledException { InnerException: TimeoutException };
}
