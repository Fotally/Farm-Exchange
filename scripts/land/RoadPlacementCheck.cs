namespace FarmExchange.Land;

/**
 * <summary>整批道路只读检查，费用以宽整数分保存；拒绝也保留请求总费用。</summary>
 */
public readonly record struct RoadPlacementCheck(LandFailure Failure, int CellCount, long CostCents)
{
    public bool Allowed => Failure == LandFailure.None;
    public string? ErrorMessage => Allowed ? null : PlacementRules.ErrorMessage(Failure);
}
