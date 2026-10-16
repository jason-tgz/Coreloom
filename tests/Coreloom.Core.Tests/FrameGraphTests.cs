using Coreloom.Core.Overlay;

namespace Coreloom.Core.Tests;

public class FrameGraphTests
{
    /// <summary>A feed of steady frames, the newest shown <paramref name="age"/> ms ago.</summary>
    private static FrameTimeReader Steady(double frameTime, int count, double age, double stutterEvery = 0, double stutter = 0)
    {
        return (Span<double> buffer, out double newestAge) =>
        {
            int n = Math.Min(count, buffer.Length);
            for (int i = 0; i < n; i++)
            {
                buffer[i] = stutterEvery > 0 && (i + 1) % stutterEvery == 0 ? stutter : frameTime;
            }

            newestAge = n > 0 ? age : 0;
            return n;
        };
    }

    [Theory]
    [InlineData(240, 120)]
    [InlineData(144, 144)]
    [InlineData(165, 82)]
    [InlineData(120, 120)]
    [InlineData(60, 60)]
    [InlineData(360, 120)]
    [InlineData(0, 60)]
    public void RedrawRate_IsAnEvenFractionOfTheRefreshRate(int refresh, int rate) =>
        Assert.Equal(rate, FrameGraphModel.RedrawRate(refresh));

    [Fact]
    public void NoFrames_NothingToDraw()
    {
        var model = new FrameGraphModel();
        Assert.False(model.Update(Steady(7, 0, 0), 0, 1 / 120.0));
        Assert.False(model.HasFrames);
        Assert.Equal(0, model.Layout(200, 40, new FrameGraphPoint[16]));
    }

    [Fact]
    public void Frames_AreLaidOutNewestFirstAtTheMomentTheyWereShown()
    {
        var model = new FrameGraphModel();
        Assert.True(model.Update(Steady(10, 500, 30), 0, 1 / 120.0));
        Assert.Equal(60, model.Delay, 6); // starts a little behind the batch's lateness (at least 60 ms)

        var points = new FrameGraphPoint[FrameGraphModel.MaxFrames];
        int n = model.Layout(200, 40, points);
        Assert.True(n > 90);
        // 200 px for 1000 ms: the newest frame (30 ms ago, graph 60 ms behind) sits 30 ms past the right edge.
        Assert.Equal(206, points[0].X, 6);
        Assert.Equal(204, points[1].X, 6);
        Assert.True(points[n - 1].X < 0);
        for (int i = 1; i < n; i++)
        {
            Assert.True(points[i].X < points[i - 1].X);
        }
    }

    [Fact]
    public void Delay_RisesAtOnceForLateBatchesAndEasesBack()
    {
        var model = new FrameGraphModel();
        model.Update(Steady(7, 400, 20), 0, 1 / 120.0);
        double start = model.Delay;
        for (int i = 0; i < 30; i++)
        {
            model.Update(Steady(7, 400, 250), 0, 1 / 120.0); // a late batch
        }

        Assert.True(model.Delay > start + 100);
        double late = model.Delay;
        for (int i = 0; i < 240; i++)
        {
            model.Update(Steady(7, 400, 20), 0, 1 / 120.0);
        }

        Assert.True(model.Delay < late);
        Assert.True(model.Delay <= FrameGraphModel.MaxDelayMilliseconds);
    }

    [Fact]
    public void Scale_LeavesRoomAboveTheFramesAndEasesTowardThem()
    {
        var model = new FrameGraphModel();
        model.Update(Steady(7, 400, 20), 0, 1 / 120.0);
        Assert.InRange(model.Scale, 14, 14.5); // twice the typical frame
        model.Update(Steady(20, 400, 20), 0, 1 / 120.0);
        Assert.InRange(model.Scale, 14.5, 40); // eases, doesn't jump
        for (int i = 0; i < 600; i++)
        {
            model.Update(Steady(20, 400, 20), 0, 1 / 120.0);
        }

        Assert.InRange(model.Scale, 39, 41);
    }

    [Fact]
    public void Target_GetsRoomAboveIt()
    {
        var model = new FrameGraphModel();
        model.Update(Steady(4, 400, 20), 1000.0 / 60, 1 / 120.0);
        Assert.Equal(1000.0 / 60, model.Target, 6);
        Assert.True(model.Scale >= model.Target * 1.6 - 1e-9);
    }

    [Theory]
    [InlineData(7, 7, false, false)]
    [InlineData(10, 7, false, false)]
    [InlineData(12, 7, true, false)]
    [InlineData(20, 7, true, true)]
    [InlineData(2.5, 1, false, false)] // tiny frames: needs to be 2 ms over as well
    public void Stutters_AreFramesWellAboveTheTypicalOne(double ms, double typical, bool stutter, bool hot) =>
        Assert.Equal((stutter, hot), FrameGraphModel.Classify(ms, typical));

