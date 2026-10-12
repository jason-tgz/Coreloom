using System.Diagnostics;
using System.Globalization;
using System.Text;
using Coreloom.Core.Telemetry;
using Coreloom.Hardware;

namespace Coreloom.Diagnostics;

/// <summary>
/// Runs <c>nvidia-smi</c> (installed with the NVIDIA driver) right after a Coreloom poll and compares the
/// readings. Both read NVML, so large differences indicate a mapping or unit bug in Coreloom.
/// </summary>
internal static class NvidiaSmiComparer
{
    private const string Query = "name,temperature.gpu,utilization.gpu,utilization.memory,clocks.gr,clocks.mem,power.draw,power.limit,memory.used,memory.total,fan.speed,pstate";

    public static string Compare(MonitoringHost host, TelemetryFrame frame)
    {
        string? exe = FindNvidiaSmi();
        if (exe is null)
        {
            return "_nvidia-smi.exe was not found (it ships with the NVIDIA driver in System32)._";
        }

        string output;
        try
        {
            var psi = new ProcessStartInfo(exe, $"--query-gpu={Query} --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi)!;
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
        }
        catch (Exception ex)
        {
            return $"_Running nvidia-smi failed: {ex.Message}_";
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return "_nvidia-smi returned no data._";
        }

        var sb = new StringBuilder();
        sb.AppendLine("| GPU | Metric | Coreloom | nvidia-smi | Δ | Result |");
        sb.AppendLine("|---|---|---:|---:|---:|---|");
        int failures = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            string[] f = lines[index].Split(',').Select(s => s.Trim()).ToArray();
            if (f.Length < 12)
            {
                continue;
            }

            // NVML index i maps to the slot registered as "nvml:i".
            int slot = host.Services.Gpus.SlotForSource($"nvml:{index}") ?? index;
            string gpu = f[0];

            void Row(string metric, string id, string smiText, double tolerance, bool relative, double scale = 1)
            {
                if (!double.TryParse(smiText, NumberStyles.Float, CultureInfo.InvariantCulture, out double smi))
                {
                    sb.AppendLine($"| {gpu} | {metric} | {Fmt(frame.GetValue(id))} | {smiText} | — | not comparable |");
                    return;
                }

                double? coreloom = frame.GetValue(id) is double v ? v * scale : null;
                if (coreloom is null)
                {
                    sb.AppendLine($"| {gpu} | {metric} | — | {smi:0.##} | — | missing in Coreloom |");
                    failures++;
                    return;
                }

                double delta = coreloom.Value - smi;
                double allowed = relative ? Math.Abs(smi) * tolerance : tolerance;
                bool ok = Math.Abs(delta) <= Math.Max(allowed, 1e-9);
                if (!ok)
                {
                    failures++;
                }

                sb.AppendLine($"| {gpu} | {metric} | {coreloom.Value:0.##} | {smi:0.##} | {delta:+0.##;-0.##;0} | {(ok ? "OK" : "CHECK")} |");
            }

            Row("Temperature (°C)", MetricIds.GpuTemperature(slot), f[1], 2, relative: false);
            Row("Utilization (%)", MetricIds.GpuLoad(slot), f[2], 15, relative: false);
            Row("Memory controller (%)", MetricIds.GpuMemoryControllerLoad(slot), f[3], 15, relative: false);
            Row("Core clock (MHz)", MetricIds.GpuCoreClock(slot), f[4], 0.10, relative: true);
            Row("Memory clock (MHz)", MetricIds.GpuMemoryClock(slot), f[5], 0.05, relative: true);
            Row("Power (W)", MetricIds.GpuPower(slot), f[6], 0.20, relative: true);
            Row("Power limit (W)", MetricIds.GpuPowerLimit(slot), f[7], 0.01, relative: true);
            Row("VRAM used (MiB)", MetricIds.GpuMemoryUsed(slot), f[8], 0.05, relative: true, scale: 1.0 / (1024 * 1024));
            Row("VRAM total (MiB)", MetricIds.GpuMemoryTotal(slot), f[9], 0.01, relative: true, scale: 1.0 / (1024 * 1024));
            Row("Fan (%)", MetricIds.GpuFanPercent(slot, 1), f[10], 5, relative: false);

            string pstate = frame.GetValue(MetricIds.GpuPerformanceState(slot)) is double p ? $"P{(int)p}" : "—";
            sb.AppendLine($"| {gpu} | Performance state | {pstate} | {f[11]} | — | {(pstate == f[11] ? "OK" : "CHECK (may change between samples)")} |");
        }

        sb.AppendLine();
        sb.AppendLine(failures == 0
            ? "All compared readings agree within tolerance."
            : $"{failures} reading(s) differ beyond tolerance. Utilization, clocks and power fluctuate between the two samples; re-run under steady load before treating this as a bug.");
        return sb.ToString();
    }

    private static string Fmt(double? value) => value is double v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "—";

    private static string? FindNvidiaSmi()
    {
        string system = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (File.Exists(system))
        {
            return system;
        }

        string? programFiles = Environment.GetEnvironmentVariable("ProgramW6432");
        if (programFiles is not null)
        {
            string legacy = Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            if (File.Exists(legacy))
            {
                return legacy;
            }
        }

        return null;
    }
}
