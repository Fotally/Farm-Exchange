using Godot;
using FarmExchange.Gameplay;

namespace FarmExchange.Land;

public sealed class BuildingSpaceSnapshot
{
    public int AnchorIndex { get; }
    public BuildingKind Building { get; }
    /**
     * <summary>登记时确定的设施朝向，建成后保持不变。</summary>
     */
    public BuildingOrientation Orientation { get; }
    public Vector2I AnchorCell { get; }
    public BuildingFootprint Footprint { get; }
    public Vector2I WorkCell => AnchorCell + Footprint.WorkOffset;

    internal BuildingSpaceSnapshot(int anchorIndex, BuildingKind building, Vector2I anchorCell,
        BuildingOrientation orientation)
    {
        AnchorIndex = anchorIndex;
        Building = building;
        Orientation = orientation;
        AnchorCell = anchorCell;
        Footprint = BuildingFootprint.Get(building);
    }
}
