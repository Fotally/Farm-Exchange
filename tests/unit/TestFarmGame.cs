using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Market;
using FarmExchange.Workers;

public partial class TestFarmGame : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        if (passed)
            GD.Print("农田、加工与交易规则检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks() =>
        CheckInitialCenter() && CheckAllCrops() && CheckBuildingCost() && CheckMatchingAndSwitching() && CheckRemoval() &&
        CheckWorkerParticipation() && CheckContinuousProduction() && CheckRainSupply() && CheckSeasonalSowing() &&
        CheckSeasonFailure() && CheckSeasonBoundaries() && CheckRawSales() &&
        CheckInvalidCrops() && CheckStateOwnership() &&
        CheckPlacementRules() && CheckRawReservePhases() && CheckRawReserveConstruction();

    private static bool CheckInitialCenter()
    {
        Vector2I[] farms = { new(63, 63), new(64, 63), new(65, 63) };
        Vector2I[] processors = { new(63, 64), new(64, 64) };
        bool sawSame = false;
        bool sawSplit = false;
        bool sawRadish = false;
        for (int seed = 0; seed < 64; seed++)
        {
            var game = new FarmGame(seed);
            var repeat = new FarmGame(seed);
            if (game.MoneyCents != 5000)
                return Fail("开局金币错误");
            var workers = game.GetWorkers();
            if (workers.Count != 3)
                return Fail("开局没有三名真实经营工人");
            for (int i = 0; i < workers.Count; i++)
                if (workers[i] != new WorkerSnapshot(i + 1, (Vector2)farms[i], null, WorkerActivity.Idle) ||
                    workers[i] != repeat.GetWorkers()[i])
                    return Fail("开局工人编号、中心位置或只读快照错误");
            CropKind primary = game.GetPlot(farms[0]).CropKind;
            CropKind secondary = game.GetPlot(farms[2]).CropKind;
            foreach (Vector2I cell in farms)
            {
                PlotSnapshot plot = game.GetPlot(cell);
                if (plot.Building != BuildingKind.Farm ||
                    plot.Crop != CropStage.None || plot.CropKind != repeat.GetPlot(cell).CropKind)
                    return Fail("中心农田提前播种或固定种子不可复现");
            }
            if (game.GetPlot(farms[1]).CropKind != primary)
                return Fail("开局农田没有形成 3+0 或 2+1");
            for (int i = 0; i < processors.Length; i++)
            {
                PlotSnapshot plot = game.GetPlot(processors[i]);
                CropKind expected = i == 0 ? primary : secondary;
                if (plot.Building != BuildingKind.Processor ||
                    plot.CropKind != expected || plot.CropKind != repeat.GetPlot(processors[i]).CropKind)
                    return Fail("中心加工场地未匹配农田或固定种子不可复现");
            }
            if (primary == CropKind.Radish || secondary == CropKind.Radish)
                sawRadish = true;
            sawSame |= primary == secondary;
            sawSplit |= primary != secondary;
        }
        if (!sawSame || !sawSplit || !sawRadish)
            return Fail("固定种子未覆盖两种开局配置");
        return true;
    }

    private static void RemoveInitialBuildings(FarmGame game)
    {
        game.RemoveBuilding(new Vector2I(63, 63));
        game.RemoveBuilding(new Vector2I(64, 63));
        game.RemoveBuilding(new Vector2I(65, 63));
        game.RemoveBuilding(new Vector2I(63, 64));
        game.RemoveBuilding(new Vector2I(64, 64));
    }

    private static bool CheckAllCrops()
    {
        int[] expectedGrowthSeconds = { 823, 1029, 618, 720, 875, 2058, 206 };
        int[] expectedProcessingSeconds = { 103, 155, 103, 52, 309, 103, 26 };
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            uint startSecond = crop.Kind == CropKind.Sugarcane ? 4320u : 0u;
            var game = new FarmGame(12345, startSecond);
            Vector2I farm = new(63, 63);
            Vector2I processor = new(100, 100);
            RemoveInitialBuildings(game);
            if (game.MoneyCents != 5000 || game.CurrentDay != (int)(startSecond * 7 / 360) + 1 ||
                game.CurrentFlourPriceCents !=
                    new MarketPriceCurve(12345).GetPriceCents(game.CurrentDay))
                return Fail("初始资源错误");
            if (game.BuildFarm(farm) != null ||
                game.SetFarmCrop(farm, crop.Kind) != null ||
                game.BuildProcessor(processor, crop.Kind) != null)
                return Fail($"{crop.CropName}的农田或加工场地建造失败");
            if (game.MoneyCents != 3000)
                return Fail($"{crop.CropName}建造费用错误");
            int growthSeconds = expectedGrowthSeconds[(int)crop.Kind];
            int processingSeconds = expectedProcessingSeconds[(int)crop.Kind];
            if (processingSeconds >= growthSeconds)
                return Fail($"{crop.CropName}加工未快于成熟");

            TickResult sow = game.AdvanceTick();
            TickResult water = game.AdvanceTick();
            if (!sow.WorkerActed || !water.WorkerActed ||
                game.GetPlot(farm).Crop != CropStage.Growing ||
                game.GetPlot(farm).RemainingSeconds != growthSeconds)
                return Fail($"{crop.CropName}播种或浇水时序错误");
            for (int i = 0; i < growthSeconds - 1; i++)
                game.AdvanceTick();
            if (game.GetRawStock(crop.Kind) != 0)
                return Fail($"{crop.CropName}过早成熟");
            TickResult harvest = game.AdvanceTick();
            if (harvest.Harvested != crop.HarvestQuantity ||
                game.GetRawStock(crop.Kind) != crop.HarvestQuantity - 1 ||
                game.GetPlot(processor).RemainingSeconds != processingSeconds)
                return Fail($"{crop.CropName}未按时收获并投入对应场地");
            SaleResult rawSale = game.SellRaw(crop.Kind);
            if (rawSale.Quantity != crop.HarvestQuantity - 1)
                return Fail($"{crop.CropName}原料库存或加工中的原料出售错误");
            for (int i = 0; i < processingSeconds - 1; i++)
                game.AdvanceTick();
            if (game.GetProductStock(crop.Kind) != 0)
                return Fail($"{crop.ProductName}过早产出");
            TickResult finished = game.AdvanceTick();
            if (finished.Produced != 1 || game.GetProductStock(crop.Kind) != 1)
                return Fail($"{crop.ProductName}未按时产出");
            int expectedRevenue = game.GetProductPriceCents(crop.Kind);
            SaleResult sale = game.SellAll();
            if (sale.Quantity != 1 || sale.RevenueCents != expectedRevenue ||
                game.MoneyCents != 3000 + expectedRevenue + rawSale.RevenueCents ||
                game.GetProductStock(crop.Kind) != 0 || game.SellAll().Quantity != 0)
                return Fail($"{crop.ProductName}出售或库存更新错误");
        }
        return true;
    }

    private static bool CheckBuildingCost()
    {
        var game = new FarmGame(12345);
        RemoveInitialBuildings(game);
        for (int col = 0; col < 5; col++)
        {
            Vector2I cell = new(col, 0);
            if (game.BuildFarm(cell) != null || game.MoneyCents != 5000 - (col + 1) * FarmGame.BuildingCostCents)
                return Fail("农田建造未按每座 10 金币收费");
            if (game.BuildProcessor(cell, CropKind.Wheat) == null ||
                game.MoneyCents != 5000 - (col + 1) * FarmGame.BuildingCostCents)
                return Fail("占用地块仍可建造或重复扣费");
        }
        Vector2I another = new(5, 0);
        if (game.BuildProcessor(another, CropKind.Wheat) == null ||
            game.GetPlot(another).Building != BuildingKind.None || game.MoneyCents != 0)
            return Fail("余额不足仍可建造");
        game.RemoveBuilding(new Vector2I(0, 0));
        if (game.MoneyCents != 0 || game.BuildFarm(new Vector2I(0, 0)) == null)
            return Fail("移除建筑退费或重建未收费");
        return true;
    }

    private static bool CheckMatchingAndSwitching()
    {
        var game = new FarmGame(12345);
        RemoveInitialBuildings(game);
        Vector2I farm = new(63, 63);
        Vector2I processor = new(1, 0);
        game.BuildFarm(farm);
        game.BuildProcessor(processor, CropKind.Rice);
        game.AdvanceTick();
        game.AdvanceTick();
        if (game.SetFarmCrop(farm, CropKind.Corn) != null ||
            game.GetPlot(farm).Crop != CropStage.None ||
            game.GetPlot(farm).RemainingSeconds != 0)
            return Fail("切换作物未丢弃生长中的旧作物");
        if (game.SetFarmCrop(farm, CropKind.Corn) != null ||
            game.SetFarmCrop(processor, CropKind.Corn) == null)
            return Fail("重复选择或非农田选择处理错误");
        game.AdvanceTick();
        game.AdvanceTick();
        for (int i = 0; i < 1029; i++)
            game.AdvanceTick();
        if (game.GetRawStock(CropKind.Wheat) != 0 || game.GetRawStock(CropKind.Corn) != 4 ||
            game.GetProductStock(CropKind.Rice) != 0 || game.GetPlot(processor).RemainingSeconds != 0)
            return Fail("加工场地消耗了不匹配原料，或切换前作物仍产出");
        if (game.SellAll().Quantity != 0)
            return Fail("原料被直接出售");
        game.RemoveBuilding(processor);
        game.BuildProcessor(processor, CropKind.Corn);
        if (game.GetRawStock(CropKind.Corn) != 3 ||
            game.GetPlot(processor).RemainingSeconds != 155)
            return Fail("新建匹配场地没有自动处理库存原料");
        return true;
    }

    private static bool CheckRemoval()
    {
        var game = new FarmGame(12345);
        RemoveInitialBuildings(game);
        Vector2I farm = new(63, 63);
        Vector2I processor = new(1, 0);
        game.BuildFarm(farm);
        game.BuildProcessor(processor, CropKind.Wheat);
        game.AdvanceTick();
        game.AdvanceTick();
        for (int i = 0; i < 823; i++)
            game.AdvanceTick();
        if (game.GetPlot(farm).Crop != CropStage.Seeded ||
            game.GetPlot(processor).RemainingSeconds == 0 ||
            game.RemoveBuilding(farm) != null || game.RemoveBuilding(processor) != null)
            return Fail("移除农田或加工中的场地失败");
        for (int i = 0; i < 103; i++)
            game.AdvanceTick();
        if (game.GetProductStock(CropKind.Wheat) != 0 ||
            game.GetPlot(farm).Building != BuildingKind.None || game.MoneyCents != 3000)
            return Fail("移除建筑后仍产出或返还建造费");
        return true;
    }

    private static bool CheckWorkerParticipation()
    {
        var game = new FarmGame(12345);
        RemoveInitialBuildings(game);
        Vector2I[] farms = { new(63, 63), new(64, 63), new(65, 63) };
        CropKind[] crops = { CropKind.Wheat, CropKind.Sunflower, CropKind.Radish };
        int[] growthSeconds = { 823, 875, 206 };
        for (int i = 0; i < farms.Length; i++)
        {
            game.BuildFarm(farms[i]);
            game.SetFarmCrop(farms[i], crops[i]);
        }
        TickResult sow = game.AdvanceTick();
        var sowWorkers = game.GetWorkers();
        if (!sow.WorkerActed)
            return Fail("三工人同格播种没有报告完成工作");
        for (int i = 0; i < farms.Length; i++)
            if (game.GetPlot(farms[i]).Crop != CropStage.Seeded ||
                sowWorkers[i].TargetCell != farms[i] || sowWorkers[i].Activity != WorkerActivity.Watering)
                return Fail("三人没有各自认领并播种，或本秒同时完成了浇水");
        TickResult water = game.AdvanceTick();
        var waterWorkers = game.GetWorkers();
        if (!water.WorkerActed)
            return Fail("三工人原地供水没有报告完成工作");
        for (int i = 0; i < farms.Length; i++)
            if (game.GetPlot(farms[i]).Crop != CropStage.Growing ||
                game.GetPlot(farms[i]).RemainingSeconds != growthSeconds[i] ||
                waterWorkers[i].Activity != WorkerActivity.Idle || waterWorkers[i].TargetCell != null ||
                sowWorkers[i].Activity != WorkerActivity.Watering)
                return Fail("三人供水未使用完整一秒，或历史快照被后续推进修改");
        if (game.AdvanceTick().WorkerActed)
            return Fail("仅作物生长的一秒误报了工人完成工作");
        for (int i = 0; i < farms.Length; i++)
            if (game.GetPlot(farms[i]).RemainingSeconds != growthSeconds[i] - 1)
                return Fail("三块农田没有从各自实际供水后的下一秒生长");

        var remote = new FarmGame(12345);
        RemoveInitialBuildings(remote);
        Vector2I target = new(63, 64);
        remote.BuildFarm(target);
        if (remote.AdvanceTick().WorkerActed || remote.GetPlot(target).Crop != CropStage.None ||
            remote.GetWorkers()[0].GridPosition != (Vector2)target)
            return Fail("移动到田的整秒与播种整秒没有区分");
        var beforePause = remote.GetWorkers();
        remote.SetPaused(true);
        for (int second = 0; second < 3; second++)
        {
            if (remote.AdvanceTick() != default || remote.Calendar.ElapsedSeconds != 1 ||
                remote.GetPlot(target).Crop != CropStage.None)
                return Fail("暂停时经营继续推进了工人工作");
            var pausedWorkers = remote.GetWorkers();
            for (int i = 0; i < pausedWorkers.Count; i++)
                if (pausedWorkers[i] != beforePause[i])
                    return Fail("暂停改变了工人的位置或已有任务");
        }
        remote.SetPaused(false);
        if (!remote.AdvanceTick().WorkerActed || remote.GetPlot(target).Crop != CropStage.Seeded ||
            remote.Calendar.ElapsedSeconds != 2)
            return Fail("恢复后工人没有从原任务继续，或补算了暂停时间");
        return true;
    }

    private static bool CheckContinuousProduction()
    {
        var game = new FarmGame(12345);
        game.RemoveBuilding(new Vector2I(63, 64));
        game.RemoveBuilding(new Vector2I(64, 64));
        Vector2I[] farms = { new(63, 63), new(64, 63), new(65, 63) };
        foreach (Vector2I farm in farms)
            game.SetFarmCrop(farm, CropKind.Radish);
        int rounds = 0;
        for (int second = 0; second < 624 && rounds < 3; second++)
        {
            TickResult tick = game.AdvanceTick();
            if (tick.Harvested == 0)
                continue;
            if (tick.Harvested != 18)
                return Fail("三个中心工人持续生产时有田块长期遗漏或重复收获");
            rounds++;
        }
        if (rounds != 3 || game.GetRawStock(CropKind.Radish) != 54 || game.MoneyCents != 5000)
            return Fail("三块中心田没有自动完成三轮，或收成与金币发生变化");
        return true;
    }

    private static bool CheckRainSupply()
    {
        var retained = new FarmGame(12345);
        RemoveInitialBuildings(retained);
        Vector2I first = new(63, 63);
        Vector2I second = new(67, 63);
        retained.BuildFarm(first);
        retained.BuildFarm(second);
        retained.SetFarmCrop(second, CropKind.Radish);
        retained.SetPaused(true);
        retained.AdvanceTick(isRaining: true);
        if (retained.GetPlot(second).HasWater || retained.Calendar.ElapsedSeconds != 0)
            return Fail("暂停期间降雨改变了农田或日历");
        retained.SetPaused(false);
        retained.AdvanceTick(isRaining: true);
        if (retained.GetPlot(second).Crop != CropStage.None ||
            !retained.GetPlot(second).HasWater ||
            retained.GetFarmDetails(second).Status != FarmStatus.WaitingForWorkerWithWater)
            return Fail("未播种农田没有在雨后留存水分");
        // 第二人从 (64,63) 走到 (67,63)，到田的秒不能同时播种。
        if (retained.AdvanceTick().WorkerActed || retained.AdvanceTick().WorkerActed ||
            retained.GetPlot(second).Crop != CropStage.None)
            return Fail("雨停后的移动秒被错误计为播种");
        TickResult usedRain = retained.AdvanceTick();
        if (!usedRain.WorkerActed || retained.GetPlot(second).Crop != CropStage.Growing ||
            retained.GetPlot(second).RemainingSeconds != 206)
            return Fail("雨停后播种没有使用留存水分开始生长");

        var seeded = new FarmGame(12345);
        RemoveInitialBuildings(seeded);
        Vector2I farm = new(63, 63);
        seeded.BuildFarm(farm);
        seeded.SetFarmCrop(farm, CropKind.Radish);
        seeded.AdvanceTick();
        if (seeded.GetPlot(farm).Crop != CropStage.Seeded || seeded.GetPlot(farm).HasWater)
            return Fail("无雨时农田未进入待水阶段");
        TickResult rainOnSeed = seeded.AdvanceTick(isRaining: true);
        if (rainOnSeed.WorkerActed || seeded.GetPlot(farm).Crop != CropStage.Growing ||
            seeded.GetPlot(farm).RemainingSeconds != 205 ||
            !seeded.GetPlot(farm).HasWater)
            return Fail("已播种农田遇雨后没有从本秒开始生长，或工人重复浇水");
        seeded.SetFarmCrop(farm, CropKind.Potato);
        if (!seeded.GetPlot(farm).HasWater || seeded.GetPlot(farm).Crop != CropStage.None ||
            !seeded.AdvanceTick().WorkerActed ||
            seeded.GetPlot(farm).Crop != CropStage.Growing)
            return Fail("改种后没有保留水分，或又安排了浇水动作");
        seeded.RemoveBuilding(farm);
        seeded.BuildFarm(farm);
        if (seeded.GetPlot(farm).HasWater)
            return Fail("拆除重建的农田保留了旧水分");

        var continuous = new FarmGame(12345);
        RemoveInitialBuildings(continuous);
        continuous.BuildFarm(farm);
        continuous.SetFarmCrop(farm, CropKind.Radish);
        continuous.AdvanceTick();
        continuous.AdvanceTick();
        TickResult repeatedRain = continuous.AdvanceTick(isRaining: true);
        if (repeatedRain.WorkerActed || continuous.GetPlot(farm).RemainingSeconds != 205)
            return Fail("人工浇水后遇雨重置了生长进度");
        TickResult harvest = default;
        for (int step = 0; step < 205; step++)
            harvest = continuous.AdvanceTick(isRaining: true);
        if (harvest.Harvested != 6 || continuous.GetRawStock(CropKind.Radish) != 6 ||
            continuous.GetPlot(farm).Crop != CropStage.Seeded ||
            continuous.GetPlot(farm).HasWater)
            return Fail("持续降雨中收获没有清除本轮水分");
        TickResult nextRain = continuous.AdvanceTick(isRaining: true);
        if (nextRain.WorkerActed || continuous.GetPlot(farm).Crop != CropStage.Growing ||
            !continuous.GetPlot(farm).HasWater ||
            continuous.GetPlot(farm).RemainingSeconds != 205)
            return Fail("收获后的下一秒持续降雨没有重新供水");
        return true;
    }

    private static bool CheckSeasonalSowing()
    {
        var summer = new FarmGame(12345, 4320);
        RemoveInitialBuildings(summer);
        Vector2I farm = new(63, 63);
        summer.BuildFarm(farm);
        summer.SetFarmCrop(farm, CropKind.Potato);
        int money = summer.MoneyCents;
        if (summer.GetFarmDetails(farm).Status != FarmStatus.WrongSeason ||
            summer.AdvanceTick().WorkerActed ||
            summer.GetPlot(farm).Crop != CropStage.None ||
            summer.GetPlot(farm).CropKind != CropKind.Potato ||
            summer.GetRawStock(CropKind.Potato) != 0 || summer.MoneyCents != money)
            return Fail("不适季仍播种，或拒绝后改动了作物、库存、金币");

        summer.SetFarmCrop(farm, CropKind.Sugarcane);
        if (summer.GetFarmDetails(farm).Status != FarmStatus.WaitingForWorker ||
            !summer.AdvanceTick().WorkerActed || summer.GetPlot(farm).Crop != CropStage.Seeded)
            return Fail("改为当季适宜作物后没有恢复自动播种");

        var boundary = new FarmGame(12345, 4320 - 207);
        RemoveInitialBuildings(boundary);
        boundary.BuildFarm(farm);
        boundary.SetFarmCrop(farm, CropKind.Radish);
        if (boundary.GetFarmDetails(farm).Status != FarmStatus.InsufficientTime ||
            boundary.AdvanceTick(isRaining: true).WorkerActed != true ||
            boundary.GetPlot(farm).Crop != CropStage.Growing)
            return Fail("季末干田时间不足后，雨水留存没有使播种恢复");
        return true;
    }

    private static bool CheckSeasonFailure()
    {
        // 春季 3 月 22 日起步，真实生产留下历史原料与成品。
        var game = new FarmGame(12345, 4000);
        RemoveInitialBuildings(game);
        Vector2I historyFarm = new(63, 63);
        Vector2I processor = new(100, 100);
        game.BuildFarm(historyFarm);
        game.SetFarmCrop(historyFarm, CropKind.Radish);
        for (int step = 0; step < 208; step++)
            game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != 6)
            return Fail("越季连接回归没有建立真实历史收成");
        game.BuildProcessor(processor, CropKind.Radish);
        for (int step = 0; step < 26; step++)
            game.AdvanceTick();
        game.RemoveBuilding(processor); // 拆除第二批已投入物，留下 4 原料与 1 成品。
        while (game.Calendar.ElapsedSeconds < 4319)
            game.AdvanceTick();
        int rawStock = game.GetRawStock(CropKind.Radish);
        int productStock = game.GetProductStock(CropKind.Radish);
        int money = game.MoneyCents;
        if (rawStock != 4 || productStock != 1)
            return Fail("越季连接回归没有保留两类历史库存");

        // 复用现有负载夹具建立临界点进行中的作物，不人为延迟工人供水。
        // 正常调度实际播种后原地接续浇水，旧轮转排队失败场景已不再适用。
        game.FillWorldForBenchmark();
        Vector2I farm = new(12, 0); // 夹具既有布局中的萝卜田。
        if (game.GetPlot(farm).CropKind != CropKind.Radish ||
            game.GetPlot(farm).Crop != CropStage.Growing || !game.GetPlot(farm).HasWater)
            return Fail("越季连接夹具没有建立进行中的萝卜");
        TickResult crossing = game.AdvanceTick();
        PlotSnapshot cleared = new(BuildingKind.Farm, CropKind.Radish, CropStage.None, 0);
        if (!crossing.DayAdvanced || crossing.Harvested != 0 ||
            game.GetPlot(farm) != cleared ||
            game.GetFarmDetails(farm).Status != FarmStatus.WrongSeason ||
            game.GetRawStock(CropKind.Radish) != rawStock ||
            game.GetProductStock(CropKind.Radish) != productStock || game.MoneyCents != money ||
            !game.HasConsistentState())
            return Fail("越季调用没有清理本轮作物，或影响选种、历史库存与金币");
        for (int step = 0; step < 10; step++)
            if (game.AdvanceTick().Harvested != 0 || game.GetPlot(farm) != cleared ||
                game.GetRawStock(CropKind.Radish) != rawStock ||
                game.GetProductStock(CropKind.Radish) != productStock || game.MoneyCents != money)
                return Fail("换季清理后反复播种，或重复清理影响历史库存与金币");
        return true;
    }

    private static bool CheckSeasonBoundaries()
    {
        Vector2I farm = new(63, 63);
        // 春末最后两秒播种玉米，春夏连续适宜，跨季后仍按原进度生长。
        var continuous = new FarmGame(12345, 4318);
        RemoveInitialBuildings(continuous);
        continuous.BuildFarm(farm);
        continuous.SetFarmCrop(farm, CropKind.Corn);
        continuous.AdvanceTick(isRaining: true);
        continuous.AdvanceTick();
        if (continuous.GetPlot(farm) !=
            new PlotSnapshot(BuildingKind.Farm, CropKind.Corn, CropStage.Growing, 1028, true))
            return Fail("相邻适宜季节误清了生长中的玉米");

        // 春末剩 208 秒播种干田萝卜，下一秒浇水，恰好在夏季起点成熟。
        var exact = new FarmGame(12345, 4112);
        RemoveInitialBuildings(exact);
        exact.BuildFarm(farm);
        exact.SetFarmCrop(farm, CropKind.Radish);
        for (int step = 0; step < 207; step++)
            exact.AdvanceTick();
        TickResult harvest = exact.AdvanceTick();
        if (!harvest.DayAdvanced || harvest.Harvested != 6 ||
            exact.GetRawStock(CropKind.Radish) != 6 ||
            exact.GetPlot(farm) != new PlotSnapshot(BuildingKind.Farm, CropKind.Radish, CropStage.None, 0) ||
            exact.GetFarmDetails(farm).Status != FarmStatus.WrongSeason)
            return Fail("恰在换季时成熟的萝卜被清理或没有先收获入库");
        return true;
    }

    private static bool CheckRawSales()
    {
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            var game = new FarmGame(12345,
                crop.Kind == CropKind.Sugarcane ? 4320u : 0u);
            RemoveInitialBuildings(game);
            Vector2I farm = new(63, 63);
            game.BuildFarm(farm);
            game.SetFarmCrop(farm, crop.Kind);
            int growthSeconds = (crop.GrowthDays * 360 + 6) / 7;
            for (int i = 0; i < growthSeconds + 2; i++)
                game.AdvanceTick();
            int stock = game.GetRawStock(crop.Kind);
            int money = game.MoneyCents;
            int price = game.GetRawPriceCents(crop.Kind);
            if (stock != crop.HarvestQuantity || game.GetProductStock(crop.Kind) != 0)
                return Fail($"{crop.CropName}原料未进入可出售库存");
            SaleResult sale = game.SellRaw(crop.Kind);
            if (sale.Quantity != stock || sale.RevenueCents != stock * price ||
                game.MoneyCents != money + sale.RevenueCents || game.GetRawStock(crop.Kind) != 0 ||
                game.SellRaw(crop.Kind).Quantity != 0 || game.SellAll().Quantity != 0)
                return Fail($"{crop.CropName}原料出售数量、当日收入或重复出售错误");
        }

        var mixed = new FarmGame(12345);
        RemoveInitialBuildings(mixed);
        Vector2I wheatFarm = new(63, 63);
        Vector2I cornFarm = new(64, 63);
        mixed.BuildFarm(wheatFarm);
        mixed.BuildFarm(cornFarm);
        mixed.SetFarmCrop(cornFarm, CropKind.Corn);
        for (int i = 0; i < 1033; i++)
            mixed.AdvanceTick();
        int cornStock = mixed.GetRawStock(CropKind.Corn);
        if (mixed.GetRawStock(CropKind.Wheat) == 0 || cornStock == 0)
            return Fail("两种原料没有分别进入库存");
        mixed.SellRaw(CropKind.Wheat);
        if (mixed.GetRawStock(CropKind.Corn) != cornStock)
            return Fail("出售小麦原料改变了玉米原料库存");
        return true;
    }

    private static bool CheckInvalidCrops()
    {
        var game = new FarmGame(12345);
        CropKind invalid = (CropKind)999;
        Vector2I processor = new(4, 5);
        if (game.BuildProcessor(processor, invalid) == null ||
            game.GetPlot(processor).Building != BuildingKind.None || game.MoneyCents != 5000)
            return Fail("无效作物的加工场地建造修改了金币或土地");

        Vector2I farm = new(5, 5);
        if (game.BuildFarm(farm) != null || game.SetFarmCrop(farm, invalid) == null ||
            game.GetPlot(farm).CropKind != CropKind.Wheat || game.MoneyCents != 4000)
            return Fail("无效作物改种修改了农田或金币");
        return true;
    }

    private static bool CheckStateOwnership()
    {
        var game = new FarmGame(12345);
        if (!game.HasConsistentState())
            return Fail("开局占用与生产状态不一致");
        RemoveInitialBuildings(game);
        Vector2I farm = new(0, 0);
        Vector2I processor = new(1, 0);
        game.BuildFarm(farm);
        game.SetFarmCrop(farm, CropKind.Rice);
        game.BuildProcessor(processor, CropKind.Rice);
        game.AdvanceTick();
        if (!game.HasConsistentState() ||
            game.GetPlot(farm).Building != BuildingKind.Farm ||
            game.GetPlot(farm).CropKind != CropKind.Rice ||
            game.GetPlot(processor).Building != BuildingKind.Processor)
            return Fail("建造或推进后占用与生产状态不一致");
        game.RemoveBuilding(farm);
        game.RemoveBuilding(processor);
        if (!game.HasConsistentState() ||
            game.GetPlot(farm).Building != BuildingKind.None ||
            game.GetPlot(processor).Building != BuildingKind.None)
            return Fail("拆除后留下农田或加工状态");

        game.FillWorldForBenchmark();
        Vector2I corner = new(127, 127);
        if (!game.HasConsistentState() ||
            game.GetPlot(new Vector2I(0, 0)).Building != BuildingKind.Farm ||
            game.GetPlot(corner).Building != BuildingKind.Processor)
            return Fail("满地图夹具占用与生产状态不一致");
        game.RemoveBuilding(corner);
        if (game.BuildFarm(corner) != null || !game.HasConsistentState() ||
            game.GetPlot(corner).Building != BuildingKind.Farm)
            return Fail("满地图拆除重建留下重复状态");
        return true;
    }

    private static bool CheckPlacementRules()
    {
        var game = new FarmGame(12345);
        Vector2I target = new(0, 0);
        PlacementCheck preview = game.CheckPlacement(target, BuildingKind.Farm, CropKind.Wheat);
        if (!preview.Allowed || preview.CostCents != FarmGame.BuildingCostCents)
            return Fail("空地放置预检没有返回费用");
        if (game.CheckPlacement(new Vector2I(-1, 0), BuildingKind.Farm, CropKind.Wheat).Failure !=
                LandFailure.OutOfBounds ||
            game.CheckPlacement(new Vector2I(128, 0), BuildingKind.Farm, CropKind.Wheat).Failure !=
                LandFailure.OutOfBounds ||
            game.TryGetPlot(new Vector2I(0, 128), out _) != LandFailure.OutOfBounds ||
            game.RemoveBuilding(new Vector2I(-1, 0)) != "地图外地块" ||
            game.SetFarmCrop(new Vector2I(128, 0), CropKind.Corn) != "地图外地块")
            return Fail("地图外放置、查询、拆除或选种结果不一致");
        if (game.TryPlace(new Vector2I(-1, 0), BuildingKind.Farm, CropKind.Wheat).Failure !=
                LandFailure.OutOfBounds ||
            game.TryPlace(target, (BuildingKind)999, CropKind.Wheat).Failure !=
                LandFailure.InvalidBuilding ||
            game.TryPlace(target, BuildingKind.Processor, (CropKind)999).Failure !=
                LandFailure.InvalidCrop || game.MoneyCents != 5000 ||
            game.GetPlot(target).Building != BuildingKind.None)
            return Fail("无效放置修改了余额或占用");

        PlacementResult placed = game.TryPlace(target, BuildingKind.Farm, CropKind.Wheat);
        if (!placed.Success || placed.ChargedCents != 1000 || game.MoneyCents != 4000 ||
            game.TryPlace(target, BuildingKind.Farm, CropKind.Wheat).Failure != LandFailure.Occupied ||
            game.GetPlot(target).Building != BuildingKind.Farm)
            return Fail("放置执行、收费或占用检查错误");
        PlotSnapshot snapshot = game.GetPlot(target);
        snapshot = snapshot with { Building = BuildingKind.Processor };
        if (game.TryGetPlot(target, out PlotSnapshot queried) != LandFailure.None ||
            queried.Building != BuildingKind.Farm || game.GetPlot(target).Building != BuildingKind.Farm ||
            !game.HasConsistentState())
            return Fail("地块快照修改了经营状态");

        Vector2I occupiedLater = new(1, 0);
        if (!game.CheckPlacement(occupiedLater, BuildingKind.Farm, CropKind.Wheat).Allowed ||
            game.BuildFarm(occupiedLater) != null ||
            game.TryPlace(occupiedLater, BuildingKind.Farm, CropKind.Wheat).Failure !=
                LandFailure.Occupied)
            return Fail("预检后占用变化未重新验证");
        for (int col = 2; col <= 3; col++)
            if (game.BuildFarm(new Vector2I(col, 0)) != null)
                return Fail("余额准备失败");
        Vector2I insufficientLater = new(5, 0);
        if (!game.CheckPlacement(insufficientLater, BuildingKind.Farm, CropKind.Wheat).Allowed ||
            game.BuildFarm(new Vector2I(4, 0)) != null ||
            game.TryPlace(insufficientLater, BuildingKind.Farm, CropKind.Wheat).Failure !=
                LandFailure.InsufficientFunds || game.MoneyCents != 0 ||
            game.GetPlot(insufficientLater).Building != BuildingKind.None)
            return Fail("预检后余额变化未重新验证");
        if (game.RemoveBuilding(target) != null || game.GetPlot(target).Building != BuildingKind.None ||
            game.BuildFarm(target) == null || !game.HasConsistentState())
            return Fail("拆除后占用或余额规则错误");
        return true;
    }

    private static bool CheckRawReservePhases()
    {
        var game = new FarmGame(12345);
        RemoveInitialBuildings(game);
        foreach (CropDefinition crop in FarmGame.Crops)
            if (game.GetRawReserve(crop.Kind) != 0)
                return Fail("经营入口的保留底线默认值错误");
        if (game.SetRawReserve((CropKind)999, 1) != RawReserveFailure.InvalidCrop ||
            game.SetRawReserve(CropKind.Radish, -1) != RawReserveFailure.InvalidQuantity ||
            game.GetRawReserve(CropKind.Radish) != 0 || game.MoneyCents != 5000)
            return Fail("非法底线命令未拒绝，或修改了经营状态");

        Vector2I farm = new(63, 63);
        Vector2I first = new(127, 0);
        Vector2I second = new(0, 1);
        game.SetRawReserve(CropKind.Radish, 6);
        game.BuildFarm(farm);
        game.SetFarmCrop(farm, CropKind.Radish);
        game.BuildProcessor(second, CropKind.Radish);
        game.BuildProcessor(first, CropKind.Radish);
        game.AdvanceTick(isRaining: true);
        for (int i = 0; i < 206; i++) game.AdvanceTick();
        game.RemoveBuilding(farm);
        if (game.GetRawStock(CropKind.Radish) != 6 ||
            game.GetProcessorDetails(first).Status != ProcessorStatus.WaitingForReserve ||
            game.GetProcessorDetails(second).Status != ProcessorStatus.WaitingForReserve)
            return Fail("原料保留底线阻塞了农田收获，或空闲场地错误领取");

        game.SetRawReserve(CropKind.Radish, 5);
        game.SetRawReserve(CropKind.Radish, 5);
        if (game.GetRawStock(CropKind.Radish) != 6 ||
            game.GetPlot(first).RemainingSeconds != 0 ||
            game.GetProcessorDetails(first).Status != ProcessorStatus.ReadyToProcess)
            return Fail("底线修改或查询立即启动了领取");
        game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != 5 ||
            game.GetPlot(first).RemainingSeconds != 26 || game.GetPlot(second).RemainingSeconds != 0)
            return Fail("下一经营领取阶段未按格序竞争，或取走了保留部分");

        game.SetRawReserve(CropKind.Radish, 99);
        if (game.GetRawStock(CropKind.Radish) != 5 || game.GetPlot(first).RemainingSeconds != 26 ||
            game.GetProcessorDetails(first).Status != ProcessorStatus.Processing)
            return Fail("提高底线退回投入物或重置了批次");
        SaleResult rawSale = game.SellRaw(CropKind.Radish);
        if (rawSale.Quantity != 5 || game.GetRawStock(CropKind.Radish) != 0 ||
            game.GetRawReserve(CropKind.Radish) != 99 ||
            game.GetProcessorDetails(second).Status != ProcessorStatus.WaitingForRaw)
            return Fail("出售未包含保留公共原料，或错误出售了投入物");
        for (int i = 0; i < 26; i++) game.AdvanceTick();
        if (game.GetProductStock(CropKind.Radish) != 1 ||
            game.GetPlot(first).RemainingSeconds != 0 ||
            rawSale.Quantity + game.GetProductStock(CropKind.Radish) != 6)
            return Fail("收获、领取、出售与加工完成数量不守恒");
        SaleResult products = game.SellAll();
        if (products.Quantity != 1 || rawSale.Quantity + products.Quantity != 6 ||
            game.GetProductStock(CropKind.Radish) != 0)
            return Fail("成品出售后数量不守恒");
        return true;
    }

    private static bool CheckRawReserveConstruction()
    {
        var game = new FarmGame(12345);
        RemoveInitialBuildings(game);
        Vector2I farm = new(63, 63);
        Vector2I existing = new(0, 0);
        Vector2I later = new(0, 1);
        Vector2I earlier = new(1, 0);
        game.SetRawReserve(CropKind.Radish, 6);
        game.BuildFarm(farm);
        game.SetFarmCrop(farm, CropKind.Radish);
        game.BuildProcessor(existing, CropKind.Radish);
        game.AdvanceTick(isRaining: true);
        for (int i = 0; i < 206; i++) game.AdvanceTick();
        game.RemoveBuilding(farm);
        game.SetRawReserve(CropKind.Radish, 5);
        if (game.BuildProcessor(later, CropKind.Radish) != null ||
            game.GetPlot(existing).RemainingSeconds != 26 || game.GetPlot(later).RemainingSeconds != 0 ||
            game.GetRawStock(CropKind.Radish) != 5)
            return Fail("新建未立即执行全场格序领取，或取走底线库存");

        game.RemoveBuilding(existing); // 已投入的一份随拆除丢弃，不返回公共库存。
        game.SetRawReserve(CropKind.Radish, 4);
        if (game.BuildProcessor(earlier, CropKind.Radish) != null ||
            game.GetPlot(earlier).RemainingSeconds != 26 || game.GetPlot(later).RemainingSeconds != 0 ||
            game.GetRawStock(CropKind.Radish) != 4 || game.GetProductStock(CropKind.Radish) != 0 ||
            game.GetRawStock(CropKind.Radish) + 1 + 1 != 6 || !game.HasConsistentState())
            return Fail("新场地未按格序立即领取，或拆除损耗/投入/公共数量不守恒");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
