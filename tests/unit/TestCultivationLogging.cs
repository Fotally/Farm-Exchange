using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.Logging;
using static TestLogging;

public static class TestCultivationLogging
{
    private static readonly Vector2I Farm = new(189, 189);
    private static readonly Vector2I Other = new(192, 189);

    public static bool RunChecks()
    {
        try
        {
            CheckPlanConfiguration();
            CheckApplyAndDelete();
            CheckManualControl();
            CheckBudgetsAndSnapshots();
            CheckOriginsParityAndLifecycle();
            return true;
        }
        catch (Exception error) { GD.PrintErr("耕作日志测试失败：" + error); return false; }
    }

    private static CultivationPlanRequest Request(string name = "春季轮作") => new(name, CultivationMode.PrepareNext,
        new[] { new CultivationEntry(1, CropKind.Radish, 0), new CultivationEntry(2, CropKind.Wheat, 5) });
    private static string[] Events(StringWriter text, string name) => Lines(text.ToString()).Where(line => HasEvent(line, name)).ToArray();
    private static string Last(StringWriter text, string name) => Events(text, name).Last();

    private static void CheckPlanConfiguration()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, log);
        var request = Request("  原表  ") with { Entries = Request().Entries.Reverse().ToArray() };
        int id = game.CreateCultivationPlan(request, CommandOrigin.Scenario).Id;
        string created = Last(text, "CultivationPlanCreated");
        Require(id > 0 && created.Contains("RequestedPlan: { Name: \"  原表  \"") &&
            created.Contains("PlanAfter: { Name: \"原表\"") && created.Contains("PlanBefore: null") &&
            created.Contains("CommandOrigin: \"Scenario\"") && game.GetCultivationPlan(id)!.Entries[0].Id == 1,
            "原始名称/条顺序与真实规范化配置或来源混淆");
        int receivedCount = Events(text, "CommandReceived").Length;
        game.CheckCultivationPlan(request);
        game.CheckCultivationEntries(request.Entries);
        game.GetCultivationPlan(id);
        Require(Events(text, "CommandReceived").Length == receivedCount, "预检或快照伪造耕作命令");

        Require(!game.CreateCultivationPlan(Request() with { Name = " " }).Success, "空名称意外通过");
        string rejected = Last(text, "CultivationPlanCreated");
        Require(!rejected.Contains("PlanId:") && rejected.Contains("PlanAfter: null") && rejected.Contains("Outcome: \"Rejected\""),
            "创建拒绝伪造有效ID或配置");
        Require(!game.UpdateCultivationPlan(id, request with { Mode = (CultivationMode)99 }).Success, "非法模式意外通过");
        rejected = Last(text, "CultivationPlanUpdated");
        Require(rejected.Contains("Mode: \"99\"") && rejected.Contains("PlanBefore: { Name: \"原表\"") &&
            rejected.Contains("PlanAfter: { Name: \"原表\""), "更新拒绝覆盖了真实有效配置");
        Require(!game.UpdateCultivationPlan(-9, request).Success, "未知表意外通过");
        rejected = Last(text, "CultivationPlanUpdated");
        Require(rejected.Contains("PlanBefore: null") && rejected.Contains("PlanAfter: null") && !rejected.Contains("PlanId:") &&
            Last(text, "CommandReceived").Contains("PlanId: -9"), "原非法编号丢失或被当作有效表ID");

        var bad = Request() with { Entries = new[] { new CultivationEntry(-4, (CropKind)99, -8) } };
        Require(!game.CreateCultivationPlan(bad).Success, "非法条意外通过");
        Require(Last(text, "CultivationPlanCreated").Contains("Id: -4, Crop: \"99\", StartDay: -8"), "非法条原值没有保留");
        Require(game.UpdateCultivationPlan(id, Request("新表")).Success, "更新有效配置失败");
        Require(Last(text, "CultivationPlanUpdated").Contains("PlanAfter: { Name: \"新表\""), "更新未输出真实新配置");
        try { game.CreateCultivationPlan(null!); Require(false, "原空请求业务异常被日志消费"); }
        catch (NullReferenceException) { }
        Require(Last(text, "BusinessException").Contains("ExceptionType: \"System.NullReferenceException\"") &&
            Last(text, "CommandFinished").Contains("CommandStatus: \"Faulted\"") && game.GetCultivationPlan(id)!.Name == "新表",
            "原业务异常未关联或改变既有有效配置");
        AssertCommandPairs(text);
    }

    private static void CheckApplyAndDelete()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, log);
        int id = game.CreateCultivationPlan(Request()).Id;
        Require(game.ApplyCultivationPlan(id, new[] { Farm, Farm + Vector2I.One, Other }) == null, "重复子格应用失败");
        string applied = Last(text, "CultivationPlanApplied");
        Require(applied.Contains("InputCellCount: 3") && applied.Contains("ResolvedUniqueTargetCount: 2") &&
            applied.Contains("AppliedTargetCount: 2") && applied.Contains("TargetAnchors: [{ X: 189, Y: 189 }, { X: 192, Y: 189 }]") &&
            applied.Contains("TargetsTruncated: false"), "应用未按真实实例去重");
        FarmCultivationSnapshot binding = game.GetFarmCultivation(Farm);
        foreach (Vector2I[] cells in new[]
        {
            new[] { new Vector2I(-1, 0), Farm }, new[] { Farm, new Vector2I(383, 383), Other },
        })
        {
            Require(game.ApplyCultivationPlan(id, cells) != null, "无效目标未拒绝");
            applied = Last(text, "CultivationPlanApplied");
            Require(applied.Contains("ResolvedUniqueTargetCount: null") && applied.Contains("AppliedTargetCount: 0") &&
                game.GetFarmCultivation(Farm) == binding, "部分解析被伪装为完整去重或拒绝非原子");
        }
        Require(game.ApplyCultivationPlan(id, Array.Empty<Vector2I>()) != null, "空目标未拒绝");
        Require(Last(text, "CultivationPlanApplied").Contains("ResolvedUniqueTargetCount: 0"), "已知空输入未保留0");
        var guarded = new PrefixCells(Farm, new Vector2I(-1, 0), 40);
        Require(game.ApplyCultivationPlan(id, guarded) != null && guarded.ReadsAfterFailure == 30,
            "日志或业务继续解析失败点后的未采样尾段");
        // 观察允许读取前32个原参数，原业务到第二项拒绝，不能再读取第33项及之后。
        Require(game.ApplyCultivationPlan(-1, new[] { Farm }) != null &&
            !Last(text, "CultivationPlanApplied").Contains("PlanId:"), "未知表伪造领域ID");
        game.AdvanceTick();
        game.AdvanceTick();
        PlotSnapshot before = game.GetPlot(Farm);
        game.SetPaused(true);
        Require(game.DeleteCultivationPlan(id) == null && game.GetPlot(Farm) == before && game.GetFarmCultivation(Farm).PlanId == null,
            "删除影响了本轮或未解除引用");
        string deleted = Last(text, "CultivationPlanDeleted");
        Require(deleted.Contains("DetachedFarmCount: 2") && deleted.Contains("PlanAfter: null") && deleted.Contains("PlanId: " + id),
            "删除真实解除数或身份错误");
        Require(game.DeleteCultivationPlan(id) != null && Last(text, "CultivationPlanDeleted").Contains("DetachedFarmCount: 0") &&
            !Last(text, "CultivationPlanDeleted").Contains("PlanId:"), "重复删除伪造解除数或身份");
        int empty = game.CreateCultivationPlan(Request() with { Entries = Array.Empty<CultivationEntry>() }).Id;
        Require(game.DeleteCultivationPlan(empty) == null && Last(text, "CultivationPlanDeleted").Contains("DetachedFarmCount: 0"),
            "无引用表删除不支持实际0");
    }

    private static void CheckManualControl()
    {
        foreach (int ticks in new[] { 0, 1, 2 })
        {
            using var text = new StringWriter();
            using var log = RuntimeLog.Capture(text);
            using var game = new FarmGame(12345, log);
            game.SetFarmCrop(Farm, CropKind.Radish);
            int id = game.CreateCultivationPlan(Request()).Id;
            game.ApplyCultivationPlan(id, new[] { Farm, Other });
            for (int i = 0; i < ticks; i++) game.AdvanceTick();
            PlotSnapshot before = game.GetPlot(Farm);
            Require((int)before.Crop == ticks, "接管夹具没有进入预期真实阶段");
            Require(game.PrepareFarmCrop(Farm + Vector2I.One, CropKind.Corn) == null, "预备接管失败");
            string manual = Last(text, "FarmManualControlChanged");
            Require(manual.Contains("TakeoverMode: \"PrepareNext\"") && manual.Contains("PlanDetached: true") &&
                manual.Contains("PreviousPlanId: " + id) && manual.Contains("Anchor: { X: 189, Y: 189 }") &&
                game.GetFarmCultivation(Other).PlanId == id, "接管没有按整田解除或影响其他田");
            Require(manual.Contains("CurrentCyclePreserved: " + (ticks > 0 ? "true" : "false")) &&
                manual.Contains("EffectiveSelectedCrop: \"" + (ticks > 0 ? "Radish" : "Corn") + "\"") &&
                manual.Contains(ticks > 0 ? "NextCycleCrop: \"Corn\"" : "NextCycleCrop: null"), "当前轮与下一轮被混淆");
            if (ticks > 0) Require(game.GetPlot(Farm) == before, "预备接管破坏当前真实轮次");
            CropKind selected = game.GetPlot(Farm).CropKind;
            Require(game.SetFarmCrop(Farm, selected) == null && game.GetPlot(Farm).Crop == CropStage.None, "同种立即接管没有重启");
            manual = Last(text, "FarmManualControlChanged");
            Require(manual.Contains("TakeoverMode: \"Immediate\"") && manual.Contains("CurrentCyclePreserved: false") &&
                manual.Contains("PlanDetached: false") && manual.Contains("NextCycleCrop: null"), "同种立即接管被省略或错误保留本轮");
            Require(game.SetFarmCrop(Farm, (CropKind)99) != null &&
                Last(text, "FarmManualControlChanged").Contains("PlanDetached: null"), "拒绝仍声称实际接管");
            Require(game.PrepareFarmCrop(new(-1, 0), CropKind.Radish) != null &&
                Last(text, "FarmManualControlChanged").Contains("PreviousSelectedCrop: null"), "无有效田时伪造状态");
            Require(game.SetFarmCrop(new(0, 0), CropKind.Wheat) != null, "无农田未拒绝");
        }
    }

    private static void CheckBudgetsAndSnapshots()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, log);
        var entries = Enumerable.Range(0, 40).Select(i => new CultivationEntry(i + 1, CropKind.Radish, i < 21 ? i * 4 : 168 + (i - 21) * 4)).ToArray();
        int id = game.CreateCultivationPlan(Request(new string('名', 550)) with { Entries = entries }).Id;
        Require(id > 0, "大合法计划夹具未保存");
        string created = Last(text, "CultivationPlanCreated");
        Require(created.Contains("RequestedPlan.Entries: { ItemCount: 40 }") && created.Contains("PlanAfter.Entries: { ItemCount: 40 }") &&
            created.Contains("ScalarCount: 550") && Regex.Matches(created, "TruncatedOriginalCounts:").Count == 1 &&
            !created.Contains("Id: 33, Crop:"), "集合采样未有界或截断元数据未合并");
        Require(!game.CreateCultivationPlan(Request() with { Mode = (CultivationMode)99, Entries = entries }).Success &&
            Last(text, "CultivationPlanCreated").Contains("RequestedPlan.Entries: { ItemCount: 40 }") &&
            Last(text, "CultivationPlanCreated").Contains("PlanAfter: null"), "大非法请求未保留原计数或伪造有效配置");
        var old = game.GetCultivationPlan(id)!;
        Require(game.UpdateCultivationPlan(id, Request("空表") with { Entries = Array.Empty<CultivationEntry>() }).Success &&
            old.Entries.Count == 40 && game.GetCultivationPlan(id)!.Entries.Count == 0 &&
            game.GetCultivationPlan(-1) == null, "按ID查询不是独立只读快照");
        var repeated = Enumerable.Repeat(Farm, 45).ToArray();
        Require(game.ApplyCultivationPlan(id, repeated) == null, "大原输入应用失败");
        Require(Last(text, "CommandReceived").Contains("CommandArguments.Cells: { ItemCount: 45 }") &&
            Last(text, "CultivationPlanApplied").Contains("ResolvedUniqueTargetCount: 1"), "原输入数被采样数替换");
        game.FillWorldForBenchmark();
        var targets = game.GetBuildingSpaces().Where(space => space.Building == BuildingKind.Farm).Take(40).Select(space => space.AnchorCell).ToArray();
        Require(game.ApplyCultivationPlan(id, targets) == null, "大目标应用失败");
        string applied = Last(text, "CultivationPlanApplied");
        Require(applied.Contains("AppliedTargetCount: 40") && applied.Contains("TargetsTruncated: true") &&
            applied.Contains("TargetAnchors: { ItemCount: 40 }"), "目标采样缺少完整计数");
        Require(game.DeleteCultivationPlan(id) == null && game.GetCultivationPlan(id) == null, "删除后按ID查询仍返回表");
    }

    private static void CheckOriginsParityAndLifecycle()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var traced = new FarmGame(12345, log);
        using var disabledLog = RuntimeLog.Disabled();
        using var disabled = new FarmGame(12345, disabledLog);
        using var plain = new FarmGame(12345);
        using var brokenText = new BrokenWriter();
        using var brokenLog = RuntimeLog.Capture(brokenText, diagnostic: _ => { });
        using var broken = new FarmGame(12345, brokenLog);
        foreach (var game in new[] { traced, disabled, plain, broken })
        {
            int id = game.CreateCultivationPlan(Request(), CommandOrigin.Scenario).Id;
            game.ApplyCultivationPlan(id, new[] { Farm });
            game.AdvanceTicks(4);
            game.PrepareFarmCrop(Farm, CropKind.Corn, CommandOrigin.Scenario);
            game.DeleteCultivationPlan(id);
            foreach (Action invalid in new Action[]
            {
                () => game.CreateCultivationPlan(Request(), (CommandOrigin)99),
                () => game.UpdateCultivationPlan(id, Request(), (CommandOrigin)99),
                () => game.DeleteCultivationPlan(id, (CommandOrigin)99),
                () => game.ApplyCultivationPlan(id, new[] { Farm }, (CommandOrigin)99),
                () => game.SetFarmCrop(Farm, CropKind.Wheat, (CommandOrigin)99),
                () => game.PrepareFarmCrop(Farm, CropKind.Wheat, (CommandOrigin)99),
            }) ExpectArgumentFailure(invalid);
        }
        foreach (var game in new[] { disabled, plain, broken })
            Require(game.GetPlot(Farm) == traced.GetPlot(Farm) && game.GetFarmCultivation(Farm) == traced.GetFarmCultivation(Farm) &&
                game.MoneyCents == traced.MoneyCents && game.Calendar.ElapsedSeconds == traced.Calendar.ElapsedSeconds,
                "采集开关或输出故障改变耕作执行");
        Require(brokenLog.Health.FailureCount > 0, "输出故障未被记录");
        using var lifecycleText = new StringWriter();
        using var lifecycleLog = RuntimeLog.Capture(lifecycleText);
        using var gameForObservation = new FarmGame(12345);
        using var context = lifecycleLog.BindGame(gameForObservation, 12345)!;
        var create = context.Cultivation.BeginCreate(Request())!;
        var result = gameForObservation.CreateCultivationPlan(Request());
        create.Complete(result); create.Complete(result); create.Faulted(new Exception("忽略"));
        var apply = context.Cultivation.BeginApply(result.Id, new[] { Farm })!;
        string? error = gameForObservation.ApplyCultivationPlan(result.Id, new[] { Farm });
        apply.Complete(error, new[] { Farm.Y * FarmGame.MapSize + Farm.X });
        apply.Complete(error, null); apply.Faulted(new Exception("忽略"));
        var manual = context.Cultivation.BeginManualControl(Farm, CropKind.Radish, CultivationMode.Immediate)!;
        manual.Complete(gameForObservation.SetFarmCrop(Farm, CropKind.Radish)); manual.Complete(null);
        manual.Faulted(new Exception("忽略"));
        var delete = context.Cultivation.BeginDelete(result.Id)!;
        delete.Complete(gameForObservation.DeleteCultivationPlan(result.Id)); delete.Complete((string?)null);
        var failedPlan = context.Cultivation.BeginUpdate(result.Id, Request())!;
        failedPlan.Faulted(new InvalidOperationException("原计划异常")); failedPlan.Complete(result);
        var failedApply = context.Cultivation.BeginApply(result.Id, new[] { Farm })!;
        failedApply.Faulted(new InvalidOperationException("原应用异常")); failedApply.Complete(null, null);
        var failedManual = context.Cultivation.BeginManualControl(Farm, CropKind.Corn, CultivationMode.PrepareNext)!;
        failedManual.Faulted(new InvalidOperationException("原接管异常")); failedManual.Complete(null);
        Require(Events(lifecycleText, "CommandFinished").Length == 7 && Events(lifecycleText, "BusinessException").Length == 3 &&
            Events(lifecycleText, "FarmManualControlChanged").Length == 1, "公开观察没有幂等终结");
        foreach (Action invalid in new Action[]
        {
            () => context.Cultivation.BeginCreate(Request(), (CommandOrigin)99),
            () => context.Cultivation.BeginUpdate(1, Request(), (CommandOrigin)99),
            () => context.Cultivation.BeginDelete(1, (CommandOrigin)99),
            () => context.Cultivation.BeginApply(1, new[] { Farm }, (CommandOrigin)99),
            () => context.Cultivation.BeginManualControl(Farm, CropKind.Radish, CultivationMode.Immediate, (CommandOrigin)99),
            () => context.Cultivation.BeginManualControl(Farm, CropKind.Radish, (CultivationMode)99),
        }) ExpectArgumentFailure(invalid);
        AssertCommandPairs(lifecycleText);
        context.Dispose();
        Require(context.Cultivation.BeginCreate(Request()) == null && context.Cultivation.BeginUpdate(1, Request()) == null &&
            context.Cultivation.BeginDelete(1) == null && context.Cultivation.BeginApply(1, new[] { Farm }) == null &&
            context.Cultivation.BeginManualControl(Farm, CropKind.Radish, CultivationMode.Immediate) == null,
            "已结束局仍建立观察");
    }

    private static void ExpectArgumentFailure(Action action)
    {
        try { action(); throw new InvalidOperationException("非法参数未拒绝"); }
        catch (ArgumentOutOfRangeException) { }
    }

    private static void AssertCommandPairs(StringWriter text)
    {
        string[] received = Events(text, "CommandReceived"), finished = Events(text, "CommandFinished");
        Require(received.Length == finished.Length, "收到与结束数量不一致");
        foreach (string start in received)
        {
            string id = Regex.Match(start, @"CommandId: (\d+)").Groups[1].Value;
            Require(finished.Count(line => Regex.IsMatch(line, @"CommandId: " + id + @"(?:,| )")) == 1, "同一命令没有唯一终结");
        }
    }

    private sealed class PrefixCells(Vector2I valid, Vector2I invalid, int count) : IReadOnlyList<Vector2I>
    {
        public int Count => count;
        public int ReadsAfterFailure { get; private set; }
        public Vector2I this[int index]
        {
            get
            {
                if (index >= 32) throw new InvalidOperationException("不应读取采样预算之后且已被业务短路的目标");
                if (index > 1) ReadsAfterFailure++;
                return index == 1 ? invalid : valid;
            }
        }
        public IEnumerator<Vector2I> GetEnumerator()
        { for (int i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("耕作输出故障");
    }
}
