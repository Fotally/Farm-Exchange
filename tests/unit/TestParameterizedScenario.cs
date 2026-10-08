using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
#if DEBUG
using FarmExchange.Development;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;
using FarmExchange.Trading;
#endif

public partial class TestParameterizedScenario : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
#if DEBUG
        string directory = Path.Combine(Path.GetTempPath(), "farm-parameterized-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            return CheckConfiguration(directory) && CheckIndependent(directory) && CheckCurrent(directory) &&
                CheckBoundaries(directory) && CheckFailures(directory) && CheckReports(directory);
        }
        catch (Exception error)
        {
            return Fail(error.ToString());
        }
        finally
        {
            // 本测试仅删除自己创建并验证的唯一临时目录。
            if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, true);
        }
#else
        return true;
#endif
    }

#if DEBUG
    private static string Config(string target = "independent", int quantity = 1, int processingLimit = 27,
        string date = "01-02-01", string crop = "Radish.Raw", string anchor = "0") =>
        "{\"schemaVersion\":1,\"caseId\":\"test\",\"revision\":1,\"flow\":\"buy-process-sell\"," +
        "\"run\":{\"target\":\"" + target + "\"" + (target == "independent" ? ",\"seed\":12345" : "") + "}," +
        "\"execution\":{\"timePlan\":[{\"endDate\":\"" + date + "\",\"rate\":1}]}," +
        "\"parameters\":{\"rawCommodity\":\"" + crop + "\",\"quantity\":" + quantity +
        ",\"processorAnchor\":{\"x\":" + anchor + ",\"y\":0},\"processingWaitLimitTicks\":" + processingLimit + ",\"orderWaitLimitTicks\":1}}";

    private static ScenarioConfiguration Load(string directory, string json)
    {
        string path = Path.Combine(directory, "case.json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return ScenarioConfiguration.Load(path);
    }

    private static BuyProcessSellScenario Start(string directory, ScenarioConfiguration configuration,
        FarmGame? current = null, int? actualSeed = null) => BuyProcessSellScenario.Start(configuration,
            ScenarioReport.CreateRunDirectory(Path.Combine(directory, "runs")), current, actualSeed);

    private static bool CheckConfiguration(string directory)
    {
        string valid = Config();
        var invalid = new List<string>
        {
            "[]", "null", "{", valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            valid.Replace("buy-process-sell", "unknown"), valid.Replace("\"caseId\":\"test\"", "\"caseId\":\" \""),
            valid.Replace("\"revision\":1", "\"revision\":0"), valid.Replace("\"revision\":1", "\"revision\":1.5"),
            valid.Replace("\"revision\":1", "\"revision\":\"1\""), valid.Replace("\"quantity\":1", "\"quantity\":0"),
            valid.Replace("\"quantity\":1", "\"quantity\":2147483648"), valid.Replace("Radish.Raw", "Wheat.Product"),
            valid.Replace("Radish.Raw", "0.Raw"), valid.Replace("\"seed\":12345", "\"seed\":null"),
            valid.Replace(",\"seed\":12345", ""), valid.Replace("independent", "unknown"),
            valid.Replace("independent", "current"), valid.Replace("\"x\":0", "\"x\":0.5"),
            valid.Replace("\"processingWaitLimitTicks\":27", "\"processingWaitLimitTicks\":0"),
            valid.Replace("\"orderWaitLimitTicks\":1", "\"orderWaitLimitTicks\":-1"),
            valid.Replace("\"rate\":1", "\"rate\":1.5"), valid.Replace("\"rate\":1", "\"rate\":0"),
            valid.Replace("\"rate\":1", "\"rate\":1e999"), valid.Replace("\"rate\":1", "\"rate\":\"1\""),
            valid.Replace("[{\"endDate\":\"01-02-01\",\"rate\":1}]", "[]"),
            valid.Replace("[{\"endDate\":\"01-02-01\",\"rate\":1}]", "{}"),
            valid.Replace("\"run\":{", "\"run\":{\"typo\":1,"),
            valid.Replace("\"x\":0", "\"x\":0,\"x\":1"),
            valid.Replace("\"quantity\":1", "\"quantity\":1,\"quantity\":2"),
            valid.Replace("\"revision\":1", "\"revision\":1,\"revision\":2"),
            valid.Replace("\"rate\":1", "\"rate\":1,\"rate\":2"),
            valid.Replace("\"rawCommodity\":\"Radish.Raw\",", ""),
            valid.Replace("\"execution\":{", "\"execution\":{\"steps\":[],"),
            valid.Replace("\"parameters\":{", "\"parameters\":{\"unknown\":1,"),
            valid.Replace("\"revision\":1", "\"revision\":1,\"unknown\":1"),
            valid.Replace("\"endDate\":\"01-02-01\",\"rate\":1", "\"endDate\":\"01-02-01\",\"rate\":1},{\"endDate\":\"01-02-01\",\"rate\":2"),
        };
        foreach (string date in new[] { "00-01-01", "01-13-01", "01-02-29", "01-00-01", "01-01-00", "1-02-01", "01/02/01", "ab-02-01", "01-01-01" })
            invalid.Add(valid.Replace("01-02-01", date));
        foreach (string json in invalid)
        {
            try { Load(directory, json); return Fail("严格校验接受了非法参数：" + json); }
            catch (ScenarioConfigurationException) { }
        }
        ScenarioConfiguration config = Load(directory, valid.Replace("\"rate\":1", "\"rate\":0.5"));
        byte[] original = File.ReadAllBytes(config.SourcePath);
        string hash = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        File.WriteAllText(config.SourcePath, Config(quantity: 3));
        if (config.Quantity != 1 || config.TimePlan[0].Rate != 0.5 || config.Sha256 != hash || config.Seed != 12345)
            return Fail("运行参数或原始摘要随文件变化漂移");
        byte[] bom = new UTF8Encoding(true).GetPreamble();
        byte[] body = Encoding.UTF8.GetBytes(valid);
        byte[] withBom = new byte[bom.Length + body.Length];
        bom.CopyTo(withBom, 0); body.CopyTo(withBom, bom.Length);
        File.WriteAllBytes(config.SourcePath, withBom);
        if (ScenarioConfiguration.Load(config.SourcePath).Sha256 != Convert.ToHexString(SHA256.HashData(withBom)).ToLowerInvariant())
            return Fail("SHA-256没有使用原始UTF-8字节");
        File.WriteAllBytes(config.SourcePath, new byte[] { 0xFF });
        try { ScenarioConfiguration.Load(config.SourcePath); return Fail("非法UTF-8未拒绝"); }
        catch (ScenarioConfigurationException) { }
        try { ScenarioConfiguration.Load(Path.Combine(directory, "missing.json")); return Fail("不存在文件未拒绝"); }
        catch (ScenarioConfigurationException) { }
        return true;
    }

    private static SimulationDriver Driver(BuyProcessSellScenario scenario)
    {
        var driver = new SimulationDriver();
        driver.RateChanged += (rate, source) =>
        {
            scenario.ObserveTime(rate, source.ToString(), scenario.Game.IsPaused);
            if (source == SimulationRateSource.Player)
                scenario.Abort("玩家手动改速");
        };
        driver.SetDevelopmentRate(scenario.CurrentRateIntent, SimulationRateSource.Scenario);
        return driver;
    }

    private static void Advance(SimulationDriver driver, BuyProcessSellScenario scenario, double seconds = 10000)
    {
        driver.Advance(seconds, scenario.Game, checkpoint =>
        {
            bool continuing = scenario.ObserveCheckpoint(checkpoint);
            if (scenario.IsRunning && driver.Rate != scenario.CurrentRateIntent)
                driver.SetDevelopmentRate(scenario.CurrentRateIntent, SimulationRateSource.Scenario);
            return continuing;
        }, scenario.GetMaxAdvanceTicks);
    }

    private static bool CheckIndependent(string directory)
    {
        foreach (string name in new[] { "wheat-basic-r1.json", "radish-three-r1.json" })
        {
            ScenarioConfiguration config = ScenarioConfiguration.Load(ProjectSettings.GlobalizePath("res://tests/scenario-configs/buy-process-sell/" + name));
            BuyProcessSellScenario scenario = Start(directory, config);
            var driver = Driver(scenario);
            Advance(driver, scenario);
            if (scenario.Outcome != ScenarioOutcome.Passed || scenario.Report.Produced != config.Quantity ||
                scenario.Report.Final!.Product.Total != 0 || scenario.Report.Final.BuildingCount != 1 ||
                scenario.Report.OrderWaitTicks != 1 || scenario.Report.ActualSeed != config.Seed || driver.Rate != config.TimePlan[^1].Rate)
                return Fail("独立Q份流程或实际配置倍率未通过：" + scenario.Progress);
            foreach (ScenarioCheck check in scenario.Report.Checks)
                if (check.Status != ScenarioCheckStatus.Passed)
                    return Fail("独立必需检查非严格通过：" + check.Name);
            uint count = 0;
            foreach (ScenarioInterval interval in scenario.Report.Intervals)
                count += interval.AdvancedTicks;
            if (count != scenario.Report.AdvancedTicks)
                return Fail("报告真实区间计数不等于实际完成tick");
        }
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            var scenario = Start(directory, Load(directory, Config(quantity: 2, processingLimit: 1800, crop: crop.Kind + ".Raw")));
            Advance(Driver(scenario), scenario);
            if (scenario.Outcome != ScenarioOutcome.Passed || scenario.Report.Produced != 2 || scenario.Game.GetBuildingSpaces().Count != 1)
                return Fail("七原料Q=2共用流程未通过：" + crop.Kind + " " + scenario.Progress);
        }
        return true;
    }

    private static FarmGame Current(uint ticks = 0)
    {
        var game = new FarmGame(12345, ticks);
        var cells = new List<Vector2I>();
        foreach (var space in game.GetBuildingSpaces()) cells.Add(space.AnchorCell);
        foreach (var cell in cells) game.RemoveBuilding(cell);
        if (game.BuildProcessor(Vector2I.Zero, CropKind.Radish) != null)
            throw new InvalidOperationException("现场夹具建造失败");
        return game;
    }

    private static bool CheckCurrent(string directory)
    {
        FarmGame game = Current();
        game.SetPaused(true);
        ScenarioConfiguration config = Load(directory, Config("current"));
        BuyProcessSellScenario scenario = Start(directory, config, game);
        var driver = Driver(scenario);
        Advance(driver, scenario);
        if (scenario.Report.AdvancedTicks != 0 || scenario.Report.ProcessingWaitTicks != 0 || !game.IsPaused || !scenario.IsRunning)
            return Fail("现场暂停消耗了预算或被解除");
        game.SetPaused(false);
        Advance(driver, scenario);
        if (scenario.Outcome != ScenarioOutcome.CompletedWithInsufficientEvidence || scenario.Report.ActualSeed != null)
            return Fail("现场被伪报严格通过或未知种子被伪造");

        game = Current();
        game.Buy(new CommodityId(CropKind.Radish, CommodityKind.Product), 1);
        game.SetRawReserve(CropKind.Radish, 100);
        scenario = Start(directory, config, game);
        Advance(Driver(scenario), scenario);
        if (scenario.Outcome != ScenarioOutcome.CompletedWithInsufficientEvidence || scenario.Report.Produced != 0 || game.GetRawReserve(CropKind.Radish) != 100)
            return Fail("现场已有产品被伪认作目标加工产出");

        game = Current();
        game.Buy(new CommodityId(CropKind.Radish, CommodityKind.Raw), 1);
        game.AdvanceTick(); // 现场原有批次，由正式经营领取。
        game.CreateTradeOrder(new TradeOrderRequest(new CommodityId(CropKind.Wheat, CommodityKind.Raw), TradeOrderSide.Buy,
            TradeOrderFrequency.Continuous, TradeOrderQuantityMode.Fixed, 1, TradeOrderBudgetMode.None, 0, 0,
            CashReserveMode.Amount, 0, new IReadOnlyList<TradeOrderCondition>[] { new[] {
                new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Less, 0) } }));
        scenario = Start(directory, config, game, 12345);
        driver = Driver(scenario);
        Advance(driver, scenario);
        if (scenario.Outcome != ScenarioOutcome.CompletedWithInsufficientEvidence || scenario.Report.Baseline!.Orders.Count != 1 ||
            scenario.Report.Baseline.Processor.Status != ProcessorStatus.Processing || game.GetTradeOrders().Count != 2)
            return Fail("现场已有批次或他单被清理/未记录");

        game = Current();
        scenario = Start(directory, config, game);
        driver = Driver(scenario);
        Advance(driver, scenario, 27);
        if (scenario.Stage != ScenarioStage.WaitingForOrder)
            return Fail("手改速夹具未到本单等待阶段");
        driver.SetRate(2, SimulationRateSource.Player);
        uint stopped = game.Calendar.ElapsedSeconds;
        Advance(driver, scenario);
        if (scenario.Outcome != ScenarioOutcome.Aborted || driver.Rate != 2 || game.Calendar.ElapsedSeconds != stopped ||
            game.GetTradeOrders()[0].Status != TradeOrderStatus.Waiting)
            return Fail("手改速没有终止全部流程或回滚了倍率/订单");
        game.AdvanceTick();
        if (game.GetTradeOrders()[0].Status != TradeOrderStatus.Completed || scenario.Report.Final!.Orders[0].Status != TradeOrderStatus.Waiting)
            return Fail("中止改变真实订单或最终报告快照继续漂移");
        return true;
    }

    private static bool CheckBoundaries(string directory)
    {
        ScenarioConfiguration config = Load(directory, Config("current", date: "01-01-02"));
        var scenario = Start(directory, config, Current(24));
        Advance(Driver(scenario), scenario);
        if (scenario.Outcome != ScenarioOutcome.CompletedWithInsufficientEvidence || scenario.Report.Final!.Calendar.ElapsedSeconds != 52)
            return Fail("末日期已有本单成交未获准完成");
        scenario = Start(directory, config, Current(25));
        Advance(Driver(scenario), scenario);
        if (scenario.Outcome != ScenarioOutcome.TimeRangeExhausted || scenario.Report.OrderId != null || scenario.Game.GetTradeOrders().Count != 0)
            return Fail("末日期产品达到Q仍创建新单");
        scenario = Start(directory, config, Current(52));
        if (scenario.Outcome != ScenarioOutcome.PreconditionsRejected || scenario.Report.Operations.Count != 0)
            return Fail("日期已过仍执行买入");
        scenario = Start(directory, Load(directory, Config(processingLimit: 26)));
        Advance(Driver(scenario), scenario);
        if (scenario.Outcome != ScenarioOutcome.WaitLimitExceeded || scenario.Report.ProcessingWaitTicks != 26)
            return Fail("加工预算未按完整tick限制");
        scenario = Start(directory, Load(directory, Config(processingLimit: 27)));
        Advance(Driver(scenario), scenario);
        return scenario.Outcome == ScenarioOutcome.Passed && scenario.Report.ProcessingWaitTicks == 27 || Fail("恰好预算最后tick达Q未通过");
    }

    private static bool CheckFailures(string directory)
    {
        var scenario = Start(directory, Load(directory, Config(quantity: 1000)));
        if (scenario.Outcome != ScenarioOutcome.OperationRejected || scenario.Game.GetRawStock(CropKind.Radish) != 0 || scenario.Game.MoneyCents != 4000)
            return Fail("正式买入拒绝后补资源或继续操作");
        scenario = Start(directory, Load(directory, Config(anchor: "383")));
        if (scenario.Outcome != ScenarioOutcome.PreconditionsRejected || scenario.Report.Operations.Count != 0)
            return Fail("独立场地非法占地未正常拒绝");
        ScenarioConfiguration current = Load(directory, Config("current"));
        var game = new FarmGame(12345);
        scenario = Start(directory, current, game);
        if (scenario.Outcome != ScenarioOutcome.PreconditionsRejected || scenario.Report.Operations.Count != 0)
            return Fail("现场目标不存在仍买入");
        game = Current();
        game.SetRawReserve(CropKind.Radish, 100);
        scenario = Start(directory, current, game);
        Advance(Driver(scenario), scenario);
        if (scenario.Outcome != ScenarioOutcome.WaitLimitExceeded || game.GetRawReserve(CropKind.Radish) != 100)
            return Fail("现场底线被自动修复或预算重置");
        game = Current();
        scenario = Start(directory, current, game);
        game.RemoveBuilding(Vector2I.Zero);
        Advance(Driver(scenario), scenario, 1);
        if (scenario.Outcome != ScenarioOutcome.PreconditionsRejected)
            return Fail("运行中目标移除未停止");
        game = Current();
        scenario = Start(directory, current, game);
        var driver = Driver(scenario);
        Advance(driver, scenario, 27);
        game.CancelTradeOrder(scenario.Report.OrderId!.Value);
        Advance(driver, scenario, 1);
        if (scenario.Outcome != ScenarioOutcome.OperationRejected || game.GetTradeOrders().Count != 1)
            return Fail("本单撤销后自动重建");
        game = Current();
        scenario = Start(directory, current, game);
        driver = Driver(scenario);
        Advance(driver, scenario, 27);
        TradeOrderSnapshot existing = game.GetTradeOrders()[0];
        var changed = existing.Request with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] { new[] {
                new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Greater, int.MaxValue) } }
        };
        if (!game.UpdateTradeOrder(existing.Id, changed).Success)
            return Fail("本单编辑夹具被拒绝");
        Advance(driver, scenario, 1);
        if (scenario.Outcome != ScenarioOutcome.OperationRejected || game.GetTradeOrders()[0].Request.ConditionGroups[0][0] != changed.ConditionGroups[0][0])
            return Fail("本单编辑后流程恢复了旧配置");
        scenario = Start(directory, current, Current());
        scenario.ObserveTime(1, "Scenario", true);
        scenario.Abort("人工中止");
        scenario.Abort("第二次");
        if (scenario.Outcome != ScenarioOutcome.Aborted || scenario.Report.Reason != "人工中止" || scenario.GetMaxAdvanceTicks() != 0)
            return Fail("中止未保留首次稳定结果");
        return true;
    }

    private static bool CheckReports(string directory)
    {
        string root = ScenarioReport.ResolveRoot(directory, Path.Combine(directory, "FarmExchange.exe"), false);
        if (root != Path.Combine(directory, "build", "test-runs") ||
            ScenarioReport.ResolveRoot("ignored", Path.Combine(directory, "FarmExchange.exe"), true) != Path.Combine(directory, "reports", "test-runs") ||
            ScenarioReport.ResolveRoot("ignored", Path.Combine(directory, "Game.app", "Contents", "MacOS", "FarmExchange"), true) != Path.Combine(directory, "reports", "test-runs"))
            return Fail("报告路径依赖工作目录或写进.app");
        string output = ScenarioReport.CreateRunDirectory(root);
        var scenario = BuyProcessSellScenario.Start(Load(directory, Config()), output);
        try { scenario.WriteReport(); return Fail("运行中写了最终报告"); }
        catch (InvalidOperationException) { }
        Advance(Driver(scenario), scenario);
        int money = scenario.Report.Final!.BalanceCents;
        scenario.Game.Buy(new CommodityId(CropKind.Radish, CommodityKind.Raw), 1);
        string path = scenario.WriteReport();
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement report = json.RootElement;
        if (Directory.GetFiles(output).Length != 1 || Path.GetFileName(path) != "report.json" ||
            report.GetProperty("configuration").GetProperty("sha256").GetString() != scenario.Report.Configuration.Sha256 ||
            report.GetProperty("configuration").TryGetProperty("parameters", out _) ||
            report.GetProperty("configurationValidation").GetString() != "Passed" ||
            report.GetProperty("initialization").GetArrayLength() != 7 || scenario.Report.Baseline!.BalanceCents != 4000 ||
            scenario.Report.Baseline.BuildingCount != 1 || scenario.Report.Baseline.RawReserve != 0 ||
            report.GetProperty("initialization")[5].GetProperty("result").GetProperty("spentCents").GetInt32() != FarmGame.BuildingCostCents ||
            report.GetProperty("final").GetProperty("balanceCents").GetInt32() != money)
            return Fail("报告非唯一JSON、复制参数或落盘时重读现场");
        try { scenario.WriteReport(); return Fail("输出冲突没有显式报错"); }
        catch (IOException) { }
        string blocked = Path.Combine(directory, "blocked");
        File.WriteAllText(blocked, "file");
        try { ScenarioReport.CreateRunDirectory(blocked); return Fail("不可写输出路径被静默替换"); }
        catch (IOException) { }
        return true;
    }
#endif

    private static bool Fail(string message)
    {
        GD.PrintErr("参数化流程检查失败：" + message);
        return false;
    }
}
