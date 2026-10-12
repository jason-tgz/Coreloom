using System.Globalization;
using System.Text;
using Coreloom.Core.Formatting;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Diagnostics;

/// <summary>Inputs for a diagnostics report.</summary>
public sealed class DiagnosticReportInput
{
    public required AppVersionInfo App { get; init; }

    public required SystemCapabilities Capabilities { get; init; }

    public required IReadOnlyList<ProviderStatus> Providers { get; init; }

    public required IReadOnlyList<DeviceInfo> Devices { get; init; }

    public required IReadOnlyList<MetricDescriptor> Metrics { get; init; }

    public required TelemetryFrame Frame { get; init; }

    public IReadOnlyList<LogEntry> RecentLog { get; init; } = [];

    /// <summary>Extra sections appended verbatim (title → markdown body).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ExtraSections { get; init; } = [];

    public bool IncludeRawSensors { get; init; } = true;

    /// <summary>
    /// Replace the computer name and the Windows user name (in profile paths) with placeholders so the
    /// report can be shared publicly. On by default.
    /// </summary>
    public bool Redact { get; init; } = true;

    /// <summary>Computer name to redact (defaults to <see cref="Environment.MachineName"/>).</summary>
    public string? ComputerName { get; init; }

    /// <summary>User name to redact in profile paths (defaults to <see cref="Environment.UserName"/>).</summary>
    public string? UserName { get; init; }
}

/// <summary>
/// Builds a Markdown report listing detected hardware, provider health, available and missing sensors,
/// and recent warnings. Intended to be shareable: the computer name and user name are redacted by default, and
/// no files, IP addresses or serial numbers are included.
/// </summary>
public static class DiagnosticReportBuilder
{
    /// <summary>Canonical metrics that the dashboard depends on, checked for availability.</summary>
    public static IReadOnlyList<(string Id, string Label)> KeyMetrics { get; } =
    [
        (MetricIds.CpuLoad, "CPU utilization"),
        (MetricIds.CpuTemperature, "CPU temperature"),
        (MetricIds.CpuPower, "CPU package power"),
        (MetricIds.CpuClockEffectiveAverage, "CPU effective clock (average)"),
        (MetricIds.CpuClockDerived, "CPU clock (derived, Task Manager style)"),
        (MetricIds.GpuLoad(0), "GPU utilization"),
        (MetricIds.GpuTemperature(0), "GPU temperature"),
        (MetricIds.GpuHotSpotTemperature(0), "GPU hot spot temperature"),
        (MetricIds.GpuMemoryJunctionTemperature(0), "GPU memory junction temperature"),
        (MetricIds.GpuCoreClock(0), "GPU core clock"),
        (MetricIds.GpuMemoryClock(0), "GPU memory clock"),
        (MetricIds.GpuMemoryUsed(0), "GPU memory used"),
        (MetricIds.GpuPower(0), "GPU power"),
        (MetricIds.GpuPowerLimit(0), "GPU power limit"),
        (MetricIds.GpuFanRpm(0, 1), "GPU fan 1 (RPM)"),
        (MetricIds.GpuFanPercent(0, 1), "GPU fan 1 (%)"),
        (MetricIds.GpuVoltage(0), "GPU core voltage"),
        (MetricIds.GpuMemoryControllerLoad(0), "GPU memory controller load"),
        (MetricIds.GpuPerformanceState(0), "GPU performance state"),
        (MetricIds.MemoryLoad, "RAM utilization"),
        (MetricIds.DiskTotalRead, "Disk read throughput"),
        (MetricIds.NetworkActiveDown, "Network download"),
    ];

