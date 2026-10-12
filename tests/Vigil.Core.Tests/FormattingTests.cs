using System.Globalization;
using Vigil.Core.Charting;
using Vigil.Core.Formatting;
using Vigil.Core.Telemetry;

namespace Vigil.Core.Tests;

public class UnitFormatterTests
{
    private static UnitFormatter F(TemperatureUnit t = TemperatureUnit.Celsius, DataUnitStyle d = DataUnitStyle.Binary, NetworkUnitStyle n = NetworkUnitStyle.BitsPerSecond) =>
        new(t, d, n, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0, "0", "B")]
    [InlineData(512, "512", "B")]
    [InlineData(1536, "1.50", "KB")]
    [InlineData(68_719_476_736d, "64.0", "GB")]
    [InlineData(1_073_741_823d, "1.00", "GB")]
    public void Bytes_Binary(double bytes, string number, string unit)
    {
        var v = F().Bytes(bytes);
        Assert.Equal(number, v.Number);
        Assert.Equal(unit, v.Unit);
    }

    [Fact]
    public void Bytes_Decimal()
    {
        var v = F(d: DataUnitStyle.Decimal).Bytes(2_000_000_000_000);
        Assert.Equal("2.00 TB", v.ToString());
    }

    [Fact]
    public void NetworkRate_UsesBitsByDefault_AndBytesWhenSelected()
    {
        Assert.Equal("100 Mbps", F().NetworkRate(12_500_000).ToString());
        Assert.Equal("11.9 MB/s", F(n: NetworkUnitStyle.BytesPerSecond).NetworkRate(12_500_000).ToString());
    }

    [Fact]
    public void Temperature_ConvertsToFahrenheit()
    {
        var f = F(TemperatureUnit.Fahrenheit);
        Assert.Equal("100 °F", f.Temperature(37.7778).ToString());
        Assert.Equal(37.7778, f.ToCelsius(100), 3);
        Assert.Equal("38 °C", F().Temperature(38.2).ToString());
        Assert.Equal(9, f.ConvertTemperatureDelta(5), 6);
    }

    [Fact]
    public void Clocks_PowerAndVoltage()
    {
        Assert.Equal("2,527 MHz", F().Clock(2527).ToString());
        Assert.Equal("5.24 GHz", F().ClockGigahertz(5240).ToString());
        Assert.Equal("95 W", F().Power(95.2).ToString());
        Assert.Equal("4.5 W", F().Power(4.49).ToString());
        Assert.Equal("950 mV", F().Millivolts(0.95).ToString());
        Assert.Equal("1.250 V", F().Voltage(1.25).ToString());
    }

    [Fact]
    public void Format_DispatchesByKind_AndHandlesNaN()
    {
        var f = F();
        Assert.Equal("42 %", f.Format(MetricKind.Load, 42.4).ToString());
        Assert.Equal("1,200 RPM", f.Format(MetricKind.Fan, 1200).ToString());
        Assert.True(f.Format(MetricKind.Temperature, double.NaN).IsUnavailable);
        Assert.Equal("—", f.Format(MetricKind.Power, double.PositiveInfinity).Number);
    }

    [Fact]
    public void Duration_IsCompact()
    {
        Assert.Equal("2d 3h", F().Duration((2 * 86400) + (3 * 3600) + 59).Number);
        Assert.Equal("1h 5m", F().Duration(3900).Number);
        Assert.Equal("0m 42s", F().Duration(42).Number);
    }

    [Fact]
    public void NegativeZero_IsNotDisplayed()
    {
        Assert.Equal("0", F().Number(-0.0001, 0));
    }

    [Fact]
    public void PerformanceState_Formats()
    {
        Assert.Equal("P0", UnitFormatter.PerformanceState(0));
        Assert.Equal("P8", UnitFormatter.PerformanceState(8));
        Assert.Equal("—", UnitFormatter.PerformanceState(32));
    }
}

public class ThresholdTests
{
    [Fact]
    public void Evaluate_ReturnsLevels()
    {
        var t = new Thresholds(75, 83);
        Assert.Equal(ThresholdLevel.Normal, t.Evaluate(60));
        Assert.Equal(ThresholdLevel.Elevated, t.Evaluate(75));
        Assert.Equal(ThresholdLevel.Critical, t.Evaluate(90));
        Assert.Equal(ThresholdLevel.Normal, t.Evaluate(double.NaN));
    }
}

public class AxisScaleTests
{
    [Theory]
    [InlineData(37, 40)]
    [InlineData(40, 40)]
    [InlineData(412, 500)]
    [InlineData(0.7, 1)]
    [InlineData(101, 200)]
    [InlineData(23, 25)]
    [InlineData(0, 1)]
    public void NiceCeiling(double value, double expected)
    {
        Assert.Equal(expected, AxisScale.NiceCeiling(value), 9);
    }

    [Fact]
    public void NiceRange_IncludesZero_AndCoversData()
    {
        var (min, max, step) = AxisScale.NiceRange(31, 47, 4);
        Assert.Equal(0, min);
        Assert.True(max >= 47);
        Assert.True(step > 0);
        Assert.InRange((max - min) / step, 1, 6);
    }

    [Fact]
    public void NiceRange_FlatData_StillHasHeight()
    {
        var (min, max, _) = AxisScale.NiceRange(0, 0, 4);
        Assert.True(max > min);
    }
}

public class MonotoneCurveTests
{
    [Fact]
    public void Curve_NeverOvershootsSegmentBounds()
    {
        double[] xs = [0, 1, 2, 3, 4, 5, 6];
        double[] ys = [0, 100, 100, 20, 25, 0, 100];
        var m = new double[xs.Length];
        MonotoneCurve.ComputeTangents(xs, ys, m);

        for (int i = 0; i < xs.Length - 1; i++)
        {
            var (x1, y1, x2, y2) = MonotoneCurve.ControlPoints(xs, ys, m, i);
            double lo = Math.Min(ys[i], ys[i + 1]);
            double hi = Math.Max(ys[i], ys[i + 1]);
            for (double t = 0; t <= 1.0001; t += 0.05)
            {
                double u = 1 - t;
                double y = (u * u * u * ys[i]) + (3 * u * u * t * y1) + (3 * u * t * t * y2) + (t * t * t * ys[i + 1]);
                Assert.InRange(y, lo - 1e-9, hi + 1e-9);
            }

            Assert.True(x1 > xs[i] && x2 < xs[i + 1]);
        }
    }

    [Fact]
    public void FlatSegments_HaveZeroTangents()
    {
        double[] xs = [0, 1, 2, 3];
        double[] ys = [5, 5, 5, 9];
        var m = new double[4];
        MonotoneCurve.ComputeTangents(xs, ys, m);
        Assert.Equal(0, m[0]);
        Assert.Equal(0, m[1]);
    }
}
