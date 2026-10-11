using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;

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
    internal static PlacementCheck Check(Vector2I cell, BuildingKind building, CropKind crop,
        LandOccupancy occupancy, int balanceCents, int costCents,
        BuildingOrientation orientation = BuildingOrientation.Q0)
    {
        if (building is not (BuildingKind.Farm or BuildingKind.Processor or BuildingKind.Road))
            return new PlacementCheck(LandFailure.InvalidBuilding, 0);
        if (building != BuildingKind.Road && !CropCatalog.IsDefined(crop))
            return new PlacementCheck(LandFailure.InvalidCrop, 0);
        LandFailure geometryFailure = occupancy.CheckFootprint(cell, building, orientation);
        if (geometryFailure != LandFailure.None)
            return new PlacementCheck(geometryFailure, 0);
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
        LandFailure.InsufficientFunds => "金币不足，无法建造建筑",
        _ => throw new System.ArgumentOutOfRangeException(nameof(failure)),
    };
}
