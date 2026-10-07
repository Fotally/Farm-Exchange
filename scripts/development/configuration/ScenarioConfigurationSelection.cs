using System.Collections.Generic;

namespace FarmExchange.Development;

/**
 * <summary>一次显式重选的真实目录与新草稿；失效时草稿为空并提供错误，不保留过期选择。</summary>
 */
public sealed record ScenarioConfigurationSelection(
    IReadOnlyList<ScenarioConfigurationEntry> Entries, ScenarioConfigurationDraft? Draft, string? Error);
