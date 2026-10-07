namespace FarmExchange.Logging;

/**
 * <summary>两类文件的轮转初值；测试可使用小阈值走同一 File 链路。</summary>
 */
public sealed record LogFileRetention(
    long RuntimeBytes = 10 * 1024 * 1024, int RuntimeFiles = 20,
    long DebugBytes = 20 * 1024 * 1024, int DebugFiles = 10);
