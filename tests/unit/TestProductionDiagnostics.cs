using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Workers;

public static class TestProductionDiagnostics
{
    private static readonly Vector2I Farm = new(189, 189);
    private static readonly Vector2I Processor = new(189, 192);
    private static readonly CommodityId Raw = new(CropKind.Radish, CommodityKind.Raw);

    public static bool RunChecks()
    {
        try
        {
            CheckWetSowingAndWorkers();
            CheckDryProductionAndSummary();
            CheckClearingAndRules();
            CheckQuietMovementAndInvalidation();
            CheckGatesAndFailureParity();
            CheckClosedGateAllocations();
            return true;
        }
        catch (Exception error) { GD.PrintErr("生产与工人详细诊断测试失败：" + error); return false; }
    }

    private static FarmGame Empty(RuntimeLog? log = null, uint seconds = 0)
    {
        var game = new FarmGame(12345, seconds, log);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        return game;
    }

    private static void CheckWetSowingAndWorkers()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = Empty(log);
        game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
        game.TryPlace(new(192, 189), BuildingKind.Farm, CropKind.Radish);
        Require(game.Log!.Diagnostics.Start(new(new[] { "ProductionStateChanged", "WorkerTaskChanged" },
            new[] { Farm }, WorkerNumbers: new[] { 1 })), "开启选定湿田诊断失败");
        game.AdvanceTick(true);
        string[] production = Events(text, "ProductionStateChanged");
        Require(production.Length == 3 && Transition(production[0], "Watered", "None", "None") &&
            Transition(production[1], "Sown", "None", "Seeded") && Transition(production[2], "GrowthStarted", "Seeded", "Growing"),
            "湿田播种漏掉真实中间Seeded状态：" + string.Join("\n", production));
        Require(production[0].Contains("HasWaterBefore: false, HasWaterAfter: true") &&
            production.All(line => line.Contains("Anchor: { X: 189, Y: 189 }") && line.Contains("CaptureId:")),
            "雨水前后或锚点范围不正确");
        game.AdvanceTick(true);
        Require(Events(text, "ProductionStateChanged").Length == 3, "重复雨水虚构Watered转换");
        string[] tasks = Events(text, "WorkerTaskChanged");
        Require(tasks.Any(line => line.Contains("ActivityBefore: \"Idle\", ActivityAfter: \"Sowing\"") &&
            line.Contains("TargetAnchorAfter: { X: 189, Y: 189 }")) &&
            tasks.Any(line => line.Contains("ActivityBefore: \"Sowing\", ActivityAfter: \"Idle\"")), "同秒认领/完成释放被合并遗漏");
        Require(tasks.All(line => line.Contains("WorkerNumber: 1")) && !tasks.Any(line => line.Contains("X: 190, Y: 190")),
            "工人范围失效或把工作中心当锚点");
    }

    private static void CheckDryProductionAndSummary()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = Empty(log);
        game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
        game.BuildProcessor(Processor, CropKind.Radish);
        game.Log!.Diagnostics.Start(new(new[] { "ProductionStateChanged", "WorkerTaskChanged" },
            new[] { Farm, Processor }, WorkerNumbers: new[] { 1 }));
        game.AdvanceTicks(2);
        string[] first = Events(text, "ProductionStateChanged");
        Require(first.Length == 3 && Transition(first[0], "Sown", "None", "Seeded") &&
            Transition(first[1], "Watered", "Seeded", "Seeded") && Transition(first[2], "GrowthStarted", "Seeded", "Growing"),
            "干田播种/供水/生长转换不对应实际赋值");
        Require(Events(text, "WorkerTaskChanged").Any(line => line.Contains("ActivityBefore: \"Sowing\", ActivityAfter: \"Watering\"")),
            "播种后原田供水任务转换遗漏");
        game.AdvanceTicks(498);
        game.Dispose();
        string[] details = Events(text, "ProductionStateChanged");
        string summary = Events(text, "ProductionSummary").Single();
        foreach (var pair in new[] { ("Harvested", "HarvestedByCrop"), ("RawClaimed", "RawConsumedByCrop"), ("Processed", "ProducedByCrop") })
        {
            long detailed = details.Where(line => line.Contains("Transition: \"" + pair.Item1 + "\"")).Sum(line => Number(line, "Quantity"));
            Require(detailed > 0 && detailed == CropCount(summary, pair.Item2), "详细事实与汇总不同源：" + pair.Item1);
        }
        Require(details.Where(line => line.Contains("Transition: \"ProcessingStarted\"")).All(line => !line.Contains("Quantity:")),
            "加工启动虚构第二次扣料");
        Require(details.Any(line => Transition(line, "RawClaimed", "ReadyToProcess", "WaitingForRaw")) &&
            details.Any(line => Transition(line, "Processed", "Processing", "ReadyToProcess")),
            "加工状态没有读取真实库存可领取状态");
        Require(details.Any(line => Transition(line, "Harvested", "Growing", "None") && line.Contains("Quantity: 6")),
            "收获没有在真实入库后记录");
    }

    private static void CheckClearingAndRules()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using (var game = Empty(log, 4319))
        {
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.Log!.Diagnostics.Start(new(new[] { "ProductionStateChanged", "RuleChecked" }, new[] { Farm }));
            game.AdvanceTick();
            Require(Events(text, "ProductionStateChanged").Any(line => Transition(line, "Cleared", "Seeded", "None") &&
                !line.Contains("Quantity:")), "清理没有真实锚点前后或虚构产量");
            Require(Events(text, "RuleChecked").Length == 1 && Events(text, "RuleChecked")[0].Contains("Season: true, Time: false") &&
                Events(text, "RuleChecked")[0].Contains("Outcome: \"Success\""),
                "成熟时间风险错误拒绝CanSow");
        }
        text.GetStringBuilder().Clear();
        using (var game = Empty(log, 4320))
        {
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.Log!.Diagnostics.Start(new(new[] { "RuleChecked" }, new[] { Farm }));
            game.AdvanceTick();
            int index = Farm.Y * FarmGame.MapSize + Farm.X;
            var farming = new FarmingSystem(FarmGame.MapSize * FarmGame.MapSize) { Diagnostics = game.Log.ProductionDiagnostics };
            farming.Place(index, CropKind.Radish);
            var workers = new WorkerScheduler(new Vector2I(190, 190));
            CalendarSnapshot spring = new GameCalendar(4319).Snapshot;
            FarmWorkRequest request = farming.GetWorkNeed(index, spring)!.Value;
            for (int read = 0; read < 20; read++)
            {
                game.GetFarmDetails(Farm);
                game.GetPlantingCheck(Farm, CropKind.Radish);
                farming.GetWorkNeed(index, spring);
                farming.GetWorkNeed(index, game.Calendar);
                workers.GetNextEventSeconds(farming, game.Calendar);
            }
            Require(Events(text, "RuleChecked").Length == 0, "只读详情/任务查询/排程预检消耗诊断事件");
            FarmSnapshot before = farming.Get(index);
            Require(!farming.TryCompleteWork(request, game.Calendar) && farming.Get(index) == before,
                "禁生季真实执行重验没有拒绝或改变了原农田状态");
            string[] rules = Events(text, "RuleChecked");
            Require(rules.Length == 1 && rules[0].Contains("Season: false") && !rules[0].Contains("Time:") &&
                rules[0].Contains("Outcome: \"Rejected\"") && rules[0].Contains("Phase: \"Workers\""), "真实禁生重验漏记或补造Time检查");
            game.Log.Diagnostics.Stop();
            Require(Number(Events(text, "DiagnosticCaptureEnded").Single(), "CapturedCount") == 1, "查询或预检占用了采集预算");
        }
    }

    private static void CheckQuietMovementAndInvalidation()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = Empty(log);
        var distant = new Vector2I(0, 0);
        game.TryPlace(distant, BuildingKind.Farm, CropKind.Radish);
        game.Log!.Diagnostics.Start(new(new[] { "WorkerMoved", "WorkerTaskChanged" }, WorkerNumbers: new[] { 1 }));
        game.AdvanceTick();
        int moves = Events(text, "WorkerMoved").Length;
        var before = game.GetWorkers()[0].GridPosition;
        var result = game.AdvanceTicks(5);
        var after = game.GetWorkers()[0].GridPosition;
        Require(result.QuietTicks == 5 && result.EventTicks == 0 && Events(text, "WorkerMoved").Length == moves + 1 && before != after,
            "quiet移动被拆逐秒或漏掉区间位置");
        string movement = Events(text, "WorkerMoved").Last();
        Require(movement.Contains("PositionBefore:") && movement.Contains("PositionAfter:") && !movement.Contains("TargetAnchor:"),
            "移动位置字段不是经营坐标");
        game.Log.Diagnostics.Stop();
        game.Log.Diagnostics.Start(new(new[] { "ProductionStateChanged" }, new[] { distant }));
        game.RemoveBuilding(distant);
        game.TryPlace(distant, BuildingKind.Farm, CropKind.Radish);
        game.AdvanceTicks(70);
        Require(!game.Log.Diagnostics.IsActive && Events(text, "ProductionStateChanged").Length == 0, "已失效实例范围复活到重建实例");
    }

    private static void CheckGatesAndFailureParity()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = Empty(log);
        // 不合法索引仅用于验证没有采集时门禁先于任何状态查询，正常业务不提交该索引。
        Require(game.Log!.ProductionDiagnostics.BeginFarm(int.MaxValue, new FarmingSystem(1)) == null && log.Health.FailureCount == 0,
            "关闭采集仍读取明细状态");
        game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
        game.Log.Diagnostics.Start(new(new[] { "ProductionStateChanged" }, new[] { Farm }, EventLimit: 1));
        Require(game.Log.ProductionDiagnostics.BeginFarm(int.MaxValue, new FarmingSystem(1)) == null && log.Health.FailureCount == 0,
            "未选中实例仍读取明细状态");
        game.AdvanceTick(true);
        Require(Events(text, "ProductionStateChanged").Length == 1 && !game.Log.Diagnostics.IsActive &&
            game.GetPlot(Farm).Crop == CropStage.Growing, "条数预算终止改变湿田业务或继续输出明细");

        using var writer = new FailingWriter();
        using var failingLog = RuntimeLog.Capture(writer, diagnostic: _ => { });
        using var traced = Empty(failingLog);
        using var plain = Empty();
        foreach (var current in new[] { traced, plain })
        {
            current.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            current.BuildProcessor(Processor, CropKind.Radish);
        }
        traced.Log!.Diagnostics.Start(new(new[] { "ProductionStateChanged", "WorkerTaskChanged", "WorkerMoved", "RuleChecked" },
            new[] { Farm, Processor }, WorkerNumbers: new[] { 1, 2, 3 }));
        writer.Fail = true;
        Require(traced.AdvanceTicks(500) == plain.AdvanceTicks(500) && traced.GetStock(Raw) == plain.GetStock(Raw) &&
            traced.GetProductStock(CropKind.Radish) == plain.GetProductStock(CropKind.Radish) &&
            traced.GetWorkers().SequenceEqual(plain.GetWorkers()), "诊断故障改变生产/工人/批量结果");
        writer.Fail = false;
    }

    private static void CheckClosedGateAllocations()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = Empty(log);
        game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
        var farming = new FarmingSystem(1);
        var processing = new ProcessingSystem(1);
        var inventory = new FarmExchange.Inventory.Inventory();
        ProductionDiagnostics production = game.Log!.ProductionDiagnostics;
        WorkerDiagnostics workers = game.Log.WorkerDiagnostics;
        CheckAllocation("关闭采集");
        game.Log.Diagnostics.Start(new(new[] { "ProductionStateChanged", "RuleChecked", "WorkerTaskChanged", "WorkerMoved" },
            new[] { Farm }, WorkerNumbers: new[] { 1 }));
        CheckAllocation("未选中对象");
        Require(Events(text, "ProductionStateChanged").Length == 0 && Events(text, "RuleChecked").Length == 0 &&
            Events(text, "WorkerTaskChanged").Length == 0 && Events(text, "WorkerMoved").Length == 0 && log.Health.FailureCount == 0,
            "关闭或不匹配门禁进入了详细状态读取");

        void CheckAllocation(string mode)
        {
            ExerciseGates(production, workers, farming, processing, inventory, 1000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            ExerciseGates(production, workers, farming, processing, inventory, 10000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(allocated == 0, mode + "的诊断门禁发生托管分配：" + allocated);
        }
    }

    private static void ExerciseGates(ProductionDiagnostics production, WorkerDiagnostics workers, FarmingSystem farming,
        ProcessingSystem processing, FarmExchange.Inventory.Inventory inventory, int count)
    {
        var farmBefore = new FarmSnapshot(CropKind.Radish, CropStage.Seeded, 0);
        var processorBefore = new ProcessorDiagnosticState(CropKind.Radish, ProcessorStatus.Processing, 1);
        var idle = new WorkerTaskDiagnosticState(WorkerActivity.Idle, null);
        var moving = new WorkerTaskDiagnosticState(WorkerActivity.Moving, 0);
        for (int i = 0; i < count; i++)
        {
            production.BeginFarm(0, farming);
            production.BeginProcessor(0, processing, inventory);
            production.Sown(0, farmBefore, farming);
            production.Processed(0, processorBefore, processing, inventory, 1);
            production.PlantingChecked(0, CropKind.Radish, PlantingFailure.None);
            workers.TaskChanged(2, idle, moving);
            workers.Moved(2, Vector2.Zero, Vector2.One);
        }
    }

    private static string[] Events(StringWriter text, string name) => text.ToString().Split('\n').Where(line => line.Contains("EventName=" + name + " ")).ToArray();
    private static bool Transition(string line, string name, string before, string after) =>
        line.Contains("Transition: \"" + name + "\"") && line.Contains("StateBefore: \"" + before + "\", StateAfter: \"" + after + "\"");
    private static long Number(string line, string field)
    {
        Match match = Regex.Match(line, @"\b" + field + @": (\d+)");
        Require(match.Success, "缺少字段 " + field);
        return long.Parse(match.Groups[1].Value);
    }
    private static long CropCount(string line, string field)
    {
        Match match = Regex.Match(line, field + @": \{ ([^}]+) \}");
        Require(match.Success, "缺少作物汇总 " + field);
        return Number(match.Groups[1].Value, "Radish");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FailingWriter : StringWriter
    {
        internal bool Fail;
        public override void Write(string? value)
        {
            if (Fail) throw new IOException("详细诊断目标故障");
            base.Write(value);
        }
    }
}
