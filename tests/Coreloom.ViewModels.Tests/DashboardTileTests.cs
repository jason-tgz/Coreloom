using Coreloom.Core.Settings;
using Coreloom.ViewModels.Pages;
using Coreloom.ViewModels.Services;

namespace Coreloom.ViewModels.Tests;

public class DashboardTileTests
{
    private static (OverviewViewModel Overview, SettingsService Settings) Create()
    {
        var (monitor, settings, _) = Fixture.Create();
        var overview = new OverviewViewModel(Fixture.Context(monitor, settings));
        settings.Changed += overview.OnSettingsChanged; // as the shell does for every page
        return (overview, settings);
    }

    private static string[] VisibleOrder(OverviewViewModel overview) =>
        overview.Tiles.OrderBy(t => t.Order).Select(t => t.Id).ToArray();

    private static DashboardTileViewModel Tile(OverviewViewModel overview, string id) => overview.Tiles.First(t => t.Id == id);

    [Fact]
    public void Tiles_AreEveryVisibleCard_InOrder_AtTheirDefaultSize()
    {
        var (overview, settings) = Create();

        Assert.Equal(DashboardCatalog.All.Count, overview.Tiles.Count);
        Assert.Equal<string>(DashboardCatalog.All.Select(c => c.Id).ToArray(), VisibleOrder(overview));
        var cpu = Tile(overview, DashboardCatalog.CpuLoad);
        Assert.Same(overview.SummaryCards.First(c => c.Id == DashboardCatalog.CpuLoad), cpu.Content);
        Assert.Equal((1, 2), (cpu.ColumnSpan, cpu.RowSpan));
        var network = Tile(overview, DashboardCatalog.Network);
        Assert.IsType<NetworkPanelViewModel>(network.Content);
        Assert.Equal((2, 3), (network.ColumnSpan, network.RowSpan));
        Assert.Equal("Network", network.Name);
        settings.Dispose();
    }

    [Fact]
    public void Dragging_ACard_MovesItAndSavesTheOrder_WithoutRebuildingTheOthers()
    {
        var (overview, settings) = Create();
        var before = overview.Tiles.ToList();
        var gpu = Tile(overview, DashboardCatalog.GpuLoad);

        overview.MoveTileCommand.Execute(new DashboardTileMove(gpu, 3));

        Assert.Equal<string>(
            new[] { DashboardCatalog.GpuTemperature, DashboardCatalog.CpuLoad, DashboardCatalog.CpuTemperature, DashboardCatalog.GpuLoad },
            VisibleOrder(overview).Take(4));
        Assert.Equal(DashboardCatalog.GpuLoad, settings.Current.Dashboard.Cards[3].Id);
        Assert.Equal(3, gpu.Order);
        Assert.Equal<DashboardTileViewModel>(before, overview.Tiles); // same tiles, same collection order: the grid orders them
        Assert.Equal(DashboardCatalog.GpuLoad, overview.SummaryCards[3].Id);

        // Back to the front, and to the very end.
        overview.MoveTileCommand.Execute(new DashboardTileMove(gpu, 0));
        Assert.Equal(DashboardCatalog.GpuLoad, VisibleOrder(overview)[0]);
        overview.MoveTileCommand.Execute(new DashboardTileMove(gpu, 99));
        Assert.Equal(DashboardCatalog.GpuLoad, VisibleOrder(overview)[^1]);
        Assert.Equal(DashboardCatalog.GpuLoad, settings.Current.Dashboard.Cards[^1].Id);
        settings.Dispose();
    }

    [Fact]
    public void Moving_KeepsHiddenCardsWhereTheyWere()
    {
        var (overview, settings) = Create();
        Tile(overview, DashboardCatalog.GpuTemperature).HideCommand.Execute(null);
        Assert.DoesNotContain(overview.Tiles, t => t.Id == DashboardCatalog.GpuTemperature);
        Assert.DoesNotContain(overview.SummaryCards, c => c.Id == DashboardCatalog.GpuTemperature);

        overview.MoveTileCommand.Execute(new DashboardTileMove(Tile(overview, DashboardCatalog.GpuLoad), 1));

        Assert.Equal<string>(new[] { DashboardCatalog.CpuLoad, DashboardCatalog.GpuLoad, DashboardCatalog.CpuTemperature }, VisibleOrder(overview).Take(3));
        Assert.Equal<string>(
            new[] { DashboardCatalog.GpuTemperature, DashboardCatalog.CpuLoad, DashboardCatalog.GpuLoad, DashboardCatalog.CpuTemperature },
            settings.Current.Dashboard.Cards.Take(4).Select(c => c.Id));
        Assert.False(settings.Current.Dashboard.Cards[0].Visible);
        settings.Dispose();
    }

