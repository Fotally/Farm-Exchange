using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FarmExchange.Gameplay;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>拥有一个进程采集会话、串行序号、输出目标及健康诊断。</summary>
 * <remarks>业务仅经过具名领域入口递交请求与结果；实例不使用 Serilog 全局 logger 或 SelfLog。</remarks>
 */
public sealed class RuntimeLog : IDisposable
{
    private readonly object _sync = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<GameLog> _games = new();
    private readonly List<TargetFailureListener> _targets = new();
    private readonly Action<string> _diagnostic;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private SerilogLoggerProvider? _provider;
    private Microsoft.Extensions.Logging.ILogger? _logger;
    private long _sequence;
    private long _failureCount;
    private long? _firstFailureUptimeMs;
    private long? _lastFailureDiagnosticUptimeMs;
    private LoggingHealth _observedHealth = LoggingHealth.Healthy;
    private bool _healthPending;
    private bool _disposed;

    private RuntimeLog(Action<string>? diagnostic) => _diagnostic = diagnostic ?? Console.Error.WriteLine;

    /**
     * <summary>创建不会采集、分配局或指令身份、写文件的独立实例。</summary>
     * <returns>可独立关闭的无采集会话。</returns>
     */
    public static RuntimeLog Disabled() => new(null);

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
        var log = new RuntimeLog(diagnostic);
        retention ??= new LogFileRetention();
        try
        {
            var configuration = new LoggerConfiguration();
            if (development) configuration.MinimumLevel.Verbose();
            else configuration.MinimumLevel.Information();
            log.AddFile(configuration, Path.Combine(directory, "runtime", "runtime-.log"),
                retention.RuntimeBytes, retention.RuntimeFiles, LogEventLevel.Information);
            if (development)
                log.AddFile(configuration, Path.Combine(directory, "debug", "debug-.log"),
                    retention.DebugBytes, retention.DebugFiles, LogEventLevel.Verbose);
            log.Connect(configuration);
        }
        catch (Exception error) { log.RecordFailure("日志初始化失败", error); }
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
        var log = new RuntimeLog(diagnostic);
        var target = new TargetFailureListener(log);
        log._targets.Add(target);
        log.Connect(new LoggerConfiguration().MinimumLevel.Verbose()
            .WriteTo.Fallible(sink => sink.Sink(new TextSink(writer)), target));
        target.IsConfigured = true;
        log.Start(environment ?? new LogEnvironment(), true);
        return log;
    }

    /**
     * <summary>查询已观察的目标故障、恢复及首次故障时间。</summary>
     * <remarks>KnownLostCount 为 null，不能从故障次数推断丢失数量。</remarks>
     */
    public LoggingHealthSnapshot Health
    {
        get
        {
            lock (_sync)
            {
                int failed = _targets.FindAll(target => target.Failed).Count;
                LoggingHealth health = _failureCount == 0 ? LoggingHealth.Healthy :
                    _logger == null || failed == _targets.Count ? LoggingHealth.Unavailable :
                    failed > 0 ? LoggingHealth.Degraded : LoggingHealth.Healthy;
                return new(health, _firstFailureUptimeMs, _failureCount, null);
            }
        }
    }

    internal bool IsEnabled => _logger != null && !_disposed;

    private void AddFile(LoggerConfiguration configuration, string path, long bytes, int files, LogEventLevel minimumLevel)
    {
        var target = new TargetFailureListener(this);
        _targets.Add(target);
        try
        {
            // 先向原始 File 绑定监听；Serilog 的级别过滤包装不转发该可选接口。
            var observed = LoggerSinkConfiguration.Wrap(sink => new FileTargetAdapter(sink, target),
                sink => sink.File(new LogTextFormatter(), path,
                fileSizeLimitBytes: bytes, retainedFileCountLimit: files, rollOnFileSizeLimit: true,
                rollingInterval: RollingInterval.Day, shared: true, buffered: false,
                encoding: new UTF8Encoding(false)));
            configuration.WriteTo.Sink(observed, restrictedToMinimumLevel: minimumLevel);
            target.IsConfigured = true;
        }
        catch (Exception error)
        {
            target.OnLoggingFailed(this, LoggingFailureKind.Permanent,
                "文件目标初始化失败：" + path, null, error);
        }
    }

    private void Connect(LoggerConfiguration configuration)
    {
        _provider = new SerilogLoggerProvider(configuration.CreateLogger(), dispose: true);
        _logger = _provider.CreateLogger("FarmExchange.Logging.RuntimeLog");
        ObserveHealthChange();
    }

    private void Start(LogEnvironment environment, bool development) => Emit(
        "SessionStarted", "日志会话开始", "FarmExchange.Logging.RuntimeLog", new()
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
     * <remarks>每局只绑定一次；调用方在局释放时关闭上下文，会话持有尚未结束的关联。</remarks>
     */
    public GameLog? BindGame(FarmGame game, int seed)
    {
        if (!IsEnabled) return null;
        lock (_sync)
        {
            var context = new GameLog(this, game);
            _games.Add(context);
            context.Initialized(seed);
            return context;
        }
    }

    internal void Release(GameLog game)
    {
        lock (_sync) _games.Remove(game);
    }

    internal void Observe(Action observation)
    {
        if (!IsEnabled) return;
        try { observation(); }
        catch (Exception error) { RecordFailure("日志投影失败", error); }
    }

    internal void Emit(string eventName, string message, string source,
        Dictionary<string, object?> fields, LogLevel level = LogLevel.Information)
    {
        lock (_sync)
        {
            if (!IsEnabled) return;
            if (_healthPending && Health.Health != LoggingHealth.Unavailable)
            {
                _healthPending = false;
                var health = Health;
                Write("LoggingHealthChanged", "日志输出健康发生变化", "FarmExchange.Logging.RuntimeLog", new()
                {
                    ["LoggingHealth"] = health.Health.ToString(),
                    ["FirstFailureUptimeMs"] = health.FirstFailureUptimeMs,
                    ["FailureCount"] = health.FailureCount,
                    ["KnownLostCount"] = health.KnownLostCount,
                }, health.Health == LoggingHealth.Healthy ? LogLevel.Information : LogLevel.Warning);
            }
            Write(eventName, message, source, fields, level);
        }
    }

    private void Write(string eventName, string message, string source,
        Dictionary<string, object?> fields, LogLevel level)
    {
        fields["SchemaVersion"] = 1;
        fields["SessionId"] = _sessionId;
        fields["Sequence"] = ++_sequence;
        fields["UptimeMs"] = _clock.ElapsedMilliseconds;
        fields["EventName"] = eventName;
        fields["SourceContext"] = source;
        fields["Message"] = message;
        long[] before = _targets.ConvertAll(target => target.FailureVersion).ToArray();
        try
        {
            _logger!.Log(level, new EventId(EventNumber(eventName), eventName), new EventState(fields),
                null, static (state, _) => (string)state.Fields["Message"]!);
            bool recovered = false;
            for (int i = 0; i < _targets.Count; i++)
                if (_targets[i].IsConfigured && _targets[i].Failed && _targets[i].FailureVersion == before[i])
                {
                    _targets[i].Failed = false;
                    recovered = true;
                }
            if (recovered && ObserveHealthChange() && _observedHealth == LoggingHealth.Healthy)
            {
                _lastFailureDiagnosticUptimeMs = null;
                Diagnose("日志目标恢复输出");
            }
        }
        catch (Exception error) { RecordFailure("日志输出失败", error); }
    }

    private static int EventNumber(string name) => name switch
    {
        "SessionStarted" => 1,
        "SessionEnded" => 2,
        "GameInitialized" => 3,
        "GameEnded" => 4,
        "CommandReceived" => 5,
        "CommandFinished" => 6,
        "TradeFinished" => 7,
        "BusinessException" => 8,
        "LoggingHealthChanged" => 9,
        "FatalException" => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private void RecordFailure(string message, Exception? error)
    {
        lock (_sync)
        {
            _firstFailureUptimeMs ??= _clock.ElapsedMilliseconds;
            _failureCount++;
            ObserveHealthChange();
            long uptime = _clock.ElapsedMilliseconds;
            if (!_lastFailureDiagnosticUptimeMs.HasValue || uptime - _lastFailureDiagnosticUptimeMs.Value >= 60000)
            {
                _lastFailureDiagnosticUptimeMs = uptime;
                Diagnose(message + (error == null ? "" : "：" + error.GetType().Name + " " + error.Message));
            }
        }
    }

    private bool ObserveHealthChange()
    {
        LoggingHealth health = Health.Health;
        if (health == _observedHealth) return false;
        _observedHealth = health;
        _healthPending = true;
        return true;
    }

    private void Diagnose(string message)
    {
        try { _diagnostic("[FarmExchange.Logging] " + message); }
        catch (Exception) { /* 独立诊断目标自身失效不进入业务 logger。 */ }
    }

    /**
     * <summary>在真实初始化异常接收点记录不可继续的异常；不消费或重抛异常。</summary>
     * <param name="error">调用方正处理的原异常；调用方继续按原语义传播。</param>
     */
    public void InitializationFailed(Exception error) => Observe(() => Emit(
        "FatalException", "经营初始化无法继续", "FarmExchange.UI.Main", new()
        {
            ["Phase"] = "Initialization",
            ["ExceptionType"] = error.GetType().FullName,
            ["Exception"] = error.ToString(),
        }, LogLevel.Critical));

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
            Emit("SessionEnded", "日志会话结束", "FarmExchange.Logging.RuntimeLog", new());
            _disposed = true;
            try { _provider?.Dispose(); }
            catch (Exception error) { RecordFailure("关闭日志失败", error); }
        }
    }

    private sealed class EventState : IEnumerable<KeyValuePair<string, object?>>
    {
        internal readonly Dictionary<string, object?> Fields;
        internal EventState(Dictionary<string, object?> fields) => Fields = fields;
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            foreach (var field in Fields) yield return field;
            yield return new("{OriginalFormat}", "{Message:l}");
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TextSink : ILogEventSink
    {
        private readonly TextWriter _writer;
        private readonly LogTextFormatter _formatter = new();
        internal TextSink(TextWriter writer) => _writer = writer;
        public void Emit(LogEvent logEvent) { _formatter.Format(logEvent, _writer); _writer.Flush(); }
    }

    private sealed class FileTargetAdapter : ILogEventSink
    {
        private readonly ILogEventSink _sink;
        private readonly TargetFailureListener _listener;

        internal FileTargetAdapter(ILogEventSink sink, TargetFailureListener listener)
        {
            _sink = sink;
            _listener = listener;
            if (sink is ISetLoggingFailureListener observable) observable.SetFailureListener(listener);
        }

        public void Emit(LogEvent logEvent)
        {
            try { _sink.Emit(logEvent); }
            catch (Exception error)
            {
                _listener.OnLoggingFailed(this, LoggingFailureKind.Permanent,
                    "文件目标输出抛出异常", new[] { logEvent }, error);
            }
        }
    }

    private sealed class TargetFailureListener : ILoggingFailureListener
    {
        private readonly RuntimeLog _owner;
        internal bool Failed;
        internal bool IsConfigured;
        internal long FailureVersion;
        internal TargetFailureListener(RuntimeLog owner) => _owner = owner;
        public void OnLoggingFailed(object sender, LoggingFailureKind kind, string message,
            IReadOnlyCollection<LogEvent>? events, Exception? exception)
        {
            Failed = true;
            FailureVersion++;
            _owner.RecordFailure(message, exception);
        }
    }
}
