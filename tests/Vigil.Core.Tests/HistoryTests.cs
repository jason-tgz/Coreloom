using Vigil.Core.History;

namespace Vigil.Core.Tests;

public class MetricSeriesTests
{
    [Fact]
    public void CopyRange_ReturnsPointsInChronologicalOrder_AfterWrapAround()
    {
        var series = new MetricSeries("m", 4);
        for (int i = 0; i < 6; i++)
        {
            series.Append(i * 1000, i);
        }

        var buffer = new HistoryPoint[1];
        int n = series.CopyRange(0, 10_000, ref buffer);

        Assert.Equal(4, n);
        Assert.Equal(2000, buffer[0].T);
        Assert.Equal(5000, buffer[3].T);
        Assert.Equal(5f, buffer[3].V);
    }

    [Fact]
    public void CopyRange_RespectsBounds_AndIncludesNeighborsWhenRequested()
    {
        var series = new MetricSeries("m", 16);
        for (int i = 0; i < 10; i++)
        {
            series.Append(i * 1000, i * 10);
        }

        var buffer = new HistoryPoint[16];
        int inner = series.CopyRange(2500, 5500, ref buffer);
        Assert.Equal(3, inner);
        Assert.Equal(3000, buffer[0].T);
        Assert.Equal(5000, buffer[2].T);

        int withNeighbors = series.CopyRange(2500, 5500, ref buffer, includeNeighbors: true);
        Assert.Equal(5, withNeighbors);
        Assert.Equal(2000, buffer[0].T);
        Assert.Equal(6000, buffer[4].T);
    }

    [Fact]
    public void Append_IgnoresNonFiniteAndOutOfOrderSamples()
    {
        var series = new MetricSeries("m", 8);
        series.Append(1000, 1);
        series.Append(2000, double.NaN);
        series.Append(500, 2);
        series.Append(3000, double.PositiveInfinity);

        Assert.Equal(1, series.Count);
    }

    [Fact]
    public void GetStats_ComputesMinMaxAverageForWindow()
    {
        var series = new MetricSeries("m", 32);
        double[] values = [10, 20, 30, 40, 50];
        for (int i = 0; i < values.Length; i++)
        {
            series.Append(i * 1000, values[i]);
        }

        var stats = series.GetStats(1000, 3000);
        Assert.Equal(3, stats.Count);
        Assert.Equal(20, stats.Min);
        Assert.Equal(40, stats.Max);
        Assert.Equal(30, stats.Average, 6);

        Assert.False(series.GetStats(10_000, 20_000).HasData);
    }

    [Fact]
    public void TryFindNearest_PicksClosestTimestamp()
    {
        var series = new MetricSeries("m", 8);
        series.Append(1000, 1);
        series.Append(2000, 2);
        series.Append(3000, 3);

        Assert.True(series.TryFindNearest(1400, out var a));
        Assert.Equal(1000, a.T);
        Assert.True(series.TryFindNearest(1600, out var b));
        Assert.Equal(2000, b.T);
        Assert.True(series.TryFindNearest(99_999, out var c));
        Assert.Equal(3000, c.T);
        Assert.True(series.TryFindNearest(0, out var d));
        Assert.Equal(1000, d.T);
    }

    [Fact]
    public void Resize_KeepsNewestSamples()
    {
        var series = new MetricSeries("m", 10);
        for (int i = 0; i < 10; i++)
        {
            series.Append(i, i);
        }

        series.Resize(4);
        Assert.Equal(4, series.Count);
        Assert.True(series.TryGetOldest(out var oldest));
        Assert.Equal(6, oldest.T);
        Assert.True(series.TryGetLatest(out var latest));
        Assert.Equal(9, latest.T);

        series.Append(10, 10);
        Assert.True(series.TryGetOldest(out oldest));
        Assert.Equal(7, oldest.T);

        series.Resize(8);
        series.Append(11, 11);
        Assert.Equal(5, series.Count);
        Assert.True(series.TryGetLatest(out latest));
        Assert.Equal(11, latest.T);
    }

    [Fact]
    public void Version_IncrementsOnAppend()
    {
        var series = new MetricSeries("m", 4);
        long before = series.Version;
        series.Append(1, 1);
        Assert.NotEqual(before, series.Version);
    }
}

public class HistoryStoreTests
{
    [Theory]
    [InlineData(1000, 3632)]
    [InlineData(250, 14432)]
    [InlineData(5000, 752)]
    [InlineData(10000, 392)]
    public void CapacityFor_KeepsOneHourOfSamples(int intervalMs, int expected)
    {
        Assert.Equal(expected, HistoryStore.CapacityFor(TimeSpan.FromMilliseconds(intervalMs)));
    }

    [Fact]
    public void SetCapacity_ResizesExistingSeries()
    {
        var store = new HistoryStore(200);
        for (int i = 0; i < 200; i++)
        {
            store.Append("a", i, i);
        }

        store.SetCapacity(150);
        Assert.Equal(150, store.Get("a")!.Capacity);
        Assert.Equal(150, store.Get("a")!.Count);
    }
}

public class DownsamplerTests
{
    [Fact]
    public void SmallInputs_PassThroughUnchanged()
    {
        var source = Enumerable.Range(0, 10).Select(i => new HistoryPoint(i, i)).ToArray();
        var dest = new HistoryPoint[20];
        int n = Downsampler.MinMax(source, 0, 9, 100, dest);
        Assert.Equal(10, n);
        Assert.Equal(source, dest[..n]);
    }

    [Fact]
    public void PreservesExtremes_AndBoundsOutputSize()
    {
        var rng = new Random(42);
        var source = Enumerable.Range(0, 3600).Select(i => new HistoryPoint(i * 1000L, (float)(rng.NextDouble() * 50))).ToArray();
        source[1234] = new HistoryPoint(source[1234].T, 99f); // spike
        source[2345] = new HistoryPoint(source[2345].T, -5f);  // dip

        var dest = new HistoryPoint[Downsampler.MaxOutput(source.Length, 200)];
        int n = Downsampler.MinMax(source, 0, 3_599_000, 200, dest);

        Assert.InRange(n, 200, 400);
        var output = dest[..n];
        Assert.Contains(output, p => p.V == 99f);
        Assert.Contains(output, p => p.V == -5f);
        for (int i = 1; i < n; i++)
        {
            Assert.True(output[i].T >= output[i - 1].T, "Output must stay chronological.");
        }
    }

    [Fact]
    public void PointsOutsideWindow_ArePassedThrough()
    {
        var source = Enumerable.Range(0, 1000).Select(i => new HistoryPoint(i, i % 7)).ToArray();
        var dest = new HistoryPoint[source.Length];
        int n = Downsampler.MinMax(source, 100, 899, 10, dest);
        var output = dest[..n];
        Assert.Equal(100, output.Count(p => p.T < 100));
        Assert.Equal(100, output.Count(p => p.T > 899));
    }
}
