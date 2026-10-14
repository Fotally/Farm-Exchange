using System;
using System.Linq;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Trading;
using FarmExchange.Weather;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public partial class TestBatchProcessing : Node
{
    private static readonly Vector2I Farm = new(189, 189);
    private static readonly Vector2I Processor = new(189, 192);
    private static readonly CommodityId Raw = new(CropKind.Radish, CommodityKind.Raw);

    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
        try
        {
            CheckAtomicClaim();
            CheckLockedSmallBatch();
            CheckFrozenCompetition();
            CheckRemoval();
            CheckCompletionCapacity();
            CheckStaticBudgets();
            CheckActualWeatherAndRandomCapacity();
            CheckSharedProcessorBacklog();
            GD.Print("九份批量加工：原子领取、锁量、冻结竞争、拆建、容量、七作物产能及逐秒/批量一致检查通过");
            return true;
        }
        catch (Exception error) { GD.PushError("批量加工测试失败：" + error); return false; }
    }

    private static FarmGame Empty(int seed = 12345, uint seconds = 0)
    {
        var game = new FarmGame(seed, seconds, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        return game;
    }

    private static void CheckAtomicClaim()
    {
        var inventory = new GoodsInventory();
        inventory.Add(Raw, 20);
        inventory.SetRawReserve(CropKind.Radish, 4);
        Require(inventory.TryReplaceFrozen(Raw, 0, 5), "冻结准备失败");
        Require(inventory.TryTakeRawForProcessing(CropKind.Radish, 9, out int first) && first == 9 &&
            inventory.Get(Raw) == 11 && inventory.GetFrozen(Raw) == 5, "满批没有扣除冻结后保留底线");
        Require(inventory.TryTakeRawForProcessing(CropKind.Radish, 9, out int tail) && tail == 2 &&
            inventory.GetAvailable(Raw) == 4, "末批未领取全部剩余可领取量");
        Require(!inventory.TryTakeRawForProcessing(CropKind.Radish, 9, out int zero) && zero == 0 &&
            inventory.Get(Raw) == 9 && inventory.GetFrozen(Raw) == 5, "等待领取发生部分扣料");
        foreach (int invalid in new[] { 0, -1 })
        {
            bool rejected = false;
            try { inventory.TryTakeRawForProcessing(CropKind.Radish, invalid, out _); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected && inventory.Get(Raw) == 9 && inventory.GetFrozen(Raw) == 5, "非法上限改变库存");
        }
        inventory.SetRawReserve(CropKind.Radish, int.MaxValue);
        Require(!inventory.TryTakeRawForProcessing(CropKind.Radish, 9, out _) && inventory.Get(Raw) == 9,
            "超库存底线引起负量或扣料");
        var maximum = new GoodsInventory();
        maximum.AddRaw(CropKind.Radish, int.MaxValue);
        Require(maximum.TryTakeRawForProcessing(CropKind.Radish, 9, out int bounded) && bounded == 9 &&
            maximum.GetRaw(CropKind.Radish) == int.MaxValue - 9, "最大库存领取溢出");
    }

    private static void CheckLockedSmallBatch()
    {
        using var game = Empty();
        game.SetPaused(true);
        Require(game.Buy(Raw, 3).Success && game.BuildProcessor(Processor, CropKind.Radish) == null,
            "少量批次准备失败");
        Require(game.GetProcessorDetails(Processor).BatchQuantity == 3 && game.GetPlot(Processor).RemainingSeconds == 26 &&
            game.GetRawStock(CropKind.Radish) == 0, "少批没有立即以完整时长开工");
        Require(game.Buy(Raw, 20).Success, "进行中补库存失败");
        game.SetRawReserve(CropKind.Radish, 100);
        int money = game.MoneyCents;
        Require(game.BuildProcessor(Processor, CropKind.Radish) != null && game.MoneyCents == money &&
            game.GetRawStock(CropKind.Radish) == 20 && game.GetProcessorDetails(Processor).BatchQuantity == 3 &&
            game.GetPlot(Processor).RemainingSeconds == 26, "失败建造追加、退还或重置进行中批次");
        Require(game.AdvanceTick() == default && game.GetProcessorDetails(Processor).BatchQuantity == 3,
            "暂停推进改变批次");
        game.SetPaused(false);
        Require(game.AdvanceTicks(25).Produced == 0 && game.GetPlot(Processor).RemainingSeconds == 1 &&
            game.GetProcessorDetails(Processor).BatchQuantity == 3, "少量批次提前完工或追加");
        TickResult completed = game.AdvanceTick();
        ProductionResult result = game.GetPresentationResults().Single(r => r.Kind == ProductionResultKind.Product);
        Require(completed.Produced == 3 && result.Quantity == 3 && game.GetProductStock(CropKind.Radish) == 3 &&
            game.GetRawStock(CropKind.Radish) == 20 && game.GetProcessorDetails(Processor).BatchQuantity == 0 &&
            game.GetProcessorDetails(Processor).Status == ProcessorStatus.WaitingForReserve,
            "锁量、完工汇总、真实结果或底线等待不一致");
        Require(game.AdvanceTicks(26).Produced == 0 && game.GetProductStock(CropKind.Radish) == 3,
            "空闲重复入库");
    }

    private static TradeOrderRequest FrozenSell(int quantity) => new(Raw, TradeOrderSide.Sell,
        TradeOrderFrequency.Once, TradeOrderQuantityMode.Fixed, quantity, TradeOrderBudgetMode.None, 0, 0,
        CashReserveMode.Amount, 0, new[] { new[] { new TradeOrderCondition(TradeConditionFactor.Price,
            TradeConditionComparison.LessOrEqual, 0) } });

    private static void CheckFrozenCompetition()
    {
        using var game = Empty();
        Vector2I first = new(381, 0), second = new(0, 3);
        Require(game.BuildProcessor(second, CropKind.Radish) == null && game.BuildProcessor(first, CropKind.Radish) == null &&
            game.Buy(Raw, 22).Success, "竞争夹具准备失败");
        game.SetRawReserve(CropKind.Radish, 2);
        var order = game.CreateTradeOrder(FrozenSell(5));
        Require(order.Success && game.GetRawStock(CropKind.Radish) == 22 &&
            game.GetProcessorDetails(first).BatchQuantity == 0, "买入、底线或建单提前触发领取");
        game.AdvanceTick();
        Require(game.GetProcessorDetails(first).BatchQuantity == 9 && game.GetProcessorDetails(second).BatchQuantity == 6 &&
            game.GetRawStock(CropKind.Radish) == 7 && game.GetFrozenStock(Raw) == 5 && game.GetAvailableStock(Raw) == 2,
            "场地未按Y再X稳定格序竞争或误领冻结/底线库存");
        Require(!game.Sell(Raw, 3).Success && game.GetRawStock(CropKind.Radish) == 7,
            "失败出售消费了冻结或正在加工原料");
        Require(game.Sell(Raw, 2).Success && game.CancelTradeOrder(order.Id).Success,
            "主动出售底线内可用量或解除冻结失败");
        Require(game.AdvanceTicks(26).Produced == 15 && game.GetProductStock(CropKind.Radish) == 15 &&
            game.GetRawStock(CropKind.Radish) == 2 && game.GetProcessorDetails(first).BatchQuantity == 3 &&
            game.GetProcessorDetails(second).BatchQuantity == 0,
            "竞争完工守恒或下一批实际余量错误");
    }

    private static void CheckRemoval()
    {
        using var game = Empty();
        Require(game.Buy(Raw, 12).Success && game.BuildProcessor(Processor, CropKind.Radish) == null &&
            game.GetProcessorDetails(Processor).BatchQuantity == 9 && game.GetRawStock(CropKind.Radish) == 3,
            "拆除夹具未建立满批");
        game.AdvanceTicks(10);
        Require(game.RemoveBuilding(Processor + new Vector2I(2, 2)) == null && game.GetRawStock(CropKind.Radish) == 3 &&
            game.GetProductStock(CropKind.Radish) == 0, "拆除退回投入或产生加工品");
        Require(game.BuildProcessor(Processor, CropKind.Radish) == null && game.GetProcessorDetails(Processor).BatchQuantity == 3 &&
            game.GetPlot(Processor).RemainingSeconds == 26 && game.GetRawStock(CropKind.Radish) == 0,
            "重建继承了旧锁量或进度");
        Require(game.AdvanceTicks(25).Produced == 0 && game.AdvanceTick().Produced == 3 &&
            game.GetProductStock(CropKind.Radish) == 3 && game.GetProcessorDetails(Processor).BatchQuantity == 0 &&
            game.HasConsistentState(), "拆建损失9份与新批3份不守恒");
    }

    private static void CheckCompletionCapacity()
    {
        var inventory = new GoodsInventory();
        var processing = new ProcessingSystem(1);
        processing.Place(0, CropKind.Radish);
        inventory.AddRaw(CropKind.Radish, 9);
        Require(processing.TryStart(0, inventory), "容量夹具开工失败");
        processing.AdvanceQuietSeconds(0, 25);
        inventory.AddProduct(CropKind.Radish, int.MaxValue - 8);
        ProcessorSnapshot before = processing.Get(0);
        bool overflow = false;
        try { processing.Advance(0, inventory, out _, out _); }
        catch (OverflowException) { overflow = true; }
        Require(overflow && processing.Get(0) == before && before.BatchQuantity == 9 && before.RemainingSeconds == 1 &&
            inventory.GetProduct(CropKind.Radish) == int.MaxValue - 8 && inventory.GetRaw(CropKind.Radish) == 0,
            "完工容量失败没有传播或已部分消耗批次");
        inventory.Remove(new(CropKind.Radish, CommodityKind.Product), 1);
        Require(processing.Advance(0, inventory, out CropKind crop, out int quantity) && crop == CropKind.Radish && quantity == 9,
            "释放容量后没有正常完成原批次");
        inventory.AddProduct(crop, quantity);
        Require(inventory.GetProduct(crop) == int.MaxValue && processing.Get(0).BatchQuantity == 0 &&
            !processing.Advance(0, inventory, out _, out _), "容量恢复后重复完工或入库");
    }

    private static void CheckStaticBudgets()
    {
        int[] maximums = { 18, 43, 32, 79, 9, 130, 59 };
        int[] budgets = { 206, 775, 412, 468, 309, 1545, 182 };
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            int bestPercent = Enum.GetValues<WeatherKind>().Max(weather => CropYieldRules.GetWeatherPercent(crop.Kind, weather));
            int maximum = CropYieldRules.CalculateQuantity(crop.HarvestQuantity * 9d, bestPercent, 1);
            int batchSeconds = GameTimeUnits.RemainingSeconds(crop.ProcessingHalfDays * GameTimeUnits.PerHalfDay);
            int budget = ((maximum + 8) / 9) * batchSeconds;
            Require(maximum == maximums[(int)crop.Kind] && budget == budgets[(int)crop.Kind] &&
                budget <= GameTimeUnits.RemainingSeconds(crop.GrowthDays * GameTimeUnits.PerDay), "七作物静态上界预算变化：" + crop.Kind);
            var inventory = new GoodsInventory();
            var processing = new ProcessingSystem(1);
            processing.Place(0, crop.Kind);
            inventory.AddRaw(crop.Kind, maximum);
            Require(processing.TryStart(0, inventory), "静态预算开工失败");
            int produced = 0;
            for (int second = 1; second <= budget; second++)
            {
                if (processing.Advance(0, inventory, out CropKind product, out int quantity))
                {
                    inventory.AddProduct(product, quantity);
                    produced += quantity;
                }
                processing.TryStart(0, inventory);
                if (second < budget) Require(produced < maximum, "不足九份末批没有耗完整周期");
            }
            Require(produced == maximum && inventory.GetRaw(crop.Kind) == 0 && inventory.GetProduct(crop.Kind) == maximum &&
                processing.Get(0).BatchQuantity == 0, "静态上界真实加工没有按预算守恒：" + crop.Kind);
        }
    }

    private static void CheckActualWeatherAndRandomCapacity()
    {
        bool differentSeedYield = false;
        bool differentWeatherYield = false;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            int? firstSeedYield = null;
            foreach (int seed in new[] { 7, 71 })
            {
                int? sunnyYield = null;
                foreach (bool mixed in new[] { false, true })
                {
                    uint start = crop.Kind == CropKind.Sugarcane ? 4320u : 0u;
                    using var single = Empty(seed, start);
                    using var batch = Empty(seed, start);
                    foreach (FarmGame game in new[] { single, batch })
                    {
                        Require(game.TryPlace(Farm, BuildingKind.Farm, crop.Kind).Success &&
                            game.BuildProcessor(Processor, crop.Kind) == null, "产能真实建造失败");
                    }
                    AdvanceBoth(single, batch, 2);
                    int growthSeconds = single.GetPlot(Farm).RemainingSeconds;
                    int half = growthSeconds / 2;
                    SetWeather(single, mixed ? WeatherKind.Cloudy : WeatherKind.Sunny);
                    SetWeather(batch, mixed ? WeatherKind.Cloudy : WeatherKind.Sunny);
                    AdvanceBoth(single, batch, (uint)half);
                    SetWeather(single, mixed ? WeatherKind.Rain : WeatherKind.Sunny);
                    SetWeather(batch, mixed ? WeatherKind.Rain : WeatherKind.Sunny);
                    int harvested = (int)AdvanceBoth(single, batch, (uint)(growthSeconds - half)).Harvested;
                    Require(harvested > 0 && single.GetRawStock(crop.Kind) + single.GetProcessorDetails(Processor).BatchQuantity == harvested,
                        "真实收获/首次领取不守恒");
                    int batchSeconds = GameTimeUnits.RemainingSeconds(crop.ProcessingHalfDays * GameTimeUnits.PerHalfDay);
                    uint budget = (uint)(((harvested + 8) / 9) * batchSeconds);
                    Require(budget <= growthSeconds, "实际随机/天气收获不满足单田一坊目标");
                    SimulationAdvanceResult processed = AdvanceBoth(single, batch, budget);
                    Require(processed.Produced == harvested && single.GetRawStock(crop.Kind) == 0 &&
                        single.GetProductStock(crop.Kind) == harvested && single.GetProcessorDetails(Processor).BatchQuantity == 0,
                        "下一正常生长期内没有清空真实收成：" + crop.Kind);
                    GD.Print($"实际单田一坊：{crop.CropName}，种子{seed}，天气{(mixed ? "多云→降雨" : "晴天")}，收成{harvested}，加工{budget}秒/生长{growthSeconds}秒");
                    if (!mixed)
                    {
                        sunnyYield = harvested;
                        if (firstSeedYield.HasValue && firstSeedYield != harvested) differentSeedYield = true;
                        firstSeedYield ??= harvested;
                    }
                    else if (sunnyYield != harvested) differentWeatherYield = true;
                }
            }
        }
        Require(differentSeedYield && differentWeatherYield, "产能测试没有产生不同实际随机量或天气量");
    }

    private static void CheckSharedProcessorBacklog()
    {
        using var one = Empty(71);
        using var two = Empty(71);
        Vector2I secondFarm = new(192, 189), secondProcessor = new(192, 192);
        foreach (FarmGame game in new[] { one, two })
        {
            Require(game.TryPlace(Farm, BuildingKind.Farm, CropKind.Radish).Success &&
                game.TryPlace(secondFarm, BuildingKind.Farm, CropKind.Radish).Success &&
                game.BuildProcessor(Processor, CropKind.Radish) == null, "多田一坊夹具失败");
            game.AdvanceTicks(208);
            game.RemoveBuilding(Farm);
            game.RemoveBuilding(secondFarm);
        }
        int quantity = one.GetRawStock(CropKind.Radish) + one.GetProcessorDetails(Processor).BatchQuantity;
        Require(quantity > 72 && two.BuildProcessor(secondProcessor, CropKind.Radish) == null,
            "两田实际收成未建立积压边界");
        one.AdvanceTicks(206);
        two.AdvanceTicks(206);
        int unfinished = one.GetRawStock(CropKind.Radish) + one.GetProcessorDetails(Processor).BatchQuantity;
        Require(unfinished > 0 && unfinished + one.GetProductStock(CropKind.Radish) == quantity &&
            two.GetRawStock(CropKind.Radish) == 0 && two.GetProcessorDetails(Processor).BatchQuantity == 0 &&
            two.GetProcessorDetails(secondProcessor).BatchQuantity == 0 && two.GetProductStock(CropKind.Radish) == quantity,
            "两田一坊积压、增建第二坊下降或投入守恒没有真实验证");
    }

    private static void SetWeather(FarmGame game, WeatherKind weather)
    {
        Rect2I map = new(0, 0, FarmGame.MapSize, FarmGame.MapSize);
        if (game.ActiveWeatherRegions.Any(region => region.Cells == map))
            Require(game.TryClearWeatherRegion(map, out string clear), clear);
        if (weather != WeatherKind.Sunny) Require(game.TrySetWeatherRegion(map, weather, out string set), set);
    }

    private static SimulationAdvanceResult AdvanceBoth(FarmGame single, FarmGame batch, uint seconds)
    {
        long harvested = 0, produced = 0;
        for (uint second = 0; second < seconds; second++)
        {
            TickResult tick = single.AdvanceTick();
            harvested += tick.Harvested;
            produced += tick.Produced;
        }
        SimulationAdvanceResult result = batch.AdvanceTicks(seconds);
        Require(result.AdvancedTicks == seconds && result.Harvested == harvested && result.Produced == produced &&
            single.Calendar == batch.Calendar && single.GetWorkers().SequenceEqual(batch.GetWorkers()) &&
            single.GetPresentationResults().Select(r => (r.Kind, r.Quantity, r.AnchorCell)).SequenceEqual(
                batch.GetPresentationResults().Select(r => (r.Kind, r.Quantity, r.AnchorCell))), "逐秒与批量结果不同");
        foreach (var space in single.GetBuildingSpaces())
        {
            Require(single.GetPlot(space.AnchorCell) == batch.GetPlot(space.AnchorCell), "逐秒/批量生产状态不同");
            if (space.Building == BuildingKind.Processor)
                Require(single.GetProcessorDetails(space.AnchorCell) == batch.GetProcessorDetails(space.AnchorCell), "逐秒/批量批次锁量不同");
        }
        foreach (CropDefinition crop in FarmGame.Crops)
            Require(single.GetRawStock(crop.Kind) == batch.GetRawStock(crop.Kind) &&
                single.GetProductStock(crop.Kind) == batch.GetProductStock(crop.Kind), "逐秒/批量库存不同");
        return result;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
