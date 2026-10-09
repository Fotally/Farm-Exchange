using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Logging;

public static class TestPerformanceLogging
{
    public static bool RunChecks()
    {
        try
        {
            CheckBatches();
            CheckFramesAndTiming();
            return true;
        }
        catch (Exception error) { GD.PrintErr("性能日志测试失败：" + error); return false; }
    }

    private static void CheckBatches()
    {
        long now = 0, timestamp = 0;
        using var text = new StringWriter();
        using var runtime = RuntimeLog.CaptureDiagnostics(text, () => now, () => timestamp);
        using var game = new FarmGame(12345, runtime);
        var result = game.AdvanceTicks(50, point => { timestamp += Stopwatch.Frequency / 1000; return true; });
        game.AdvanceTicks(0);
        game.SetPaused(true);
        game.AdvanceTicks(50);
        game.SetPaused(false);
        var expected = new InvalidOperationException("原检查点异常");
        try { game.AdvanceTicks(1, _ => { timestamp += Stopwatch.Frequency / 100; throw expected; }); }
        catch (InvalidOperationException error) { Require(ReferenceEquals(expected, error), "异常身份被日志改变"); }
        now = 60000;
        game.AdvanceTicks(0);
        var first = Summaries(text, "GameAdvance").Single();
        Require(first.Contains("BatchCount: 4") && first.Contains("FailedBatchCount: 1"), "零请求、暂停或失败批次计数错误");
        Require(first.Contains("AdvancedTicks: " + result.AdvancedTicks + ",") && first.Contains("QuietTicks: " + result.QuietTicks + ",") && first.Contains("EventTicks: " + result.EventTicks + ","), "失败批次混入成功推进汇总");
        Require(first.Contains("FailedBatchTotalMs: 10"), "高精度失败耗时不正确");
        game.Dispose();
        Require(Summaries(text, "GameAdvance").Length == 2 && Summaries(text, "GameAdvance").Last().Contains("BatchMaxMs: null"), "空尾段最大值不是未知");
        using var manualGame = new FarmGame(1);
        runtime.BindGame(manualGame, 1)!.Dispose();
        Require(Summaries(text, "GameAdvance").Length == 2, "手动绑定冒充自动推进统计");
    }

    private static void CheckFramesAndTiming()
    {
        long now = 0, timestamp = 0;
        using var text = new StringWriter();
        using var runtime = RuntimeLog.CaptureDiagnostics(text, () => now, () => timestamp);
        using var first = new FarmGame(42, runtime);
        using var second = new FarmGame(42, runtime);
        runtime.ProcessFrame();
        timestamp += Stopwatch.Frequency / 100;
        runtime.ProcessFrame();
        first.AdvanceTicks(5);
        second.AdvanceTicks(5);
        now = 60000;
        timestamp += Stopwatch.Frequency / 50;
        runtime.ProcessFrame();
        var frames = Summaries(text, "ProcessFrames").Single();
        Require(frames.Contains("FrameCount: 2") && frames.Contains("FrameAverageMs: 15") && frames.Contains("FrameMaxMs: 20"), "主帧间隔重复或首次基线被计入");
        Require(!frames.Contains("GameInstanceId") && !frames.Contains("SimulationSeconds") && !frames.Contains("FacilityCount"), "进程帧错误归属经营局");
        first.Log!.Diagnostics.Start(new(new[] { "EventTickTiming" }, IncludeGameEvents: true));
        var result = first.AdvanceTicks(100);
        var ticks = TestDiagnosticCapture.Lines(text, "EventTickTiming");
        Require(ticks.Length == result.EventTicks && ticks.All(line => line.Contains("StageDurationsMs: ")), "事件秒测量冒充平静秒");
        runtime.Dispose();
        var lines = text.ToString().Split('\n');
        int lastGame = Array.FindLastIndex(lines, line => line.Contains("EventName=GameEnded "));
        int frameTail = Array.FindLastIndex(lines, line => line.Contains("SummaryScope: \"ProcessFrames\""));
        int ended = Array.FindLastIndex(lines, line => line.Contains("EventName=SessionEnded "));
        Require(lastGame < frameTail && frameTail < ended, "关闭顺序不是局、帧尾、会话");
    }

    private static string[] Summaries(StringWriter writer, string scope) => TestDiagnosticCapture.Lines(writer, "PerformanceSummary").Where(line => line.Contains("SummaryScope: \"" + scope + "\"")).ToArray();
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}
