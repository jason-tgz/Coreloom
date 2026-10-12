using System.Windows.Media;
using Vigil.App.Themes;

namespace Vigil.App.Controls;

/// <summary>Resolves theme colors for custom-drawn controls and caches frozen pens/brushes per color.</summary>
internal static class ChartColors
{
    private static readonly Dictionary<(uint Color, double Thickness), Pen> Pens = [];
    private static readonly Dictionary<(uint Color, int Opacity), Brush> Fills = [];
    private static readonly Dictionary<uint, SolidColorBrush> Solids = [];

    public static Color Series(string? colorKey) => ColorOf(ThemeService.Instance?.GetSeriesBrush(colorKey));

    public static Color Theme(string key) => ColorOf(ThemeService.Instance?.GetBrush("Brush." + key));

    public static Color ColorOf(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Gray;

    public static SolidColorBrush Solid(Color color)
    {
        uint key = Pack(color);
        if (!Solids.TryGetValue(key, out var brush))
        {
            if (Solids.Count > 256)
            {
                Solids.Clear();
            }

            brush = new SolidColorBrush(color);
            brush.Freeze();
            Solids[key] = brush;
        }

        return brush;
    }

    public static Pen Pen(Color color, double thickness, bool round = true)
    {
        var key = (Pack(color), thickness + (round ? 0 : 1000));
        if (!Pens.TryGetValue(key, out var pen))
        {
            if (Pens.Count > 256)
            {
                Pens.Clear();
            }

            pen = new Pen(Solid(color), thickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
                EndLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
            };
            pen.Freeze();
            Pens[key] = pen;
        }

        return pen;
    }

    public static Pen DashedPen(Color color, double thickness)
    {
        var pen = new Pen(Solid(color), thickness) { DashStyle = new DashStyle([4, 4], 0), DashCap = PenLineCap.Flat };
        pen.Freeze();
        return pen;
    }

    /// <summary>Vertical gradient from <paramref name="opacity"/> at the top to transparent at the bottom.</summary>
    public static Brush AreaFill(Color color, double opacity)
    {
        var key = (Pack(color), (int)(opacity * 100));
        if (!Fills.TryGetValue(key, out var brush))
        {
            if (Fills.Count > 256)
            {
                Fills.Clear();
            }

            var top = Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), color.R, color.G, color.B);
            var bottom = Color.FromArgb(0, color.R, color.G, color.B);
            var gradient = new LinearGradientBrush(top, bottom, 90);
            gradient.Freeze();
            Fills[key] = gradient;
            brush = gradient;
        }

        return brush;
    }

    private static uint Pack(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
}
