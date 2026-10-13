using System.Globalization;
using System.Text.RegularExpressions;
using Coreloom.Core.Formatting;
using Coreloom.Core.Settings;

namespace Coreloom.ViewModels.Pages;

/// <summary>How fast the fans in the PC picture turn, and what they follow.</summary>
public readonly record struct PcFanReading(double TopSpeed, double BottomSpeed, string ToolTip);

/// <summary>A motherboard fan reading (<c>board/fan/…</c>) as the PC picture uses it.</summary>
public sealed record PcFanSensor(string Id, string Name);

/// <summary>
/// Maps the motherboard's fan readings onto the PC picture: the three fans at the top (the CPU cooler's radiator)
/// follow the fans named for the CPU, the three at the bottom follow the other case fans. Pumps are left out. When the
/// board doesn't say which fan is which, all six follow the average of the connected fans.
/// </summary>
/// <remarks>
/// Real fans turn far faster than a picture can show without flickering, so the drawing turns at a thousandth of the
/// real speed (1,200 RPM → 1.2 turns a second) and blurs the blades as they speed up. A fan that stops stops in the
/// picture too.
/// </remarks>
public static partial class PcFans
{
    /// <summary>Turns per second when no fan speed can be read.</summary>
    public const double UnknownSpeed = 0.6;

    /// <summary>The fastest the drawing turns (3,000 RPM and up).</summary>
    public const double MaxSpeed = 3.0;

    public const string UnknownToolTip = "Coreloom can't read this PC's fan speeds, so the fans turn at an idle pace. "
        + "Fan speeds come from the motherboard and need PawnIO (see Settings › Diagnostics).";

    /// <summary>Picture speed (turns per second) for a real speed in RPM.</summary>
    public static double SpeedFromRpm(double rpm) => double.IsFinite(rpm) ? Math.Clamp(rpm / 1000.0, 0, MaxSpeed) : UnknownSpeed;

    /// <summary>Splits the board's fans into the CPU cooler's and the case's; pumps are dropped.</summary>
    public static (IReadOnlyList<PcFanSensor> Cpu, IReadOnlyList<PcFanSensor> Case) Classify(IEnumerable<PcFanSensor> fans)
    {
        var usable = fans.Where(f => !f.Name.Contains("pump", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Number(f.Name))
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var cpu = usable.Where(f => CpuFanName().IsMatch(f.Name)).ToArray();
        var rest = usable.Except(cpu).ToArray();
        return (cpu, rest);
    }

    /// <summary>
    /// Reads the fans for one frame. <paramref name="rpmOf"/> returns a fan's RPM, or null when it has no reading.
    /// Headers reading 0 RPM usually have nothing plugged in, so they are ignored.
    /// </summary>
    public static PcFanReading Read(IReadOnlyList<PcFanSensor> cpuFans, IReadOnlyList<PcFanSensor> caseFans, Func<string, double?> rpmOf, UnitFormatter formatter)
    {
        var cpu = Connected(cpuFans, rpmOf);
        var other = Connected(caseFans, rpmOf);
        string Rpm(double rpm) => formatter.Fan(rpm).ToString();

        if (cpu.Count == 0 && other.Count == 0)
        {
            return new PcFanReading(UnknownSpeed, UnknownSpeed, UnknownToolTip);
        }

        if (cpu.Count == 0 || other.Count == 0)
        {
            // Either only CPU fans or the board doesn't name its fans: everything follows the average.
            var all = cpu.Count > 0 ? cpu : other;
            double average = all.Average(f => f.Rpm);
            string what = all.Count == 1 ? all[0].Sensor.Name : $"the average of {all.Count.ToString(CultureInfo.InvariantCulture)} fans";
            return new PcFanReading(SpeedFromRpm(average), SpeedFromRpm(average), $"The fans turn with {what}: {Rpm(average)}.");
        }

        double top = cpu.Max(f => f.Rpm);
        double bottom = other.Average(f => f.Rpm);
        string topWhat = cpu.Count == 1 ? cpu[0].Sensor.Name : "the CPU fans";
        string bottomWhat = other.Count == 1 ? other[0].Sensor.Name : $"{other.Count.ToString(CultureInfo.InvariantCulture)} case fans";
        return new PcFanReading(SpeedFromRpm(top), SpeedFromRpm(bottom),
            $"Top fans: {topWhat}, {Rpm(top)} · Bottom fans: {bottomWhat}, {Rpm(bottom)}");
    }

    /// <summary>Swatch and lighting color for a fixed glow (#RRGGBB); null for Rainbow and Accent.</summary>
    public static string? GlowHex(PcGlow glow) => glow switch
    {
        PcGlow.Blue => "#0D6BFF",
        PcGlow.Purple => "#9E4DFF",
        PcGlow.Red => "#FF2933",
        PcGlow.Green => "#1AD96B",
        _ => null,
    };

    private static List<(PcFanSensor Sensor, double Rpm)> Connected(IReadOnlyList<PcFanSensor> fans, Func<string, double?> rpmOf) =>
        fans.Select(f => (Sensor: f, Rpm: rpmOf(f.Id) ?? 0))
            .Where(f => double.IsFinite(f.Rpm) && f.Rpm >= 1)
            .ToList();

    private static int Number(string name) =>
        TrailingNumber().Match(name) is { Success: true } m && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

    [GeneratedRegex(@"\bCPU|\bAIO\b|Radiator", RegexOptions.IgnoreCase)]
    private static partial Regex CpuFanName();

    [GeneratedRegex(@"(\d+)\s*$")]
    private static partial Regex TrailingNumber();
}
