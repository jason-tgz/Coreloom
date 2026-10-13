using System.Globalization;
using Coreloom.Core.Formatting;
using Coreloom.Core.Hardware;
using Coreloom.Core.History;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

public class GpuTuningViewModelTests
{
    private static readonly UnitFormatter Formatter = new(culture: CultureInfo.InvariantCulture);

    private sealed class Rig
    {
        public Rig(Action<ScriptedProvider> define)
        {
            Provider = new ScriptedProvider();
            define(Provider);
            Engine = new TelemetryEngine([Provider], new MetricCatalog(), new DeviceRegistry(), new HistoryStore(64), SystemCapabilities.Unknown);
            Engine.InitializeAll();
            Recorder = new VoltageFrequencyRecorder();
            Tuning = new GpuTuningViewModel(Recorder);
        }

        public ScriptedProvider Provider { get; }

        public TelemetryEngine Engine { get; }

        public VoltageFrequencyRecorder Recorder { get; }

        public GpuTuningViewModel Tuning { get; }

        public TelemetryFrame Poll()
        {
            var frame = Engine.PollOnce();
            Recorder.Record(frame, 0);
            return frame;
        }

        public void Update(HardwareVendor vendor = HardwareVendor.Nvidia, bool force = false) =>
            Tuning.Update(Poll(), 0, vendor, Formatter, Engine.Catalog, force);
    }

    private static Rig Stock(double core = 0, double memory = 0, double limit = 575, double stock = 575) => new(p => p
        .Define(MetricIds.GpuCoreClockOffset(0), MetricCategory.Gpu, MetricKind.ClockOffset, core)
        .Define(MetricIds.GpuCoreClockOffsetMin(0), MetricCategory.Gpu, MetricKind.ClockOffset, -500)
        .Define(MetricIds.GpuCoreClockOffsetMax(0), MetricCategory.Gpu, MetricKind.ClockOffset, 1000)
        .Define(MetricIds.GpuMemoryClockOffset(0), MetricCategory.Gpu, MetricKind.ClockOffset, memory)
        .Define(MetricIds.GpuPowerLimit(0), MetricCategory.Gpu, MetricKind.Power, limit)
        .Define(MetricIds.GpuPowerLimitDefault(0), MetricCategory.Gpu, MetricKind.Power, stock)
        .Define(MetricIds.GpuPowerLimitMin(0), MetricCategory.Gpu, MetricKind.Power, 300)
        .Define(MetricIds.GpuPowerLimitMax(0), MetricCategory.Gpu, MetricKind.Power, 600)
        .Define(MetricIds.GpuCoreClock(0), MetricCategory.Gpu, MetricKind.Clock, 2700)
        .Define(MetricIds.GpuVoltage(0), MetricCategory.Gpu, MetricKind.Voltage, 0.912));

    [Fact]
    public void StockSettings_ShowNoBadge()
    {
        var rig = Stock();

        rig.Update();

        Assert.Equal(GpuTuningKind.NotDetected, rig.Tuning.Kind);
        Assert.False(rig.Tuning.HasBadge);
        Assert.Empty(rig.Tuning.FindingLines);
        Assert.True(rig.Tuning.IsReadable);
        Assert.Equal(string.Empty, rig.Tuning.UnavailableMessage);
        Assert.Equal(TuningTone.None, rig.Tuning.BadgeTone);
    }

    [Fact]
    public void CoreOffset_ShowsAnOverclockBadge_WithDetailsInTheTooltip()
    {
        var rig = Stock(core: 150);

        rig.Update();

        Assert.True(rig.Tuning.HasBadge);
        Assert.Equal("Overclock", rig.Tuning.BadgeText);
        Assert.Equal(TuningTone.Raised, rig.Tuning.BadgeTone);
        Assert.Equal("Core clock offset +150 MHz", Assert.Single(rig.Tuning.FindingLines));
        Assert.Contains("+150 MHz", rig.Tuning.BadgeToolTip);
        Assert.Contains("never changes GPU settings", rig.Tuning.BadgeToolTip);
    }

    [Fact]
    public void LoweredPowerLimit_IsReducedAndBlue()
    {
        var rig = Stock(limit: 450);

        rig.Update();

        Assert.Equal(GpuTuningKind.Reduced, rig.Tuning.Kind);
        Assert.Equal(TuningTone.Lowered, rig.Tuning.BadgeTone);
        Assert.Contains("78%", Assert.Single(rig.Tuning.FindingLines));
    }

    [Fact]
    public void MixedSettings_AreCustomTuning()
    {
        var rig = Stock(core: 100, limit: 450);

        rig.Update();

        Assert.Equal(GpuTuningKind.Mixed, rig.Tuning.Kind);
        Assert.Equal("Custom tuning", rig.Tuning.BadgeText);
        Assert.Equal(2, rig.Tuning.FindingLines.Count);
    }

