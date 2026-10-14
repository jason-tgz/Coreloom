using Coreloom.Core.Benchmarks;
using Coreloom.Core.Formatting;

namespace Coreloom.Core.Tests;

public class DiskBenchmarkTests
{
    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "coreloom-bench-" + Guid.NewGuid().ToString("N"), DiskBenchmark.FolderName);

    [Fact]
    public async Task Run_MeasuresEveryTest_AndCleansUp()
    {
        string folder = NewFolder();
        var options = new DiskBenchmarkOptions(folder, 4L << 20) { TestDuration = TimeSpan.FromMilliseconds(150), Unbuffered = false, Headroom = 0 };
        var reports = new List<DiskBenchmarkProgress>();
        var progress = new SyncProgress<DiskBenchmarkProgress>(reports.Add);

        var result = await DiskBenchmark.RunAsync(options, progress: progress);

        Assert.Equal(DiskTests.All.Count, result.Tests.Count);
        Assert.All(result.Tests, t =>
        {
            Assert.True(t.BytesPerSecond > 0, t.Test.Id);
            Assert.True(t.Iops > 0, t.Test.Id);
        });
        Assert.Equal(4L << 20, result.FileSize);
        Assert.NotNull(result.Get(DiskTests.RandomRead));
        Assert.Equal(1, reports[^1].Fraction, 6);
        Assert.True(reports.Select(r => r.Fraction).SequenceEqual(reports.Select(r => r.Fraction).Order()), "progress only moves forward");
        Assert.False(Directory.Exists(folder)); // the folder it made is gone with the file
        Directory.Delete(Path.GetDirectoryName(folder)!);
    }

    [Fact]
    public async Task Run_CanBeCancelled_AndStillCleansUp()
    {
        string folder = NewFolder();
        Directory.CreateDirectory(folder);
        using var cts = new CancellationTokenSource();
        var options = new DiskBenchmarkOptions(folder, 4L << 20) { TestDuration = TimeSpan.FromSeconds(30), Unbuffered = false, Headroom = 0 };
        var progress = new SyncProgress<DiskBenchmarkProgress>(p =>
        {
            if (p.Test is not null)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiskBenchmark.RunAsync(options, progress: progress, cancellationToken: cts.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder)); // test file deleted
        Assert.True(Directory.Exists(folder)); // a folder that was already there is left alone
        Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
    }

    [Fact]
    public async Task Run_RefusesWhenTheDriveIsTooFull()
    {
        string folder = NewFolder();
        var options = new DiskBenchmarkOptions(folder, 1L << 20) { Headroom = long.MaxValue / 2, Unbuffered = false };
        var error = await Assert.ThrowsAsync<DiskBenchmarkException>(() => DiskBenchmark.RunAsync(options));
        Assert.StartsWith("Not enough free space", error.Message);
        Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
    }

    [Fact]
    public void Tests_HaveTheUsualNames()
    {
        Assert.Equal("SEQ1M Q8", DiskTests.SequentialRead.ShortName);
        Assert.Equal("RND4K Q32", DiskTests.RandomDeepWrite.ShortName);
        Assert.Equal("RND4K Q1", DiskTests.RandomRead.ShortName);
        Assert.True(DiskTests.All.First(t => t.Write).Id == "seq-write", "writes come first so reads find data");
    }

    /// <summary>Progress&lt;T&gt; posts to the thread pool; tests need the reports in order and in time.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly object _gate = new();

        public void Report(T value)
        {
            lock (_gate)
            {
                report(value);
            }
        }
    }
}

public class SpeedRatingTests
{
    [Theory]
    [InlineData(7000.0, ThresholdLevel.Cool)]
    [InlineData(550.0, ThresholdLevel.Normal)]
    [InlineData(180.0, ThresholdLevel.Elevated)]
    [InlineData(40.0, ThresholdLevel.Critical)]
    public void SequentialDriveSpeeds_AreRated(double megabytesPerSecond, ThresholdLevel expected)
    {
        Assert.Equal(expected, SpeedRating.Disk(DiskTests.SequentialRead, megabytesPerSecond));
    }

    [Fact]
    public void RandomSpeeds_UseTheirOwnScale()
    {
        Assert.Equal(ThresholdLevel.Cool, SpeedRating.Disk(DiskTests.RandomRead, 70));
        Assert.Equal(ThresholdLevel.Normal, SpeedRating.Disk(DiskTests.RandomRead, 35));
        Assert.Equal(ThresholdLevel.Critical, SpeedRating.Disk(DiskTests.RandomRead, 0.8)); // a hard drive
        Assert.Equal(ThresholdLevel.Normal, SpeedRating.Disk(DiskTests.RandomDeepRead, 350));
    }

    [Fact]
    public void Latency_LowerIsBetter()
    {
        Assert.Equal(ThresholdLevel.Cool, SpeedRating.LowerIsBetter(12, 20, 50, 100));
        Assert.Equal(ThresholdLevel.Elevated, SpeedRating.LowerIsBetter(80, 20, 50, 100));
        Assert.Equal(ThresholdLevel.Critical, SpeedRating.LowerIsBetter(180, 20, 50, 100));
    }

