using Coreloom.Core.Hardware;
using Coreloom.Core.Settings;
using Coreloom.Core.Telemetry;
using Coreloom.ViewModels.Infrastructure;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

public class OverviewHeaderTests
{
    [Fact]
    public void Header_DescribesTheBoardGraphicsCardAndSpecs()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));

        overview.Activate(monitor.Engine.LatestFrame);
        var specs = overview.Specs;

        Assert.Equal("ASRock X870E Nova WiFi", specs.Title);
        Assert.Equal("NVIDIA GeForce RTX 5090", specs.Subtitle);
        Assert.Equal(GpuSize.Large, specs.GpuSize);
        Assert.True(specs.ShowGpuLabel);
        Assert.Equal("RTX 5090", specs.GpuLabel);
        Assert.Equal<KeyValueItem>(
            new[] { new KeyValueItem("Processor", "AMD Ryzen 9 9950X3D"), new KeyValueItem("Cores", "16 cores · 32 threads"), new KeyValueItem("Memory", "64 GB") },
            specs.LeftSpecs);
        Assert.Equal<KeyValueItem>(new[] { new KeyValueItem("System", "Windows 11 Pro 24H2") }, specs.RightSpecs);

        // No CPU temperature in the fixture (it needs administrator rights): the pump screen shows the GPU's.
        Assert.Equal("38°", specs.ScreenValue);
        Assert.Equal("GPU", specs.ScreenLabel);
        settings.Dispose();
    }

    [Fact]
    public void Header_FollowsTheSelectedGraphicsCard()
    {
        var (fixtureMonitor, settings, _) = Fixture.Create();
        var monitor = new MonitorContext
        {
            Engine = fixtureMonitor.Engine,
            Summary = fixtureMonitor.Summary with { Motherboard = null },
            Processes = fixtureMonitor.Processes,
            PrimaryGpuSlot = () => 1,
            Gpus = () => [(0, "AMD Radeon(TM) Graphics", true), (1, "AMD Radeon RX 9060 XT", false)],
            NetworkAdapters = () => [],
            LibreHardwareMonitorIncluded = true,
        };
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));

        overview.Activate(monitor.Engine.LatestFrame);
        Assert.Equal("PC", overview.Specs.Title); // no board name: the computer name
        Assert.Equal("AMD Radeon RX 9060 XT", overview.Specs.Subtitle);
        Assert.Equal(GpuSize.Medium, overview.Specs.GpuSize);
        Assert.Equal("RX 9060 XT", overview.Specs.GpuLabel);

        settings.Current.Monitoring.PreferredGpuSlot = 0;
        overview.Update(monitor.Engine.LatestFrame);
        Assert.Equal("AMD Radeon Graphics", overview.Specs.Subtitle);
        Assert.Equal(GpuSize.None, overview.Specs.GpuSize);
        Assert.False(overview.Specs.ShowGpuLabel); // integrated graphics: no model name on the pictured card
        settings.Dispose();
    }
}

