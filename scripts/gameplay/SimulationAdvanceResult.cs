namespace FarmExchange.Gameplay;

/**
 * <summary>批量推进在真实事件或请求终点提供的完整经营检查点。</summary>
 * <remarks>AdvancedTicks 为本次请求累计，IntervalTicks 为上个检查点以来秒数；回调可在稳定点提交正式命令，随后须返回 false 停止并重算下一请求。</remarks>
 */
public readonly record struct SimulationCheckpoint(
    uint AdvancedTicks, uint IntervalTicks, uint ElapsedSeconds, TickResult Result, bool IsEvent);

/**
 * <summary>批量推进的实际完整秒数、宽整数产出与执行成本。</summary>
 * <remarks>QuietTicks 一次累计无离散事件的秒数，EventTicks 是完整相位逐秒结算次数；暂停不推进。</remarks>
 */
public readonly record struct SimulationAdvanceResult(
    uint AdvancedTicks, long Harvested, long Produced, bool WorkerActed, bool DayAdvanced,
    bool StoppedAtCheckpoint, uint QuietTicks, uint EventTicks);
