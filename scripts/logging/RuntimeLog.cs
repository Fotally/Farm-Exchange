using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FarmExchange.Gameplay;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>组装进程日志会话、局观察上下文与真实生命周期记录。</summary>
 * <remarks>通用提交与文件输出交给 LogOutput；业务字段由对应领域 Adapter 维护。</remarks>
 */
public sealed class RuntimeLog : IDisposable
{
    private const string Source = "FarmExchange.Logging.RuntimeLog";
    private static readonly LogEventDescriptor SessionStarted = new(1, "SessionStarted", Source);
    private static readonly LogEventDescriptor SessionEnded = new(2, "SessionEnded", Source);
    private static readonly LogEventDescriptor FatalException = new(10, "FatalException", "FarmExchange.UI.Main", LogLevel.Critical);
    private readonly LogOutput _output;
    private readonly List<GameLog> _games = new();
    private readonly object _sync = new();
    private bool _disposed;
    private Func<long>? _productionUptime;

    private RuntimeLog(LogOutput output) => _output = output;

    /**
     * <summary>创建不会采集、分配局或指令身份、写文件的独立实例。</summary>
     * <returns>可独立关闭的无采集会话。</returns>
     */
    public static RuntimeLog Disabled() => new(LogOutput.Disabled());

    /**
     * <summary>在指定目录建立同步共享 File 输出；开发额外建立 debug 目标。</summary>
     * <param name="directory">组装点确定的 logs 绝对目录；故障不改路径。</param>
     * <param name="development">是否启用开发采集。</param>
     * <param name="environment">真实启动环境；未知值保持 null。</param>
     * <param name="retention">文件预算；省略时使用首版初值。</param>
     * <param name="diagnostic">不经过业务日志的故障接收点；默认 stderr。</param>
     * <returns>可释放会话，输出故障不阻止经营初始化。</returns>
     */
    public static RuntimeLog OpenFile(string directory, bool development, LogEnvironment environment,
        LogFileRetention? retention = null, Action<string>? diagnostic = null)
    {
#if !DEBUG
        development = false;
#endif
        var log = new RuntimeLog(LogOutput.OpenFile(directory, development, retention, diagnostic));
        log.Start(environment, development);
        return log;
    }

    /**
     * <summary>使用真实 MEL 和 formatter 采集到调用方拥有的内存文本目标。</summary>
     * <param name="writer">内存文本接收点；会话关闭不释放该对象。</param>
     * <param name="environment">测试启动环境。</param>
     * <param name="diagnostic">独立故障接收点。</param>
     * <returns>与文件输出共用领域入口的会话。</returns>
     */
    public static RuntimeLog Capture(TextWriter writer, LogEnvironment? environment = null,
        Action<string>? diagnostic = null)
    {
        var log = new RuntimeLog(LogOutput.Capture(writer, diagnostic));
        log.Start(environment ?? new LogEnvironment(), true);
        return log;
    }

    // 测试窗口预算的组装接缝；不替换公共事件头的真实单调时间。
    internal static RuntimeLog Capture(TextWriter writer, Func<long> productionUptime, Action<string>? diagnostic = null)
    {
        var log = Capture(writer, diagnostic: diagnostic);
        log._productionUptime = productionUptime;
        return log;
    }

    /**
     * <summary>查询通用输出已观察的故障及恢复；未知丢失数量保持 null。</summary>
     */
    public LoggingHealthSnapshot Health => _output.Health;

    internal LogOutput Output => _output;

    private void Start(LogEnvironment environment, bool development) => _output.Submit(
        SessionStarted, "日志会话开始", new()
        {
            ["GameVersion"] = string.IsNullOrWhiteSpace(environment.GameVersion) ? null : environment.GameVersion,
            ["BuildId"] = environment.BuildId,
            ["EngineVersion"] = environment.EngineVersion,
            ["DotnetVersion"] = Environment.Version.ToString(),
            ["BuildKind"] = environment.BuildKind,
            ["LoggingProfile"] = development ? "Development" : "Runtime",
            ["OSDescription"] = RuntimeInformation.OSDescription,
            ["Renderer"] = environment.Renderer,
            ["WindowSize"] = environment.WindowWidth is > 0 && environment.WindowHeight is > 0
                ? new Dictionary<string, object?> { ["Width"] = environment.WindowWidth, ["Height"] = environment.WindowHeight }
                : null,
        });

    /**
     * <summary>绑定一局已真实初始化的经营并记录其初始化基线。</summary>
     * <param name="game">已完成真实初始化的局；调用方在同一经营线程绑定和使用。</param>
     * <param name="seed">该局实际使用的初始化种子，不重新抽取。</param>
     * <returns>该局的领域观察上下文；关闭采集时为 null。</returns>
     * <remarks>每局只绑定一次；本入口仅供手动领域观察，不反向接入经营自动生产。局释放时关闭上下文，会话持有尚未结束的关联。</remarks>
     */
    public GameLog? BindGame(FarmGame game, int seed) => BindGame(game, seed, collectProduction: false, GamePurpose.Main);

    internal GameLog? BindGame(FarmGame game, int seed, bool collectProduction, GamePurpose purpose)
    {
        lock (_sync)
        {
            if (_disposed || !_output.IsEnabled) return null;
            var context = new GameLog(this, game, collectProduction, purpose, _productionUptime);
            _games.Add(context);
            context.Initialized(seed);
            context.Production?.Start();
            return context;
        }
    }

    internal void Release(GameLog game)
    {
        lock (_sync) _games.Remove(game);
    }

    /**
     * <summary>在真实初始化异常接收点记录不可继续的异常；不消费或重抛异常。</summary>
     * <param name="error">调用方正处理的原异常；调用方继续按原语义传播。</param>
     */
    public void InitializationFailed(Exception error) => _output.Observe(() => _output.Submit(
        FatalException, "经营初始化无法继续", new()
        {
            ["Phase"] = "Initialization",
            ["ExceptionType"] = error.GetType().FullName,
            ["Exception"] = error.ToString(),
        }));

    /**
     * <summary>先结束尚未释放的局，再记录进程结束并释放全部文件。</summary>
     * <remarks>重复关闭无副作用；正常 flush 不承诺强杀或断电末条必达。</remarks>
     */
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            foreach (GameLog game in _games.ToArray()) game.End("Shutdown");
            _output.Submit(SessionEnded, "日志会话结束", new());
            _disposed = true;
            _output.Dispose();
        }
    }
}
