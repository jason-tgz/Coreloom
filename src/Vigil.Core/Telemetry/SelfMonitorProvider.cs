using System.Diagnostics;

namespace Vigil.Core.Telemetry;

/// <summary>
/// Measures Vigil's own resource usage (so overhead can be verified in the app) and system uptime.
/// </summary>
public sealed class SelfMonitorProvider : ITelemetryProvider
{
    private Process? _process;
    private MetricHandle? _cpu;
    private MetricHandle? _workingSet;
    private MetricHandle? _private;
    private MetricHandle? _uptime;
    private TimeSpan _lastCpu;
    private long _lastWallTicks;

    public string Name => "Vigil process";

    public MonitoringSection Section => MonitoringSection.System;

    public int InitStage => 0;

    public TimeSpan MinimumPollInterval => TimeSpan.Zero;

    public ProviderInitResult Initialize(IProviderContext context)
    {
        Shutdown();
        _process = Process.GetCurrentProcess();
        _lastCpu = _process.TotalProcessorTime;
        _lastWallTicks = Stopwatch.GetTimestamp();

        _cpu = context.Register(new MetricDescriptor(MetricIds.AppCpu, "Vigil CPU usage", MetricCategory.Application, MetricKind.Load)
        {
            Group = "Vigil",
            DeviceId = MetricIds.SystemDeviceId,
        }, Provenance.Measured, 0, "Process CPU time ÷ (elapsed time × logical processors)");

        _workingSet = context.Register(new MetricDescriptor(MetricIds.AppWorkingSet, "Vigil working set", MetricCategory.Application, MetricKind.Data)
        {
            Group = "Vigil",
            DeviceId = MetricIds.SystemDeviceId,
        });

        _private = context.Register(new MetricDescriptor(MetricIds.AppPrivateBytes, "Vigil private memory", MetricCategory.Application, MetricKind.Data)
        {
            Group = "Vigil",
            DeviceId = MetricIds.SystemDeviceId,
        });

        _uptime = context.Register(new MetricDescriptor(MetricIds.SystemUptime, "System uptime", MetricCategory.System, MetricKind.Duration)
        {
            Group = "System",
            DeviceId = MetricIds.SystemDeviceId,
            TrackHistory = false,
        });

        return ProviderInitResult.Running();
    }

    public void Poll(IMetricWriter writer)
    {
        if (_process is null)
        {
            return;
        }

        _process.Refresh();
        var cpu = _process.TotalProcessorTime;
        long now = Stopwatch.GetTimestamp();
        double wallSeconds = Stopwatch.GetElapsedTime(_lastWallTicks, now).TotalSeconds;
        if (wallSeconds > 0.05)
        {
            double cpuSeconds = (cpu - _lastCpu).TotalSeconds;
            double percent = cpuSeconds / (wallSeconds * Environment.ProcessorCount) * 100.0;
            writer.Write(_cpu!, Math.Clamp(percent, 0, 100));
            _lastCpu = cpu;
            _lastWallTicks = now;
        }

        writer.Write(_workingSet!, _process.WorkingSet64);
        writer.Write(_private!, _process.PrivateMemorySize64);
        writer.Write(_uptime!, Environment.TickCount64 / 1000.0);
    }

    public void Shutdown()
    {
        _process?.Dispose();
        _process = null;
    }

    public void Dispose() => Shutdown();
}
