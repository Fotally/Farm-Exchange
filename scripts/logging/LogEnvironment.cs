namespace FarmExchange.Logging;

/**
 * <summary>启动时取得的纯环境值；未知值保持 null。</summary>
 */
public sealed record LogEnvironment(
    string? GameVersion = null, string? BuildId = null, string? EngineVersion = null,
    string? Renderer = null, int? WindowWidth = null, int? WindowHeight = null,
    string? BuildKind = null);
