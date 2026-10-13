namespace Coreloom.Core.Dashboard;

/// <summary>A card's size on the dashboard grid, in columns and rows.</summary>
public readonly record struct TileSize(int Columns, int Rows);

/// <summary>Where a card sits on the dashboard grid (zero-based cells) and how many it covers.</summary>
public readonly record struct TilePlacement(int Column, int Row, int Columns, int Rows)
{
    public int Right => Column + Columns;

    public int Bottom => Row + Rows;
}

/// <summary>
/// Lays out the Overview's cards on a grid of equal columns and short rows. Cards are placed in their order, left to
/// right and top to bottom, each after the one before it, so the dashboard reads in the order you set; a card that does
/// not fit in what is left of a row goes to the next row it fits in. Pure math, used by the dashboard panel for layout,
/// dragging (where a card would land) and resizing (how many cells a size snaps to).
/// </summary>
public static class DashboardLayout
{
    /// <summary>
    /// Column count for a width: 6, 4, 2 or 1, whichever is the most that are each at least
    /// <paramref name="minColumnWidth"/> wide. Even counts keep two-column panels side by side without gaps.
    /// </summary>
    public static int ColumnsFor(double width, double minColumnWidth, double spacing, int maxColumns = 6)
    {
        if (double.IsPositiveInfinity(width))
        {
            return Even(maxColumns);
        }

        if (!(width > 0))
        {
            return 1;
        }

        int fit = (int)Math.Floor((width + spacing) / (Math.Max(1, minColumnWidth) + spacing));
        return Even(Math.Min(fit, maxColumns));
    }

    /// <summary>Places the cards in order. Sizes wider than the grid are narrowed to fit it.</summary>
    public static TilePlacement[] Place(IReadOnlyList<TileSize> tiles, int columns)
    {
        columns = Math.Max(1, columns);
        var placements = new TilePlacement[tiles.Count];
        var taken = new List<bool[]>();
        int row = 0;
        int column = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            int width = Math.Clamp(tiles[i].Columns, 1, columns);
            int height = Math.Max(1, tiles[i].Rows);
            while (true)
            {
                if (column + width > columns)
                {
                    row++;
                    column = 0;
                }
                else if (IsFree(taken, column, row, width, height))
                {
                    break;
                }
                else
                {
                    column++;
                }
            }

            Take(taken, column, row, width, height, columns);
            placements[i] = new TilePlacement(column, row, width, height);
            column += width;
        }

        return placements;
    }

    /// <summary>Rows the placed cards cover.</summary>
    public static int RowCount(IReadOnlyList<TilePlacement> placements)
    {
        int rows = 0;
        foreach (var p in placements)
        {
            rows = Math.Max(rows, p.Bottom);
        }

        return rows;
    }

    /// <summary>
    /// Where a card being dragged would land: of every place in the order it could go, the one that puts its top-left
    /// corner nearest to where it is being held (<paramref name="column"/>, <paramref name="row"/>, in cells; pitches turn
    /// cells into pixels so distances are fair). It stays at <paramref name="current"/> unless another place is clearly
    /// nearer, so cards don't flicker between two spots while you hold one between them.
    /// </summary>
    public static int DropIndex(IReadOnlyList<TileSize> tiles, int dragged, int columns, double column, double row,
        double columnPitch, double rowPitch, int current = -1)
    {
        if (tiles.Count == 0 || dragged < 0 || dragged >= tiles.Count)
        {
            return 0;
        }

        double best = double.MaxValue;
        int bestIndex = dragged;
        double currentDistance = double.MaxValue;
        for (int index = 0; index < tiles.Count; index++)
        {
            var placement = Place(Move(tiles, dragged, index), columns)[index];
            double dx = (placement.Column - column) * columnPitch;
            double dy = (placement.Row - row) * rowPitch;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance < best - 1e-6)
            {
                best = distance;
                bestIndex = index;
            }

            if (index == current)
            {
                currentDistance = distance;
            }
        }

        double hysteresis = 0.25 * Math.Min(columnPitch, rowPitch);
        return current >= 0 && current < tiles.Count && currentDistance - best < hysteresis ? current : bestIndex;
    }

    /// <summary>A copy of the list with one item moved to another position.</summary>
    public static List<T> Move<T>(IReadOnlyList<T> items, int from, int to)
    {
        var list = items.ToList();
        if (from < 0 || from >= list.Count)
        {
            return list;
        }

        var item = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Clamp(to, 0, list.Count), item);
        return list;
    }

    /// <summary>Cells for a size in pixels while resizing: the nearest whole number, within limits.</summary>
    public static int Snap(double size, double cell, double spacing, int min, int max)
    {
        int count = (int)Math.Round((size + spacing) / Math.Max(1, cell + spacing), MidpointRounding.AwayFromZero);
        return Math.Clamp(count, min, Math.Max(min, max));
    }

    /// <summary>
    /// Row height: the smallest that fits every card at its smallest size (<c>MinRows</c> rows), and at least
    /// <paramref name="minimum"/>. Heights are what the cards need at their width.
    /// </summary>
    public static double RowHeight(IEnumerable<(double Height, int MinRows)> cards, double spacing, double minimum)
    {
        double height = minimum;
        foreach (var (needed, minRows) in cards)
        {
            int rows = Math.Max(1, minRows);
            height = Math.Max(height, (needed - ((rows - 1) * spacing)) / rows);
        }

        return Math.Ceiling(height);
    }

    /// <summary>Pixels covered by a number of cells and the gaps between them.</summary>
    public static double Extent(int cells, double cell, double spacing) => cells <= 0 ? 0 : (cells * cell) + ((cells - 1) * spacing);

    private static int Even(int columns) => columns >= 6 ? 6 : columns >= 4 ? 4 : columns >= 2 ? 2 : 1;

    private static bool IsFree(List<bool[]> taken, int column, int row, int width, int height)
    {
        for (int r = row; r < row + height && r < taken.Count; r++)
        {
            for (int c = column; c < column + width; c++)
            {
                if (taken[r][c])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void Take(List<bool[]> taken, int column, int row, int width, int height, int columns)
    {
        while (taken.Count < row + height)
        {
            taken.Add(new bool[columns]);
        }

        for (int r = row; r < row + height; r++)
        {
            for (int c = column; c < column + width; c++)
            {
                taken[r][c] = true;
            }
        }
    }
}
