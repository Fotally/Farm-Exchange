using System;
using System.Diagnostics;

namespace FarmExchange.Logging;

/**
 * <summary>只在进程主帧入口采样一次实际帧间隔，以常量空间累计窗口。</summary>
 */
internal sealed class FramePerformanceLog
{
    private readonly LogOutput _output;
    private readonly Func<long> _uptime;
    private readonly Func<long> _timestamp;
    private long? _previous;
    private long _start, _window, _count;
    private double _sum, _max;

    internal FramePerformanceLog(LogOutput output, Func<long>? uptime, Func<long>? timestamp)
    { _output = output; _uptime = uptime ?? (() => output.UptimeMs); _timestamp = timestamp ?? Stopwatch.GetTimestamp; }

    internal void Sample() => _output.Observe(() =>
    {
        long now = _uptime();
        long timestamp = _timestamp();
        if (!_previous.HasValue) { _previous = timestamp; _start = now; return; }
        double interval = Math.Max(0, (timestamp - _previous.Value) * 1000.0 / Stopwatch.Frequency);
        _previous = timestamp;
        _count++;
        _sum += interval;
        _max = Math.Max(_max, interval);
        if (now - _start >= 60000) Flush(now, false);
    });

    internal void End()
    {
        if (_previous.HasValue) _output.Observe(() => Flush(_uptime(), true));
    }

    private void Flush(long now, bool partial)
    {
        _output.Submit(PerformanceLog.Summary, "进程主帧间隔窗口", new()
        {
            ["SummaryScope"] = "ProcessFrames",
            ["WindowId"] = ++_window,
            ["IntervalStartUptimeMs"] = _start,
            ["IntervalEndUptimeMs"] = now,
            ["IsPartialWindow"] = partial,
            ["FrameCount"] = _count,
            ["FrameAverageMs"] = _count > 0 ? _sum / _count : null,
            ["FrameMaxMs"] = _count > 0 ? _max : null,
        });
        _start = now;
        _count = 0;
        _sum = _max = 0;
    }
}
