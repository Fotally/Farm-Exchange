using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;

namespace FarmExchange.World;

internal static class PlacementPreviewGeometry
{
    private static readonly Vector2I[] Neighbours = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

    internal readonly record struct PreviewCell(Vector2I Cell, bool Blocked);

    internal static PreviewCell[] GetCells(Vector2I anchorCell, IReadOnlyList<Vector2I> offsets, FarmGame game)
    {
        var cells = new PreviewCell[offsets.Count];
        for (int index = 0; index < offsets.Count; index++)
        {
            Vector2I cell = anchorCell + offsets[index];
            cells[index] = new PreviewCell(cell,
                !MapCoordinates.ContainsCell(cell) || game.GetBuildingSpace(cell) != null);
        }
        return cells;
    }

    internal static IEnumerable<(Vector2 Start, Vector2 End)> GetCellOuterEdges(
        Vector2I anchorCell, Vector2I offset, IReadOnlySet<Vector2I> offsets)
    {
        Vector2[] corners = MapCoordinates.GridRectangleOutline(anchorCell + offset, 1, 1);
        for (int side = 0; side < Neighbours.Length; side++)
            if (!offsets.Contains(offset + Neighbours[side]))
                yield return (corners[side], corners[(side + 1) % corners.Length]);
    }
}