    [Fact]
    public void BadgeFollowsTheSettings_WhenTheyChangeWhileRunning()
    {
        var rig = Stock();
        rig.Update();
        Assert.False(rig.Tuning.HasBadge);

        rig.Provider.Values[MetricIds.GpuCoreClockOffset(0)] = 75;
        rig.Update();
        Assert.True(rig.Tuning.HasBadge);

        rig.Provider.Values[MetricIds.GpuCoreClockOffset(0)] = 0;
        rig.Update();
        Assert.False(rig.Tuning.HasBadge);
    }

    [Fact]
    public void NothingReported_NvidiaGpu_SaysSoInsteadOfClaimingStock()
    {
        var rig = new Rig(p => p.Define(MetricIds.GpuLoad(0), MetricCategory.Gpu, MetricKind.Load, 5));

        rig.Update(HardwareVendor.Nvidia);

        Assert.Equal(GpuTuningKind.Unavailable, rig.Tuning.Kind);
        Assert.False(rig.Tuning.HasBadge);
        Assert.False(rig.Tuning.IsReadable);
        Assert.Contains("NVIDIA driver did not report", rig.Tuning.UnavailableMessage);
        Assert.Equal("Tuning settings can't be read on this GPU", rig.Tuning.Headline);
    }

    [Fact]
    public void NonNvidiaGpu_ExplainsThatOnlyNvidiaIsRead()
    {
        var rig = new Rig(p => p.Define(MetricIds.GpuLoad(0), MetricCategory.Gpu, MetricKind.Load, 5));

        rig.Update(HardwareVendor.Amd);

        Assert.False(rig.Tuning.IsReadable);
        Assert.Contains("NVIDIA GPUs only", rig.Tuning.UnavailableMessage);
    }

    [Fact]
    public void ClosedViewer_DoesNotBuildDetails_OpeningFillsThem()
    {
        var rig = Stock(core: 150);

        rig.Update();
        Assert.False(rig.Tuning.CoreOffset.HasValue);
        Assert.Empty(rig.Tuning.CurvePoints);

        rig.Tuning.IsOpen = true;
        rig.Update(force: true);

        Assert.True(rig.Tuning.CoreOffset.HasValue);
    }

    [Fact]
    public void OpenViewer_MetersShowValueRangeAndStockReference()
    {
        var rig = Stock(core: 150, limit: 600);
        rig.Tuning.IsOpen = true;

        rig.Update();

        var core = rig.Tuning.CoreOffset;
        Assert.Equal("+150 MHz", core.ValueText);
        Assert.True(core.HasRange);
        Assert.Equal(-500, core.Minimum);
        Assert.Equal(1000, core.Maximum);
        Assert.Equal(0, core.Reference);
        Assert.Equal(TuningTone.Raised, core.Tone);
        Assert.Contains("-500 MHz", core.RangeText);
        Assert.Contains("+1,000 MHz", core.RangeText);

        var memory = rig.Tuning.MemoryOffset;
        Assert.Equal("0 MHz", memory.ValueText);
        Assert.Equal(TuningTone.None, memory.Tone);
        Assert.Equal("Stock (no offset)", memory.StatusText);

        var power = rig.Tuning.PowerLimit;
        Assert.Equal("600 W", power.ValueText);
        Assert.Equal(575, power.Reference);
        Assert.Equal(300, power.Minimum);
        Assert.Equal(600, power.Maximum);
        Assert.Equal("Default 575 W", power.ReferenceText);
        Assert.Equal(TuningTone.Raised, power.Tone);
        Assert.Contains("104%", power.StatusText);
    }

    [Fact]
    public void Meter_WithoutARange_StillShowsItsValue_ButNoBar()
    {
        var rig = new Rig(p => p
            .Define(MetricIds.GpuCoreClockOffset(0), MetricCategory.Gpu, MetricKind.ClockOffset, -50)
            .Define(MetricIds.GpuMemoryClockOffset(0), MetricCategory.Gpu, MetricKind.ClockOffset, 0));
        rig.Tuning.IsOpen = true;

        rig.Update();

        Assert.True(rig.Tuning.CoreOffset.HasValue);
        Assert.False(rig.Tuning.CoreOffset.HasRange);
        Assert.Equal("-50 MHz", rig.Tuning.CoreOffset.ValueText);
        Assert.Equal(string.Empty, rig.Tuning.CoreOffset.RangeText);
        Assert.Equal(TuningTone.Lowered, rig.Tuning.CoreOffset.Tone);
        Assert.False(rig.Tuning.PowerLimit.HasValue);
        Assert.NotEqual(string.Empty, rig.Tuning.PowerLimit.UnavailableText);
    }

