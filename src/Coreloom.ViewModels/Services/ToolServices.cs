using Coreloom.Core.Benchmarks;
using Coreloom.Core.Optimize;

namespace Coreloom.ViewModels.Services;

/// <summary>Measures drive speed (the Storage page's benchmark).</summary>
public interface IDiskBenchmarkService
{
    IReadOnlyList<BenchmarkDrive> Drives();

    Task<DiskBenchmarkResult> RunAsync(DiskBenchmarkOptions options, IProgress<DiskBenchmarkProgress> progress, CancellationToken cancellationToken);
}

/// <summary>Measures the internet connection (the Network page's speed test).</summary>
public interface ISpeedTestService
{
    Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress> progress, CancellationToken cancellationToken);
}

/// <summary>What Smart Optimize needs from Windows: junk locations, memory, Microsoft Defender, startup apps and maintenance.</summary>
public interface IOptimizeService
{
    /// <summary>False where Windows features are missing (tests, other systems): the page explains instead of offering actions.</summary>
    bool IsSupported { get; }

    IReadOnlyList<JunkCategory> JunkCategories();

    /// <summary>Size of the Recycle Bin on all drives.</summary>
    JunkSize RecycleBinSize();

    bool EmptyRecycleBin();

    /// <summary>Trims the memory of background apps (and, if asked, clears the standby cache).</summary>
    Task<MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken);

    Task<ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken);

    Task<ScanOutcome> QuickScanAsync(CancellationToken cancellationToken);

    Task<MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken);

    Task<MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken);

    /// <summary>Opens Windows Security (virus and threat protection).</summary>
    void OpenSecurity();

    IReadOnlyList<StartupApp> StartupApps();

    bool SetStartupEnabled(StartupApp app, bool enabled);

    Task<MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken);

    Task<MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken);
}

public sealed class DiskBenchmarkService : IDiskBenchmarkService
{
    public IReadOnlyList<BenchmarkDrive> Drives() => DiskBenchmark.Drives();

    public Task<DiskBenchmarkResult> RunAsync(DiskBenchmarkOptions options, IProgress<DiskBenchmarkProgress> progress, CancellationToken cancellationToken) =>
        Task.Run(() => DiskBenchmark.RunAsync(options, null, progress, cancellationToken), cancellationToken);
}

public sealed class SpeedTestService(string? userAgent = null) : ISpeedTestService
{
    public Task<SpeedTestResult> RunAsync(IProgress<SpeedTestProgress> progress, CancellationToken cancellationToken) =>
        Task.Run(() => new InternetSpeedTest(userAgent: userAgent).RunAsync(progress, cancellationToken), cancellationToken);
}

/// <summary>Stand-in where Smart Optimize can't work: nothing to clean, nothing changes.</summary>
public sealed class UnsupportedOptimizeService : IOptimizeService
{
    public bool IsSupported => false;

    public IReadOnlyList<JunkCategory> JunkCategories() => [];

    public JunkSize RecycleBinSize() => default;

    public bool EmptyRecycleBin() => false;

    public Task<MemoryCleanResult> FreeMemoryAsync(bool clearStandby, CancellationToken cancellationToken) => Task.FromResult(new MemoryCleanResult(0, 0, 0, 0));

    public Task<ProtectionStatus> ProtectionAsync(CancellationToken cancellationToken) => Task.FromResult(ProtectionStatus.Unavailable("Not available on this system."));

    public Task<ScanOutcome> QuickScanAsync(CancellationToken cancellationToken) => Task.FromResult(new ScanOutcome(false, 0, "Not available on this system."));

    public Task<MaintenanceResult> UpdateSignaturesAsync(CancellationToken cancellationToken) => Unavailable();

    public Task<MaintenanceResult> RemoveThreatsAsync(CancellationToken cancellationToken) => Unavailable();

    public void OpenSecurity()
    {
    }

    public IReadOnlyList<StartupApp> StartupApps() => [];

    public bool SetStartupEnabled(StartupApp app, bool enabled) => false;

    public Task<MaintenanceResult> FlushDnsAsync(CancellationToken cancellationToken) => Unavailable();

    public Task<MaintenanceResult> OptimizeDrivesAsync(CancellationToken cancellationToken) => Unavailable();

    private static Task<MaintenanceResult> Unavailable() => Task.FromResult(new MaintenanceResult(false, "Not available on this system."));
}

/// <summary>The tools the pages offer (benchmarks, speed test, Smart Optimize). Tests swap in fakes.</summary>
public sealed class ToolServices
{
    public IDiskBenchmarkService DiskBenchmark { get; init; } = new DiskBenchmarkService();

    public ISpeedTestService SpeedTest { get; init; } = new SpeedTestService();

    public IOptimizeService Optimizer { get; init; } = new UnsupportedOptimizeService();
}
