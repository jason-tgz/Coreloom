using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Vigil.Core;
using Vigil.Core.Diagnostics;
using Vigil.Core.Formatting;
using Vigil.Core.Telemetry;
using Vigil.Hardware;

namespace Vigil.Diagnostics;

/// <summary>
/// VigilDiag — headless hardware discovery and verification.
///   VigilDiag.exe [--samples N] [--interval MS] [--out FILE] [--no-lhm] [--compare] [--lhm-report] [--no-raw] [--verbose]
/// Writes a Markdown report listing detected hardware, available/missing sensors (with reasons),
/// provider health, polling cost, Vigil's own CPU/memory overhead and optional nvidia-smi cross-checks.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var options = Options.Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        var memorySink = new MemoryLogSink(500);
        Log.AddSink(memorySink);
        Log.MinimumLevel = options.Verbose ? LogLevel.Debug : LogLevel.Info;
        if (options.Verbose)
        {
            Log.AddSink(new ConsoleSink());
        }

        var app = AppVersionInfo.FromAssembly(Assembly.GetExecutingAssembly());
        var formatter = new UnitFormatter(culture: CultureInfo.InvariantCulture);
        WriteHeading($"Vigil diagnostics {app.InformationalVersion}");

        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();

        using var host = MonitoringHost.Create(new HardwareOptions
        {
            UseLibreHardwareMonitor = options.UseLhm,
            PollingInterval = TimeSpan.FromMilliseconds(options.IntervalMs),
        });

        var caps = host.Capabilities;
        Console.WriteLine($"  Administrator:        {(caps.IsElevated ? "yes" : "no")}");
        Console.WriteLine($"  PawnIO driver:        {(caps.PawnIoInstalled ? "installed " + caps.PawnIoVersion : "not installed")}");
        Console.WriteLine($"  LibreHardwareMonitor: {(options.UseLhm && MonitoringHost.LibreHardwareMonitorIncluded ? "enabled" : "disabled")}");
        Console.WriteLine();

        WriteHeading("Initializing providers");
        var initWatch = Stopwatch.StartNew();
        host.Engine.InitializeAll();
        initWatch.Stop();
        foreach (var status in host.Engine.ProviderStatuses)
        {
            Console.WriteLine($"  {status.Name,-26} {status.State,-12} {status.InitMilliseconds,6:0} ms  {status.Message}{(status.LastError is null ? string.Empty : " — " + status.LastError)}");
        }

        Console.WriteLine($"  Total initialization: {initWatch.Elapsed.TotalMilliseconds:0} ms");
        Console.WriteLine();

        WriteHeading($"Sampling ({options.Samples} × {options.IntervalMs} ms)");
        var pollTimes = new List<double>();
        TelemetryFrame frame = host.Engine.PollOnce();
        var cpuAfterInit = process.TotalProcessorTime;
        var wallAfterInit = wall.Elapsed;
        long peakWorkingSet = 0;
        for (int i = 0; i < options.Samples; i++)
        {
            Thread.Sleep(options.IntervalMs);
            var sw = Stopwatch.StartNew();
            frame = host.Engine.PollOnce();
            sw.Stop();
            pollTimes.Add(sw.Elapsed.TotalMilliseconds);
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            Console.WriteLine($"  sample {i + 1,2}: {frame.Count,4} values in {sw.Elapsed.TotalMilliseconds,6:0.0} ms");
        }

        process.Refresh();
        double samplingCpu = (process.TotalProcessorTime - cpuAfterInit).TotalSeconds;
        double samplingWall = (wall.Elapsed - wallAfterInit).TotalSeconds;
        double overheadPercent = samplingWall > 0 ? samplingCpu / (samplingWall * Environment.ProcessorCount) * 100 : 0;
        Console.WriteLine();

        WriteHeading("Key sensors");
        foreach (var (id, label) in DiagnosticReportBuilder.KeyMetrics)
        {
            if (frame.TryGetSample(id, out var sample))
            {
                Console.WriteLine($"  {label,-40} {formatter.Format(sample.Source.Descriptor.Kind, sample.Value),14}   {sample.Source.Provider} ({sample.Source.Provenance})");
            }
            else
            {
                string reason = host.Engine.Catalog.Get(id)?.UnavailableReason ?? host.Engine.Catalog.GetUnavailableReason(id) ?? "not reported";
                Console.WriteLine($"  {label,-40} {"—",14}   {reason}");
            }
        }

        Console.WriteLine();

        var extra = new List<KeyValuePair<string, string>>();
        var overhead = new StringBuilder();
        overhead.AppendLine($"- Samples: {options.Samples} at {options.IntervalMs} ms");
        overhead.AppendLine($"- Provider initialization: {initWatch.Elapsed.TotalMilliseconds:0} ms");
        if (pollTimes.Count > 0)
        {
            overhead.AppendLine($"- Poll duration: avg {pollTimes.Average():0.0} ms, max {pollTimes.Max():0.0} ms");
        }

        overhead.AppendLine($"- CPU used while sampling: {overheadPercent:0.00} % of total CPU capacity ({samplingCpu * 1000:0} ms CPU over {samplingWall:0.0} s)");
        overhead.AppendLine($"- Peak working set: {formatter.Bytes(peakWorkingSet)}");
        overhead.AppendLine($"- History buffer capacity: {host.Engine.History.Capacity} samples per metric ({host.Engine.History.SeriesCount} tracked metrics, ≈{formatter.Bytes(host.Engine.History.EstimatedBytes)})");
        overhead.AppendLine();
        overhead.AppendLine("_This measures the headless collector only. The WPF app shows its own live overhead under Settings › Diagnostics._");
        extra.Add(new("Polling cost and overhead", overhead.ToString()));
        Console.WriteLine($"  Overhead while sampling: {overheadPercent:0.00} % CPU, peak working set {formatter.Bytes(peakWorkingSet)}");

        if (options.CompareNvidiaSmi)
        {
            WriteHeading("Cross-check with nvidia-smi");
            string comparison = NvidiaSmiComparer.Compare(host, frame);
            Console.WriteLine(comparison);
            extra.Add(new("Cross-check with nvidia-smi", comparison));
        }

        extra.Add(new("Manual comparison checklist", ManualChecklist()));

        if (options.IncludeLhmReport && host.GetLibreHardwareMonitorReport() is { } lhmReport)
        {
            extra.Add(new("LibreHardwareMonitor report", "```\n" + lhmReport + "\n```"));
        }

        string report = DiagnosticReportBuilder.Build(new DiagnosticReportInput
        {
            App = app,
            Capabilities = caps,
            Providers = host.Engine.ProviderStatuses,
            Devices = host.Engine.Devices.GetAll(),
            Metrics = host.Engine.Catalog.Snapshot(),
            Frame = frame,
            RecentLog = memorySink.Snapshot(),
            ExtraSections = extra,
            IncludeRawSensors = options.IncludeRaw,
        }, formatter);

        string path = options.OutputPath ?? Path.Combine(Environment.CurrentDirectory, $"vigil-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        try
        {
            File.WriteAllText(path, report, new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine($"Report written to {path}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not write the report: {ex.Message}");
            return 2;
        }

        return host.Engine.ProviderStatuses.Any(s => s.State == ProviderState.Faulted) ? 1 : 0;
    }

    private static string ManualChecklist() => """
        Compare these readings side by side with established tools while the system is idle and again under load
        (e.g. a game or benchmark). Expect small differences because tools sample at different moments.

        | Vigil reading | Compare with | Typical tolerance |
        |---|---|---|
        | GPU temperature, utilization, clocks, power, VRAM | NVIDIA app overlay / `nvidia-smi` | ±1–2 °C, ±5 %, ±1 bin |
        | GPU hot spot, memory junction, fan RPM, voltage | HWiNFO64 (GPU section) | ±2 °C, ±50 RPM |
        | CPU utilization (total and per thread) | Task Manager › Performance › CPU | ±3 % |
        | CPU temperature (Tctl/Tdie), CCD temps, package power | HWiNFO64 / Ryzen Master | ±1–2 °C, ±5 W |
        | Effective clocks | HWiNFO64 "Core effective clock" | ±50 MHz |
        | RAM in use / available | Task Manager › Memory | ±0.1 GB |
        | Disk read/write, active time | Task Manager › Disk / Resource Monitor | ±5 % |
        | Network throughput | Task Manager › Ethernet/Wi-Fi | ±5 % |
        """;

    private static void WriteHeading(string text)
    {
        Console.WriteLine(text);
        Console.WriteLine(new string('─', Math.Min(78, text.Length + 4)));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            VigilDiag — Vigil hardware discovery and diagnostics

            Usage: VigilDiag.exe [options]

              --samples N      Number of polling samples to take (default 5)
              --interval MS    Interval between samples in milliseconds (default 1000)
              --out FILE       Report path (default: vigil-diagnostics-<timestamp>.md in the current folder)
              --no-lhm         Disable LibreHardwareMonitor (Windows APIs + NVML only)
              --compare        Cross-check NVIDIA readings against nvidia-smi
              --lhm-report     Append LibreHardwareMonitor's full hardware report
              --no-raw         Omit the raw sensor table
              --verbose        Print log output to the console
              --help           Show this help

            Run from an elevated terminal to include sensors that need administrator rights.
            """);
    }

    private sealed class Options
    {
        public int Samples { get; private set; } = 5;

        public int IntervalMs { get; private set; } = 1000;

        public string? OutputPath { get; private set; }

        public bool UseLhm { get; private set; } = true;

        public bool CompareNvidiaSmi { get; private set; }

        public bool IncludeLhmReport { get; private set; }

        public bool IncludeRaw { get; private set; } = true;

        public bool Verbose { get; private set; }

        public bool ShowHelp { get; private set; }

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string? Next() => i + 1 < args.Length ? args[++i] : null;
                switch (a)
                {
                    case "--samples" when int.TryParse(Next(), out int n):
                        o.Samples = Math.Clamp(n, 1, 600);
                        break;
                    case "--interval" when int.TryParse(Next(), out int ms):
                        o.IntervalMs = Math.Clamp(ms, 250, 10_000);
                        break;
                    case "--out":
                        o.OutputPath = Next();
                        break;
                    case "--no-lhm":
                        o.UseLhm = false;
                        break;
                    case "--compare":
                        o.CompareNvidiaSmi = true;
                        break;
                    case "--lhm-report":
                        o.IncludeLhmReport = true;
                        break;
                    case "--no-raw":
                        o.IncludeRaw = false;
                        break;
                    case "--verbose":
                        o.Verbose = true;
                        break;
                    case "--help" or "-h" or "/?":
                        o.ShowHelp = true;
                        break;
                }
            }

            return o;
        }
    }

    private sealed class ConsoleSink : ILogSink
    {
        public void Write(in LogEntry entry) => Console.Error.WriteLine("  " + entry.Format());

        public void Flush()
        {
        }
    }
}
