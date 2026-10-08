using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Trading;

public static class TestProductionLogging
{
    private static readonly CommodityId Raw = new(CropKind.Radish, CommodityKind.Raw);
    private static readonly CommodityId Product = new(CropKind.Radish, CommodityKind.Product);
    private static readonly Vector2I Farm = new(189, 189);
    private static readonly Vector2I Processor = new(189, 192);

    public static bool RunChecks()
    {
        try
        {
            CheckCrossWindowAndPausedClaims();
            CheckWorkRainAndSeason();
            CheckBatchAndNestedCommands();
            CheckCoverageAndFailures();
            CheckInitialClockFailure();
            CheckIsolationAndFile();
            CheckManualBinding();
            return true;
        }
        catch (Exception error) { GD.PrintErr("生产日志测试失败：" + error); return false; }
    }

    private static FarmGame Empty(RuntimeLog? logging = null, uint second = 0)
    {
        var game = new FarmGame(12345, second, logging);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        return game;
    }

    private static void CheckCrossWindowAndPausedClaims()
    {
        long now = 0;
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text, () => now);
        using var game = Empty(logging);
        game.SetPaused(true);
        Require(game.BuildProcessor(Processor, CropKind.Radish) == null, "准备空闲加工场地失败");
        Require(game.Buy(Raw, 4).Success, "买入原料失败");
        game.SetRawReserve(CropKind.Radish, 4);
        Require(game.BuildProcessor(new(192, 192), CropKind.Radish) == null, "准备第二场地失败");
        Require(game.GetStock(Raw) == 4, "底线未阻挡开工");
        game.SetRawReserve(CropKind.Radish, 0);
        now = 60_000;
        Require(game.BuildProcessor(new(195, 192), CropKind.Radish) == null, "暂停新建失败");
        string first = Events(text).Single();
        Require(Value(first, "RawConsumedByCrop", "Radish") == 3 && Value(first, "ProducedByCrop", "Radish") == 0,
            "暂停新建触发的全场领取没有独立记实际投入");
        Require(Stock(first, "Start", Raw) == 0 && Stock(first, "End", Raw) == 1 && Number(first, "SimulationSecondsEnd") == 0,
            "暂停窗口边界或库存错误");
        Require(Number(first, "MoneyStartCents") - Number(first, "MoneyEndCents") == 3000 + 4 * game.GetQuote(Raw).PriceCents,
            "建造与买入资金边界错误");
        Require(game.CreateTradeOrder(new TradeOrderRequest(Raw, TradeOrderSide.Sell, TradeOrderFrequency.Once,
            TradeOrderQuantityMode.Fixed, 1, TradeOrderBudgetMode.None, 0, 0, CashReserveMode.Amount, 0,
            new IReadOnlyList<TradeOrderCondition>[] { new[] { new TradeOrderCondition(TradeConditionFactor.Price,
                TradeConditionComparison.LessOrEqual, 0) } })).Success, "原料冻结准备失败");
        Require(game.GetFrozenStock(Raw) == 1, "原料没有冻结");
        game.SetPaused(false);
        now = 120_000;
        game.AdvanceTicks(26);
        string second = Events(text).Last();
        Require(Value(second, "RawConsumedByCrop", "Radish") == 0 && Value(second, "ProducedByCrop", "Radish") == 3,
            "跨窗完工被倒算为当窗投入");
        Require(Stock(second, "Start", Raw) == Stock(first, "End", Raw) && Stock(second, "End", Product) == 3,
            "期末未复用为下窗期初");
        Require(game.Sell(Product, 1).Success && game.Buy(Product, 2).Success && game.SellAll().Quantity == 4,
            "商品买卖准备失败");
        game.Dispose();
        string tail = Events(text).Last();
        Require(Stock(tail, "Start", Product) == 3 && Stock(tail, "End", Product) == 0 &&
            Value(tail, "ProducedByCrop", "Radish") == 0 && tail.Contains("IsPartialWindow: true"), "尾段交易或窗口重复计产出");
        foreach (string line in Events(text))
        {
            Require(line.Contains("CoverageStatus: \"Complete\""), "真实路径不完整");
            foreach (var crop in FarmGame.Crops)
            {
                Stock(line, "Start", new(crop.Kind, CommodityKind.Raw));
                Stock(line, "End", new(crop.Kind, CommodityKind.Product));
                Value(line, "HarvestedByCrop", crop.Kind.ToString());
            }
        }
    }

    private static void CheckWorkRainAndSeason()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using (var game = Empty(logging))
        {
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.AdvanceTicks(2);
            game.AdvanceTick(true);
            game.AdvanceTick(true);
        }
        string work = Events(text).Single();
        Require(Number(work, "SownCount") == 1 && Number(work, "WateredCount") == 1, "成功工人次数错误或降雨重复计水");
        text.GetStringBuilder().Clear();
        using (var game = Empty(logging))
        {
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.AdvanceTick();
            game.AdvanceTick(true);
        }
        Require(Number(Events(text).Single(), "WateredCount") == 0, "降雨被当成工人供水");
        text.GetStringBuilder().Clear();
        using (var game = Empty(logging, 4319))
        {
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.AdvanceTick();
        }
        string clear = Events(text).Single();
        Require(Value(clear, "ClearedByCrop", "Radish") == 1 && Value(clear, "HarvestedByCrop", "Radish") == 0 &&
            Stock(clear, "End", Raw) == 0, "越季清理没有按实际轮次记录");
        text.GetStringBuilder().Clear();
        using (var game = Empty(logging, 4120))
        {
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.AdvanceTicks(200);
            Require(game.GetPresentationResults().Any(result => result.Kind == ProductionResultKind.Harvest && result.Quantity == 6) &&
                game.GetPresentationResults().Any(result => result.Kind == ProductionResultKind.Sow),
                "换季事件秒没有先促熟收获再由工人播下新一轮");
        }
        string rescue = Events(text).Single();
        // 促熟在工人阶段前完成；同秒重新播种的新轮次由随后换季清理，不能把它误认成收获轮次重复清理。
        Require(Value(rescue, "HarvestedByCrop", "Radish") == 6 && Value(rescue, "ClearedByCrop", "Radish") == 1 &&
            Number(rescue, "SownCount") == 2 && Number(rescue, "WateredCount") == 1 && Stock(rescue, "End", Raw) == 6,
            "促熟收获与同秒新播轮次清理不符：" + rescue);
    }

    private static void CheckBatchAndNestedCommands()
    {
        using var oneText = new StringWriter();
        using var batchText = new StringWriter();
        using var oneLog = RuntimeLog.Capture(oneText);
        long now = 0;
        using var batchLog = RuntimeLog.Capture(batchText, () => now);
        using var one = Empty(oneLog);
        using var batch = Empty(batchLog);
        using var plain = Empty();
        foreach (var game in new[] { one, batch, plain })
        {
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.BuildProcessor(Processor, CropKind.Radish);
        }
        for (int i = 0; i < 500; i++) one.AdvanceTick();
        var expected = plain.AdvanceTicks(500);
        var actual = batch.AdvanceTicks(500, _ =>
        {
            now = 60_000;
            Require(batch.SetRawReserve(CropKind.Wheat, -1) == RawReserveFailure.InvalidQuantity, "检查点拒绝夹具错误");
            Require(Events(batchText).Length == 0, "检查点内部命令拆分了长批次窗口");
            return true;
        });
        Require(expected == actual && actual.QuietTicks > 0, "日志改变批量或拆散quiet");
        one.Dispose();
        string oneSummary = Events(oneText).Single();
        string batchSummary = Events(batchText).Single();
        foreach (string field in new[] { "HarvestedByCrop", "RawConsumedByCrop", "ProducedByCrop", "ClearedByCrop" })
            foreach (var crop in FarmGame.Crops)
                Require(Value(oneSummary, field, crop.Kind.ToString()) == Value(batchSummary, field, crop.Kind.ToString()), "单秒批量流量不同");
        Require(Number(oneSummary, "SownCount") == Number(batchSummary, "SownCount") &&
            Number(oneSummary, "WateredCount") == Number(batchSummary, "WateredCount"), "单秒批量工人计数不同");
        Require(Stock(batchSummary, "End", Raw) == Value(batchSummary, "HarvestedByCrop", "Radish") - Value(batchSummary, "RawConsumedByCrop", "Radish") &&
            Stock(batchSummary, "End", Product) == Value(batchSummary, "ProducedByCrop", "Radish"), "生产库存等式不成立");
        Require(Number(batchSummary, "SimulationSecondsEnd") == 500, "批次未整体归窗");
        var stopped = batch.AdvanceTicks(100, _ =>
        {
            now = 120_000;
            Require(batch.Buy(Raw, 1).Success && Events(batchText).Length == 1, "实际修改命令在批次内封窗");
            return false;
        });
        Require(stopped.StoppedAtCheckpoint && Events(batchText).Length == 2, "检查点停止后未在真实外层终点封窗");
    }

    private static void CheckCoverageAndFailures()
    {
        long now = 0;
        bool clockFails = false;
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text, () => clockFails ? throw new IOException("测试时钟故障") : now, _ => { });
        using var game = Empty(logging);
        clockFails = true;
        Require(game.Buy(Raw, 1).Success, "采集故障改变真实买入");
        clockFails = false;
        now = 60_000;
        game.AdvanceTicks(1);
        Require(Events(text).Single().Contains("CoverageStatus: \"Partial\"") && Events(text).Single().Contains("CollectorFailure"),
            "采集故障仍声称完整");
        game.FillWorldForBenchmark();
        now = 120_000;
        game.AdvanceTicks(1);
        Require(Events(text).Last().Contains("UnregisteredMutation"), "夹具未登记修改没有降低覆盖");
        now = 180_000;
        game.AdvanceTicks(1);
        Require(Events(text).Last().Contains("CoverageStatus: \"Complete\""), "新真实边界之后未恢复本区间覆盖");
        try { game.AdvanceTicks(1, _ => throw new InvalidOperationException("原业务异常")); }
        catch (InvalidOperationException error) { Require(error.Message == "原业务异常", "异常被替换"); }
        game.Dispose();
        Require(Events(text).Last().Contains("BoundaryMissing"), "非完整推进边界被称为完整");
        int count = Events(text).Length;
        game.Dispose();
        Require(Events(text).Length == count, "重复关闭生成重复尾段");

        using var failingWriter = new ToggleWriter();
        using var failedLog = RuntimeLog.Capture(failingWriter, diagnostic: _ => { });
        using var failedGame = Empty(failedLog);
        failingWriter.Fail = true;
        Require(failedGame.Buy(Raw, 1).Success, "输出故障改变业务");
        failingWriter.Fail = false;
        failedGame.Dispose();
        Require(Events(failingWriter).Single().Contains("CoverageStatus: \"Complete\""), "输出缺失被混同为进程内生产累计缺失");
    }

    private static void CheckInitialClockFailure()
    {
        bool clockFails = true;
        long now = 0;
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text, () => clockFails ? throw new IOException("首次窗口时钟故障") : now, _ => { });
        using var game = new FarmGame(12345, 4120, logging);
        clockFails = false;
        Require(game.Buy(Raw, 1).Success, "首次时钟失败影响真实业务");
        string first = Events(text).Single();
        Require(first.Contains("IntervalStartUptimeMs: null") && Number(first, "IntervalEndUptimeMs") == 0 &&
            Number(first, "SimulationSecondsStart") == 4120 && Number(first, "SimulationSecondsEnd") == 4120 &&
            Stock(first, "Start", Raw) == 0 && Stock(first, "End", Raw) == 1 &&
            first.Contains("CoverageStatus: \"Partial\"") && first.Contains("CollectorFailure") && first.Contains("BoundaryMissing"),
            "首次时钟失败用0冒充未知起点或丢失真实日历/库存边界：" + first);
        now = 60_000;
        Require(game.Buy(Raw, 1).Success, "时钟恢复后的业务失败");
        string second = Events(text).Last();
        Require(Events(text).Length == 2 && Number(second, "IntervalStartUptimeMs") == 0 &&
            Number(second, "IntervalEndUptimeMs") == 60_000 && Number(second, "SimulationSecondsStart") == 4120 &&
            Stock(second, "Start", Raw) == 1 && Stock(second, "End", Raw) == 2 &&
            second.Contains("CoverageStatus: \"Complete\"") && second.Contains("CoverageReasons: []"),
            "新真实起点没有恢复后续区间覆盖：" + second);
    }

    private static void CheckIsolationAndFile()
    {
        using var text = new StringWriter();
        using (var logging = RuntimeLog.Capture(text))
        {
            var first = Empty(logging);
            var second = Empty(logging);
            first.Buy(Raw, 2);
            second.Buy(Product, 3);
            first.Dispose();
            logging.Dispose();
            second.Dispose();
        }
        string[] summaries = Events(text);
        Require(summaries.Length == 2 && summaries.All(line => Number(line, "WindowId") == 1) &&
            Stock(summaries[0], "End", Raw) == 2 && Stock(summaries[1], "End", Product) == 3, "多局窗口或关闭尾段串局");
        Require(text.ToString().LastIndexOf("EventName=GameEnded", StringComparison.Ordinal) <
            text.ToString().LastIndexOf("EventName=SessionEnded", StringComparison.Ordinal), "关闭顺序错误");
        string path = Path.GetFullPath(Path.Combine("build", "test-results", "logging", "production-" + Guid.NewGuid().ToString("N")));
        using (var file = RuntimeLog.OpenFile(path, true, new LogEnvironment()))
        using (var game = Empty(file))
        {
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.BuildProcessor(Processor, CropKind.Radish);
            game.AdvanceTicks(500);
        }
        string runtime = string.Concat(Directory.GetFiles(Path.Combine(path, "runtime"), "*.log").Select(File.ReadAllText));
        string debug = string.Concat(Directory.GetFiles(Path.Combine(path, "debug"), "*.log").Select(File.ReadAllText));
        string line = runtime.Split('\n').Single(item => item.Contains("EventName=ProductionSummary "));
        Require(debug.Contains(line) && !runtime.Contains("EventName=ProductionStateChanged"), "真实File汇总分流错误或默认写逐实例生产");
    }

    private static void CheckManualBinding()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = Empty();
        using (var context = logging.BindGame(game, 12345))
        {
            var observation = context!.Trading.BeginBuy(Raw, 1);
            observation!.Complete(game.Buy(Raw, 1));
            game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish);
            game.AdvanceTicks(500);
        }
        Require(Events(text).Length == 0 && text.ToString().Contains("EventName=TradeFinished "),
            "手动领域观察被错误宣称为自动生产完整覆盖");
    }

    private static string[] Events(StringWriter text) => text.ToString().Split('\n').Where(line => line.Contains("EventName=ProductionSummary ")).ToArray();
    private static long Number(string text, string key)
    {
        Match match = Regex.Match(text, @"\b" + key + @": (\d+)");
        Require(match.Success, "缺少字段 " + key);
        return long.Parse(match.Groups[1].Value);
    }
    private static long Value(string text, string field, string crop)
    {
        Match match = Regex.Match(text, field + @": \{ ([^}]+) \}");
        Require(match.Success, "缺少作物流量 " + field);
        return Number(match.Groups[1].Value, crop);
    }
    private static long Stock(string text, string suffix, CommodityId commodity)
    {
        string section = text.Split("Inventory" + suffix + ": ")[1].Split("Money" + suffix + "Cents:")[0];
        Match match = Regex.Match(section, "\"?" + commodity.Crop + @"\." + commodity.Kind + "\"?: \\{ Total: (\\d+), Available: (\\d+), Frozen: (\\d+) \\}");
        Require(match.Success, "缺少十四商品边界 " + commodity);
        Require(long.Parse(match.Groups[1].Value) == long.Parse(match.Groups[2].Value) + long.Parse(match.Groups[3].Value), "库存边界可用冻结关系错误");
        return long.Parse(match.Groups[1].Value);
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private sealed class ToggleWriter : StringWriter
    {
        internal bool Fail;
        public override void Write(string? value)
        {
            if (Fail) throw new IOException("测试输出故障");
            base.Write(value);
        }
    }
}
