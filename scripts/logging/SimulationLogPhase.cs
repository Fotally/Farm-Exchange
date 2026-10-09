namespace FarmExchange.Logging;

/**
 * <summary>推进异常的实际执行位置；由业务进入对应阶段前更新。</summary>
 */
public enum SimulationLogPhase
{
    AdvanceValidation,
    EventSearch,
    QuietAdvance,
    TickPreparation,
    Harvest,
    Processing,
    RawClaim,
    Workers,
    Calendar,
    Orders,
    Checkpoint,
    DriverValidation,
    DriverBudget,
    DriverProgress,
}
