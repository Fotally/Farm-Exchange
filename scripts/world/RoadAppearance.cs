using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;

namespace FarmExchange.World;

// 世界内部只读投影：正式道路来自土地快照，临时候选不登记占地。
internal sealed class RoadAppearance
{
    private static readonly Vector2I[] Neighbours = { Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left };
    private readonly Dictionary<Vector2I, RoadMaterial> _roads = new();
    private readonly HashSet<Vector2I> _added = new();
    private readonly HashSet<Vector2I> _removed = new();
    private RoadMaterial _material;

    internal IReadOnlyList<RoadPreviewCell> PreviewCells { get; private set; } = Array.Empty<RoadPreviewCell>();

    internal HashSet<Vector2I> Synchronize(IReadOnlyList<BuildingSpaceSnapshot> spaces)
    {
        var next = new Dictionary<Vector2I, RoadMaterial>();
        var changed = new HashSet<Vector2I>();
        foreach (BuildingSpaceSnapshot space in spaces)
            if (space.Building == BuildingKind.Road)
            {
                RoadMaterial material = space.RoadMaterial!.Value;
                next.Add(space.AnchorCell, material);
                if (!_roads.TryGetValue(space.AnchorCell, out RoadMaterial old) || old != material)
                    AddWithNeighbours(changed, space.AnchorCell);
            }
        foreach (Vector2I cell in _roads.Keys)
            if (!next.ContainsKey(cell)) AddWithNeighbours(changed, cell);
        _roads.Clear();
        foreach (var pair in next) _roads.Add(pair.Key, pair.Value);
        return changed;
    }

    internal HashSet<Vector2I> UpdatePreview(IReadOnlyList<Vector2I> cells, RoadMaterial material,
        bool removing, FarmGame game)
    {
        if (!PlacementRules.IsValidRoadMaterial(material)) throw new ArgumentOutOfRangeException(nameof(material));
        var added = new HashSet<Vector2I>();
        var removed = new HashSet<Vector2I>();
        var preview = new List<RoadPreviewCell>();
        var seen = new HashSet<Vector2I>();
        foreach (Vector2I cell in cells)
        {
            if (!seen.Add(cell)) continue;
            bool inMap = MapCoordinates.ContainsCell(cell);
            BuildingSpaceSnapshot? space = inMap ? game.GetBuildingSpace(cell) : null;
            if (removing)
            {
                if (space?.Building != BuildingKind.Road) continue;
                removed.Add(cell);
                preview.Add(new RoadPreviewCell(cell, true));
            }
            else
            {
                bool blocked = !inMap || space != null;
                if (!blocked) added.Add(cell);
                preview.Add(new RoadPreviewCell(cell, blocked));
            }
        }
        var changed = new HashSet<Vector2I>();
        foreach (Vector2I cell in _added) AddWithNeighbours(changed, cell);
        foreach (Vector2I cell in _removed) AddWithNeighbours(changed, cell);
        foreach (Vector2I cell in added) AddWithNeighbours(changed, cell);
        foreach (Vector2I cell in removed) AddWithNeighbours(changed, cell);
        _added.Clear();
        _added.UnionWith(added);
        _removed.Clear();
        _removed.UnionWith(removed);
        _material = material;
        PreviewCells = preview;
        return changed;
    }

    internal HashSet<Vector2I> ClearPreview()
    {
        var changed = new HashSet<Vector2I>();
        foreach (Vector2I cell in _added) AddWithNeighbours(changed, cell);
        foreach (Vector2I cell in _removed) AddWithNeighbours(changed, cell);
        _added.Clear();
        _removed.Clear();
        PreviewCells = Array.Empty<RoadPreviewCell>();
        return changed;
    }

    internal RoadTile? GetTile(Vector2I cell)
    {
        bool added = _added.Contains(cell);
        if (!_roads.TryGetValue(cell, out RoadMaterial material) && !added) return null;
        bool removed = _removed.Contains(cell);
        if (added) material = _material;
        int mask = 0;
        for (int side = 0; side < Neighbours.Length; side++)
        {
            Vector2I neighbour = cell + Neighbours[side];
            bool exists = removed ? _roads.ContainsKey(neighbour) :
                (_roads.ContainsKey(neighbour) || _added.Contains(neighbour)) && !_removed.Contains(neighbour);
            if (exists) mask |= 1 << side;
        }
        // 固定视觉种子混合格坐标；材质、连接和预览生命周期均不改变变体。
        return new RoadTile(material, mask, GetVariant(cell), added, removed);
    }

    internal static int GetVariant(Vector2I cell)
    {
        uint hash = unchecked((uint)cell.X * 73856093u ^ (uint)cell.Y * 19349663u ^ 0x143u);
        hash ^= hash >> 13;
        return (int)(hash & 3);
    }

    private static void AddWithNeighbours(HashSet<Vector2I> cells, Vector2I cell)
    {
        if (MapCoordinates.ContainsCell(cell)) cells.Add(cell);
        foreach (Vector2I offset in Neighbours)
            if (MapCoordinates.ContainsCell(cell + offset)) cells.Add(cell + offset);
    }

    internal readonly record struct RoadPreviewCell(Vector2I Cell, bool Blocked);
    internal readonly record struct RoadTile(RoadMaterial Material, int Mask, int Variant, bool Added, bool Removed);
}
