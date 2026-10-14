using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;

namespace FarmExchange.World;

/**
 * <summary>地图内部占地几何，正式选框与候选预览共用外围边计算。</summary>
 */
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
                !game.Terrain.IsFullLand(cell) || game.GetBuildingSpace(cell) != null);
        }
        return cells;
    }

    /**
     * <summary>从实际占地偏移返回指定格的外围边，排除占地内部的相邻边。</summary>
     * <param name="anchorCell">实例或候选的基础格锚点，保留地图范围外的位置。</param>
     * <param name="offset">当前格相对锚点的偏移。</param>
     * <param name="offsets">同一占地的完整偏移集合。</param>
     * <returns>地图本地坐标的外围线段，绘制颜色和线宽由调用方决定。</returns>
     */
    internal static IEnumerable<(Vector2 Start, Vector2 End)> GetCellOuterEdges(
        Vector2I anchorCell, Vector2I offset, IReadOnlySet<Vector2I> offsets)
    {
        Vector2[] corners = MapCoordinates.GridRectangleOutline(anchorCell + offset, 1, 1);
        for (int side = 0; side < Neighbours.Length; side++)
            if (!offsets.Contains(offset + Neighbours[side]))
                yield return (corners[side], corners[(side + 1) % corners.Length]);
    }
}
