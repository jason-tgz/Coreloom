using System.Windows.Media;

namespace Coreloom.App.Themes;

/// <summary>
/// Color tokens for the dark and light themes. Brushes are created once (unfrozen) and their colors are
/// updated in place on theme/accent changes, so every element using them updates without re-binding.
/// </summary>
internal static class ThemePalette
{
    public static IReadOnlyDictionary<string, Color> Dark { get; } = new Dictionary<string, Color>
    {
        ["Background"] = Hex("#101114"),
        ["Surface"] = Hex("#181A1F"),
        ["Card"] = Hex("#202228"),
        ["CardHover"] = Hex("#262931"),
        ["CardBorder"] = Hex("#14FFFFFF"),
        ["Divider"] = Hex("#1AFFFFFF"),
        ["Text"] = Hex("#F5F6F7"),
        ["TextSecondary"] = Hex("#9297A3"),
        ["TextTertiary"] = Hex("#666B77"),
        ["Track"] = Hex("#2C2F37"),
        ["ChartGrid"] = Hex("#12FFFFFF"),
        ["ChartAxis"] = Hex("#6B707C"),
        ["ChartCrosshair"] = Hex("#40FFFFFF"),
        ["Tooltip"] = Hex("#F02A2D35"),
        ["TooltipBorder"] = Hex("#24FFFFFF"),
        ["Input"] = Hex("#24262C"),
        ["InputBorder"] = Hex("#22FFFFFF"),
        ["InputHover"] = Hex("#2C2F36"),
        ["ControlHover"] = Hex("#14FFFFFF"),
        ["ControlPressed"] = Hex("#0CFFFFFF"),
        ["NavSelected"] = Hex("#1AFFFFFF"),
        ["Elevated"] = Hex("#FFB454"),
        ["Critical"] = Hex("#FF5C5C"),
        ["Success"] = Hex("#45D6A0"),
        ["Info"] = Hex("#5B8CFF"),
        ["BannerBackground"] = Hex("#1FFFB454"),
        ["BannerBorder"] = Hex("#40FFB454"),
        ["InfoSoft"] = Hex("#1F5B8CFF"),
        ["InfoBorder"] = Hex("#405B8CFF"),
        ["Series.Cpu"] = Hex("#5B8CFF"),
        ["Series.Gpu"] = Hex("#45D6A0"),
        ["Series.Memory"] = Hex("#AA86FF"),
        ["Series.Storage"] = Hex("#3CC8D8"),
        ["Series.Temperature"] = Hex("#FFB454"),
        ["Series.Power"] = Hex("#FF8A5B"),
        ["Series.Fan"] = Hex("#7FD1FF"),
        ["Series.NetworkDown"] = Hex("#5B8CFF"),
        ["Series.NetworkUp"] = Hex("#AA86FF"),
        ["Series.Read"] = Hex("#3CC8D8"),
        ["Series.Write"] = Hex("#FFB454"),
        ["Series.Neutral"] = Hex("#3A3E48"),
    };

    public static IReadOnlyDictionary<string, Color> Light { get; } = new Dictionary<string, Color>
    {
        ["Background"] = Hex("#F3F4F6"),
        ["Surface"] = Hex("#EAECEF"),
        ["Card"] = Hex("#FFFFFF"),
        ["CardHover"] = Hex("#F8F9FA"),
        ["CardBorder"] = Hex("#14000000"),
        ["Divider"] = Hex("#14000000"),
        ["Text"] = Hex("#15171C"),
        ["TextSecondary"] = Hex("#5D6370"),
        ["TextTertiary"] = Hex("#8A909C"),
        ["Track"] = Hex("#E6E8EC"),
        ["ChartGrid"] = Hex("#12000000"),
        ["ChartAxis"] = Hex("#8A909C"),
        ["ChartCrosshair"] = Hex("#40000000"),
        ["Tooltip"] = Hex("#F7FFFFFF"),
        ["TooltipBorder"] = Hex("#1F000000"),
        ["Input"] = Hex("#FFFFFF"),
        ["InputBorder"] = Hex("#26000000"),
        ["InputHover"] = Hex("#F5F6F8"),
        ["ControlHover"] = Hex("#0F000000"),
        ["ControlPressed"] = Hex("#08000000"),
        ["NavSelected"] = Hex("#FFFFFF"),
        ["Elevated"] = Hex("#D9861A"),
        ["Critical"] = Hex("#E5484D"),
        ["Success"] = Hex("#169F70"),
        ["Info"] = Hex("#3D6FF2"),
        ["BannerBackground"] = Hex("#1FD9861A"),
        ["BannerBorder"] = Hex("#40D9861A"),
        ["InfoSoft"] = Hex("#1F3D6FF2"),
        ["InfoBorder"] = Hex("#403D6FF2"),
        ["Series.Cpu"] = Hex("#3D6FF2"),
        ["Series.Gpu"] = Hex("#169F70"),
        ["Series.Memory"] = Hex("#8B63F0"),
        ["Series.Storage"] = Hex("#1BA3B5"),
        ["Series.Temperature"] = Hex("#E08A16"),
        ["Series.Power"] = Hex("#EE6A3A"),
        ["Series.Fan"] = Hex("#2E9BD6"),
        ["Series.NetworkDown"] = Hex("#3D6FF2"),
        ["Series.NetworkUp"] = Hex("#8B63F0"),
        ["Series.Read"] = Hex("#1BA3B5"),
        ["Series.Write"] = Hex("#E08A16"),
        ["Series.Neutral"] = Hex("#DADDE3"),
    };

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>Picks black or white text for good contrast on the given background.</summary>
    public static Color ContrastingText(Color background)
    {
        double luminance = (0.2126 * background.R) + (0.7152 * background.G) + (0.0722 * background.B);
        return luminance > 150 ? Hex("#15171C") : Colors.White;
    }
}
