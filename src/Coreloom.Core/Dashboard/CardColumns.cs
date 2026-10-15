namespace Coreloom.Core.Dashboard;

/// <summary>Where a card goes when cards are stacked in columns: its column and its top edge.</summary>
public readonly record struct StackPlacement(int Column, double Top);

/// <summary>
/// Column math for the card grids (pure, used by the app's grid panels). <see cref="Fit"/> works out how many equal
/// columns a row holds and how wide they are; <see cref="Stack"/> stacks cards of different heights into those columns,
/// each into the column that is shortest so far, so groups of different sizes pack tightly and line up on one grid.
/// </summary>
public static class CardColumns
{
    /// <summary>
    /// Columns for a row <paramref name="width"/> wide. Without a maximum width (infinity) this is as many columns as fit
    /// at <paramref name="minWidth"/>, never more than there are <paramref name="items"/>, stretched to fill the row.
    /// With a maximum, it's the fewest columns that keep each one within the maximum (while still at least the minimum
    /// wide), the count doesn't drop when there are fewer items, and columns are never wider than the maximum, so cards
    /// keep a compact width and line up from the left.
    /// </summary>
    public static (int Count, double Width) Fit(double width, double spacing, double minWidth, double maxWidth, int maxColumns, int items)
    {
        width = Math.Max(0, width);
        spacing = Math.Max(0, spacing);
        bool capped = IsCapped(maxWidth);
        int columns = (int)Math.Floor((width + spacing) / (Math.Max(1, minWidth) + spacing));
        if (capped)
        {
            int fill = (int)Math.Ceiling((width + spacing) / (Math.Max(1, maxWidth) + spacing));
            columns = Math.Min(columns, fill);
        }

        columns = Math.Clamp(columns, 1, Math.Max(1, maxColumns));
        if (items > 0 && !capped)
        {
            columns = Math.Min(columns, items);
        }

        return (columns, ColumnWidth(width, spacing, columns, maxWidth));
    }

    /// <summary>The width of each of <paramref name="columns"/> columns filling a row, but no more than the maximum.</summary>
    public static double ColumnWidth(double width, double spacing, int columns, double maxWidth)
    {
        columns = Math.Max(1, columns);
        double fill = Math.Max(0, (width - (Math.Max(0, spacing) * (columns - 1))) / columns);
        return IsCapped(maxWidth) ? Math.Min(fill, maxWidth) : fill;
    }

    /// <summary>
    /// Stacks cards (their heights, in order) into <paramref name="columns"/> columns, <paramref name="rowSpacing"/> below
    /// the card above. Each goes under the column that is shortest so far (the leftmost when several are), or, with
    /// <paramref name="inOrder"/>, simply into the next column in turn, so cards never swap columns as their heights change.
    /// </summary>
    public static IReadOnlyList<StackPlacement> Stack(IReadOnlyList<double> heights, int columns, double rowSpacing, out double totalHeight, bool inOrder = false)
    {
        columns = Math.Max(1, columns);
        var next = new double[columns];   // where the next card in each column would start
        var used = new bool[columns];
        var placements = new List<StackPlacement>(heights.Count);
        foreach (double raw in heights)
        {
            double height = double.IsFinite(raw) ? Math.Max(0, raw) : 0;
            int column = inOrder ? placements.Count % columns : 0;
            for (int c = 1; c < columns && !inOrder; c++)
            {
                if (next[c] < next[column] - 0.5)
                {
                    column = c;
                }
            }

            placements.Add(new StackPlacement(column, next[column]));
            next[column] += height + rowSpacing;
            used[column] = true;
        }

        totalHeight = 0;
        for (int c = 0; c < columns; c++)
        {
            if (used[c])
            {
                totalHeight = Math.Max(totalHeight, next[c] - rowSpacing);
            }
        }

        return placements;
    }

    /// <summary>
    /// Heights that line up the bottoms of the columns: the last card in each column grows to end where the tallest column
    /// ends; every other card keeps its own height.
    /// </summary>
    public static double[] FillColumns(IReadOnlyList<StackPlacement> placements, IReadOnlyList<double> heights, double totalHeight)
    {
        var filled = heights.Select(h => double.IsFinite(h) ? Math.Max(0, h) : 0).ToArray();
        var last = new Dictionary<int, int>();
        for (int i = 0; i < placements.Count && i < filled.Length; i++)
        {
            last[placements[i].Column] = i;
        }

        foreach (int i in last.Values)
        {
            filled[i] = Math.Max(filled[i], totalHeight - placements[i].Top);
        }

        return filled;
    }

    private static bool IsCapped(double maxWidth) => double.IsFinite(maxWidth) && maxWidth > 0;
}