    [Fact]
    public void Resizing_SavesTheSize_WithinLimits()
    {
        var (overview, settings) = Create();
        var network = Tile(overview, DashboardCatalog.Network);

        overview.ResizeTileCommand.Execute(new DashboardTileResize(network, 6, 5));
        Assert.Equal((6, 5), (network.ColumnSpan, network.RowSpan));
        var saved = settings.Current.Dashboard.Cards.First(c => c.Id == DashboardCatalog.Network);
        Assert.Equal((6, 5), (saved.Columns, saved.Rows));
        Assert.False(network.IsDefaultSize);

        // Panels are at least two columns by three rows; going back to the default stores nothing.
        overview.ResizeTileCommand.Execute(new DashboardTileResize(network, 1, 1));
        Assert.Equal((2, 3), (network.ColumnSpan, network.RowSpan));
        Assert.Null(saved.Columns);
        Assert.Null(saved.Rows);
        Assert.True(network.IsDefaultSize);
        settings.Dispose();
    }

    [Fact]
    public void MenuCommands_ResizeOneStepAtATime()
    {
        var (overview, settings) = Create();
        var card = Tile(overview, DashboardCatalog.Memory);
        Assert.False(card.NarrowerCommand.CanExecute(null));
        Assert.False(card.ShorterCommand.CanExecute(null));
        Assert.False(card.ResetSizeCommand.CanExecute(null));

        card.WiderCommand.Execute(null);
        card.TallerCommand.Execute(null);
        Assert.Equal((2, 3), (card.ColumnSpan, card.RowSpan));
        Assert.True(card.NarrowerCommand.CanExecute(null));
        Assert.True(card.ResetSizeCommand.CanExecute(null));

        card.ResetSizeCommand.Execute(null);
        Assert.Equal((1, 2), (card.ColumnSpan, card.RowSpan));

        for (int i = 0; i < 10; i++)
        {
            card.WiderCommand.Execute(null);
        }

        Assert.Equal(DashboardCatalog.MaxColumns, card.ColumnSpan);
        Assert.False(card.WiderCommand.CanExecute(null));
        settings.Dispose();
    }

    [Fact]
    public void Customize_KeepsSizes_AndResetLayoutClearsThem()
    {
        var (overview, settings) = Create();
        overview.ResizeTileCommand.Execute(new DashboardTileResize(Tile(overview, DashboardCatalog.LoadChart), 4, 4));
        overview.CustomizeCommand.Execute(null);
        var editor = overview.Customize;

        editor.Cards.First(c => c.Id == DashboardCatalog.GpuPower).IsVisible = false;
        editor.Cards.First(c => c.Id == DashboardCatalog.LoadChart).MoveUpCommand.Execute(null);
        Assert.Equal((4, 4), (Tile(overview, DashboardCatalog.LoadChart).ColumnSpan, Tile(overview, DashboardCatalog.LoadChart).RowSpan));
        Assert.Equal(5, Tile(overview, DashboardCatalog.LoadChart).Order); // before the hidden GPU power card's old place

        editor.ResetLayoutCommand.Execute(null);
        Assert.Equal(DashboardCatalog.All.Count, overview.Tiles.Count);
        Assert.Equal((2, 3), (Tile(overview, DashboardCatalog.LoadChart).ColumnSpan, Tile(overview, DashboardCatalog.LoadChart).RowSpan));
        Assert.All(settings.Current.Dashboard.Cards, c => Assert.Null(c.Columns));
        settings.Dispose();
    }

    [Fact]
    public void PcPicture_IsShownByDefault_AndCustomizeHidesIt()
    {
        var (overview, settings) = Create();
        Assert.True(overview.ShowPcPicture);
        Assert.True(overview.Customize.ShowPcPicture);
        var changed = new List<string?>();
        overview.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        overview.Customize.ShowPcPicture = false;

        Assert.False(settings.Current.Dashboard.ShowPcPicture);
        Assert.False(overview.ShowPcPicture);
        Assert.Contains(nameof(OverviewViewModel.ShowPcPicture), changed);

        overview.Customize.ShowPcPicture = true;
        Assert.True(overview.ShowPcPicture);
        settings.Dispose();
    }
}
