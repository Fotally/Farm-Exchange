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
    /**
     * <summary>道路登记时固定的材质；农田与加工场地为 null。</summary>
     */
    public RoadMaterial? RoadMaterial { get; }
    public Vector2I AnchorCell { get; }
    public BuildingFootprint Footprint { get; }
    public Vector2I WorkCell => AnchorCell + Footprint.WorkOffset;

    internal BuildingSpaceSnapshot(int anchorIndex, BuildingKind building, Vector2I anchorCell,
        BuildingOrientation orientation, RoadMaterial? roadMaterial)
    {
        AnchorIndex = anchorIndex;
        Building = building;
        Orientation = orientation;
        RoadMaterial = roadMaterial;
        AnchorCell = anchorCell;
        Footprint = BuildingFootprint.Get(building);
    }
}
