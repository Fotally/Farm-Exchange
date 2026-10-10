using System;
using System.IO;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Logging;
using FarmExchange.Land;

public static class TestDiagnosticCapture
{
    public static bool RunChecks()
    {
        try
        {
            CheckLimitsAndLifetime();
            CheckScopeAndReplacement();
            CheckPlacementAndView();
            CheckSubmissionExpiry();
            CheckViewAllocations();
            return true;
        }
        catch (Exception error) { GD.PrintErr("诊断采集测试失败：" + error); return false; }
    }

    private static void CheckLimitsAndLifetime()
    {
        long now = 0;
        using var text = new StringWriter();
        using var runtime = RuntimeLog.CaptureDiagnostics(text, () => now);
        using var game = new FarmGame(12345, runtime);
        var capture = game.Log!.Diagnostics;
        Require(!capture.IsActive, "默认不得采集详细事件");
        Invalid(() => capture.Start(new(new[] { "ViewChanged" }, IncludeGameEvents: true, EventLimit: 5001)));
        Invalid(() => capture.Start(new(new[] { "ViewChanged" }, IncludeGameEvents: true, DurationLimitMs: 120001)));
        Invalid(() => capture.Start(new(Array.Empty<string>(), IncludeGameEvents: true)));
        Invalid(() => capture.Start(new(new[] { "ViewChanged" })));
        Invalid(() => capture.Start(new(new[] { "WorkerMoved" }, WorkerNumbers: new[] { 4 })));
        Invalid(() => capture.Start(new(new[] { "OrderEvaluated" }, OrderIds: new[] { 999 })));
        Invalid(() => capture.Start(new(new[] { "ProductionStateChanged" }, Anchors: new[] { new Vector2I(0, 0) })));
        Invalid(() => capture.Start(new(new[] { "RuleChecked" }, Cells: Enumerable.Repeat(new Vector2I(0, 0), 33).ToArray())));
        Require(capture.Start(new(new[] { "ViewChanged" }, IncludeGameEvents: true, EventLimit: 1)), "开始失败");
        Require(!capture.Start(new(new[] { "ViewChanged" }, IncludeGameEvents: true)), "不能覆盖正在采集的范围");
        var view = game.Log.View;
        view.Observe(new(1920, 1080), new(1920, 1080), 1.5f, Vector2.Zero, 0);
        view.Observe(new(1920, 1080), new(1920, 1080), 1.6f, Vector2.One, 2);
        Require(!capture.IsActive && Lines(text, "ViewChanged").Length == 1, "明细到限未停止");
        Require(Lines(text, "DiagnosticCaptureEnded").Single().Contains("StopReason: \"EventLimit\""), "条数停止原因错误");
        Require(capture.Start(new(new[] { "ViewChanged" }, IncludeGameEvents: true, DurationLimitMs: 10)), "重启失败");
        game.SetPaused(true);
        runtime.ProcessFrame();
        now = 11;
        runtime.ProcessFrame();
        Require(!capture.IsActive && Lines(text, "DiagnosticCaptureEnded").Last().Contains("StopReason: \"DurationLimit\""), "暂停现实主帧未结束采集");
        Require(capture.Start(new(new[] { "ViewChanged" }, IncludeGameEvents: true)), "尾段开始失败");
        game.Dispose();
        capture.Stop();
        Require(Lines(text, "DiagnosticCaptureEnded").Length == 3 && Lines(text, "DiagnosticCaptureEnded").Last().Contains("StopReason: \"GameEnded\""), "局结束未一次结束诊断");
        Require(!capture.Start(new(new[] { "ViewChanged" }, IncludeGameEvents: true)), "已结束局不能重启诊断");
        using var disabled = RuntimeLog.Disabled();
        using var off = new FarmGame(12345, disabled);
        Require(off.Log == null, "关闭采集分配了局上下文");
    }