    [Fact]
    public void Sample_IsAStillTraceForThePreview()
    {
        var model = new FrameGraphModel();
        model.LoadSample();
        Assert.True(model.HasFrames);
        var points = new FrameGraphPoint[FrameGraphModel.MaxFrames];
        int n = model.Layout(200, 40, points);
        Assert.Equal(200, points[0].X, 6); // newest at the right edge
        Assert.True(n > 100);
    }

    private static uint Alpha(uint pixel) => pixel >> 24;

    [Fact]
    public void Raster_DrawsAClearFlatLineWithoutFrames_AndAFaintBaselineUnderATrace()
    {
        var model = new FrameGraphModel();
        var pixels = new uint[100 * 20];
        new FrameGraphRaster().Draw(pixels, 100, 20, model, new FrameGraphLook(0xFFFFFF, 1, 1));
        Assert.Equal((uint)FrameGraphRaster.IdleAlpha, Alpha(pixels[(19 * 100) + 50])); // seen, so the graph's place shows
        Assert.Equal(0u, pixels[(10 * 100) + 50]);

        model.Update(Steady(7, 600, 20), 0, 1 / 120.0);
        new FrameGraphRaster().Draw(pixels, 100, 20, model, new FrameGraphLook(0xFFFFFF, 1, 1));
        Assert.True(Alpha(pixels[(19 * 100) + 50]) < FrameGraphRaster.IdleAlpha, "under a trace the baseline steps back");
    }

    [Fact]
    public void Raster_DrawsTheLineFillTargetAndStutters()
    {
        var model = new FrameGraphModel();
        model.Update(Steady(7, 600, 20, stutterEvery: 50, stutter: 21), 1000.0 / 120, 1 / 120.0);
        const int W = 200, H = 40;
        var pixels = new uint[W * H];
        new FrameGraphRaster().Draw(pixels, W, H, model, new FrameGraphLook(0xFFFFFF, 1, 1));

        // The line: a steady 7 ms frame sits at Y(7); the pixel there is nearly opaque white, the one well above clear.
        int lineRow = (int)Math.Round(model.Y(7, H) - 0.5);
        int column = 20;
        uint line = pixels[(lineRow * W) + column];
        Assert.True(Alpha(line) > 0x80, $"line alpha {Alpha(line):X}");
        Assert.Equal(Alpha(line), (line >> 16) & 0xFF); // white, premultiplied
        Assert.Equal(0u, Alpha(pixels[(1 * W) + column]) & 0xF0); // far above the line: (almost) nothing

        // The fill under it fades toward the bottom.
        uint under = pixels[((lineRow + 4) * W) + column];
        uint deep = pixels[((H - 3) * W) + column];
        Assert.True(Alpha(under) > Alpha(deep));
        Assert.True(Alpha(deep) > 0);

        // Stutters are tinted (red: 21 ms is three times the typical frame).
        bool red = false;
        for (int i = 0; i < pixels.Length; i++)
        {
            uint p = pixels[i];
            uint a = Alpha(p), r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF;
            if (a > 0xC0 && r > g + 60)
            {
                red = true;
                break;
            }
        }

        Assert.True(red, "no red stutter mark");

        // The target (8.3 ms) is a dashed green line: green on a dash, not between dashes.
        int targetRow = (int)Math.Round(model.Y(1000.0 / 120, H));
        uint onDash = pixels[(targetRow * W) + 1];
        uint offDash = pixels[(targetRow * W) + 4];
        Assert.True(((onDash >> 8) & 0xFF) > ((onDash >> 16) & 0xFF), "target dash should be green");
        Assert.NotEqual(onDash, offDash);
    }

    [Fact]
    public void Raster_Opacity_FadesEverything()
    {
        var model = new FrameGraphModel();
        model.Update(Steady(7, 600, 20), 0, 1 / 120.0);
        var full = new uint[200 * 40];
        var half = new uint[200 * 40];
        var raster = new FrameGraphRaster();
        raster.Draw(full, 200, 40, model, new FrameGraphLook(0xFFFFFF, 1, 1));
        raster.Draw(half, 200, 40, model, new FrameGraphLook(0xFFFFFF, 0.5, 1));
        uint maxFull = full.Max(Alpha), maxHalf = half.Max(Alpha);
        Assert.InRange(maxHalf, maxFull * 0.45, maxFull * 0.62);
        for (int i = 0; i < full.Length; i++)
        {
            Assert.True(Alpha(half[i]) <= Alpha(full[i]));
        }
    }

    [Fact]
    public void Blend_IsPremultipliedSourceOver()
    {
        uint pixel = 0;
        FrameGraphRaster.Blend(ref pixel, 0xFF0000, 0.5);
        Assert.Equal(0x80800000u, pixel);
        FrameGraphRaster.Blend(ref pixel, 0x0000FF, 1);
        Assert.Equal(0xFF0000FFu, pixel);
    }
}