    [Fact]
    public void ObservedCurve_FillsFromRealReadings_AndMarksTheCurrentPoint()
    {
        var rig = Stock();
        rig.Tuning.IsOpen = true;

        rig.Update();
        rig.Update();
        rig.Update(force: true);

        Assert.True(rig.Tuning.HasCurve);
        var point = Assert.Single(rig.Tuning.CurvePoints);
        Assert.Equal(0.912, point.MeanVolts, 6);
        Assert.Equal(2700, rig.Tuning.CurrentClockMhz);
        Assert.Equal(0.912, rig.Tuning.CurrentVolts, 6);
        Assert.Contains("readings", rig.Tuning.CurveSummary);
        Assert.Equal(string.Empty, rig.Tuning.CurveMessage);
    }

    [Fact]
    public void ObservedCurve_WithoutVoltage_ExplainsInsteadOfDrawingAnything()
    {
        var rig = new Rig(p => p
            .Define(MetricIds.GpuCoreClockOffset(0), MetricCategory.Gpu, MetricKind.ClockOffset, 0)
            .Define(MetricIds.GpuCoreClock(0), MetricCategory.Gpu, MetricKind.Clock, 2700));
        rig.Tuning.IsOpen = true;

        rig.Update(force: true);

        Assert.False(rig.Tuning.HasCurve);
        Assert.Empty(rig.Tuning.CurvePoints);
        Assert.Contains("voltage isn't being reported", rig.Tuning.CurveMessage);
        Assert.True(double.IsNaN(rig.Tuning.CurrentVolts));
    }

    [Fact]
    public void ClearingTheCurve_EmptiesOnlyCoreloomsRecordings()
    {
        var rig = Stock();
        rig.Tuning.IsOpen = true;
        rig.Update();
        rig.Update(force: true);
        Assert.True(rig.Tuning.HasCurve);

        rig.Tuning.ResetCurveCommand.Execute(null);

        Assert.False(rig.Tuning.HasCurve);
        Assert.Equal(0, rig.Recorder.For(0).TotalSamples);
        Assert.Contains("Cleared", rig.Tuning.CurveMessage);
    }

    [Fact]
    public void TogglingTheViewer_RaisesOpenChanged_ForAnImmediateRefresh()
    {
        var rig = Stock();
        int raised = 0;
        rig.Tuning.OpenChanged += () => raised++;

        rig.Tuning.ToggleCommand.Execute(null);
        rig.Tuning.ToggleCommand.Execute(null);

        Assert.Equal(2, raised);
        Assert.False(rig.Tuning.IsOpen);
    }

    [Fact]
    public void SwitchingGpuSlot_ReevaluatesTheBadge()
    {
        var rig = Stock(core: 150);
        rig.Update();
        Assert.True(rig.Tuning.HasBadge);

        // Slot 1 reports nothing: the badge must not stay on from slot 0.
        rig.Tuning.Update(rig.Poll(), 1, HardwareVendor.Nvidia, Formatter, rig.Engine.Catalog);

        Assert.False(rig.Tuning.HasBadge);
        Assert.Equal(GpuTuningKind.Unavailable, rig.Tuning.Kind);
    }
}

public class VendorMarkViewModelTests
{
    [Fact]
    public void GpuPage_ExposesTheVendorKeyOfTheSelectedGpu()
    {
        var (monitor, settings, _) = Fixture.Create();
        var gpu = new GpuViewModel(Fixture.Context(monitor, settings));

        gpu.Activate(monitor.Engine.LatestFrame);

        Assert.Equal(HardwareVendor.Nvidia, gpu.Vendor);
        Assert.Equal("nvidia", gpu.VendorKey);
        Assert.NotNull(gpu.Tuning);
        settings.Dispose();
    }

    [Fact]
    public void CpuPage_ExposesTheVendorKeyFromTheProcessorName_BeforeDeviceInfoArrives()
    {
        var (monitor, settings, _) = Fixture.Create();
        var cpu = new CpuViewModel(Fixture.Context(monitor, settings));

        Assert.Equal("amd", cpu.VendorKey);
        settings.Dispose();
    }

    [Fact]
    public void CpuPage_PrefersTheVendorReportedByTheDevice()
    {
        var (monitor, settings, _) = Fixture.Create();
        monitor.Engine.Devices.Upsert(new DeviceInfo(MetricIds.CpuDeviceId, MetricCategory.Cpu, "Intel(R) Core(TM) Ultra 9 285K") { Vendor = "Intel" });
        var cpu = new CpuViewModel(Fixture.Context(monitor, settings));

        cpu.Activate(monitor.Engine.LatestFrame);

        Assert.Equal("intel", cpu.VendorKey);
        settings.Dispose();
    }
}
