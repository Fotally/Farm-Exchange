namespace FarmExchange.Logging;

/**
 * <summary>观察真实经营推进异常，不持有推进状态或执行业务。</summary>
 */
public sealed class SimulationLog
{
    private const string Source = "FarmExchange.Gameplay.FarmGame";
    private readonly GameLog _context;

    internal SimulationLog(GameLog context) => _context = context;

    /**
     * <summary>建立一次单经营秒的同步异常观察。</summary>
     * <returns>须使用 using 释放的观察；采集关闭时为 null。</returns>
     */
    public SimulationLogOperation? BeginTick() => _context.CanObserve ?
        new(_context, Source, "单秒经营推进抛出异常", SimulationLogPhase.AdvanceValidation) : null;

    /**
     * <summary>建立一次完整批量推进的同步异常观察。</summary>
     * <returns>须使用 using 释放的观察；采集关闭时为 null。</returns>
     */
    public SimulationLogOperation? BeginBatch() => _context.CanObserve ?
        new(_context, Source, "批量经营推进抛出异常", SimulationLogPhase.AdvanceValidation) : null;
}
