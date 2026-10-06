using System;

namespace FarmExchange.Development;

/**
 * <summary>参数文件未满足固定流程协议，尚未执行业务操作。</summary>
 */
public sealed class ScenarioConfigurationException : Exception
{
    public ScenarioConfigurationException(string message, Exception? inner = null) : base(message, inner) { }
}
