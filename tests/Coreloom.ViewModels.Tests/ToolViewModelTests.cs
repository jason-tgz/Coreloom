using Coreloom.Core.Benchmarks;
using Coreloom.Core.Formatting;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

public class StorageBenchmarkViewModelTests
{
    private sealed class FakeBenchmark : IDiskBenchmarkService
    {
        public Exception? Failure { get; init; }

        public DiskBenchmarkOptions? LastOptions { get; private set; }

        public IReadOnlyList<BenchmarkDrive> Drives() =>
        [
            new("C:", "System", @"C:\Temp\Coreloom benchmark", 300L << 30, 1L << 40),
            new("D:", string.Empty, @"D:\Coreloom benchmark", 900L << 30, 2L << 40),
        ];

        public async Task<DiskBenchmarkResult> RunAsync(DiskBenchmarkOptions options, IProgress<DiskBenchmarkProgress> progress, CancellationToken cancellationToken)
        {
            LastOptions = options;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                throw Failure;
            }

            progress.Report(new DiskBenchmarkProgress(DiskTests.SequentialRead, 0.5, 3_000_000_000));
            var results = DiskTests.All.Select(t => new DiskTestResult(t, t.Random ? 60_000_000 : 6_500_000_000, t.Random ? 15_000 : 6_200, 0.07)).ToList();
            return new DiskBenchmarkResult(results, options.FileSize, TimeSpan.FromSeconds(27));
        }
    }

    [Fact]
    public void Drives_AreListed_AndTheFirstIsChosen()
    {
        var vm = new StorageBenchmarkViewModel(new FakeBenchmark());
        Assert.Empty(vm.Drives);
        vm.EnsureDrives();
        Assert.Equal(2, vm.Drives.Count);
        Assert.Equal("C: System · 300 GB free of 1 TB", vm.Drives[0].Text);
        Assert.Same(vm.Drives[0], vm.SelectedDrive);
        Assert.Equal("1 GB", vm.SelectedSize.Text);
        Assert.True(vm.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task Run_FillsTheGaugesAndTable()
    {
        var service = new FakeBenchmark();
        var vm = new StorageBenchmarkViewModel(service);
        vm.EnsureDrives();
        vm.SelectedDrive = vm.Drives[1];
        vm.SelectedSize = vm.Sizes[0];

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\Coreloom benchmark", service.LastOptions!.Directory);
        Assert.Equal(256L << 20, service.LastOptions.FileSize);
        Assert.True(vm.HasResult);
        Assert.False(vm.IsRunning);
        Assert.Equal("6,500", vm.ReadGauge.Number);
        Assert.Equal(ColorKeys.ToneCool, vm.ReadGauge.ColorKey); // NVMe-class: green
        Assert.Equal("Sequential read", vm.ReadGauge.Detail);
        var random = vm.Rows[2];
        Assert.Equal("60.0", random.Read.Number);
        Assert.Equal(ColorKeys.ToneCool, random.Read.ColorKey);
        Assert.Equal("15,000 IOPS · 0.07 ms", random.Read.Detail);
        Assert.InRange(vm.Rows[0].Write.Fraction, 0.6, 0.75); // square-root scale against 14 GB/s
        Assert.Equal("D: · 256 MB test · finished in 27 s", vm.Summary);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Problems_AreShown_AndNothingIsLeftHalfFilled()
    {
        var vm = new StorageBenchmarkViewModel(new FakeBenchmark { Failure = new DiskBenchmarkException("Not enough free space: the test needs 1.3 GB free on this drive.") });
        vm.EnsureDrives();
        await vm.RunCommand.ExecuteAsync(null);

        Assert.False(vm.HasResult);
        Assert.StartsWith("Not enough free space", vm.Error);
        Assert.False(vm.ReadGauge.HasValue);
        Assert.All(vm.Rows, r => Assert.False(r.Read.HasValue));
        Assert.True(vm.RunCommand.CanExecute(null));
    }
}

public class SpeedTestViewModelTests
{
    private sealed class FakeSpeedTest : ISpeedTestService
    {
        public bool Offline { get; init; }

        public async Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress> progress, CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (Offline)
            {
                throw new SpeedTestException("Couldn't reach the test server. Check that you're online and try again.");
            }

            progress.Report(new SpeedTestProgress(SpeedTestPhase.Download, 0.4, 480_000_000, 11, 2));
            return new SpeedTestResult(512_300_000, 48_200_000, 11.4, 1.8, new SpeedTestServer("Cloudflare", "Lagos (LOS)", "Example Telecom"), new DateTime(2026, 10, 14, 14, 32, 0));
        }
    }

    [Fact]
    public async Task Run_RatesEachResult()
    {
        var vm = new SpeedTestViewModel(new FakeSpeedTest());
        Assert.Equal("Start test", vm.RunText);
        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.HasResult);
        Assert.Equal("Test again", vm.RunText);
        Assert.Equal("512", vm.Download.Number);
        Assert.Equal(ColorKeys.ToneCool, vm.Download.ColorKey); // 300+ Mbps: excellent
        Assert.Equal("48.2", vm.Upload.Number);
        Assert.Equal(ColorKeys.ToneNormal, vm.Upload.ColorKey);
        Assert.Equal("11.4", vm.Ping.Number);
        Assert.Equal(ColorKeys.ToneCool, vm.Ping.ColorKey);
        Assert.Equal(GaugeTone.Green, vm.Live.Tone);
        Assert.Equal("Cloudflare · Lagos (LOS) · Example Telecom", vm.Server);
        Assert.Equal("Done", vm.Phase);
        Assert.Equal(1, vm.Progress, 6);
    }

    [Fact]
    public async Task Offline_ShowsTheProblem()
    {
        var vm = new SpeedTestViewModel(new FakeSpeedTest { Offline = true });
        await vm.RunCommand.ExecuteAsync(null);
        Assert.False(vm.HasResult);
        Assert.Contains("online", vm.Error);
        Assert.False(vm.Download.HasValue);
        Assert.Equal("Ready", vm.Phase);
    }
}
