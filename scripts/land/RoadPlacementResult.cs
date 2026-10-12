namespace FarmExchange.Land;

/**
 * <summary>整批道路建造结果；失败实际格数与扣费均为零。</summary>
 */
public readonly record struct RoadPlacementResult(LandFailure Failure, int CellCount, long ChargedCents)
{
    public bool Success => Failure == LandFailure.None;
    public string? ErrorMessage => Success ? null : PlacementRules.ErrorMessage(Failure);
}
