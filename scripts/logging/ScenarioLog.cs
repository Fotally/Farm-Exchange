namespace FarmExchange.Logging;

/**
 * <summary>流程生命周期的日志入口，只接收开发侧已投影的纯值。</summary>
 */
public sealed class ScenarioLog
{
    private readonly GameLog _context;
    internal ScenarioLog(GameLog context) => _context = context;

    /**
     * <summary>在首个流程命令之前记录已加载配置及报告目录关联。</summary>
     * <param name="runId">预先创建的真实报告目录名称。</param>
     * <param name="runMode">Current 或 Independent。</param>
     * <param name="configurationRevision">实际加载配置修订。</param>
     * <param name="configurationHash">实际加载原字节的 SHA-256。</param>
     * <returns>本次流程的观察；采集关闭为 null。</returns>
     */
    public ScenarioRunLog? Begin(string runId, string runMode, int configurationRevision, string configurationHash)
    {
        if (!_context.CanObserve) return null;
        var run = new ScenarioRunLog(_context, runId, runMode);
        run.Started(configurationRevision, configurationHash);
        return run;
    }
}
