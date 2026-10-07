namespace FarmExchange.Logging;

/**
 * <summary>独立诊断观察到的日志健康；故障次数不代表丢失事件数。</summary>
 */
public readonly record struct LoggingHealthSnapshot(
    LoggingHealth Health, long? FirstFailureUptimeMs, long FailureCount, long? KnownLostCount);
