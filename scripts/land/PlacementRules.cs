using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Terrain;

namespace FarmExchange.Land;

public enum LandFailure
{
    None,
    InvalidBuilding,
    InvalidCrop,
    OutOfBounds,
    Occupied,
    InsufficientFunds,
    InvalidOrientation,
    InvalidRoadMaterial,
    InvalidRoadRange,
    NoRoads,
    Water,
}

public readonly record struct PlacementCheck(LandFailure Failure, int CostCents)
{
    public bool Allowed => Failure == LandFailure.None;
}

public readonly record struct PlacementResult(LandFailure Failure, int ChargedCents)
{
    public bool Success => Failure == LandFailure.None;
    public string? ErrorMessage => Success ? null : PlacementRules.ErrorMessage(Failure);
}

internal static class PlacementRules
{
    /**
     * <summary>按道路材质枚举的实际定义判断材质是否合法。</summary>
     * <param name="material">待检查的道路材质。</param>
     * <returns>已定义材质为真，负值或未定义值为假。</returns>
     */
    internal static bool IsValidRoadMaterial(RoadMaterial material) => System.Enum.IsDefined(material);

    internal static PlacementCheck Check(Vector2I cell, BuildingKind building, CropKind crop,
        LandOccupancy occupancy, TerrainMap terrain, int balanceCents, int costCents,
        BuildingOrientation orientation = BuildingOrientation.Q0)
    {
        if (building is not (BuildingKind.Farm or BuildingKind.Processor or BuildingKind.Road))
            return new PlacementCheck(LandFailure.InvalidBuilding, 0);
        if (building != BuildingKind.Road && !CropCatalog.IsDefined(crop))
            return new PlacementCheck(LandFailure.InvalidCrop, 0);
        LandFailure geometryFailure = occupancy.CheckFootprint(cell, building, orientation);
        if (geometryFailure != LandFailure.None)
            return new PlacementCheck(geometryFailure, 0);
        foreach (Vector2I offset in BuildingFootprint.Get(building).Offsets)
            if (!terrain.IsFullLand(cell + offset))
                return new PlacementCheck(LandFailure.Water, 0);
        if (balanceCents < costCents)
            return new PlacementCheck(LandFailure.InsufficientFunds, 0);
        return new PlacementCheck(LandFailure.None, costCents);
    }

    internal static string ErrorMessage(LandFailure failure) => failure switch
    {
        LandFailure.InvalidBuilding => "无效建筑",
        LandFailure.InvalidCrop => "无效作物",
        LandFailure.InvalidOrientation => "无效建筑朝向",
        LandFailure.InvalidRoadMaterial => "无效道路材质",
        LandFailure.InvalidRoadRange => "道路范围不能为空或包含重复地块",
        LandFailure.NoRoads => "没有可拆除的道路",
        LandFailure.OutOfBounds => "地图外地块",
        LandFailure.Occupied => "该土地已有建筑",
        LandFailure.Water => "水域或含水岸格不能建造",
        LandFailure.InsufficientFunds => "金币不足，无法建造建筑",
        _ => throw new System.ArgumentOutOfRangeException(nameof(failure)),
    };
}
