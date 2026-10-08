using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>拥有通用事件输出、进程身份、串行序号、文件目标及健康诊断。</summary>
 * <remarks>业务仅经过具名领域入口递交请求与结果；实例不使用 Serilog 全局 logger 或 SelfLog。</remarks>
 */
internal sealed class LogOutput : IDisposable
{
    private readonly object _sync = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
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
    private bool _development;
    private static readonly LogEventDescriptor HealthChanged = new(9, "LoggingHealthChanged",
        "FarmExchange.Logging.RuntimeLog", LogLevel.Warning);

    private LogOutput(Action<string>? diagnostic) => _diagnostic = diagnostic ?? Console.Error.WriteLine;

    /**
     * <summary>创建不会采集、分配局或指令身份、写文件的独立实例。</summary>
     * <returns>可独立关闭的无采集会话。</returns>
     */
    public static LogOutput Disabled() => new(null);

    /**
     * <summary>在指定目录建立同步共享 File 输出；开发额外建立 debug 目标。</summary>
     * <param name="directory">组装点确定的 logs 绝对目录；故障不改路径。</param>
     * <param name="development">是否启用开发采集。</param>
     * <param name="retention">文件预算；省略时使用首版初值。</param>
     * <param name="diagnostic">不经过业务日志的故障接收点；默认 stderr。</param>
     * <returns>可释放会话，输出故障不阻止经营初始化。</returns>
     */
    internal static LogOutput OpenFile(string directory, bool development,
        LogFileRetention? retention = null, Action<string>? diagnostic = null)
    {
#if !DEBUG
        development = false;
#endif
        var log = new LogOutput(diagnostic);
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
        log._development = development;
        return log;
    }

    /**
     * <summary>使用真实 MEL 和 formatter 采集到调用方拥有的内存文本目标。</summary>
     * <param name="writer">内存文本接收点；会话关闭不释放该对象。</param>
     * <param name="diagnostic">独立故障接收点。</param>
     * <returns>与文件输出共用领域入口的会话。</returns>
     */
    internal static LogOutput Capture(TextWriter writer,
        Action<string>? diagnostic = null)
    {
        var log = new LogOutput(diagnostic);
        var target = new TargetFailureListener(log);
        log._targets.Add(target);
        log.Connect(new LoggerConfiguration().MinimumLevel.Verbose()
            .WriteTo.Fallible(sink => sink.Sink(new TextSink(writer)), target));
        target.IsConfigured = true;
        log._development = true;
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
    internal long UptimeMs => _clock.ElapsedMilliseconds;

    private void AddFile(LoggerConfiguration configuration, string path, long bytes, int files, LogEventLevel minimumLevel)
    {
        var target = new TargetFailureListener(this) { RuntimeOnly = minimumLevel == LogEventLevel.Information };
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
        _logger = _provider.CreateLogger("FarmExchange.Logging.LogOutput");
        ObserveHealthChange();
    }

    internal void Observe(Action observation)
    {
        if (!IsEnabled) return;
        try { observation(); }
        catch (Exception error) { RecordFailure("日志投影失败", error); }
    }

    internal void Submit(LogEventDescriptor description, string message, Dictionary<string, object?> fields)
    {
        lock (_sync)
        {
            if (!IsEnabled || !_development && (!description.RuntimeIncluded || description.Level < LogLevel.Information)) return;
            if (_healthPending && Health.Health != LoggingHealth.Unavailable)
            {
                _healthPending = false;
                var health = Health;
                Write(HealthChanged with { Level = health.Health == LoggingHealth.Healthy ? LogLevel.Information : LogLevel.Warning }, "日志输出健康发生变化", new()
                {
                    ["LoggingHealth"] = health.Health.ToString(),
                    ["FirstFailureUptimeMs"] = health.FirstFailureUptimeMs,
                    ["FailureCount"] = health.FailureCount,
                    ["KnownLostCount"] = health.KnownLostCount,
                });
            }
            Write(description, message, fields);
        }
    }

    private void Write(LogEventDescriptor description, string message, Dictionary<string, object?> fields)
    {
        fields["SchemaVersion"] = 1;
        fields["SessionId"] = _sessionId;
        fields["Sequence"] = ++_sequence;
        fields["UptimeMs"] = _clock.ElapsedMilliseconds;
        fields["EventName"] = description.Name;
        fields["SourceContext"] = description.Source;
        fields["Message"] = message;
        fields["RuntimeIncluded"] = description.RuntimeIncluded;
        long[] before = _targets.ConvertAll(target => target.FailureVersion).ToArray();
        try
        {
            _logger!.Log(description.Level, new EventId(description.Number, description.Name), new EventState(fields),
                null, static (state, _) => (string)state.Fields["Message"]!);
            bool recovered = false;
            for (int i = 0; i < _targets.Count; i++)
                if (_targets[i].Accepts(description) && _targets[i].IsConfigured && _targets[i].Failed && _targets[i].FailureVersion == before[i])
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
     * <summary>释放全部输出目标；生命周期记录由组装入口提交。</summary>
     * <remarks>重复关闭无副作用；正常 flush 不承诺强杀或断电末条必达。</remarks>
     */
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
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
            if (_listener.RuntimeOnly && ((ScalarValue)logEvent.Properties["RuntimeIncluded"]).Value is not true) return;
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
        private readonly LogOutput _owner;
        internal bool RuntimeOnly;
        internal bool Accepts(LogEventDescriptor description) => !RuntimeOnly || description.RuntimeIncluded && description.Level >= LogLevel.Information;
        internal bool Failed;
        internal bool IsConfigured;
        internal long FailureVersion;
        internal TargetFailureListener(LogOutput owner) => _owner = owner;
        public void OnLoggingFailed(object sender, LoggingFailureKind kind, string message,
            IReadOnlyCollection<LogEvent>? events, Exception? exception)
        {
            Failed = true;
            FailureVersion++;
            _owner.RecordFailure(message, exception);
        }
    }
}
