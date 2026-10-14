using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Coreloom.Core.Formatting;

namespace Coreloom.Core.Benchmarks;

public enum SpeedTestPhase
{
    Connecting,
    Latency,
    Download,
    Upload,
    Done,
}

/// <summary>Where the test is: phase, overall progress, the live speed (bits/s) and the latency once measured.</summary>
public readonly record struct SpeedTestProgress(SpeedTestPhase Phase, double Fraction, double BitsPerSecond, double? PingMs = null, double? JitterMs = null);

/// <summary>The test server and the connection it saw.</summary>
public sealed record SpeedTestServer(string Provider, string? Location, string? Network);

public sealed record SpeedTestResult(double DownloadBitsPerSecond, double UploadBitsPerSecond, double PingMs, double JitterMs, SpeedTestServer Server, DateTime Finished)
{
    public double DownloadMbps => DownloadBitsPerSecond / 1_000_000;

    public double UploadMbps => UploadBitsPerSecond / 1_000_000;
}

/// <summary>A problem the user can act on (offline, the server didn't answer…).</summary>
public sealed class SpeedTestException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record SpeedTestOptions
{
    /// <summary>Cloudflare's speed test service: no account or key needed, servers close to almost everyone.</summary>
    public Uri Server { get; init; } = new("https://speed.cloudflare.com/");

    public int LatencySamples { get; init; } = 12;

