namespace Coreloom.Core.Overlay;

/// <summary>How the graph is drawn: its line color (0xRRGGBB), how opaque it is (0–1) and the display scale (1 at 96 DPI).</summary>
public readonly record struct FrameGraphLook(uint Color, double Opacity, double Scale);

/// <summary>
/// Draws <see cref="FrameGraphModel"/> into a block of pixels (premultiplied BGRA, one <see cref="uint"/> each, rows top
/// to bottom), anti-aliased and without any graphics library, so the overlay's own drawing thread can paint it straight
/// into its window and the settings preview into a bitmap, the two looking exactly alike. Layers, bottom to top: a faint
/// baseline, a soft fill under the line that fades downward, the line, tinted marks on stutters, and the frame limiter's
/// target as a dashed green line.
/// </summary>
public sealed class FrameGraphRaster
{
    private const uint WarmColor = 0xFFB547;
    private const uint HotColor = 0xFF5C5C;
    private const uint TargetColor = 0x45D6A0;

    /// <summary>How opaque the flat line is while there are no frames (0–255).</summary>
    public const int IdleAlpha = 0x50;

    private FrameGraphPoint[] _points = new FrameGraphPoint[FrameGraphModel.MaxFrames + 1];
    private float[] _coverage = [];

    /// <summary>Draws the graph; <paramref name="pixels"/> holds <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
    public void Draw(Span<uint> pixels, int width, int height, FrameGraphModel model, FrameGraphLook look)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return;
        }

        var canvas = pixels[..(width * height)];
        canvas.Clear();
        double opacity = Math.Clamp(look.Opacity, 0, 1);
        double scale = look.Scale > 0 && double.IsFinite(look.Scale) ? look.Scale : 1;

        // The baseline: faint under a trace; with nothing to draw (no game in front, or it isn't drawing), a clearer
        // flat line, so the graph's place always shows.
        int baseline = Math.Max(1, (int)Math.Round(scale));
        int count = model.Layout(width, height, _points);
        FillRect(canvas, width, height, 0, height - baseline, width, baseline, look.Color, (count < 2 ? IdleAlpha : 0x10) / 255.0 * opacity);
        if (count < 2)
        {
            return;
        }

        var points = _points.AsSpan(0, count);
        Fill(canvas, width, height, points, look.Color, (0x48 / 255.0) * opacity);
        Line(canvas, width, height, points, 0.7 * scale, look.Color, (0xE6 / 255.0) * opacity);

        // Stutters: frames well above the typical one, as tinted marks over the line.
        double typical = model.Typical(count);
        double radius = scale;
        foreach (var p in points)
        {
            var (stutter, hot) = FrameGraphModel.Classify(p.Milliseconds, typical);
            if (stutter && p.X >= -radius && p.X <= width + radius)
            {
                // From a quarter of the graph below the spike's top (or the top of the graph, for a spike taller than it).
                double top = Math.Min(p.Milliseconds, model.Scale);
                double from = model.Y(Math.Max(typical, top - (model.Scale * 0.25)), height);
                Capsule(canvas, width, height, p.X, from, p.X, p.Y, radius, hot ? HotColor : WarmColor, opacity);
            }
        }

        // The frame limiter's target.
        if (model.Target > 0)
        {
            int row = (int)Math.Round(model.Y(model.Target, height));
            int thickness = Math.Max(1, (int)Math.Round(scale));
            row = Math.Clamp(row, 0, height - thickness);
            double dash = 3 * scale;
            for (double x = 0; x < width; x += dash * 2)
            {
                int from = (int)Math.Round(x);
                int to = Math.Min(width, (int)Math.Round(x + dash));
                FillRect(canvas, width, height, from, row, to - from, thickness, TargetColor, (0xCC / 255.0) * opacity);
            }
        }
    }

    /// <summary>The area under the line, fading from the line's color at the top of the area to clear at the bottom.</summary>
    private static void Fill(Span<uint> canvas, int width, int height, ReadOnlySpan<FrameGraphPoint> points, uint color, double alpha)
    {
        double top = height;
        foreach (var p in points)
        {
            top = Math.Min(top, p.Y);
        }

        double depth = Math.Max(1, height - top);
        int segment = 0; // points run newest (right) to oldest (left)
        for (int column = width - 1; column >= 0; column--)
        {
            double x = column + 0.5;
            if (x > points[0].X || x < points[^1].X)
            {
                continue;
            }

            while (segment < points.Length - 2 && points[segment + 1].X > x)
            {
                segment++;
            }

            var right = points[segment];
            var left = points[segment + 1];
            double span = right.X - left.X;
            double y = span > 1e-9 ? left.Y + ((right.Y - left.Y) * ((x - left.X) / span)) : Math.Min(left.Y, right.Y);
            int first = Math.Clamp((int)Math.Floor(y), 0, height - 1);
            for (int row = first; row < height; row++)
            {
                double covered = row == first ? Math.Clamp(row + 1 - y, 0, 1) : 1;
                double fade = 1 - (((row + 0.5) - top) / depth);
                Blend(ref canvas[(row * width) + column], color, alpha * covered * Math.Clamp(fade, 0, 1));
            }
        }
    }

    /// <summary>The line, anti-aliased: each pixel takes the most any segment covers it, so joins don't double up.</summary>
    private void Line(Span<uint> canvas, int width, int height, ReadOnlySpan<FrameGraphPoint> points, double radius, uint color, double alpha)
    {
        if (_coverage.Length < width * height)
        {
            _coverage = new float[width * height];
        }

        var coverage = _coverage.AsSpan(0, width * height);
        coverage.Clear();
        for (int i = 0; i < points.Length - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if ((a.X < -radius - 1 && b.X < -radius - 1) || (a.X > width + radius + 1 && b.X > width + radius + 1))
            {
                continue;
            }

            Cover(coverage, width, height, a.X, a.Y, b.X, b.Y, radius);
        }

        for (int i = 0; i < coverage.Length; i++)
        {
            if (coverage[i] > 0)
            {
                Blend(ref canvas[i], color, alpha * coverage[i]);
            }
        }
    }

    /// <summary>A short line with round ends (stutter marks), blended directly.</summary>
    private void Capsule(Span<uint> canvas, int width, int height, double x0, double y0, double x1, double y1, double radius, uint color, double alpha)
    {
        if (_coverage.Length < width * height)
        {
            _coverage = new float[width * height];
        }

        var coverage = _coverage.AsSpan(0, width * height);
        int left = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - radius - 1));
        int right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(x0, x1) + radius + 1));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - radius - 1));
        int bottom = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(y0, y1) + radius + 1));
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                coverage[(y * width) + x] = 0;
            }
        }

        Cover(coverage, width, height, x0, y0, x1, y1, radius);
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                int i = (y * width) + x;
                if (coverage[i] > 0)
                {
                    Blend(ref canvas[i], color, alpha * coverage[i]);
                    coverage[i] = 0;
                }
            }
        }
    }

    /// <summary>Raises each pixel's coverage to how much of it a line of the given radius from (x0, y0) to (x1, y1) covers.</summary>
    private static void Cover(Span<float> coverage, int width, int height, double x0, double y0, double x1, double y1, double radius)
    {
        int left = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - radius - 1));
        int right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(x0, x1) + radius + 1));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - radius - 1));
        int bottom = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(y0, y1) + radius + 1));
        double dx = x1 - x0, dy = y1 - y0;
        double length2 = (dx * dx) + (dy * dy);
        for (int y = top; y <= bottom; y++)
        {
            double py = y + 0.5;
            for (int x = left; x <= right; x++)
            {
                double px = x + 0.5;
                double t = length2 > 1e-12 ? Math.Clamp((((px - x0) * dx) + ((py - y0) * dy)) / length2, 0, 1) : 0;
                double ex = px - (x0 + (t * dx));
                double ey = py - (y0 + (t * dy));
                double d = Math.Sqrt((ex * ex) + (ey * ey));
                float c = (float)Math.Clamp(radius + 0.5 - d, 0, 1);
                ref float cell = ref coverage[(y * width) + x];
                if (c > cell)
                {
                    cell = c;
                }
            }
        }
    }

    private static void FillRect(Span<uint> canvas, int width, int height, int x, int y, int w, int h, uint color, double alpha)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(width, x + w), y1 = Math.Min(height, y + h);
        for (int row = y0; row < y1; row++)
        {
            for (int column = x0; column < x1; column++)
            {
                Blend(ref canvas[(row * width) + column], color, alpha);
            }
        }
    }

    /// <summary>Paints <paramref name="color"/> (0xRRGGBB) at <paramref name="alpha"/> over a premultiplied BGRA pixel.</summary>
    internal static void Blend(ref uint pixel, uint color, double alpha)
    {
        if (alpha <= 0)
        {
            return;
        }

        alpha = Math.Min(alpha, 1);
        double keep = 1 - alpha;
        double r = ((color >> 16) & 0xFF) * alpha;
        double g = ((color >> 8) & 0xFF) * alpha;
        double b = (color & 0xFF) * alpha;
        uint dst = pixel;
        uint da = dst >> 24, dr = (dst >> 16) & 0xFF, dg = (dst >> 8) & 0xFF, db = dst & 0xFF;
        uint oa = (uint)Math.Clamp(Math.Round((alpha * 255) + (da * keep)), 0, 255);
        uint or = (uint)Math.Clamp(Math.Round(r + (dr * keep)), 0, oa);
        uint og = (uint)Math.Clamp(Math.Round(g + (dg * keep)), 0, oa);
        uint ob = (uint)Math.Clamp(Math.Round(b + (db * keep)), 0, oa);
        pixel = (oa << 24) | (or << 16) | (og << 8) | ob;
    }
}
