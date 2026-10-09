using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FarmExchange.Gameplay;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>累计真实批次耗时，完整请求完成后封存窗口；不重新执行平静秒。</summary>
 */
internal sealed class PerformanceLog
{
    internal static readonly LogEventDescriptor Summary = new(92, "PerformanceSummary", "FarmExchange.Logging.PerformanceLog");
    private readonly GameLog _context;
    private readonly FarmGame _game;
    private readonly Func<long> _uptime;
    private readonly Func<long> _timestamp;
    private long _startMs;
    private uint _startSeconds;
    private long _window;
    private long _batches, _failed, _advanced, _quiet, _events;
    private double _totalMs, _maxMs, _failedMs;

    internal PerformanceLog(GameLog context, FarmGame game, Func<long>? uptime, Func<long>? timestamp)
    {
        _context = context;
        _game = game;
        _uptime = uptime ?? (() => context.Output.UptimeMs);
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    }

    internal void Start() => _context.Observe(() => { _startMs = _uptime(); _startSeconds = _game.Calendar.ElapsedSeconds; });

    internal long? BeginBatch()
    {
        long? start = null;
        _context.Observe(() => start = _timestamp());
        return start;
    }

    internal void EndBatch(long? start, bool completed, SimulationAdvanceResult result)
    {
        if (!start.HasValue) return;
        _context.Observe(() =>
        {
            double elapsed = Math.Max(0, (_timestamp() - start.Value) * 1000.0 / Stopwatch.Frequency);
            if (completed)
            {
                _batches++;
                _totalMs += elapsed;
                _maxMs = Math.Max(_maxMs, elapsed);
                _advanced += result.AdvancedTicks;
                _quiet += result.QuietTicks;
                _events += result.EventTicks;
            }
            else { _failed++; _failedMs += elapsed; }
            long now = _uptime();
            if (now - _startMs >= 60000) Flush(now, false);
        });
    }

    internal void End() => _context.Observe(() => Flush(_uptime(), true));

    private void Flush(long now, bool partial)
    {
        var fields = _context.Context("Command");
        fields.Remove("Phase");
        fields["SummaryScope"] = "GameAdvance";
        fields["WindowId"] = ++_window;
        fields["IntervalStartUptimeMs"] = _startMs;
        fields["IntervalEndUptimeMs"] = now;
        fields["IsPartialWindow"] = partial;
        fields["SimulationSecondsStart"] = _startSeconds;
        fields["SimulationSecondsEnd"] = _game.Calendar.ElapsedSeconds;
        fields["BatchCount"] = _batches;
        fields["FailedBatchCount"] = _failed;
        fields["BatchTotalMs"] = _totalMs;
        fields["BatchMaxMs"] = _batches > 0 ? _maxMs : null;
        fields["FailedBatchTotalMs"] = _failedMs;
        fields["AdvancedTicks"] = _advanced;
        fields["QuietTicks"] = _quiet;
        fields["EventTicks"] = _events;
        fields["AmortizedMsPerSimulationSecond"] = _advanced > 0 ? _totalMs / _advanced : null;
        fields["FacilityCount"] = _game.GetBuildingSpaces().Count(space => space.Building is BuildingKind.Farm or BuildingKind.Processor);
        _context.Output.Submit(Summary, "完整经营批次性能窗口", fields);
        _startMs = now;
        _startSeconds = _game.Calendar.ElapsedSeconds;
        _batches = _failed = _advanced = _quiet = _events = 0;
        _totalMs = _maxMs = _failedMs = 0;
    }

    internal EventTickMeasurement? BeginEventTick(uint startSeconds) =>
        _context.Diagnostics.ShouldCapture("EventTickTiming") ? new EventTickMeasurement(_context, startSeconds) : null;
}

internal sealed class EventTickMeasurement
{
    private static readonly LogEventDescriptor Timing = new(106, "EventTickTiming", "FarmExchange.Gameplay.FarmGame", LogLevel.Trace, false);
    private readonly GameLog _context;
    private readonly uint _startSeconds;
    private readonly Dictionary<string, object?> _stages = new();
    private long _stageStart;

    internal EventTickMeasurement(GameLog context, uint startSeconds) { _context = context; _startSeconds = startSeconds; }
    internal void BeginStage() => _stageStart = Stopwatch.GetTimestamp();
    internal void EndStage(string stage)
    {
        double ms = Stopwatch.GetElapsedTime(_stageStart).TotalMilliseconds;
        _stages[stage] = (_stages.TryGetValue(stage, out var previous) ? (double)previous! : 0) + ms;
    }
    internal void Complete() => _context.Observe(() =>
    {
        var fields = _context.Context("Command");
        fields.Remove("Phase");
        fields["EventTickStartSimulationSeconds"] = _startSeconds;
        fields["StageDurationsMs"] = _stages;
        _context.Diagnostics.Capture(Timing, "完整事件秒阶段实测", fields);
    });
}