    public static string Build(DiagnosticReportInput input, UnitFormatter? formatter = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        formatter ??= new UnitFormatter(culture: CultureInfo.InvariantCulture);
        var sb = new StringBuilder(16 * 1024);
        var caps = input.Capabilities;

        sb.AppendLine("# Coreloom diagnostics report");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- Coreloom version: {input.App.InformationalVersion}");
        sb.AppendLine($"- Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");
        sb.AppendLine($"- OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} (build {caps.OsBuild})");
        sb.AppendLine($"- Running as administrator: {YesNo(caps.IsElevated)}");
        sb.AppendLine($"- PawnIO driver installed: {YesNo(caps.PawnIoInstalled)}{(caps.PawnIoVersion is null ? string.Empty : $" (v{caps.PawnIoVersion})")}");
        sb.AppendLine($"- Frame #{input.Frame.Sequence}: {input.Frame.Count} values, polled in {input.Frame.PollDuration.TotalMilliseconds:0.0} ms");
        sb.AppendLine();

        if (!caps.LowLevelSensorsAccessible)
        {
            sb.AppendLine("> **Note:** CPU temperature, CPU package power, per-core clocks and motherboard sensors are read by");
            sb.AppendLine("> LibreHardwareMonitor through the PawnIO driver, which requires running Coreloom as administrator *and*");
            sb.AppendLine("> having PawnIO installed. Everything else (GPU via NVML, CPU load, memory, storage, network) works without it.");
            sb.AppendLine();
        }

        sb.AppendLine("## Detected hardware");
        sb.AppendLine();
        if (input.Devices.Count == 0)
        {
            sb.AppendLine("_No devices detected yet._");
        }

        foreach (var device in input.Devices)
        {
            sb.AppendLine($"### {Escape(device.Name)} (`{device.Id}`)");
            if (device.Kind is not null) sb.AppendLine($"- Type: {device.Kind}");
            if (device.Vendor is not null) sb.AppendLine($"- Vendor: {device.Vendor}");
            if (device.Source is not null) sb.AppendLine($"- Source: {device.Source}");
            foreach (var (key, value) in device.Properties)
            {
                if (input.Redact && key.Contains("address", StringComparison.OrdinalIgnoreCase))
                {
                    continue;  // IP / MAC addresses are not useful for diagnosing sensors
                }

                sb.AppendLine($"- {key}: {Escape(value)}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Monitoring providers");
        sb.AppendLine();
        sb.AppendLine("| Provider | State | Init (ms) | Avg poll (ms) | Values | Failures | Message / last error |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var p in input.Providers)
        {
            string message = string.Join(" — ", new[] { p.Message, p.LastError }.Where(s => !string.IsNullOrWhiteSpace(s)));
            sb.AppendLine($"| {p.Name} | {p.State} | {p.InitMilliseconds:0} | {p.AveragePollMilliseconds:0.0} | {p.MetricsWritten} | {p.TotalFailures} | {Escape(message)} |");
        }

        sb.AppendLine();

        sb.AppendLine("## Key sensors");
        sb.AppendLine();
        sb.AppendLine("| Sensor | Value | Source | Provenance | Status |");
        sb.AppendLine("|---|---:|---|---|---|");
        var byId = input.Metrics.ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var (id, label) in KeyMetrics)
        {
            byId.TryGetValue(id, out var descriptor);
            if (input.Frame.TryGetSample(id, out var sample))
            {
                var kind = descriptor?.Kind ?? sample.Source.Descriptor.Kind;
                sb.AppendLine($"| {label} | {formatter.Format(kind, sample.Value)} | {sample.Source.Provider} | {sample.Source.Provenance} | OK |");
            }
            else
            {
                string reason = descriptor?.UnavailableReason ?? "Not reported by any provider on this system";
                sb.AppendLine($"| {label} | — | — | — | Unavailable: {Escape(reason)} |");
            }
        }

        sb.AppendLine();

        var canonical = input.Metrics.Where(m => !m.IsRaw).OrderBy(m => m.Category).ThenBy(m => m.Id, StringComparer.Ordinal).ToArray();
        sb.AppendLine($"## All canonical metrics ({canonical.Length})");
        sb.AppendLine();
        sb.AppendLine("| Id | Name | Value | Source |");
        sb.AppendLine("|---|---|---:|---|");
        foreach (var m in canonical)
        {
            string value = input.Frame.TryGetSample(m.Id, out var s) ? formatter.Format(m.Kind, s.Value).ToString() : "—";
            string source = input.Frame.TryGetSample(m.Id, out var s2) ? $"{s2.Source.Provider} ({s2.Source.Provenance})" : (m.UnavailableReason ?? "no value");
            sb.AppendLine($"| `{m.Id}` | {Escape(m.Name)} | {value} | {Escape(source)} |");
        }

        sb.AppendLine();

        if (input.IncludeRawSensors)
        {
            var raw = input.Metrics.Where(m => m.IsRaw).ToArray();
            sb.AppendLine($"## Raw sensors ({raw.Length})");
            sb.AppendLine();
            if (raw.Length > 0)
            {
                sb.AppendLine("| Device | Sensor | Type | Value |");
                sb.AppendLine("|---|---|---|---:|");
                foreach (var m in raw.OrderBy(m => m.DeviceName).ThenBy(m => m.Kind).ThenBy(m => m.SortOrder))
                {
                    string value = input.Frame.TryGetValue(m.Id, out double v) ? formatter.Format(m.Kind, v).ToString() : "—";
                    sb.AppendLine($"| {Escape(m.DeviceName ?? string.Empty)} | {Escape(m.Name)} | {m.Kind} | {value} |");
                }
            }
            else
            {
                sb.AppendLine("_None (LibreHardwareMonitor disabled or not initialized)._");
            }

            sb.AppendLine();
        }

        foreach (var (title, body) in input.ExtraSections)
        {
            sb.AppendLine($"## {title}");
            sb.AppendLine();
            sb.AppendLine(body);
            sb.AppendLine();
        }

        var warnings = input.RecentLog.Where(e => e.Level >= LogLevel.Warning).TakeLast(40).ToArray();
        sb.AppendLine($"## Recent warnings and errors ({warnings.Length})");
        sb.AppendLine();
        if (warnings.Length == 0)
        {
            sb.AppendLine("_None._");
        }
        else
        {
            sb.AppendLine("```");
            foreach (var entry in warnings)
            {
                sb.AppendLine($"{entry.Time:HH:mm:ss} {entry.Level} [{entry.Category}] {entry.Message}{(entry.Exception is null ? string.Empty : $" ({entry.Exception.GetType().Name}: {entry.Exception.Message})")}");
            }

            sb.AppendLine("```");
        }

        return input.Redact
            ? RedactIdentifiers(sb.ToString(), input.ComputerName ?? Environment.MachineName, input.UserName ?? Environment.UserName)
            : sb.ToString();
    }

    /// <summary>Replaces the computer name and profile-path user names with placeholders.</summary>
    public static string RedactIdentifiers(string text, string? computerName, string? userName)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!string.IsNullOrWhiteSpace(computerName) && computerName.Length >= 3)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, $@"(?<![\w-]){System.Text.RegularExpressions.Regex.Escape(computerName)}(?![\w-])", "<computer-name>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, $@"([\\/]Users[\\/]){System.Text.RegularExpressions.Regex.Escape(userName)}(?=[\\/]|$)", "$1<user>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Multiline);
        }

        return text;
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
