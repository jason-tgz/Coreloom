using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;

namespace Coreloom.Core.Tests;

public class HardwareVendorTests
{
    [Theory]
    [InlineData("NVIDIA", HardwareVendor.Nvidia)]
    [InlineData("NVIDIA Corporation", HardwareVendor.Nvidia)]
    [InlineData("AMD", HardwareVendor.Amd)]
    [InlineData("AuthenticAMD", HardwareVendor.Amd)]
    [InlineData("Advanced Micro Devices, Inc.", HardwareVendor.Amd)]
    [InlineData("GenuineIntel", HardwareVendor.Intel)]
    [InlineData("Intel", HardwareVendor.Intel)]
    [InlineData("Microsoft", HardwareVendor.Unknown)]
    [InlineData("", HardwareVendor.Unknown)]
    [InlineData(null, HardwareVendor.Unknown)]
    public void VendorStrings(string? vendor, HardwareVendor expected) =>
        Assert.Equal(expected, HardwareVendors.FromVendorString(vendor));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090", HardwareVendor.Nvidia)]
    [InlineData("GeForce GTX 1080 Ti", HardwareVendor.Nvidia)]
    [InlineData("NVIDIA RTX A6000", HardwareVendor.Nvidia)]
    [InlineData("Quadro P2200", HardwareVendor.Nvidia)]
    [InlineData("AMD Ryzen 9 9950X3D 16-Core Processor", HardwareVendor.Amd)]
    [InlineData("AMD Radeon RX 7900 XTX", HardwareVendor.Amd)]
    [InlineData("AMD Radeon(TM) Graphics", HardwareVendor.Amd)]
    [InlineData("Ryzen Threadripper PRO 7995WX", HardwareVendor.Amd)]
    [InlineData("Intel(R) Core(TM) Ultra 9 285K", HardwareVendor.Intel)]
    [InlineData("Intel(R) Core(TM) i9-14900K", HardwareVendor.Intel)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", HardwareVendor.Intel)]
    [InlineData("Intel(R) UHD Graphics 770", HardwareVendor.Intel)]
    [InlineData("Microsoft Basic Render Driver", HardwareVendor.Unknown)]
    [InlineData("", HardwareVendor.Unknown)]
    public void DeviceNames(string name, HardwareVendor expected) =>
        Assert.Equal(expected, HardwareVendors.FromDeviceName(name));

    [Fact]
    public void VendorString_WinsOverTheName()
    {
        // A provider that says "Intel" is trusted even when the name has no recognisable keyword.
        Assert.Equal(HardwareVendor.Intel, HardwareVendors.Detect("GenuineIntel", "Processor"));
        Assert.Equal(HardwareVendor.Amd, HardwareVendors.Detect(null, "AMD Ryzen 9 9950X3D"));
        Assert.Equal(HardwareVendor.Unknown, HardwareVendors.Detect(null, null));
    }

    [Fact]
    public void Keys_RoundTrip_AndUnknownIsEmpty()
    {
        foreach (var vendor in new[] { HardwareVendor.Nvidia, HardwareVendor.Amd, HardwareVendor.Intel })
        {
            Assert.Equal(vendor, HardwareVendors.FromKey(HardwareVendors.Key(vendor)));
            Assert.False(string.IsNullOrEmpty(HardwareVendors.DisplayName(vendor)));
        }

        Assert.Equal(string.Empty, HardwareVendors.Key(HardwareVendor.Unknown));
        Assert.Equal(HardwareVendor.Unknown, HardwareVendors.FromKey("matrox"));
        Assert.Equal(HardwareVendor.Nvidia, HardwareVendors.FromKey(" NVIDIA "));
    }
}

public class GpuTuningAnalyzerTests
{
    private static GpuTuningStatus Analyze(double? core, double? memory, double? limit, double? stock) =>
        GpuTuningAnalyzer.Analyze(new GpuTuningReading(core, memory, limit, stock));

    [Fact]
    public void NothingReported_IsUnavailable_NotStock()
    {
        var status = Analyze(null, null, null, null);

        Assert.Equal(GpuTuningKind.Unavailable, status.Kind);
        Assert.False(status.IsTuned);
        Assert.Empty(status.Findings);
        Assert.Equal(0, status.ReadableSettings);
    }

    [Fact]
    public void LimitWithoutADefault_CannotBeJudged()
    {
        var status = Analyze(null, null, 600, null);

        Assert.Equal(GpuTuningKind.Unavailable, status.Kind);
    }

    [Fact]
    public void AllAtStock_IsNotDetected()
    {
        var status = Analyze(0, 0, 575, 575);

        Assert.Equal(GpuTuningKind.NotDetected, status.Kind);
        Assert.Equal(3, status.ReadableSettings);
        Assert.False(status.IsTuned);
        Assert.Empty(status.Findings);
        Assert.Equal(string.Empty, status.BadgeText);
    }

    [Theory]
    [InlineData(574.4)]
    [InlineData(575.9)]
    [InlineData(577.5)] // within max(1 W, 0.5 %) of the default
    public void PowerLimitRoundingNoise_IsIgnored(double limit) =>
        Assert.Equal(GpuTuningKind.NotDetected, Analyze(0, 0, limit, 575).Kind);

