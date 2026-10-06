using System;
using System.Diagnostics;
using System.Linq;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;
using FarmExchange.Trading;

public partial class TestBatchedSimulation : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
        try
        {
            CheckRandomSegmentsAndRain();
            CheckPlansAndSeasons();
            CheckWorkerCancellation();
            CheckFrozenResourcesAndCompetition();
            CheckDenseOrders();
            CheckExactCheckpointsAndCapacity();
            CheckQuietCost();
            GD.Print("等价批量经营、七作物、事件边界与实际跳段检查通过");
            return true;
        }
        catch (Exception error)
        {
            GD.PushError($"等价批量经营失败：{error}");
            return false;
        }
    }

    private static void CheckRandomSegmentsAndRain()
    {
        foreach (CropKind crop in Enum.GetValues<CropKind>())
        {
            uint start = crop == CropKind.Sugarcane ? 4320u : 0u;
            FarmGame reference = ProductionGame(740 + (int)crop, start, crop);
            FarmGame batched = ProductionGame(740 + (int)crop, start, crop);
            var random = new Random(900 + (int)crop);
            for (int segment = 0; segment < 55; segment++)
            {
                CompareAdvance(reference, batched, (uint)random.Next(1, 380));
                if (segment % 7 == 0)
                {
                    Require(reference.AdvanceTick(true) == batched.AdvanceTick(true), "显式雨 tick 相位不同");
                    Compare(reference, batched);
                }
                if (segment % 11 == 0)
                {
                    reference.SetRawReserve(crop, segment % 3);
                    batched.SetRawReserve(crop, segment % 3);
                }
            }
            // 再跨一个年度，检查此前隐藏的随机状态、游标和生产凭据。
            CompareAdvance(reference, batched, 18000);
        }
    }

    private static void CheckPlansAndSeasons()
    {
        foreach (CultivationMode mode in Enum.GetValues<CultivationMode>())
        {
            FarmGame reference = ProductionGame(837, 16400, CropKind.Radish);
            FarmGame batched = ProductionGame(837, 16400, CropKind.Radish);
            var request = new CultivationPlanRequest("冬春接续", mode, new[]
            {
                new CultivationEntry(1, CropKind.Radish, 332),
                new CultivationEntry(2, CropKind.Radish, 0),
                new CultivationEntry(3, CropKind.Wheat, 20),
                new CultivationEntry(4, CropKind.Radish, 180),
            });
            int id = reference.CreateCultivationPlan(request).Id;
            Require(batched.CreateCultivationPlan(request).Id == id, "计划创建不同");
            var farms = new[] { new Vector2I(189, 189) };
            Require(reference.ApplyCultivationPlan(id, farms) == null &&
                batched.ApplyCultivationPlan(id, farms) == null, "计划应用失败");
            CompareAdvance(reference, batched, 23000);
            reference.DeleteCultivationPlan(id);
            batched.DeleteCultivationPlan(id);
            CompareAdvance(reference, batched, 600);
        }
        // 5040 单位的严格十分之一：边界扣减后剩余 511、504、497。
        foreach (uint start in new[] { 3671u, 3670u, 3669u })
        {
            FarmGame reference = ProductionGame(993, start, CropKind.Potato);
            FarmGame batched = ProductionGame(993, start, CropKind.Potato);
            SimulationAdvanceResult result = CompareAdvance(reference, batched, 4320 - start);
            Require(result.Harvested == (start == 3669 ? 8 : 0), "禁生严格阈值不一致");
            CompareAdvance(reference, batched, 40);
        }
    }

    private static void CheckWorkerCancellation()
    {
        FarmGame reference = EmptyGame(921);
        FarmGame batched = EmptyGame(921);
        Vector2I remote = new(12, 32);
        foreach (FarmGame game in new[] { reference, batched })
            Require(game.TryPlace(remote, BuildingKind.Farm, CropKind.Radish).Success, "远田建造失败");
        CompareAdvance(reference, batched, 1);
        CompareAdvance(reference, batched, 23);
        foreach (FarmGame game in new[] { reference, batched })
            game.SetFarmCrop(remote, CropKind.Wheat);
        CompareAdvance(reference, batched, 12);
        foreach (FarmGame game in new[] { reference, batched })
        {
            game.RemoveBuilding(remote);
            game.TryPlace(new Vector2I(250, 130), BuildingKind.Farm, CropKind.Radish);
        }
        CompareAdvance(reference, batched, 220);
        foreach (FarmGame game in new[] { reference, batched })
            game.PrepareFarmCrop(new Vector2I(250, 130), CropKind.Wheat);
        CompareAdvance(reference, batched, 500);
    }

    private static void CheckFrozenResourcesAndCompetition()
    {
        FarmGame reference = ProductionGame(889, 0, CropKind.Radish);
        FarmGame batched = ProductionGame(889, 0, CropKind.Radish);
        var raw = new CommodityId(CropKind.Radish, CommodityKind.Raw);
        var freeze = Order(raw, TradeOrderSide.Sell, TradeOrderFrequency.Once, 2,
            new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Greater, int.MaxValue));
        foreach (FarmGame game in new[] { reference, batched })
        {
            Require(game.Buy(raw, 10).Success, "原料买入失败");
            game.SetRawReserve(CropKind.Radish, 5);
            Require(game.CreateTradeOrder(freeze).Success, "原料冻结失败");
        }
        CompareAdvance(reference, batched, 207);
        Require(reference.GetFrozenStock(raw) == 2, "冻结原料被加工领取");
        foreach (FarmGame game in new[] { reference, batched })
        {
            game.CancelTradeOrder(1);
            game.SetRawReserve(CropKind.Radish, 0);
        }
        CompareAdvance(reference, batched, 1100);
        var buy = Order(raw, TradeOrderSide.Buy, TradeOrderFrequency.Once, 3) with
        {
            BudgetMode = TradeOrderBudgetMode.LimitPrice,
            LimitPriceCents = reference.GetRawPriceCents(CropKind.Radish),
        };
        foreach (FarmGame game in new[] { reference, batched })
            Require(game.CreateTradeOrder(buy).Success, "买单冻结失败");
        CompareAdvance(reference, batched, 400);
    }

    private static void CheckDenseOrders()
    {
        FarmGame reference = EmptyGame(322);
        FarmGame batched = EmptyGame(322);
        var raw = new CommodityId(CropKind.Radish, CommodityKind.Raw);
        foreach (FarmGame game in new[] { reference, batched })
        {
            Require(game.CreateTradeOrder(Order(raw, TradeOrderSide.Buy, TradeOrderFrequency.Continuous, 1)).Success,
                "持续买单失败");
            Require(game.CreateTradeOrder(Order(raw, TradeOrderSide.Sell, TradeOrderFrequency.Continuous, 1)).Success,
                "持续卖单失败");
        }
        SimulationAdvanceResult result = CompareAdvance(reference, batched, 250);
        Require(result.EventTicks == 250 && result.QuietTicks == 0, "密集成交被错误合并");
        Require(reference.GetRawStock(CropKind.Radish) == 0 && reference.MoneyCents == 4500,
            "逐笔费用或订单资源前缀未保留");
        FarmGame measured = EmptyGame(322);
        measured.CreateTradeOrder(Order(raw, TradeOrderSide.Buy, TradeOrderFrequency.Continuous, 1));
        measured.CreateTradeOrder(Order(raw, TradeOrderSide.Sell, TradeOrderFrequency.Continuous, 1));
        var timer = Stopwatch.StartNew();
        SimulationAdvanceResult measuredResult = measured.AdvanceTicks(250);
        timer.Stop();
        Compare(reference, measured);
        GD.Print($"批量密集成本：250 tick 中 {measuredResult.QuietTicks} 平静、{measuredResult.EventTicks} 事件；纯数据耗时 {timer.Elapsed.TotalMilliseconds:F3} ms");
        foreach (FarmGame game in new[] { reference, batched })
        {
            game.SetTradeOrderEnabled(1, false);
            game.CancelTradeOrder(2);
        }
        CompareAdvance(reference, batched, 3000);
    }

    private static void CheckExactCheckpointsAndCapacity()
    {
        FarmGame reference = EmptyGame(990);
        FarmGame batched = EmptyGame(990);
        var raw = new CommodityId(CropKind.Radish, CommodityKind.Raw);
        foreach (FarmGame game in new[] { reference, batched })
        {
            game.TryPlace(new Vector2I(189, 192), BuildingKind.Processor, CropKind.Radish);
            game.Buy(raw, 4);
        }
        var sell = Order(new CommodityId(CropKind.Radish, CommodityKind.Product),
            TradeOrderSide.Sell, TradeOrderFrequency.Once, 2);
        SimulationAdvanceResult first = batched.AdvanceTicks(200, point =>
        {
            if (batched.GetProductStock(CropKind.Radish) < 2)
                return true;
            Require(batched.CreateTradeOrder(sell).Success, "稳定检查点建卖单失败");
            return false;
        });
        Require(first.AdvancedTicks == 53 && first.Produced == 2 && first.StoppedAtCheckpoint,
            "产品第一次达到目标时没有精确停止（每批26秒，首次领取1秒）");
        for (uint i = 0; i < first.AdvancedTicks; i++)
            reference.AdvanceTick();
        Require(reference.CreateTradeOrder(sell).Success, "参考局建卖单失败");
        Compare(reference, batched);
        Require(batched.GetTradeOrders()[0].Status == TradeOrderStatus.Waiting &&
            batched.GetFrozenStock(sell.Commodity) == 2, "检查点建单在本秒提前成交");
        CompareAdvance(reference, batched, 1);
        Require(batched.GetTradeOrders()[0].Status == TradeOrderStatus.Completed,
            "检查点建单没有从随后经营秒执行");
        SimulationAdvanceResult budget = CompareAdvance(reference, batched, 7);
        Require(budget.AdvancedTicks == 7, "等待预算恰好到期被跨过");
        batched.SetPaused(true);
        uint before = batched.Calendar.ElapsedSeconds;
        Require(batched.AdvanceTicks(100, _ => throw new Exception("暂停调用检查点")).AdvancedTicks == 0,
            "暂停仍批量推进");
        Require(batched.Calendar.ElapsedSeconds == before, "暂停日历变化");
        batched.SetPaused(false);
        Require(batched.AdvanceTicks(0).AdvancedTicks == 0, "零请求推进");
        bool rejected = false;
        try { batched.AdvanceTicks(uint.MaxValue); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected && batched.Calendar.ElapsedSeconds == before, "溢出请求没有整段零修改拒绝");
        var nearLimit = new GameCalendar(uint.MaxValue - 2);
        uint futureDay = nearLimit.Snapshot.ElapsedDays + 1;
        ulong futureSecond = ((ulong)futureDay * GameTimeUnits.PerDay + GameTimeUnits.PerSecond - 1) /
            GameTimeUnits.PerSecond;
        Require(nearLimit.SecondsUntilDay(futureDay) == futureSecond - (uint.MaxValue - 2u),
            "临近上限的未来日距离发生回绕");
        Require(!nearLimit.TryAdvanceSeconds(3) && nearLimit.TryAdvanceSeconds(2) &&
            nearLimit.Snapshot.ElapsedSeconds == uint.MaxValue && !nearLimit.TryAdvanceSeconds(1),
            "日历临近上限没有零修改拒绝或精确到达");
        CompareAdvance(reference, batched, 100);
    }

    private static void CheckQuietCost()
    {
        FarmGame reference = EmptyGame(887);
        FarmGame batched = EmptyGame(887);
        var timer = Stopwatch.StartNew();
        SimulationAdvanceResult result = CompareAdvance(reference, batched, 17280);
        timer.Stop();
        Require(result.QuietTicks > 17200 && result.EventTicks < 80, "空局没有跳过平静秒");
        GD.Print($"批量平静成本：17280 tick 中 {result.QuietTicks} 一次累计、{result.EventTicks} 事件结算；对照耗时 {timer.ElapsedMilliseconds} ms");
        FarmGame measuredReference = EmptyGame(887);
        FarmGame measuredBatch = EmptyGame(887);
        timer.Restart();
        for (uint i = 0; i < 17280; i++)
            measuredReference.AdvanceTick();
        double referenceMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        measuredBatch.AdvanceTicks(17280);
        double batchMs = timer.Elapsed.TotalMilliseconds;
        timer.Stop();
        Compare(measuredReference, measuredBatch);
        GD.Print($"空局整年纯数据耗时：逐秒 {referenceMs:F3} ms，批量 {batchMs:F3} ms");
        FarmGame fullReference = new(112);
        FarmGame fullBatched = new(112);
        fullReference.FillWorldForBenchmark();
        fullBatched.FillWorldForBenchmark();
        SimulationAdvanceResult full = CompareAdvance(fullReference, fullBatched, 50);
        Require(full.QuietTicks > 45 && full.EventTicks < 5, "满地图50 tick未跳过平静区间");
    }

    private static SimulationAdvanceResult CompareAdvance(FarmGame reference, FarmGame batched, uint ticks)
    {
        long harvested = 0, produced = 0;
        bool acted = false, dayAdvanced = false;
        SimulationAdvanceResult result = batched.AdvanceTicks(ticks, point =>
        {
            long spanHarvested = 0, spanProduced = 0;
            bool spanActed = false, spanDay = false;
            for (uint i = 0; i < point.IntervalTicks; i++)
            {
                TickResult one = reference.AdvanceTick();
                spanHarvested += one.Harvested;
                spanProduced += one.Produced;
                spanActed |= one.WorkerActed;
                spanDay |= one.DayAdvanced;
            }
            Require(spanHarvested == point.Result.Harvested && spanProduced == point.Result.Produced &&
                spanActed == point.Result.WorkerActed && spanDay == point.Result.DayAdvanced,
                $"事件汇总不同，绝对tick {point.ElapsedSeconds}");
            harvested += spanHarvested;
            produced += spanProduced;
            acted |= spanActed;
            dayAdvanced |= spanDay;
            Compare(reference, batched);
            return true;
        });
        Require(result.AdvancedTicks == ticks && result.Harvested == harvested && result.Produced == produced &&
            result.WorkerActed == acted && result.DayAdvanced == dayAdvanced &&
            result.EventTicks + result.QuietTicks == ticks, "批量最终汇总或执行秒数错误");
        return result;
    }

    private static void Compare(FarmGame reference, FarmGame batched)
    {
        Require(reference.Calendar == batched.Calendar && reference.MoneyCents == batched.MoneyCents &&
            reference.FrozenMoneyCents == batched.FrozenMoneyCents &&
            reference.AvailableMoneyCents == batched.AvailableMoneyCents, "日历或现金状态不同");
        Require(reference.GetWorkers().SequenceEqual(batched.GetWorkers()),
            $"工人位置、任务或活动不同，绝对tick {reference.Calendar.ElapsedSeconds}");
        var spaces = reference.GetBuildingSpaces();
        Require(spaces.Count == batched.GetBuildingSpaces().Count, "设施数量不同");
        foreach (var space in spaces)
        {
            Require(reference.GetPlot(space.AnchorCell) == batched.GetPlot(space.AnchorCell), "设施生产状态不同");
            if (space.Building == BuildingKind.Farm)
                Require(reference.GetFarmCultivation(space.AnchorCell) == batched.GetFarmCultivation(space.AnchorCell),
                    "计划引用或预备安排不同");
        }
        foreach (CropKind crop in Enum.GetValues<CropKind>())
        {
            Require(reference.GetRawReserve(crop) == batched.GetRawReserve(crop), "原料底线不同");
            foreach (CommodityKind kind in Enum.GetValues<CommodityKind>())
            {
                var id = new CommodityId(crop, kind);
                Require(reference.GetStock(id) == batched.GetStock(id) &&
                    reference.GetFrozenStock(id) == batched.GetFrozenStock(id) &&
                    reference.GetAvailableStock(id) == batched.GetAvailableStock(id), "库存或冻结归属不同");
            }
        }
        var expectedMarket = reference.GetMarketSnapshot();
        var actualMarket = batched.GetMarketSnapshot();
        Require(expectedMarket.LastQuoteDate == actualMarket.LastQuoteDate &&
            expectedMarket.NextQuoteDate == actualMarket.NextQuoteDate &&
            expectedMarket.Quotes.SequenceEqual(actualMarket.Quotes), "行情价格、旧价或排期不同");
        Require(expectedMarket.News.HasValue == actualMarket.News.HasValue, "行情公告存在性不同");
        if (expectedMarket.News is { } expectedNews && actualMarket.News is { } actualNews)
            Require(expectedNews.PublishedDate == actualNews.PublishedDate && expectedNews.QuoteDate == actualNews.QuoteDate &&
                expectedNews.Lines.SequenceEqual(actualNews.Lines), "行情随机公告不同");
        var expectedOrders = reference.GetTradeOrders();
        var actualOrders = batched.GetTradeOrders();
        Require(expectedOrders.Count == actualOrders.Count, "订单数量不同");
        for (int i = 0; i < expectedOrders.Count; i++)
            Require(expectedOrders[i] == (actualOrders[i] with { Request = expectedOrders[i].Request }),
                $"订单状态、等待原因或最后逐笔成交不同，单据 {i + 1}");
    }

    private static FarmGame ProductionGame(int seed, uint seconds, CropKind crop)
    {
        FarmGame game = EmptyGame(seed, seconds);
        Require(game.TryPlace(new Vector2I(189, 189), BuildingKind.Farm, crop).Success &&
            game.TryPlace(new Vector2I(189, 192), BuildingKind.Processor, crop).Success &&
            game.TryPlace(new Vector2I(192, 192), BuildingKind.Processor, crop).Success, "生产夹具准备失败");
        return game;
    }

    private static FarmGame EmptyGame(int seed, uint seconds = 0)
    {
        var game = new FarmGame(seed, seconds);
        foreach (var space in game.GetBuildingSpaces().ToArray())
            game.RemoveBuilding(space.AnchorCell);
        return game;
    }

    private static TradeOrderRequest Order(CommodityId commodity, TradeOrderSide side,
        TradeOrderFrequency frequency, int quantity, TradeOrderCondition? condition = null) =>
        new(commodity, side, frequency, TradeOrderQuantityMode.Fixed, quantity,
            TradeOrderBudgetMode.None, 0, 0, CashReserveMode.Amount, 0,
            new[] { new[] { condition ?? new TradeOrderCondition(TradeConditionFactor.Price,
                TradeConditionComparison.GreaterOrEqual, 0) } });

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
