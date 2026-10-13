using Coreloom.Core.Hardware;
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
        Assert.True(specs.HasLargeGpu);
        Assert.False(specs.HasMediumGpu);
        Assert.Equal("RTX 5090", specs.GpuLabel);
        Assert.False(specs.FourMemorySticks);
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
        Assert.True(overview.Specs.HasMediumGpu);
        Assert.Equal("RX 9060 XT", overview.Specs.GpuLabel);

        settings.Current.Monitoring.PreferredGpuSlot = 0;
        overview.Update(monitor.Engine.LatestFrame);
        Assert.Equal("AMD Radeon Graphics", overview.Specs.Subtitle);
        Assert.Equal(GpuSize.None, overview.Specs.GpuSize);
        settings.Dispose();
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