    [Fact]
    public void GaugeFraction_IsASquareRootScale()
    {
        Assert.Equal(0.5, SpeedRating.GaugeFraction(25, 100), 6);
        Assert.Equal(1, SpeedRating.GaugeFraction(500, 100), 6);
        Assert.Equal(0, SpeedRating.GaugeFraction(double.NaN, 100), 6);
        Assert.Equal(GaugeTone.Green, SpeedRating.Tone(ThresholdLevel.Cool));
        Assert.Equal(GaugeTone.Red, SpeedRating.Tone(ThresholdLevel.Critical));
    }
}

public class InternetSpeedTestTests
{
    private static SpeedTestOptions Fast => new()
    {
        Server = new Uri("https://speed.example.test/"),
        LatencySamples = 3,
        DownloadDuration = TimeSpan.FromMilliseconds(400),
        UploadDuration = TimeSpan.FromMilliseconds(400),
        Streams = 2,
        DownloadChunkBytes = 200_000,
        UploadChunkBytes = 100_000,
    };

    [Fact]
    public async Task Run_MeasuresLatencyDownloadAndUpload()
    {
        var handler = new FakeSpeedServer();
        var test = new InternetSpeedTest(Fast, () => handler);
        var phases = new List<SpeedTestPhase>();
        var result = await test.RunAsync(new InlineProgress<SpeedTestProgress>(p =>
        {
            lock (phases)
            {
                if (phases.Count == 0 || phases[^1] != p.Phase)
                {
                    phases.Add(p.Phase);
                }
            }
        }));

        Assert.True(result.DownloadBitsPerSecond > 0);
        Assert.True(result.UploadBitsPerSecond > 0);
        Assert.InRange(result.PingMs, 0, 1000);
        Assert.Equal("speed.example.test", result.Server.Provider);
        Assert.Equal("Lagos (LOS)", result.Server.Location);
        Assert.Equal("Example Telecom", result.Server.Network);
        Assert.Equal<SpeedTestPhase>(new[] { SpeedTestPhase.Connecting, SpeedTestPhase.Latency, SpeedTestPhase.Download, SpeedTestPhase.Upload, SpeedTestPhase.Done }, phases);
        Assert.True(handler.UploadedBytes > 0);
    }

    [Fact]
    public async Task Run_Offline_GivesAFriendlyError()
    {
        var test = new InternetSpeedTest(Fast, () => new FakeSpeedServer { Offline = true });
        var error = await Assert.ThrowsAsync<SpeedTestException>(() => test.RunAsync());
        Assert.Contains("online", error.Message);
    }

    [Fact]
    public void Latency_IsTheMedian_AndJitterTheAverageChange()
    {
        var (ping, jitter) = InternetSpeedTest.Summarize(new double[] { 10, 14, 12, 30, 11 });
        Assert.Equal(12, ping, 6);
        Assert.Equal((4 + 2 + 18 + 19) / 4.0, jitter, 6);
    }

    [Fact]
    public void Speeds_SkipTheRampUp()
    {
        var samples = new List<(double, long)> { (0, 0), (0.5, 1_000), (1.0, 101_000), (2.0, 301_000) };
        Assert.Equal(200_000 * 8 / 1.0, InternetSpeedTest.MeasuredSpeed(samples, 1.0), 3);
        Assert.Equal(200_000 * 8 / 1.0, InternetSpeedTest.WindowSpeed(samples, 1.0), 3);
    }

    [Fact]
    public void Ratings_FollowTheScale()
    {
        Assert.Equal(ThresholdLevel.Cool, InternetRating.Download(940));
        Assert.Equal(ThresholdLevel.Normal, InternetRating.Download(120));
        Assert.Equal(ThresholdLevel.Critical, InternetRating.Download(4));
        Assert.Equal(ThresholdLevel.Elevated, InternetRating.Ping(75));
        Assert.Equal(1000, InternetRating.Ceiling(870));
        Assert.Equal(100, InternetRating.Ceiling(12));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>Answers like speed.cloudflare.com: /meta, /__down?bytes=N and /__up.</summary>
    private sealed class FakeSpeedServer : HttpMessageHandler
    {
        private long _uploaded;

        public bool Offline { get; init; }

        public long UploadedBytes => Interlocked.Read(ref _uploaded);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
            {
                throw new HttpRequestException("No such host is known.");
            }

            await Task.Delay(2, cancellationToken);
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/meta")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"city\":\"Lagos\",\"colo\":\"LOS\",\"asOrganization\":\"Example Telecom\",\"clientIp\":\"203.0.113.9\"}"),
                };
            }

            if (path == "/__up")
            {
                var sink = new CountingStream(n => Interlocked.Add(ref _uploaded, n));
                await request.Content!.CopyToAsync(sink, cancellationToken);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }

            int bytes = int.Parse(request.RequestUri.Query.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes]) };
            response.Headers.TryAddWithoutValidation("Server-Timing", "cfRequestDuration;dur=1.5");
            return response;
        }
    }

    private sealed class CountingStream(Action<int> written) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => written(count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            written(buffer.Length);
            return ValueTask.CompletedTask;
        }
    }
}
