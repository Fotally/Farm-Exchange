using System;
using FarmExchange.Gameplay;

namespace FarmExchange.Land;

internal sealed class LandOccupancy
{
    private readonly BuildingKind[] _buildings;

    internal LandOccupancy(int cellCount) => _buildings = new BuildingKind[cellCount];

    internal BuildingKind Get(int index) => _buildings[index];

    internal void Place(int index, BuildingKind building)
    {
        if (building is not (BuildingKind.Farm or BuildingKind.Processor))
            throw new ArgumentOutOfRangeException(nameof(building));
        if (_buildings[index] != BuildingKind.None)
            throw new InvalidOperationException("土地已有建筑");
        _buildings[index] = building;
    }

    internal void Remove(int index)
    {
        if (_buildings[index] == BuildingKind.None)
            throw new InvalidOperationException("土地没有建筑");
        _buildings[index] = BuildingKind.None;
    }

    internal void Clear() => Array.Clear(_buildings);
}
