using System.Diagnostics;
using Coreloom.Hardware.Native;

namespace Coreloom.Hardware.Tests;

/// <summary>The overlay graph's timer and window, on real Windows. Skipped on other platforms.</summary>
public class OverlaySurfaceTests
{
    [Fact]
    public void PreciseTimer_WakesOnTimeBetweenTheSystemsTicks()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var stop = new ManualResetEvent(false);
        using var timer = new PreciseTimer(stop);
        Assert.True(timer.IsHighResolution, "high-resolution waitable timers need Windows 10 1803 or later");

        const int Waits = 60;
        double interval = Stopwatch.Frequency * 0.004; // 4 ms: well under the 15.6 ms system tick
        long start = Stopwatch.GetTimestamp();
        long deadline = start;
        double worstLate = 0;
        for (int i = 0; i < Waits; i++)
        {
            deadline += (long)interval;
            Assert.Equal(-1, timer.WaitUntil(deadline));
            worstLate = Math.Max(worstLate, (Stopwatch.GetTimestamp() - deadline) * 1000.0 / Stopwatch.Frequency);
        }

        double average = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / Waits;
        Assert.InRange(average, 3.9, 9); // an ordinary timer would average 15.6 ms
        Assert.True(worstLate < 40, $"a wait ran {worstLate:0.0} ms late");
    }

    [Fact]
    public void PreciseTimer_StopsWhenAskedTo()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var stop = new ManualResetEvent(true);
        using var wake = new AutoResetEvent(false);
        using var timer = new PreciseTimer(stop, wake);
        Assert.Equal(0, timer.WaitUntil(Stopwatch.GetTimestamp() + Stopwatch.Frequency));
        stop.Reset();
        wake.Set();
        Assert.Equal(1, timer.WaitUntil(Stopwatch.GetTimestamp() + Stopwatch.Frequency));
    }

    [Fact]
    public void LayeredSurface_TakesPixelsFromAnotherThread()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        LayeredSurface surface;
        try
        {
            surface = LayeredSurface.Create(IntPtr.Zero);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Assert.Skip($"No desktop to create windows on: {e.Message}");
            return;
        }

        try
        {
            surface.Place(-4000, -4000, 64, 16);
            surface.SetVisible(true);
            var pixels = new uint[64 * 16];
            Array.Fill(pixels, 0x80404040u);

            bool here = surface.Present(pixels, 64, 16);
            Assert.SkipUnless(here, "This session can't show layered windows.");
            Assert.False(surface.Present(pixels, 32, 16)); // not the size it was placed at

            bool there = false;
            var thread = new Thread(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    there = surface.Present(pixels, 64, 16);
                }
            });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "presenting from another thread must never wait on the window's thread");
            Assert.True(there, "UpdateLayeredWindow from the drawing thread failed");

            surface.Place(-4000, -4000, 80, 20); // resized on the window's thread: the next frame is drawn at the new size
            Assert.Equal((80, 20), surface.Size);
            Assert.True(surface.Present(new uint[80 * 20], 80, 20));
        }
        finally
        {
            surface.Dispose();
        }

        Assert.False(surface.Present(new uint[80 * 20], 80, 20));
    }
}
