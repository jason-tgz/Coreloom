using Coreloom.Core.Dashboard;
using Coreloom.Core.Settings;

namespace Coreloom.Core.Tests;

public class DashboardLayoutTests
{
    private static readonly TileSize Card = new(1, 2);
    private static readonly TileSize Panel = new(2, 3);

    private static TileSize[] DefaultDashboard() => [Card, Card, Card, Card, Card, Card, Panel, Panel, Panel, Panel, Panel, Panel];

    [Theory]
    [InlineData(1472.0, 6)] // six 232px columns and five 16px gaps
    [InlineData(1471.0, 4)] // five would fit, but even counts keep panels side by side
    [InlineData(976.0, 4)]
    [InlineData(975.0, 2)]
    [InlineData(480.0, 2)]
    [InlineData(479.0, 1)]
    [InlineData(0.0, 1)]
    [InlineData(double.PositiveInfinity, 6)]
    public void ColumnsFor_UsesSixFourTwoOrOne(double width, int expected)
    {
        Assert.Equal(expected, DashboardLayout.ColumnsFor(width, 232, 16));
    }

    [Fact]
    public void Place_DefaultDashboard_IsARowOfCardsThenRowsOfThreePanels()
    {
        var placements = DashboardLayout.Place(DefaultDashboard(), 6);

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(new TilePlacement(i, 0, 1, 2), placements[i]);
        }

        Assert.Equal(new TilePlacement(0, 2, 2, 3), placements[6]);
        Assert.Equal(new TilePlacement(2, 2, 2, 3), placements[7]);
        Assert.Equal(new TilePlacement(4, 2, 2, 3), placements[8]);
        Assert.Equal(new TilePlacement(0, 5, 2, 3), placements[9]);
        Assert.Equal(8, DashboardLayout.RowCount(placements));
    }

    [Fact]
    public void Place_FourColumns_PutsTheFirstPanelBesideTheLastTwoCards()
    {
        var placements = DashboardLayout.Place(DefaultDashboard(), 4);

        Assert.Equal(new TilePlacement(0, 2, 1, 2), placements[4]);
        Assert.Equal(new TilePlacement(1, 2, 1, 2), placements[5]);
        Assert.Equal(new TilePlacement(2, 2, 2, 3), placements[6]);
        Assert.Equal(new TilePlacement(0, 4, 2, 3), placements[7]); // under the two cards
        Assert.Equal(new TilePlacement(2, 5, 2, 3), placements[8]);
    }

    [Fact]
    public void Place_NarrowsCardsWiderThanTheGrid()
    {
        var placements = DashboardLayout.Place([new TileSize(6, 3), Card], 4);
        Assert.Equal(new TilePlacement(0, 0, 4, 3), placements[0]);
        Assert.Equal(new TilePlacement(0, 3, 1, 2), placements[1]);
    }

    [Fact]
    public void Place_KeepsTheOrder_LeavingAGapRatherThanMovingALaterCardUp()
    {
        var placements = DashboardLayout.Place([new TileSize(1, 1), new TileSize(3, 1), new TileSize(1, 1)], 3);
        Assert.Equal(new TilePlacement(0, 0, 1, 1), placements[0]);
        Assert.Equal(new TilePlacement(0, 1, 3, 1), placements[1]);
        Assert.Equal(new TilePlacement(0, 2, 1, 1), placements[2]);
    }

    [Fact]
    public void Place_TallCardsLetOthersFlowBesideThem()
    {
        var placements = DashboardLayout.Place([new TileSize(2, 4), Card, Card, Card, Card], 4);
        Assert.Equal(new TilePlacement(2, 0, 1, 2), placements[1]);
        Assert.Equal(new TilePlacement(3, 0, 1, 2), placements[2]);
        Assert.Equal(new TilePlacement(2, 2, 1, 2), placements[3]);
        Assert.Equal(new TilePlacement(3, 2, 1, 2), placements[4]);
    }

    [Fact]
    public void DropIndex_TakesThePlaceOfTheCardUnderIt()
    {
        var tiles = DefaultDashboard();

        // The first card held over the fourth card's place, and the last card held over the first's.
        Assert.Equal(3, DashboardLayout.DropIndex(tiles, 0, 6, 3.1, 0.2, 248, 108));
        Assert.Equal(0, DashboardLayout.DropIndex(tiles, 5, 6, -0.2, 0.1, 248, 108));

        // A panel held over the third panel's place.
        Assert.Equal(8, DashboardLayout.DropIndex(tiles, 6, 6, 4.1, 2.2, 248, 108));

        // A card dropped among the panels goes between them.
        int index = DashboardLayout.DropIndex(tiles, 0, 6, 2, 2, 248, 108);
        var placed = DashboardLayout.Place(DashboardLayout.Move(tiles, 0, index), 6)[index];
        Assert.Equal((2, 2), (placed.Column, placed.Row));
    }

    [Fact]
    public void DropIndex_StaysPutUntilAnotherPlaceIsClearlyNearer()
    {
        var tiles = DefaultDashboard();

        // Halfway between the second and third places: keeps whichever it already had.
        Assert.Equal(1, DashboardLayout.DropIndex(tiles, 0, 6, 1.5, 0, 248, 108, current: 1));
        Assert.Equal(2, DashboardLayout.DropIndex(tiles, 0, 6, 1.5, 0, 248, 108, current: 2));

        // Well over the third place: moves.
        Assert.Equal(2, DashboardLayout.DropIndex(tiles, 0, 6, 1.9, 0, 248, 108, current: 1));
    }

    [Fact]
    public void Move_MovesOneItem()
    {
        string[] items = ["a", "b", "c"];
        Assert.Equal<string>(new[] { "b", "c", "a" }, DashboardLayout.Move(items, 0, 2));
        Assert.Equal<string>(new[] { "c", "a", "b" }, DashboardLayout.Move(items, 2, 0));
        Assert.Equal<string>(items, DashboardLayout.Move(items, 5, 0));
    }

    [Theory]
    [InlineData(232.0, 1)]
    [InlineData(330.0, 1)]   // less than halfway into the next column
    [InlineData(380.0, 2)]
    [InlineData(2000.0, 6)]  // limited to the maximum
    [InlineData(40.0, 1)]    // and the minimum
    public void Snap_RoundsToWholeCells(double size, int expected)
    {
        Assert.Equal(expected, DashboardLayout.Snap(size, 232, 16, 1, 6));
    }

    [Fact]
    public void RowHeight_FitsEveryCardAtItsSmallestSize()
    {
        // A 200px summary card over two rows needs 92px rows; a 300px panel over three needs 90.
        Assert.Equal(92, DashboardLayout.RowHeight([(200, 2), (300, 3)], 16, 40));
        Assert.Equal(90, DashboardLayout.RowHeight([(170, 2), (300, 3)], 16, 40));
        Assert.Equal(40, DashboardLayout.RowHeight([], 16, 40));
        Assert.Equal(200, DashboardLayout.Extent(2, 92, 16));
        Assert.Equal(0, DashboardLayout.Extent(0, 92, 16));
    }
}