    [Fact]
    public void PositiveCoreOffset_IsOverclock()
    {
        var status = Analyze(150, 0, 575, 575);

        Assert.Equal(GpuTuningKind.Overclock, status.Kind);
        Assert.True(status.IsTuned);
        var finding = Assert.Single(status.Findings);
        Assert.Equal(TuningDirection.Increased, finding.Direction);
        Assert.Equal("Core clock offset +150 MHz", finding.Description);
        Assert.Equal("Overclock", status.BadgeText);
    }

    [Fact]
    public void MemoryOffset_IsDetectedOnItsOwn()
    {
        var status = Analyze(0, 1000, null, null);

        Assert.Equal(GpuTuningKind.Overclock, status.Kind);
        Assert.Equal("Memory clock offset +1000 MHz", Assert.Single(status.Findings).Description);
        Assert.Equal(2, status.ReadableSettings);
    }

    [Fact]
    public void RaisedPowerLimit_IsOverclock_AndDescribedAgainstTheDefault()
    {
        var status = Analyze(0, 0, 600, 575);

        Assert.Equal(GpuTuningKind.Overclock, status.Kind);
        Assert.Equal("Power limit 600 W (104% of the 575 W default)", Assert.Single(status.Findings).Description);
    }

    [Fact]
    public void NegativeOffsetOrLoweredLimit_IsReduced()
    {
        var offset = Analyze(-100, 0, 575, 575);
        var cap = Analyze(0, 0, 460, 575);

        Assert.Equal(GpuTuningKind.Reduced, offset.Kind);
        Assert.Equal("Core clock offset -100 MHz", Assert.Single(offset.Findings).Description);
        Assert.Equal(GpuTuningKind.Reduced, cap.Kind);
        Assert.Equal(TuningDirection.Decreased, Assert.Single(cap.Findings).Direction);
        Assert.Equal("Reduced limits", cap.BadgeText);
    }

    [Fact]
    public void RaisedAndLowered_AreMixed()
    {
        var status = Analyze(200, 0, 480, 575);

        Assert.Equal(GpuTuningKind.Mixed, status.Kind);
        Assert.Equal(2, status.Findings.Count);
        Assert.Equal("Custom tuning", status.BadgeText);
    }

    [Fact]
    public void NonFiniteValues_AreTreatedAsNotReported()
    {
        var status = Analyze(double.NaN, double.PositiveInfinity, double.NaN, 575);

        Assert.Equal(GpuTuningKind.Unavailable, status.Kind);
    }

    [Theory]
    [InlineData(150, "+150 MHz")]
    [InlineData(-75, "-75 MHz")]
    [InlineData(0, "0 MHz")]
    [InlineData(0.4, "0 MHz")]
    public void SignedMhz(double value, string expected) => Assert.Equal(expected, GpuTuningAnalyzer.SignedMhz(value));

    [Fact]
    public void Classify_UsesTheDocumentedTolerances()
    {
        Assert.Null(GpuTuningAnalyzer.ClassifyOffset(0));
        Assert.Null(GpuTuningAnalyzer.ClassifyOffset(0.5));
        Assert.Equal(TuningDirection.Increased, GpuTuningAnalyzer.ClassifyOffset(1));
        Assert.Equal(TuningDirection.Decreased, GpuTuningAnalyzer.ClassifyOffset(-1));
        Assert.Null(GpuTuningAnalyzer.ClassifyPower(575, 0));
        Assert.Null(GpuTuningAnalyzer.ClassifyPower(575, 575));
        Assert.Equal(TuningDirection.Increased, GpuTuningAnalyzer.ClassifyPower(580, 575));
        Assert.Equal(TuningDirection.Decreased, GpuTuningAnalyzer.ClassifyPower(500, 575));
    }
}

public class ClockOffsetFormattingTests
{
    private static UnitFormatter F() => new(culture: CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(150, "+150 MHz")]
    [InlineData(-200, "-200 MHz")]
    [InlineData(0, "0 MHz")]
    [InlineData(1000, "+1,000 MHz")]
    public void ClockOffset_HasAnExplicitSign(double value, string expected) => Assert.Equal(expected, F().ClockOffset(value).ToString());

    [Fact]
    public void ClockOffset_NonFinite_IsUnavailable() => Assert.True(F().ClockOffset(double.NaN).IsUnavailable);

    [Fact]
    public void MetricKind_ClockOffset_FormatsThroughTheKindSwitch() =>
        Assert.Equal("+90 MHz", F().Format(MetricKind.ClockOffset, 90).ToString());
}

public class VoltageFrequencyCurveTests
{
    [Fact]
    public void Record_BinsByClock_AndKeepsMinMeanMax()
    {
        var curve = new VoltageFrequencyCurve();

        Assert.True(curve.Record(2700, 0.90));
        Assert.True(curve.Record(2705, 0.92));
        Assert.True(curve.Record(2708, 0.94));

        var point = Assert.Single(curve.Snapshot());
        Assert.Equal(0.90, point.MinVolts, 6);
        Assert.Equal(0.92, point.MeanVolts, 6);
        Assert.Equal(0.94, point.MaxVolts, 6);
        Assert.Equal(3, point.Samples);
        Assert.InRange(point.ClockMhz, 2695, 2715);
        Assert.Equal(3, curve.TotalSamples);
    }

