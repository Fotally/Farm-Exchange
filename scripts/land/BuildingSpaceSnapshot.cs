using Godot;
using FarmExchange.Gameplay;

namespace FarmExchange.Land;

public sealed class BuildingSpaceSnapshot
{
    public int AnchorIndex { get; }
    public BuildingKind Building { get; }
    public Vector2I AnchorCell { get; }
    public BuildingFootprint Footprint { get; }
    public Vector2I WorkCell => AnchorCell + Footprint.WorkOffset;

    internal BuildingSpaceSnapshot(int anchorIndex, BuildingKind building, Vector2I anchorCell)
    {
        AnchorIndex = anchorIndex;
        Building = building;
        AnchorCell = anchorCell;
        Footprint = BuildingFootprint.Get(building);
    }
}