    private static void CheckScopeAndReplacement()
    {
        using var text = new StringWriter();
        using var runtime = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, runtime);
        var anchor = game.GetBuildingSpaces().First(space => space.Building == BuildingKind.Farm).AnchorCell;
        var capture = game.Log!.Diagnostics;
        Require(capture.Start(new(new[] { "ProductionStateChanged" }, Anchors: new[] { anchor })), "实例采集开始失败");
        Require(!capture.ShouldCapture("ProductionStateChanged", anchorIndex: 0), "范围外实例被纳入");
        game.RemoveBuilding(anchor);
        Require(!capture.IsActive && Lines(text, "DiagnosticCaptureEnded").Single().Contains("SuppressedCount: 0"), "实例失效或实际抑制计数错误");
        Require(game.BuildFarm(anchor) == null, "重建失败");
        Require(!capture.ShouldCapture("ProductionStateChanged", anchorIndex: anchor.Y * FarmGame.MapSize + anchor.X), "重建继承旧实例采集");
        Require(capture.Start(new(new[] { "ViewChanged", "ProductionStateChanged" }, Anchors: new[] { anchor }, IncludeGameEvents: true)), "混合范围开始失败");
        game.RemoveBuilding(anchor);
        Require(capture.IsActive && capture.ShouldCapture("ViewChanged"), "局级范围不应因最后建筑失效停止");
        capture.Stop();
        Require(Lines(text, "DiagnosticCaptureEnded").Last().Contains("StopReason: \"UserStopped\""), "主动停止原因错误");
    }

    private static void CheckPlacementAndView()
    {
        using var text = new StringWriter();
        using var runtime = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, runtime);
        var cell = new Vector2I(10, 10);
        var farm = game.GetBuildingSpaces().First(space => space.Building == BuildingKind.Farm).AnchorCell;
        var capture = game.Log!.Diagnostics;
        capture.Start(new(new[] { "RuleChecked", "ViewChanged" }, Anchors: new[] { farm }, Cells: new[] { cell }, IncludeGameEvents: true));
        for (int i = 0; i < 100; i++)
        {
            Require(game.CheckPlacement(cell, BuildingKind.Road, default).Allowed, "道路预检失败");
            Require(game.CheckPlacement(cell, (BuildingKind)99, default).Failure == LandFailure.InvalidBuilding, "非法建筑预检失败");
            game.GetFarmDetails(farm);
            game.GetPlantingCheck(farm, CropKind.Wheat);
        }
        Require(Lines(text, "RuleChecked").Length == 0 && Lines(text, "CommandReceived").Length == 0,
            "反复预检或设施查询机械采集规则事实或命令");
        Require(game.TryPlace(cell, BuildingKind.Road, default).Success, "道路放置失败");
        Require(game.CheckPlacement(cell, BuildingKind.Farm, default).Failure == LandFailure.Occupied, "冲突检查失败");
        Require(Lines(text, "RuleChecked").Length == 1, "冲突预检消耗实际执行诊断预算");
        Require(game.TryPlace(cell, BuildingKind.Farm, default).Failure == LandFailure.Occupied, "实际冲突未拒绝");
        var placement = Lines(text, "RuleChecked").Last();
        Require(placement.Contains("Occupancy: false") && !placement.Contains("Funds: "), "短路后补造余额检查");
        Require(Lines(text, "RuleChecked").First().Contains("Building: true") && !Lines(text, "RuleChecked").First().Contains("Crop: true"), "道路补造作物校验");
        Require(game.TryPlace(cell, (BuildingKind)99, default).Failure == LandFailure.InvalidBuilding, "实际非法建筑未拒绝");
        Require(Lines(text, "RuleChecked").Last().Contains("CheckResults: { Building: false }"), "非法建筑早拒丢失或补造后续检查");
        Require(Lines(text, "RuleChecked").Length == 3 && Lines(text, "CommandReceived").Length == 3 &&
            Lines(text, "CommandFinished").Length == 3, "实际检查或命令被重复执行和记录");
        var view = game.Log.View;
        view.Observe(new(1920, 1080), new(1920, 1080), 1.5f, Vector2.Zero, 0);
        view.Observe(new(1920, 1080), new(1920, 1080), 1.5f, Vector2.Zero, 0);
        Require(Lines(text, "ViewChanged").Length == 0, "稳定帧重复记录");
        view.Observe(new(2560, 1440), new(2560, 1440), 1.6f, Vector2.One, 3);
        Require(Lines(text, "ViewChanged").Single().Contains("RebuiltChunkCount: 3"), "真实绘制计数没有按增量记录");
        runtime.Dispose();
        Require(Lines(text, "DiagnosticCaptureEnded").Single().Contains("StopReason: \"Shutdown\""), "会话关闭原因错误");
    }

    internal static string[] Lines(StringWriter writer, string name) => writer.ToString().Split('\n').Where(line => line.Contains("EventName=" + name + " ", StringComparison.Ordinal)).ToArray();

    private static void CheckViewAllocations()
    {
        using var text = new StringWriter();
        using var runtime = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, runtime);
        var view = game.Log!.View;
        CheckUnallocatedViewChanges(view, "未开启采集");
        game.Log.Diagnostics.Start(new(new[] { "RuleChecked" }, IncludeGameEvents: true));
        CheckUnallocatedViewChanges(view, "未选择视图事件");
        game.Log.Diagnostics.Stop();
        game.Log.Diagnostics.Start(new(new[] { "ViewChanged" }, IncludeGameEvents: true));
        view.Observe(new(1920, 1080), new(1920, 1080), 1.5f, Vector2.Zero, 0);
        for (int i = 0; i < 2000; i++)
            view.Observe(new(1920, 1080), new(1920, 1080), 1.5f, Vector2.Zero, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
            view.Observe(new(1920, 1080), new(1920, 1080), 1.5f, Vector2.Zero, 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, "已选择视图事件的稳定帧产生托管分配：" + allocated);
    }

    private static void CheckUnallocatedViewChanges(ViewDiagnostics view, string mode)
    {
        for (int i = 0; i < 2000; i++)
            view.Observe(new(1920, 1080), new(1920, 1080), 1.5f, new(i, i), i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
            view.Observe(new(1920, 1080), new(1920, 1080), 1.5f, new(i, i), i);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, mode + "的实际视图变化产生托管分配：" + allocated);
    }

    private static void CheckSubmissionExpiry()
    {
        long now = 0;
        using var text = new StringWriter();
        using var runtime = RuntimeLog.CaptureDiagnostics(text, () => now);
        using var game = new FarmGame(12345, runtime);
        var capture = game.Log!.Diagnostics;
        capture.Start(new(new[] { "ObservedFact" }, IncludeGameEvents: true, DurationLimitMs: 10));
        Require(capture.ShouldCapture("ObservedFact"), "真实候选门禁失败");
        var fields = game.Log.Context("Command");
        now = 10;
        capture.Capture(new(900, "ObservedFact", "Tests.ActualObservation", RuntimeIncluded: false), "已到提交入口的真实候选", fields);
        Require(Lines(text, "ObservedFact").Length == 0 && Lines(text, "DiagnosticCaptureEnded").Single().Contains("SuppressedCount: 1"), "实际到期提交没有记录一次抑制");
        capture.Capture(new(900, "ObservedFact", "Tests.ActualObservation", RuntimeIncluded: false), "停采后不推算", fields);
        Require(Lines(text, "DiagnosticCaptureEnded").Length == 1, "停采后重复结束或推算抑制");
    }
    private static void Invalid(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("非法采集请求未拒绝");
    }
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}