    [Fact]
    public void SingleSampleBins_AreNotDrawnYet()
    {
        var curve = new VoltageFrequencyCurve();
        curve.Record(1500, 0.8);
        curve.Record(2700, 0.9);
        curve.Record(2701, 0.9);

        var points = curve.Snapshot();

        Assert.Single(points);
        Assert.True(points[0].ClockMhz > 2600);
        Assert.Equal(2701, curve.Latest!.Value.ClockMhz);
        Assert.Equal(0.9, curve.Latest!.Value.Volts, 6);
    }

    [Fact]
    public void Snapshot_IsOrderedByClock()
    {
        var curve = new VoltageFrequencyCurve();
        foreach (double clock in new[] { 3000.0, 600.0, 1800.0 })
        {
            curve.Record(clock, 0.9);
            curve.Record(clock + 1, 0.9);
        }

        var clocks = curve.Snapshot().Select(p => p.ClockMhz).ToArray();

        Assert.True(clocks.SequenceEqual(clocks.OrderBy(c => c)));
        Assert.Equal(3, clocks.Length);
    }

    [Theory]
    [InlineData(double.NaN, 0.9)]
    [InlineData(2700, double.NaN)]
    [InlineData(0, 0.9)]
    [InlineData(2700, 0)]
    [InlineData(2700, 5)]
    [InlineData(99999, 0.9)]
    public void ImplausibleReadings_AreRejected(double clock, double volts)
    {
        var curve = new VoltageFrequencyCurve();

        Assert.False(curve.Record(clock, volts));
        Assert.Equal(0, curve.TotalSamples);
        Assert.Null(curve.Latest);
    }

    [Fact]
    public void Clear_EmptiesEverything()
    {
        var curve = new VoltageFrequencyCurve();
        curve.Record(2700, 0.9);
        curve.Record(2700, 0.9);

        curve.Clear();

        Assert.Empty(curve.Snapshot());
        Assert.Equal(0, curve.TotalSamples);
        Assert.Null(curve.Latest);
        Assert.True(curve.Record(2700, 0.95));
        Assert.Equal(0.95, curve.Latest!.Value.Volts, 6);
    }
}

public class VoltageFrequencyRecorderTests
{
    private static (TelemetryEngine Engine, FakeProvider Provider) Create(bool withVoltage)
    {
        var provider = new FakeProvider("gpu", MonitoringSection.Gpu)
            .Define(MetricIds.GpuCoreClock(0), MetricCategory.Gpu, MetricKind.Clock)
            .Define(MetricIds.GpuCoreClock(1), MetricCategory.Gpu, MetricKind.Clock);
        if (withVoltage)
        {
            provider.Define(MetricIds.GpuVoltage(0), MetricCategory.Gpu, MetricKind.Voltage);
        }

        var engine = new TelemetryEngine([provider], new MetricCatalog(), new DeviceRegistry(), new HistoryStore(64), SystemCapabilities.Unknown, new FakeClock());
        engine.InitializeAll();
        return (engine, provider);
    }

    [Fact]
    public void RecordsWhenBothClockAndVoltageArePresent()
    {
        var (engine, provider) = Create(withVoltage: true);
        var recorder = new VoltageFrequencyRecorder();

        provider.Values[MetricIds.GpuCoreClock(0)] = 2700;
        provider.Values[MetricIds.GpuVoltage(0)] = 0.91;
        recorder.Record(engine.PollOnce(), 0);
        recorder.Record(engine.PollOnce(), 0);

        Assert.Equal(2, recorder.For(0).TotalSamples);
        Assert.Single(recorder.For(0).Snapshot());
    }

    [Fact]
    public void NeverInventsAVoltage_WhenItIsNotReported()
    {
        var (engine, provider) = Create(withVoltage: false);
        var recorder = new VoltageFrequencyRecorder();

        provider.Values[MetricIds.GpuCoreClock(0)] = 2700;
        recorder.Record(engine.PollOnce(), 0);

        Assert.Equal(0, recorder.For(0).TotalSamples);
    }

    [Fact]
    public void SlotsAreIndependent_AndNegativeSlotsAreIgnored()
    {
        var (engine, provider) = Create(withVoltage: true);
        var recorder = new VoltageFrequencyRecorder();
        provider.Values[MetricIds.GpuCoreClock(0)] = 2700;
        provider.Values[MetricIds.GpuVoltage(0)] = 0.91;
        var frame = engine.PollOnce();

        recorder.Record(frame, 0);
        recorder.Record(frame, 1);
        recorder.Record(frame, -1);

        Assert.Equal(1, recorder.For(0).TotalSamples);
        Assert.Equal(0, recorder.For(1).TotalSamples);
    }
}