public class PcPictureTests
{
    [Fact]
    public void Glow_IsLoadedFromSettings_AndAChoiceIsSaved()
    {
        var (monitor, settings, _) = Fixture.Create();
        settings.Current.Dashboard.PcGlow = PcGlow.Purple;
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));
        var specs = overview.Specs;

        Assert.Equal(PcGlow.Purple, specs.Glow);
        Assert.True(specs.GlowOptions.Single(o => o.Glow == PcGlow.Purple).IsSelected);
        Assert.Equal(6, specs.GlowOptions.Count);
        Assert.Equal("#FF2933", specs.GlowOptions.Single(o => o.Glow == PcGlow.Red).Hex);
        Assert.True(specs.GlowOptions.Single(o => o.Glow == PcGlow.Rainbow).IsRainbow);

        string? changed = null;
        settings.Changed += section => changed = section;
        specs.SelectGlowCommand.Execute(specs.GlowOptions.Single(o => o.Glow == PcGlow.Rainbow));

        Assert.Equal(PcGlow.Rainbow, specs.Glow);
        Assert.Equal(PcGlow.Rainbow, settings.Current.Dashboard.PcGlow);
        Assert.Equal("Dashboard", changed);
        Assert.Single(specs.GlowOptions, o => o.IsSelected);
        Assert.True(specs.GlowOptions.Single(o => o.Glow == PcGlow.Rainbow).IsSelected);
        settings.Dispose();
    }

    [Fact]
    public void ReduceMotion_StopsTheFans()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));
        Assert.True(overview.Specs.AnimateFans);

        settings.Current.Appearance.ReducedMotion = true;
        overview.OnSettingsChanged("Appearance");

        Assert.False(overview.Specs.AnimateFans);
        settings.Dispose();
    }

    [Fact]
    public void Fans_FollowTheBoardsFanReadings()
    {
        var (monitor, settings, provider) = Fixture.Create(extra: p => p
            .Define("board/fan/cpu-fan", MetricCategory.Motherboard, MetricKind.Fan, 1200)
            .Define("board/fan/chassis-fan-1", MetricCategory.Motherboard, MetricKind.Fan, 800)
            .Define("board/fan/chassis-fan-2", MetricCategory.Motherboard, MetricKind.Fan, 1000)
            .Define("board/fan/chassis-fan-3", MetricCategory.Motherboard, MetricKind.Fan, 0)
            .Define("board/fan/aio-pump", MetricCategory.Motherboard, MetricKind.Fan, 2800));
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));

        overview.Activate(monitor.Engine.LatestFrame);

        Assert.Equal(1.2, overview.Specs.TopFanSpeed, 6);     // the CPU fan
        Assert.Equal(0.9, overview.Specs.BottomFanSpeed, 6);  // the two connected case fans; the pump is left out
        Assert.Contains("1,200 RPM", overview.Specs.FanToolTip);
        Assert.Contains("2 case fans", overview.Specs.FanToolTip);

        provider.Values["board/fan/cpu-fan"] = 0;  // the fan stopped (or was unplugged)
        provider.Values["board/fan/chassis-fan-1"] = 0;
        provider.Values["board/fan/chassis-fan-2"] = 0;
        monitor.Engine.PollOnce();
        overview.Update(monitor.Engine.LatestFrame);
        Assert.Equal(PcFans.UnknownSpeed, overview.Specs.TopFanSpeed, 6);
        settings.Dispose();
    }

    [Fact]
    public void WithoutFanReadings_TheFansIdle()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));

        overview.Activate(monitor.Engine.LatestFrame);

        Assert.Equal(PcFans.UnknownSpeed, overview.Specs.TopFanSpeed, 6);
        Assert.Equal(PcFans.UnknownSpeed, overview.Specs.BottomFanSpeed, 6);
        Assert.Equal(PcFans.UnknownToolTip, overview.Specs.FanToolTip);
        settings.Dispose();
    }
}

public class PcFansTests
{
    private static readonly Core.Formatting.UnitFormatter Formatter = new(culture: System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1200, 1.2)]
    [InlineData(2500, 2.5)]
    [InlineData(9000, PcFans.MaxSpeed)]
    [InlineData(double.NaN, PcFans.UnknownSpeed)]
    public void SpeedFromRpm(double rpm, double expected) => Assert.Equal(expected, PcFans.SpeedFromRpm(rpm), 6);

    [Fact]
    public void Classify_SeparatesCpuAndCaseFans_AndDropsPumps()
    {
        var (cpu, rest) = PcFans.Classify([
            new PcFanSensor("a", "System Fan #2"), new PcFanSensor("b", "CPU Fan"), new PcFanSensor("c", "AIO Pump"),
            new PcFanSensor("d", "CPU Optional Fan"), new PcFanSensor("e", "System Fan #1"), new PcFanSensor("f", "W_PUMP+"),
        ]);

        Assert.Equal<string>(new[] { "CPU Fan", "CPU Optional Fan" }, cpu.Select(f => f.Name));
        Assert.Equal<string>(new[] { "System Fan #1", "System Fan #2" }, rest.Select(f => f.Name));
    }

    [Fact]
    public void UnnamedFans_AllFollowTheAverage()
    {
        var (cpu, rest) = PcFans.Classify([new PcFanSensor("1", "Fan #1"), new PcFanSensor("2", "Fan #2"), new PcFanSensor("3", "Fan #3")]);
        var rpm = new Dictionary<string, double> { ["1"] = 900, ["2"] = 1500, ["3"] = 0 };

        var reading = PcFans.Read(cpu, rest, id => rpm[id], Formatter);

        Assert.Equal(1.2, reading.TopSpeed, 6);
        Assert.Equal(1.2, reading.BottomSpeed, 6);
        Assert.Equal("The fans turn with the average of 2 fans: 1,200 RPM.", reading.ToolTip);
    }

    [Fact]
    public void ACpuFanOnly_DrivesAllSix()
    {
        var reading = PcFans.Read([new PcFanSensor("c", "CPU Fan")], [], _ => 700, Formatter);

        Assert.Equal(0.7, reading.TopSpeed, 6);
        Assert.Equal(0.7, reading.BottomSpeed, 6);
        Assert.Equal("The fans turn with CPU Fan: 700 RPM.", reading.ToolTip);
    }
}