    public TimeSpan DownloadDuration { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan UploadDuration { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Connections used at once; several are needed to fill a fast line.</summary>
    public int Streams { get; init; } = 6;

    /// <summary>The first part of each transfer, while the connections speed up, isn't counted.</summary>
    public double RampUp { get; init; } = 0.2;

    public int DownloadChunkBytes { get; init; } = 25_000_000;

    public int UploadChunkBytes { get; init; } = 8_000_000;
}

/// <summary>
/// Measures the internet connection: latency and jitter (round trips to the server), then download and upload speed
/// over several connections at once for a fixed time, not counting the first part while connections ramp up.
/// </summary>
public sealed class InternetSpeedTest
{
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly SpeedTestOptions _options;
    private readonly string _userAgent;

    public InternetSpeedTest(SpeedTestOptions? options = null, Func<HttpMessageHandler>? handlerFactory = null, string? userAgent = null)
    {
        _options = options ?? new SpeedTestOptions();
        _handlerFactory = handlerFactory ?? (() => new SocketsHttpHandler
        {
            MaxConnectionsPerServer = Math.Max(1, _options.Streams),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });
        _userAgent = userAgent ?? "Coreloom";
    }

    public async Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(_handlerFactory(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        double total = 2 + _options.DownloadDuration.TotalSeconds + _options.UploadDuration.TotalSeconds; // latency counts as ~2 s
        progress?.Report(new SpeedTestProgress(SpeedTestPhase.Connecting, 0, 0));

        try
        {
            var server = await MetaAsync(client, cancellationToken).ConfigureAwait(false);
            var (ping, jitter) = await LatencyAsync(client, f => progress?.Report(new SpeedTestProgress(SpeedTestPhase.Latency, f * 2 / total, 0)), cancellationToken).ConfigureAwait(false);
            progress?.Report(new SpeedTestProgress(SpeedTestPhase.Latency, 2 / total, 0, ping, jitter));

            double downloadStart = 2;
            double download = await TransferAsync(client, upload: false, _options.DownloadDuration,
                (f, speed) => progress?.Report(new SpeedTestProgress(SpeedTestPhase.Download, (downloadStart + (f * _options.DownloadDuration.TotalSeconds)) / total, speed, ping, jitter)),
                cancellationToken).ConfigureAwait(false);

            double uploadStart = downloadStart + _options.DownloadDuration.TotalSeconds;
            double upload = await TransferAsync(client, upload: true, _options.UploadDuration,
                (f, speed) => progress?.Report(new SpeedTestProgress(SpeedTestPhase.Upload, (uploadStart + (f * _options.UploadDuration.TotalSeconds)) / total, speed, ping, jitter)),
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new SpeedTestProgress(SpeedTestPhase.Done, 1, 0, ping, jitter));
            return new SpeedTestResult(download, upload, ping, jitter, server, DateTime.Now);
        }
        catch (HttpRequestException e)
        {
            throw new SpeedTestException(e.StatusCode is { } code
                ? $"The test server answered with an error ({(int)code}). Try again in a minute."
                : "Couldn't reach the test server. Check that you're online and try again.", e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SpeedTestException("The test server stopped answering. Try again.", e);
        }
    }

    /// <summary>Server location and the network (ISP) it saw; optional, so failures are ignored.</summary>
    private async Task<SpeedTestServer> MetaAsync(HttpClient client, CancellationToken cancellationToken)
    {
        string provider = string.Equals(_options.Server.Host, "speed.cloudflare.com", StringComparison.OrdinalIgnoreCase) ? "Cloudflare" : _options.Server.Host;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await client.GetAsync(new Uri(_options.Server, "meta"), timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ParseMeta(provider, json);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new SpeedTestServer(provider, null, null);
        }
    }

    internal static SpeedTestServer ParseMeta(string provider, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

        string? city = Text("city");
        string? colo = Text("colo");
        string? location = city is not null && colo is not null ? $"{city} ({colo})" : city ?? colo;
        return new SpeedTestServer(provider, location, Text("asOrganization"));
    }

    /// <summary>Median round trip of small requests (the first, which opens the connection, is dropped) and jitter.</summary>
    private async Task<(double Ping, double Jitter)> LatencyAsync(HttpClient client, Action<double> report, CancellationToken cancellationToken)
    {
        var samples = new List<double>(_options.LatencySamples);
        int attempts = Math.Max(2, _options.LatencySamples + 1);
        for (int i = 0; i < attempts; i++)
        {
            using var request = Request(HttpMethod.Get, "__down?bytes=0");
            long started = Stopwatch.GetTimestamp();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            response.EnsureSuccessStatusCode();
            ms -= ServerTime(response.Headers);
            if (i > 0)
            {
                samples.Add(Math.Max(0, ms));
            }

            report((i + 1.0) / attempts);
        }

        return Summarize(samples);
    }

    internal static (double Ping, double Jitter) Summarize(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            return (double.NaN, double.NaN);
        }

        var sorted = samples.Order().ToList();
        double median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
        double jitter = samples.Count < 2 ? 0 : samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return (median, jitter);
    }

    /// <summary>Time the server spent on the request ("Server-Timing: cfRequestDuration;dur=12.3"), in ms.</summary>
    internal static double ServerTime(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Server-Timing", out var values))
        {
            return 0;
        }

        foreach (string value in values)
        {
            int dur = value.IndexOf("dur=", StringComparison.OrdinalIgnoreCase);
            if (dur < 0)
            {
                continue;
            }

            int end = dur + 4;
            while (end < value.Length && (char.IsDigit(value[end]) || value[end] == '.'))
            {
                end++;
            }

            if (double.TryParse(value.AsSpan(dur + 4, end - dur - 4), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
            {
                return Math.Max(0, ms);
            }
        }

        return 0;
    }

    /// <summary>Moves data over several connections for <paramref name="duration"/>; returns bits per second after the ramp-up.</summary>
    private async Task<double> TransferAsync(HttpClient client, bool upload, TimeSpan duration, Action<double, double> report, CancellationToken cancellationToken)
    {
        long bytes = 0;
        var clock = Stopwatch.StartNew();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(duration);
        var failures = new List<Exception>();

        async Task Stream()
        {
            var buffer = new byte[64 * 1024];
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (upload)
                    {
                        using var request = Request(HttpMethod.Post, "__up");
                        request.Content = new CountingContent(_options.UploadChunkBytes, n => Interlocked.Add(ref bytes, n));
                        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                    }
                    else
                    {
                        using var request = Request(HttpMethod.Get, $"__down?bytes={_options.DownloadChunkBytes.ToString(CultureInfo.InvariantCulture)}");
                        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        await using var body = await response.Content.ReadAsStreamAsync(stop.Token).ConfigureAwait(false);
                        int read;
                        while ((read = await body.ReadAsync(buffer, stop.Token).ConfigureAwait(false)) > 0)
                        {
                            Interlocked.Add(ref bytes, read);
                        }
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return; // time's up
                }
                catch (Exception e) when (e is HttpRequestException or IOException)
                {
                    lock (failures)
                    {
                        failures.Add(e);
                    }

                    if (failures.Count > _options.Streams * 2)
                    {
                        return;
                    }

                    await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        var streams = Task.WhenAll(Enumerable.Range(0, Math.Max(1, _options.Streams)).Select(_ => Task.Run(Stream, CancellationToken.None)));
        var samples = new List<(double Seconds, long Bytes)> { (0, 0) };
        while (!streams.IsCompleted)
        {
            await Task.WhenAny(streams, Task.Delay(250, CancellationToken.None)).ConfigureAwait(false);
            double now = clock.Elapsed.TotalSeconds;
            long done = Interlocked.Read(ref bytes);
            samples.Add((now, done));
            report(Math.Clamp(now / duration.TotalSeconds, 0, 1), WindowSpeed(samples, 1.0));
        }

        await streams.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        double result = MeasuredSpeed(samples, duration.TotalSeconds * _options.RampUp);
        if (result <= 0 && failures.Count > 0)
        {
            throw failures[0] is HttpRequestException http ? http : new HttpRequestException(failures[0].Message, failures[0]);
        }

        return result;
    }

    /// <summary>Bits per second over the last <paramref name="window"/> seconds (the live reading).</summary>
    internal static double WindowSpeed(IReadOnlyList<(double Seconds, long Bytes)> samples, double window)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        var last = samples[^1];
        var first = samples.LastOrDefault(s => s.Seconds <= last.Seconds - window, samples[0]);
        double seconds = last.Seconds - first.Seconds;
        return seconds > 0 ? (last.Bytes - first.Bytes) * 8 / seconds : 0;
    }

    /// <summary>Bits per second from <paramref name="skipSeconds"/> to the end (the result).</summary>
    internal static double MeasuredSpeed(IReadOnlyList<(double Seconds, long Bytes)> samples, double skipSeconds)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        var last = samples[^1];
        var first = samples.LastOrDefault(s => s.Seconds <= skipSeconds, samples[0]);
        if (last.Seconds - first.Seconds < 0.5)
        {
            first = samples[0]; // too short to skip anything
        }

        double seconds = last.Seconds - first.Seconds;
        return seconds > 0 ? (last.Bytes - first.Bytes) * 8 / seconds : 0;
    }

    private HttpRequestMessage Request(HttpMethod method, string path) => new(method, new Uri(_options.Server, path))
    {
        Version = HttpVersion.Version11, // one TCP connection per stream, like browsers' speed tests
        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    };

    /// <summary>Upload body of incompressible bytes that counts what has been handed to the connection.</summary>
    private sealed class CountingContent : HttpContent
    {
        private static readonly byte[] Chunk = CreateChunk();
        private readonly long _length;
        private readonly Action<int> _sent;

        public CountingContent(long length, Action<int> sent)
        {
            _length = length;
            _sent = sent;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            long left = _length;
            while (left > 0)
            {
                int n = (int)Math.Min(Chunk.Length, left);
                await stream.WriteAsync(Chunk.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
                _sent(n);
                left -= n;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        private static byte[] CreateChunk()
        {
            var chunk = new byte[64 * 1024];
            System.Random.Shared.NextBytes(chunk);
            return chunk;
        }
    }
}

/// <summary>Ratings for an internet connection on the four-color scale.</summary>
public static class InternetRating
{
    /// <summary>Download speed in Mbit/s: 300+ excellent, 50+ good, 10+ slow, below that very slow.</summary>
    public static ThresholdLevel Download(double mbps) => SpeedRating.HigherIsBetter(mbps, 300, 50, 10);

    /// <summary>Upload speed in Mbit/s: 100+ excellent, 20+ good, 5+ slow.</summary>
    public static ThresholdLevel Upload(double mbps) => SpeedRating.HigherIsBetter(mbps, 100, 20, 5);

    /// <summary>Latency in ms: 20 or less excellent, 50 good, 100 slow.</summary>
    public static ThresholdLevel Ping(double ms) => SpeedRating.LowerIsBetter(ms, 20, 50, 100);

    /// <summary>The top of the live gauge: the next step above the fastest speed seen (Mbit/s).</summary>
    public static double Ceiling(double mbps) => mbps switch
    {
        <= 90 => 100,
        <= 450 => 500,
        <= 900 => 1000,
        <= 2250 => 2500,
        _ => 10000,
    };
}
