namespace FarmExchange.Logging;

/**
 * <summary>日志局初始化基线的真实用途，不改变经营规则。</summary>
 */
public enum GamePurpose
{
    /**
     * <summary>玩家主经营局。</summary>
     */
    Main,
    /**
     * <summary>开发流程独立创建的经营局。</summary>
     */
    ScenarioIndependent,
}
