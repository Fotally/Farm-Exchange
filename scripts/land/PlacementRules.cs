using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.World;

namespace FarmExchange.Land;

public enum LandFailure
{
    None,
    InvalidBuilding,
    InvalidCrop,
    OutOfBounds,
    Occupied,
    InsufficientFunds,
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
        LandOccupancy occupancy, int balanceCents, int costCents)
    {
        if (building is not (BuildingKind.Farm or BuildingKind.Processor or BuildingKind.Road))
            return new PlacementCheck(LandFailure.InvalidBuilding, 0);
        if (building != BuildingKind.Road && !CropCatalog.IsDefined(crop))
            return new PlacementCheck(LandFailure.InvalidCrop, 0);
        if (!MapCoordinates.ContainsCell(cell))
            return new PlacementCheck(LandFailure.OutOfBounds, 0);
        int index = cell.Y * FarmGame.MapSize + cell.X;
        if (occupancy.Get(index) != BuildingKind.None)
            return new PlacementCheck(LandFailure.Occupied, 0);
        if (balanceCents < costCents)
            return new PlacementCheck(LandFailure.InsufficientFunds, 0);
        return new PlacementCheck(LandFailure.None, costCents);
    }

    internal static string ErrorMessage(LandFailure failure) => failure switch
    {
        LandFailure.InvalidBuilding => "无效建筑",
        LandFailure.InvalidCrop => "无效作物",
        LandFailure.OutOfBounds => "地图外地块",
        LandFailure.Occupied => "该土地已有建筑",
        LandFailure.InsufficientFunds => "金币不足，无法建造建筑",
        _ => throw new System.ArgumentOutOfRangeException(nameof(failure)),
    };
}
