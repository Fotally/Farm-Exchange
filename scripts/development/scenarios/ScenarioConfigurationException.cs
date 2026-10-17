using System;

namespace FarmExchange.Development;

/**
 * <summary>参数文件未满足固定流程协议，尚未执行业务操作。</summary>
 */
public sealed class ScenarioConfigurationException : Exception
{
    /**
     * <summary>校验失败的字段路径；空字符串表示整份配置或文件操作错误。</summary>
     */
    public string FieldPath { get; }

    /**
     * <summary>保留显示说明与独立字段位置；包装配置异常时沿用内层位置。</summary>
     * <param name="message">人类可读说明，不承担字段定位协议。</param>
     * <param name="inner">原始错误；配置异常可提供既有字段位置。</param>
     * <param name="fieldPath">显式字段路径；省略时继承内层配置异常，无内层位置则为空。</param>
     */
    public ScenarioConfigurationException(string message, Exception? inner = null, string? fieldPath = null) : base(message, inner)
    {
        FieldPath = fieldPath ?? (inner as ScenarioConfigurationException)?.FieldPath ?? "";
    }
}
