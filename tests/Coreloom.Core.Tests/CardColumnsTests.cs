using Coreloom.Core.Dashboard;

namespace Coreloom.Core.Tests;

public class CardColumnsTests
{
    [Fact]
    public void Capped_KeepsCompactColumnsThatFillTheRow()
    {
        // 1,943 px wide, cards 270–330 px with 12 px gaps: six columns of ~314 px.
        var (count, width) = CardColumns.Fit(1943, 12, 270, 330, 12, items: 2);
        Assert.Equal(6, count); // not reduced to the two cards
        Assert.Equal((1943 - (5 * 12)) / 6.0, width, 6);

        // Too narrow for two columns at the minimum: one column, no wider than the maximum.
        (count, width) = CardColumns.Fit(520, 12, 270, 330, 12, items: 5);
        Assert.Equal(1, count);
        Assert.Equal(330, width, 6);

        // Every width from a phone-sized pane to a wide monitor stays within the range (or capped when only one fits).
        for (int w = 600; w <= 3000; w += 7)
        {
            var (c, cw) = CardColumns.Fit(w, 12, 270, 330, 12, 3);
            Assert.InRange(cw, 250, 330.0001);
            Assert.True((c * cw) + ((c - 1) * 12) <= w + 0.001);
        }
    }

    [Fact]
    public void Uncapped_StretchesAndNeverHasMoreColumnsThanItems()
    {
        var (count, width) = CardColumns.Fit(1000, 16, 240, double.PositiveInfinity, 6, items: 2);
        Assert.Equal(2, count);
        Assert.Equal(492, width, 6);

        (count, _) = CardColumns.Fit(1000, 16, 240, double.PositiveInfinity, 3, items: 10);
        Assert.Equal(3, count);
    }

    [Fact]
    public void Stack_PutsEachCardUnderTheShortestColumn()
    {
        // Groups of 3, 3, 2, 1, 1 and 1 devices in five columns: the sixth goes under the first short one (column 3).
        double[] heights = [220, 220, 150, 80, 80, 80];
        var placements = CardColumns.Stack(heights, 5, 20, out double total);

        Assert.Equal<int>(new[] { 0, 1, 2, 3, 4, 3 }, placements.Select(p => p.Column).ToArray());
        Assert.Equal(0, placements[4].Top, 6);
        Assert.Equal(100, placements[5].Top, 6); // 80 + 20 below Displays
        Assert.Equal(220, total, 6);
    }

    [Fact]
    public void Stack_OneColumn_IsAPlainList()
    {
        var placements = CardColumns.Stack([10, 20, 30], 1, 5, out double total);
        Assert.Equal<double>(new[] { 0.0, 15, 40 }, placements.Select(p => p.Top).ToArray());
        Assert.Equal(70, total, 6);

        Assert.Empty(CardColumns.Stack([], 4, 5, out total));
        Assert.Equal(0, total, 6);
    }
}
