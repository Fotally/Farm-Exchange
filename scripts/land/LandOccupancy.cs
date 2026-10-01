using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.World;

namespace FarmExchange.Land;

internal sealed class LandOccupancy
{
    private static readonly IComparer<BuildingSpaceSnapshot> AnchorOrder =
        Comparer<BuildingSpaceSnapshot>.Create((first, second) => first.AnchorIndex.CompareTo(second.AnchorIndex));
    private readonly BuildingSpaceSnapshot?[] _spaces;
    private readonly List<BuildingSpaceSnapshot> _instances = new();
    private IReadOnlyList<BuildingSpaceSnapshot>? _snapshot = Array.Empty<BuildingSpaceSnapshot>();

    internal LandOccupancy(int cellCount) => _spaces = new BuildingSpaceSnapshot?[cellCount];

    internal IReadOnlyList<BuildingSpaceSnapshot> Instances =>
        _snapshot ??= Array.AsReadOnly(_instances.ToArray());

    internal BuildingKind Get(int index) => _spaces[index]?.Building ?? BuildingKind.None;

    internal int ResolveAnchorIndex(int index) => _spaces[index]?.AnchorIndex ?? -1;

    internal BuildingSpaceSnapshot? GetSpace(int index) => _spaces[index];

    internal LandFailure CheckFootprint(Vector2I anchorCell, BuildingKind building)
    {
        if (building is not (BuildingKind.Farm or BuildingKind.Processor or BuildingKind.Road))
            return LandFailure.InvalidBuilding;
        BuildingFootprint footprint = BuildingFootprint.Get(building);
        foreach (Vector2I offset in footprint.Offsets)
            if (!MapCoordinates.ContainsCell(anchorCell + offset))
                return LandFailure.OutOfBounds;
        foreach (Vector2I offset in footprint.Offsets)
            if (_spaces[IndexOf(anchorCell + offset)] != null)
                return LandFailure.Occupied;
        return LandFailure.None;
    }

    internal void Place(int index, BuildingKind building)
    {
        Vector2I anchorCell = new(index % FarmGame.MapSize, index / FarmGame.MapSize);
        LandFailure failure = CheckFootprint(anchorCell, building);
        if (failure == LandFailure.InvalidBuilding)
            throw new ArgumentOutOfRangeException(nameof(building));
        if (failure == LandFailure.OutOfBounds)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (failure == LandFailure.Occupied)
            throw new InvalidOperationException("土地已有建筑");
        var space = new BuildingSpaceSnapshot(index, building, anchorCell);
        int insertion = _instances.BinarySearch(space, AnchorOrder);
        _instances.Insert(~insertion, space);
        foreach (Vector2I offset in space.Footprint.Offsets)
            _spaces[IndexOf(anchorCell + offset)] = space;
        _snapshot = null;
    }

    internal void Remove(int index)
    {
        BuildingSpaceSnapshot space = _spaces[index] ?? throw new InvalidOperationException("土地没有建筑");
        _instances.RemoveAt(_instances.BinarySearch(space, AnchorOrder));
        foreach (Vector2I offset in space.Footprint.Offsets)
            _spaces[IndexOf(space.AnchorCell + offset)] = null;
        _snapshot = null;
    }

    internal void Clear()
    {
        Array.Clear(_spaces);
        _instances.Clear();
        _snapshot = Array.Empty<BuildingSpaceSnapshot>();
    }

    private static int IndexOf(Vector2I cell) => cell.Y * FarmGame.MapSize + cell.X;
}
