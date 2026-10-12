namespace FarmExchange.Land;

/**
 * <summary>道路范围拆除结果，只统计实际移除道路且不退款。</summary>
 */
public readonly record struct RoadRemovalResult(LandFailure Failure, int RemovedCount)
{
    public bool Success => Failure == LandFailure.None;
    public string? ErrorMessage => Success ? null : PlacementRules.ErrorMessage(Failure);
}
