using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Weather;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public static class TestProductionDueIntegration
{
    public static bool RunChecks()
    {
        try
        {
            CheckMixedFifoAndCapacity();
            CheckReservationFailure();
            CheckCancellationAndPromotion();
            CheckCalendarLimit();
            CheckGameFifo();
            CheckInitialStockPreparation();
            CheckGameHarvestOverflowContinuation();
            CheckGameProductOverflowContinuation();
            GD.Print("生产到期索引混合FIFO、FarmGame库存溢出续推、容量保留、撤销与绝对时钟边界通过");
            return true;
        }
        catch (Exception error) { GD.PushError("生产到期接入失败：" + error); return false; }
    }

    private static void CheckMixedFifoAndCapacity()
    {
        var due = new SimulationDueIndex(3);
        var farming = new FarmingSystem(3, due, 65);
        var processing = new ProcessingSystem(3, due);
        var inventory = new GoodsInventory();
        processing.Place(2, CropKind.Sunflower);
        inventory.AddRaw(CropKind.Sunflower, 9);
        Require(processing.TryStart(2, inventory), "首个加工预约失败");
        Move(103);
        farming.Place(1, CropKind.Radish, WeatherKind.Sunny);
        farming.SupplyWater(1);
        Require(farming.TryWork(1, new GameCalendar(103).Snapshot), "中间农田预约失败");
        Move(200);
        farming.SetWeather(1, WeatherKind.Rain);
        Move(257);
        processing.Place(0, CropKind.Potato);
        inventory.AddRaw(CropKind.Potato, 4);
        Require(processing.TryStart(0, inventory), "末个加工预约失败");
        Move(309);
        Require(due.TryPeekDue(out _, out int first) && first == 2, "同秒产出被锚点或类型排序");
        Require(processing.Complete(first, inventory, out CropKind product, out int amount), "首项未完成");
        inventory.AddProduct(product, amount);
        Require(due.TryPeekDue(out var failedHandle, out int second) && second == 1, "农田未保持混合登记次序");
        inventory.AddRaw(CropKind.Radish, int.MaxValue);
        FarmSnapshot before = farming.Get(1);
        Require(before.RemainingTimeUnits == 0 && before.HasWater, "到期未结算快照错误");
        Throws<OverflowException>(() => farming.CompleteGrowth(1, inventory, out _));
        Require(farming.Get(1) == before && due.Count == 2 && due.TryPeekDue(out var retained, out second) &&
            retained == failedHandle && second == 1 && inventory.GetProduct(CropKind.Sunflower) == 9 &&
            processing.Get(0).BatchQuantity == 4, "容量异常丢弃当前/后续项或回滚成功项");
        farming.SetWeather(1, WeatherKind.Cloudy); // 已到期，切天气不能把未生长时间计入原轮。
        inventory.Remove(new(CropKind.Radish, CommodityKind.Raw), 200);
        Require(farming.CompleteGrowth(1, inventory, out CropHarvestResult harvest), "容量释放后原轮未完成");
        double basis = 6 * (6 + new Random(65).NextDouble() * 3);
        int expected = (int)Math.Round(basis * (1 - 0.1 * 761 / 1440), MidpointRounding.AwayFromZero);
        Require(harvest.Quantity == expected, "失败重复天气积分、重抽随机量或计入到期后天气");
        inventory.AddRaw(harvest.Crop, harvest.Quantity);
        Require(due.TryPeekDue(out _, out int last) && last == 0 &&
            processing.Complete(last, inventory, out product, out amount) && amount == 4, "尾项未保留或锁量改变");
        inventory.AddProduct(product, amount);
        Require(!due.TryPeekDue(out _, out _) && !farming.CompleteGrowth(1, inventory, out _) &&
            !processing.Complete(2, inventory, out _, out _) && inventory.GetProduct(CropKind.Sunflower) == 9,
            "显式续读重复提交成功项");

        void Move(long second)
        {
            due.AdvanceTo(second);
            farming.AdvanceTo(second);
            processing.AdvanceTo(second);
        }
    }

    private static void CheckReservationFailure()
    {
        var due = new SimulationDueIndex(1);
        var farming = new FarmingSystem(3, due, 78);
        var processing = new ProcessingSystem(3, due);
        var inventory = new GoodsInventory();
        processing.Place(0, CropKind.Radish);
        inventory.AddRaw(CropKind.Radish, 18);
        Require(processing.TryStart(0, inventory), "预约容量夹具开批失败");
        processing.Place(1, CropKind.Radish);
        Throws<InvalidOperationException>(() => processing.TryStart(1, inventory));
        Require(inventory.GetRaw(CropKind.Radish) == 9 && processing.Get(1).BatchQuantity == 0 && due.Count == 1,
            "预约容量失败扣除了原料或建立了无预约批次");
        farming.Place(2, CropKind.Radish, WeatherKind.Sunny);
        farming.TryWork(2, new GameCalendar().Snapshot);
        FarmSnapshot seeded = farming.Get(2);
        Throws<InvalidOperationException>(() => farming.SupplyWater(2));
        Require(farming.Get(2) == seeded && !seeded.HasWater, "供水预约失败仍修改轮次或水分");
        farming.RestartCrop(2, CropKind.Radish);
        farming.SupplyWater(2);
        FarmSnapshot wet = farming.Get(2);
        Throws<InvalidOperationException>(() => farming.TryWork(2, new GameCalendar().Snapshot));
        Require(farming.Get(2) == wet, "湿田播种预约失败仍播种");
        processing.Remove(0);
        Require(farming.TryWork(2, new GameCalendar().Snapshot) && due.Count == 1, "释放槽位后不能启动原轮");
        due.AdvanceTo(206);
        farming.AdvanceTo(206);
        Require(farming.CompleteGrowth(2, inventory, out CropHarvestResult harvest) &&
            harvest.Quantity == (int)Math.Round(6 * (6 + new Random(78).NextDouble() * 3), MidpointRounding.AwayFromZero),
            "失败预约消耗了农田随机抽取");
    }

    private static void CheckCancellationAndPromotion()
    {
        var due = new SimulationDueIndex(2);
        var farming = new FarmingSystem(2, due, 91);
        var inventory = new GoodsInventory();
        foreach (int index in new[] { 0, 1 })
        {
            farming.Place(index, CropKind.Potato, WeatherKind.Cloudy);
            farming.SupplyWater(index);
            farming.TryWork(index, new GameCalendar().Snapshot);
        }
        farming.SetSowingEnabled(0, false);
        Require(due.Count == 2, "关闭复种取消当前轮");
        farming.RestartCrop(1, CropKind.Potato);
        Require(due.Count == 1, "同种重启未撤销成熟预约");
        due.AdvanceTo(649);
        farming.AdvanceTo(649);
        Require(farming.Get(0).RemainingTimeUnits == 497, "精确目标丢失促熟阈值精度");
        inventory.AddRaw(CropKind.Potato, int.MaxValue);
        FarmSnapshot before = farming.Get(0);
        Throws<OverflowException>(() => farming.TryMatureBeforeDisallowedSeason(0, Season.Summer, inventory, out _));
        Require(farming.Get(0) == before && due.Count == 1, "促熟容量异常提前清轮或撤销预约");
        inventory.Remove(new(CropKind.Potato, CommodityKind.Raw), 200);
        Require(farming.TryMatureBeforeDisallowedSeason(0, Season.Summer, inventory, out CropHarvestResult harvest) &&
            harvest.Quantity == (int)Math.Round(8 * (6 + new Random(91).NextDouble() * 3) * 1.1, MidpointRounding.AwayFromZero) &&
            due.Count == 0 && !farming.Get(0).HasWater && !farming.Get(0).SowingEnabled,
            "促熟重算未来天气、丢失复种状态或没有取消正常成熟");
        farming.TryWork(1, new GameCalendar().Snapshot);
        Require(due.Count == 1, "重启保留水分后未重登");
        farming.Remove(1);
        farming.Place(1, CropKind.Radish, WeatherKind.Sunny);
        Require(due.Count == 0 && farming.Get(1).Stage == CropStage.None, "拆建继承旧预约");
        farming.SupplyWater(1);
        farming.TryWork(1, new GameCalendar().Snapshot);
        farming.ClearDisallowedCrops(Season.Summer);
        Require(due.Count == 0, "禁生清理遗留预约");
        farming.SupplyWater(1);
        farming.TryWork(1, new GameCalendar().Snapshot);
        farming.Clear();
        Require(due.Count == 0, "清局遗留成熟预约");
    }

    private static void CheckCalendarLimit()
    {
        var due = new SimulationDueIndex(1, uint.MaxValue - 1L);
        var processing = new ProcessingSystem(1, due);
        var inventory = new GoodsInventory();
        processing.Place(0, CropKind.Radish);
        inventory.AddRaw(CropKind.Radish, 1);
        Require(processing.TryStart(0, inventory) && due.TryGetNext(out long target) && target == (long)uint.MaxValue + 25,
            "uint终点附近开工错误拒绝或目标截断");
        due.AdvanceTo(uint.MaxValue);
        processing.AdvanceTo(uint.MaxValue);
        Require(processing.Get(0).RemainingSeconds == 25, "uint终点附近精确剩余错误");
        using var game = new FarmGame(65, uint.MaxValue - 1, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        game.AdvanceTick();
        Require(game.Calendar.ElapsedSeconds == uint.MaxValue, "最后合法秒未完成");
        Throws<InvalidOperationException>(() => game.AdvanceTick());
        Throws<InvalidOperationException>(() => game.AdvanceTicks(1));
        Require(game.Calendar.ElapsedSeconds == uint.MaxValue, "uint终点拒绝推进改变日历");
    }

    private static void CheckGameFifo()
    {
        using var game = new FarmGame(65, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        var raw = new CommodityId(CropKind.Radish, CommodityKind.Raw);
        Require(game.Buy(raw, 18).Success, "正式FIFO夹具买入失败");
        Vector2I first = new(210, 210), second = new(180, 180);
        Require(game.BuildProcessor(first, CropKind.Radish) == null &&
            game.BuildProcessor(second, CropKind.Radish) == null, "正式FIFO夹具建造失败");
        Require(game.AdvanceTicks(25).Produced == 0, "命令开批提前完工");
        TickResult result = game.AdvanceTick();
        var output = game.GetPresentationResults();
        Require(result.Produced == 18 && output.Count == 2 && output[0].AnchorCell == first &&
            output[1].AnchorCell == second && output[0].Quantity == 9 && output[1].Quantity == 9 &&
            game.GetProductStock(CropKind.Radish) == 18, "正式产出未按登记FIFO或重复入库");
    }

    private static void CheckInitialStockPreparation()
    {
        var initialStock = FarmGame.Crops.ToDictionary(crop => new CommodityId(crop.Kind, CommodityKind.Raw), _ => 27);
        using var game = new FarmGame(65, 0, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules, initialStock: initialStock);
        initialStock.Clear();
        Require(FarmGame.Crops.All(crop => game.GetRawStock(crop.Kind) == 27) &&
            game.GetBuildingSpaces().Where(space => space.Building == BuildingKind.Processor)
                .All(space => game.GetProcessorDetails(space.AnchorCell).BatchQuantity == 0),
            "初始库存未复制、开局加工提前领取或商品间数量串扰");
        foreach (var invalid in new[]
        {
            new KeyValuePair<CommodityId, int>(new(CropKind.Radish, CommodityKind.Raw), -1),
            new KeyValuePair<CommodityId, int>(new((CropKind)99, CommodityKind.Raw), 1),
            new KeyValuePair<CommodityId, int>(new(CropKind.Radish, (CommodityKind)99), 1),
        })
            Throws<ArgumentOutOfRangeException>(() =>
            {
                using var rejected = new FarmGame(65, 0, weatherMode: WeatherMode.Explicit,
                    terrainRules: TerrainTestFixtures.AllLandRules,
                    initialStock: new Dictionary<CommodityId, int> { [invalid.Key] = invalid.Value });
            });
    }

    private static void CheckGameHarvestOverflowContinuation()
    {
        // 无溢出对照局给出同种子真实收成，避免把随机算法复制进经营集成测试。
        using var reference = CreateGrowingFarmGame(0);
        TickResult expectedTick = reference.AdvanceTick();
        ProductionResult[] expectedHarvests = reference.GetPresentationResults()
            .Where(result => result.Kind == ProductionResultKind.Harvest).ToArray();
        Require(expectedHarvests.Length == 3 && expectedTick.Harvested == expectedHarvests.Sum(result => result.Quantity),
            "原料溢出对照夹具未形成同秒三田成熟");
        using var game = CreateGrowingFarmGame(int.MaxValue - expectedHarvests[0].Quantity);
        Vector2I[] cells = expectedHarvests.Select(result => result.AnchorCell).ToArray();
        var workersBefore = game.GetWorkers().ToArray();
        int moneyBefore = game.MoneyCents;
        Require(game.Calendar.ElapsedSeconds == 206 && cells.All(cell => game.GetPlot(cell).RemainingSeconds == 1),
            "原料溢出夹具未停在共同到期前一秒");

        Throws<OverflowException>(() => game.AdvanceTick());
        var partial = game.GetPresentationResults();
        PlotSnapshot[] failedPlots = cells.Select(game.GetPlot).ToArray();
        Require(game.Calendar.ElapsedSeconds == 206 && game.GetRawStock(CropKind.Radish) == int.MaxValue &&
            game.MoneyCents == moneyBefore && game.GetProductStock(CropKind.Radish) == 0 &&
            partial.Count == 1 && partial[0].AnchorCell == cells[0] && partial[0].Quantity == expectedHarvests[0].Quantity &&
            partial[0].ElapsedSeconds == 207 && !game.IsPresentationResultCurrent(partial[0]),
            "原料溢出错误提交日历、跳过失败田或发布未完成秒的可播放结果");
        Require(failedPlots[0].Crop == CropStage.None && !failedPlots[0].HasWater &&
            failedPlots.Skip(1).All(plot => plot.Crop == CropStage.Growing && plot.HasWater && plot.RemainingSeconds == 0) &&
            game.GetWorkers().SequenceEqual(workersBefore), "原料溢出提前清理失败轮次、尾项或推进工人");

        int failedCheckpoints = 0;
        Throws<OverflowException>(() => game.AdvanceTicks(1, _ => { failedCheckpoints++; return true; }));
        Require(failedCheckpoints == 0 && game.Calendar.ElapsedSeconds == 206 && game.MoneyCents == moneyBefore &&
            game.GetRawStock(CropKind.Radish) == int.MaxValue && game.GetPresentationResults().SequenceEqual(partial) &&
            cells.Select(game.GetPlot).SequenceEqual(failedPlots) && game.GetWorkers().SequenceEqual(workersBefore),
            "未释放容量的重复请求重跑降雨、重提首项、修改失败轮次或发布检查点");

        int released = expectedHarvests.Skip(1).Sum(result => result.Quantity);
        Require(game.Sell(new(CropKind.Radish, CommodityKind.Raw), released).Success, "原料容量释放失败");
        TickResult resumed = game.AdvanceTick();
        Require(resumed == expectedTick && game.Calendar == reference.Calendar && game.GetRawStock(CropKind.Radish) == int.MaxValue,
            "原料续推没有在同秒完成，或漏计/重计异常前已入库收成");
        Require(SamePresentation(game, reference) && cells.All(cell => game.GetPlot(cell) == reference.GetPlot(cell)) &&
            game.IsPresentationResultCurrent(partial[0]) && partial.Count == 1,
            "原料续推丢失FIFO真实结果、改变产量/轮次，或修改旧只读结果快照");
        var followup = game.AdvanceTicks(1);
        TickResult referenceFollowup = reference.AdvanceTick();
        Require(followup.Harvested == 0 && followup.Produced == 0 && referenceFollowup.Harvested == 0 &&
            game.GetRawStock(CropKind.Radish) == int.MaxValue && !game.IsPresentationResultCurrent(partial[0]) &&
            SamePresentation(game, reference), "原料续推后下一秒重复入库或重播旧收获");
    }

    private static FarmGame CreateGrowingFarmGame(int rawStock)
    {
        var game = new FarmGame(65, 0, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules,
            initialStock: new Dictionary<CommodityId, int> { [new(CropKind.Radish, CommodityKind.Raw)] = rawStock });
        try
        {
            foreach (var space in game.GetBuildingSpaces())
                if (space.Building == BuildingKind.Processor) game.RemoveBuilding(space.AnchorCell);
            Require(game.GetRawStock(CropKind.Radish) == rawStock, "初始库存被开局设施提前领取");
            foreach (var space in game.GetBuildingSpaces())
                Require(game.SetFarmCrop(space.AnchorCell, CropKind.Radish) == null, "原料溢出夹具改种失败");
            Require(game.TrySetWeatherRegion(new Rect2I(0, 0, FarmGame.MapSize, FarmGame.MapSize), WeatherKind.Rain, out _),
                "原料溢出夹具降雨设置失败");
            game.AdvanceTick();
            Require(game.GetBuildingSpaces().All(space => game.GetPlot(space.AnchorCell).Crop == CropStage.Growing &&
                game.GetPlot(space.AnchorCell).RemainingSeconds == 206), "三名工人未在首秒完成湿田播种");
            Require(game.AdvanceTicks(205).Harvested == 0, "原料溢出夹具提前成熟");
            return game;
        }
        catch { game.Dispose(); throw; }
    }

    private static void CheckGameProductOverflowContinuation()
    {
        var product = new CommodityId(CropKind.Radish, CommodityKind.Product);
        var initialStock = new Dictionary<CommodityId, int> { [product] = int.MaxValue - 9 };
        using var game = new FarmGame(65, 0, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules, initialStock: initialStock);
        initialStock.Clear();
        Require(game.GetStock(product) == int.MaxValue - 9, "初始库存仍引用调用方可变容器");
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        Vector2I[] cells = { new(180, 180), new(183, 180), new(186, 180) };
        foreach (Vector2I cell in cells)
            Require(game.BuildProcessor(cell, CropKind.Radish) == null, "成品溢出夹具建造失败");
        Require(game.Buy(new(CropKind.Radish, CommodityKind.Raw), 27).Success, "成品溢出夹具买入失败");
        var checkpoints = new List<SimulationCheckpoint>();
        var workersBefore = game.GetWorkers().ToArray();
        int moneyBefore = game.MoneyCents;
        Throws<OverflowException>(() => game.AdvanceTicks(30, point => { checkpoints.Add(point); return true; }));
        // 第1秒正常领取，26秒批次在第27秒到期；异常前的平静段已完成到第26秒。
        Require(checkpoints.Count == 1 && checkpoints[0].ElapsedSeconds == 1 && checkpoints[0].AdvancedTicks == 1 &&
            checkpoints[0].IntervalTicks == 1 && checkpoints[0].IsEvent && checkpoints[0].Result.Produced == 0 &&
            game.Calendar.ElapsedSeconds == 26, "失败批次丢失此前完整检查点，或发布了第27秒检查点");
        var partial = game.GetPresentationResults();
        PlotSnapshot[] failedPlots = cells.Select(game.GetPlot).ToArray();
        Require(game.GetStock(product) == int.MaxValue && game.GetRawStock(CropKind.Radish) == 0 &&
            partial.Count == 1 && partial[0].AnchorCell == cells[0] && partial[0].Quantity == 9 &&
            partial[0].ElapsedSeconds == 27 && !game.IsPresentationResultCurrent(partial[0]) &&
            game.GetProcessorDetails(cells[0]).BatchQuantity == 0 &&
            cells.Skip(1).All(cell => game.GetProcessorDetails(cell).BatchQuantity == 9 && game.GetPlot(cell).RemainingSeconds == 0),
            "成品溢出回滚首项、丢失锁量/尾项或把未完成秒结果视为可播放");
        var workersAtFailure = game.GetWorkers().ToArray();
        Require(workersAtFailure.All(worker => worker.TargetCell == null) &&
            workersAtFailure.Select(worker => worker.GridPosition).SequenceEqual(workersBefore.Select(worker => worker.GridPosition)),
            "无农田夹具错误产生工人移动");
        Throws<OverflowException>(() => game.AdvanceTick());
        Require(game.Calendar.ElapsedSeconds == 26 && game.MoneyCents == moneyBefore && game.GetStock(product) == int.MaxValue &&
            game.GetRawStock(CropKind.Radish) == 0 && cells.Select(game.GetPlot).SequenceEqual(failedPlots) &&
            game.GetWorkers().SequenceEqual(workersAtFailure) && game.GetPresentationResults().SequenceEqual(partial),
            "成品重复失败推进时钟、重复首项或修改保留批次");

        Require(game.Sell(product, 18).Success, "成品容量释放失败");
        checkpoints.Clear();
        var resumed = game.AdvanceTicks(5, point =>
        {
            checkpoints.Add(point);
            if (point.ElapsedSeconds == 27)
            {
                var output = game.GetPresentationResults();
                Require(output.Count == 3 && output.Select(result => result.AnchorCell).SequenceEqual(cells) &&
                    output.All(result => result.Kind == ProductionResultKind.Product && result.Quantity == 9 &&
                        result.ElapsedSeconds == 27 && game.IsPresentationResultCurrent(result)) && partial.Count == 1,
                    "成品续推没有保留首项并按FIFO发布三项真实结果");
            }
            return true;
        });
        Require(resumed.AdvancedTicks == 5 && resumed.Produced == 27 && resumed.Harvested == 0 &&
            resumed.EventTicks == 1 && resumed.QuietTicks == 4 && !resumed.StoppedAtCheckpoint &&
            checkpoints.Count == 2 && checkpoints[0].ElapsedSeconds == 27 && checkpoints[0].AdvancedTicks == 1 &&
            checkpoints[0].IntervalTicks == 1 && checkpoints[0].Result.Produced == 27 && checkpoints[0].IsEvent &&
            checkpoints[1].ElapsedSeconds == 31 && checkpoints[1].AdvancedTicks == 5 && checkpoints[1].IntervalTicks == 4 &&
            !checkpoints[1].IsEvent && checkpoints[1].Result.Produced == 0,
            "成品续推批量汇总或完整检查点重复/漏计异常前产出");
        Require(game.Calendar.ElapsedSeconds == 31 && game.GetStock(product) == int.MaxValue &&
            cells.All(cell => game.GetProcessorDetails(cell).BatchQuantity == 0) &&
            game.GetPresentationResults().Count == 0 && !game.IsPresentationResultCurrent(partial[0]),
            "平静尾段重复入库、保留已完成批次或重播旧产出");
        Require(game.AdvanceTick().Produced == 0 && game.GetStock(product) == int.MaxValue && game.Calendar.ElapsedSeconds == 32,
            "完成续推后的普通秒重复提交成品");
    }

    private static bool SamePresentation(FarmGame left, FarmGame right) =>
        left.GetPresentationResults().Select(result => (result.ElapsedSeconds, result.AnchorCell,
            result.CropKind, result.Kind, result.Quantity, result.WorkerNumber)).SequenceEqual(
            right.GetPresentationResults().Select(result => (result.ElapsedSeconds, result.AnchorCell,
                result.CropKind, result.Kind, result.Quantity, result.WorkerNumber)));

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("没有抛出预期异常：" + typeof(T).Name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