public class SensorColumnsTests
{
    [Fact]
    public void ShortLists_StayInOneColumn()
    {
        var groups = new[] { Group("Temperatures", 5), Group("Fans", 3) };

        var (first, second) = SensorColumns.Split(groups);

        Assert.Equal<SensorGroupViewModel>(groups, first);
        Assert.Empty(second);
    }

    [Fact]
    public void WholeGroups_AreBalancedAcrossColumns()
    {
        var groups = new[] { Group("Temperatures", 10), Group("Clocks", 4), Group("Power", 5) };

        var (first, second) = SensorColumns.Split(groups);

        Assert.Equal<string>(new[] { "Temperatures" }, first.Select(g => g.Name));
        Assert.Equal<string>(new[] { "Clocks", "Power" }, second.Select(g => g.Name));
    }

    [Fact]
    public void OneLongGroup_IsSplitWithAContinuedTitle()
    {
        var (first, second) = SensorColumns.Split([Group("Temperatures", 20)]);

        Assert.Equal(10, Assert.Single(first).Rows.Count);
        var rest = Assert.Single(second);
        Assert.Equal("Temperatures, continued", rest.Name);
        Assert.Equal(10, rest.Rows.Count);
    }

    [Fact]
    public void AGroupIsSplit_OnlyWhenThatIsClearlyMoreEven()
    {
        var groups = new[] { Group("Temperatures", 12), Group("Fans", 2), Group("Voltages", 2) };

        var (first, second) = SensorColumns.Split(groups);

        Assert.Equal(9, Assert.Single(first).Rows.Count);
        Assert.Equal<string>(new[] { "Temperatures, continued", "Fans", "Voltages" }, second.Select(g => g.Name));
        Assert.Equal(16, first.Concat(second).Sum(g => g.Rows.Count));
    }

    private static SensorGroupViewModel Group(string name, int rows) =>
        new(name, Enumerable.Range(0, rows)
            .Select(i => new SensorRowViewModel(new MetricDescriptor($"{name}/{i}", $"{name} {i}", MetricCategory.Gpu, MetricKind.Temperature)))
            .ToArray());
}

public class DashboardEditorTests
{
    [Fact]
    public void Customize_OpensOnTheOverview_AndCloses()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));
        Assert.False(overview.Customize.IsOpen);

        overview.CustomizeCommand.Execute(null);
        Assert.True(overview.Customize.IsOpen);
        Assert.Equal(settings.Current.Dashboard.Cards.Count, overview.Customize.Cards.Count);
        Assert.Same(overview.Specs, overview.Customize.Specs); // the lighting colors are in the same card

        overview.Customize.CloseCommand.Execute(null);
        Assert.False(overview.Customize.IsOpen);
        settings.Dispose();
    }

    [Fact]
    public void Edits_ApplyToTheDashboardRightAway()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));
        settings.Changed += overview.OnSettingsChanged; // as the shell does for every page
        overview.CustomizeCommand.Execute(null);
        var editor = overview.Customize;

        editor.Cards.First(c => c.Id == DashboardCatalog.GpuPower).IsVisible = false;
        Assert.DoesNotContain(overview.SummaryCards, c => c.Id == DashboardCatalog.GpuPower);
        Assert.False(settings.Current.Dashboard.Cards.First(c => c.Id == DashboardCatalog.GpuPower).Visible);

        var cpu = editor.Cards.First(c => c.Id == DashboardCatalog.CpuLoad);
        while (editor.Cards.IndexOf(cpu) > 0)
        {
            cpu.MoveUpCommand.Execute(null);
        }

        Assert.Equal(DashboardCatalog.CpuLoad, overview.SummaryCards[0].Id);
        Assert.Equal(DashboardCatalog.CpuLoad, settings.Current.Dashboard.Cards[0].Id);

        editor.ShowStatistics = false;
        Assert.False(settings.Current.Dashboard.ShowStatistics);
        Assert.False(overview.ShowStatisticsOnCards);

        editor.ResetLayoutCommand.Execute(null);
        Assert.Equal<string>(DashboardCatalog.Defaults().Select(c => c.Id), editor.Cards.Select(c => c.Id));
        Assert.Contains(overview.SummaryCards, c => c.Id == DashboardCatalog.GpuPower);
        settings.Dispose();
    }
}
