using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>所属记录入口维护的事件身份、级别、来源与采集资格。</summary>
 * <remarks>仅日志模块内部使用；通用输出不登记业务名称。编号沿用 schema 事件身份，测试可提供独立描述。</remarks>
 */
internal sealed record LogEventDescriptor(int Number, string Name, string Source,
    LogLevel Level = LogLevel.Information, bool RuntimeIncluded = true);
