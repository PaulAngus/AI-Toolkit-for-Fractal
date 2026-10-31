namespace Fractal.Core;

/// <summary>Route words on a destination cell select source rows in the preceding column.</summary>
public static class GridIsolation
{
    public static bool IsIsolated(DeviceDefinition device, IReadOnlyCollection<GridCell> grid, int row, int column)
    {
        if (row < 1 || row > device.GridRows || column < 1 || column > device.GridColumns || grid.Count != device.GridRows * device.GridColumns ||
            grid.Select(c => (c.Row, c.Column)).Distinct().Count() != grid.Count ||
            grid.Any(c => c.Row < 1 || c.Row > device.GridRows || c.Column < 1 || c.Column > device.GridColumns)) return false;
        var cell = grid.Single(c => c.Row == row && c.Column == column);
        if (cell.RouteFlag != 0) return false;
        int mask = (1 << device.GridRows) - 1, source = 1 << (row - 1);
        return !grid.Any(c => c.Column == column + 1 && ((c.RouteFlag & ~mask) != 0 || (c.RouteFlag & source) != 0));
    }
}