public class DashboardCardSizeTests
{
    [Fact]
    public void Defaults_AreOneByTwoForCards_AndTwoByThreeForPanels()
    {
        Assert.Equal(new DashboardCardSize(1, 2, 1, 2), DashboardCatalog.SizeOf(DashboardCatalog.CpuLoad));
        Assert.Equal(new DashboardCardSize(2, 3, 2, 3), DashboardCatalog.SizeOf(DashboardCatalog.Network));
        Assert.All(DashboardCatalog.Defaults(), c => Assert.Null(c.Columns));
        Assert.All(DashboardCatalog.Defaults(), c => Assert.Null(c.Rows));
    }

    [Fact]
    public void Normalize_KeepsSizesWithinLimits_AndForgetsDefaults()
    {
        var cards = new List<DashboardCardSetting>
        {
            new() { Id = DashboardCatalog.CpuLoad, Columns = 3, Rows = 4 },
            new() { Id = DashboardCatalog.Network, Columns = 1, Rows = 99 },
            new() { Id = DashboardCatalog.Memory, Columns = 1, Rows = 2 },
        };

        var normalized = DashboardCatalog.Normalize(cards);
        var cpu = normalized.First(c => c.Id == DashboardCatalog.CpuLoad);
        var network = normalized.First(c => c.Id == DashboardCatalog.Network);
        var memory = normalized.First(c => c.Id == DashboardCatalog.Memory);

        Assert.Equal((3, 4), (cpu.Columns, cpu.Rows));
        Assert.Null(network.Columns); // panels are at least two columns, which is their default
        Assert.Equal(DashboardCatalog.MaxRows, network.Rows);
        Assert.Null(memory.Columns);
        Assert.Null(memory.Rows);
        Assert.Equal((2, 8), DashboardCatalog.SizeOf(network));
    }

    [Fact]
    public void Sizes_RoundTripThroughSettingsJson_AndDefaultsAreNotWritten()
    {
        var settings = new AppSettings();
        settings.Dashboard.Cards[0].Columns = 2;
        settings.Dashboard.Cards[0].Rows = 3;
        settings.Dashboard.ShowPcPicture = false;

        string json = SettingsStore.Serialize(settings);
        Assert.Contains("\"columns\": 2", json);
        Assert.DoesNotContain("\"columns\": null", json);
        Assert.Contains("\"showPcPicture\": false", json);

        var read = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions)!.Normalize();
        Assert.Equal((2, 3), (read.Dashboard.Cards[0].Columns, read.Dashboard.Cards[0].Rows));
        Assert.False(read.Dashboard.ShowPcPicture);
        Assert.True(new AppSettings().Dashboard.ShowPcPicture);
    }
}
