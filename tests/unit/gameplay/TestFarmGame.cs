using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Market;
using FarmExchange.Time;
using FarmExchange.Trading;
using FarmExchange.Workers;
using FarmExchange.Weather;
using FarmExchange.Terrain;
using FarmExchange.Cultivation;

public partial class TestFarmGame : Node
{
    public override async void _Ready()
    {
        try
        {
            bool passed = RunChecks() && await RunRoadChecksAsync(GetTree()) && await RunWaterChecksAsync(GetTree());
            if (passed)
                GD.Print("农田、加工与交易规则检查通过");
            GetTree().Quit(passed ? 0 : 1);
        }
        catch (System.Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    public static bool RunChecks() =>
        TestProductionDueIntegration.RunChecks() && CheckInitialCenter() && CheckInitialOtherSeasons() && CheckAllCrops() && CheckBuildingCost() && CheckMatchingAndSwitching() && CheckRemoval() &&
        CheckWorkerParticipation() && CheckContinuousProduction() && CheckRainSupply() && CheckSeasonalSowing() &&
        CheckSeasonFailure() && CheckSeasonBoundaries() && CheckRawSales() &&
        CheckInvalidCrops() && CheckStateOwnership() && CheckTerrainReadCapability() &&
        CheckPlacementRules() && CheckRawReservePhases() && CheckRawReserveConstruction() &&
        CheckMultiCellFacilities() && CheckBuildingOrientations() && CheckOrientationFailures() &&
        CheckStableSnapshotForwarding() && CheckBatchRoadPlacement() && CheckBatchRoadFailures() &&
        CheckBatchRoadFrozenMoney() && CheckBatchRoadRemoval() && CheckMovementSowingPermission();

    public static async Task<bool> RunRoadChecksAsync(SceneTree tree) =>
        await CheckRoadPlacement(tree) && await CheckRoadDoesNotClaimInventory(tree) &&
        await CheckRoadIndependentProduction(tree) && await CheckRoadPreparationAsync(tree);

    private static async Task<bool> CheckRoadPreparationAsync(SceneTree tree)
    {
        using var single = new FarmGame(12345, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        using var batch = new FarmGame(12345, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        Vector2I farm = new(210, 211);
        foreach (FarmGame game in new[] { single, batch })
        {
            RemoveInitialBuildings(game);
            if (!game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish).Success)
                return Fail("道路导航夹具建田失败");
            game.AdvanceTick();
            var before = game.GetWorkers();
            if (before[0].Path == null || before[0].Activity != WorkerActivity.Moving)
                return Fail("道路准备前没有已领取的真实行程");
            uint seconds = game.Calendar.ElapsedSeconds;
            Vector2I road = new(191, 185);
            if (!game.TryPlaceRoads(new[] { road }, RoadMaterial.Gravel).Success || game.IsNavigationReady)
                return Fail("成功铺路没有立即关闭经营准备状态");
            try { game.AdvanceTick(); return Fail("道路尚未同步却推进了单秒经营"); }
            catch (System.InvalidOperationException) { }
            try { game.AdvanceTicks(5); return Fail("道路尚未同步却推进了批量经营"); }
            catch (System.InvalidOperationException) { }
            if (game.Calendar.ElapsedSeconds != seconds || !before.SequenceEqual(game.GetWorkers()))
                return Fail("准备前拒绝改变了经营秒或工人行程");
            Task preparation = game.WaitForNavigationAsync(tree);
            if (!ReferenceEquals(preparation, game.WaitForNavigationAsync(tree)))
                return Fail("同一局同时启动了多个道路准备任务");
            if (!game.TryPlace(new(192, 185), BuildingKind.Road, default).Success || game.RemoveBuilding(road) != null)
                return Fail("道路准备期间不能继续成功建拆");
            using (var waiter = new System.Threading.CancellationTokenSource())
            {
                Task canceledWait = game.WaitForNavigationAsync(tree, waiter.Token);
                waiter.Cancel();
                try { await canceledWait; return Fail("调用者取消没有终止自己的等待"); }
                catch (System.OperationCanceledException) { }
            }
            await preparation;
            if (!game.IsNavigationReady || game.Calendar.ElapsedSeconds != seconds ||
                !before.SequenceEqual(game.GetWorkers()) || !ReferenceEquals(before[0].Path, game.GetWorkers()[0].Path))
                return Fail("连续建拆未同步最终版本，或准备修改了既有行程与经营秒");
            int money = game.MoneyCents;
            var spaces = game.GetBuildingSpaces();
            try { game.FillWorldForBenchmark(); return Fail("有已同步道路时满图夹具清除了道路成本对应的占用"); }
            catch (System.InvalidOperationException) { }
            if (!game.IsNavigationReady || !ReferenceEquals(spaces, game.GetBuildingSpaces()) ||
                money != game.MoneyCents || game.Calendar.ElapsedSeconds != seconds || !before.SequenceEqual(game.GetWorkers()))
                return Fail("满图夹具拒绝前修改了道路、经营资源或导航就绪状态");
            if (game.TryPlaceRoads(new[] { new Vector2I(192, 185) }, RoadMaterial.Brick).Success ||
                game.TryRemoveRoads(new[] { new Vector2I(-1, 0) }).Success ||
                !game.IsNavigationReady || money != game.MoneyCents || !ReferenceEquals(spaces, game.GetBuildingSpaces()))
                return Fail("失败道路命令改变了就绪状态或正式资源");
            game.AdvanceTick();
            WorkerSnapshot continuing = game.GetWorkers()[0];
            if (!ReferenceEquals(before[0].Path, continuing.Path) ||
                continuing.ExactPosition != continuing.Path!.GetPositionAtDistance(6))
                return Fail("道路准备后原任务没有沿原行程继续每秒三格");
            if (game.SetFarmCrop(farm, CropKind.Radish) != null)
                return Fail("道路重规划夹具不能重新开始本轮");
            LandPath latest = game.Terrain.FindLandPath(continuing.ExactPosition,
                LandPosition.FromCell(BuildingFootprint.WorkCell(farm, BuildingKind.Farm))).Path!;
            game.AdvanceTick();
            WorkerSnapshot replanned = game.GetWorkers()[0];
            if (replanned.Path == null || ReferenceEquals(replanned.Path, continuing.Path) || !replanned.Path.Equals(latest))
                return Fail("任务失效后的新规划未使用已同步道路与真实连续起点");
        }
        single.AdvanceTick();
        batch.AdvanceTicks(1);
        for (int tick = 0; tick < 48; tick++) single.AdvanceTick();
        batch.AdvanceTicks(48);
        if (single.Calendar != batch.Calendar || !single.GetWorkers().SequenceEqual(batch.GetWorkers()) ||
            single.GetPlot(farm) != batch.GetPlot(farm) || single.GetRawStock(CropKind.Radish) != batch.GetRawStock(CropKind.Radish))
            return Fail("有路准备后的单秒与批量经营不一致");
        foreach (FarmGame game in new[] { single, batch })
        {
            if (!game.TryRemoveRoads(new[] { new Vector2I(192, 185) }).Success || game.IsNavigationReady)
                return Fail("最后道路拆除没有触发准备");
            await game.WaitForNavigationAsync(tree);
            if (!game.IsNavigationReady || game.GetBuildingSpaces().Any(space => space.Building == BuildingKind.Road))
                return Fail("道路清空后未恢复已就绪的普通陆地导航");
        }
        var disposed = new FarmGame(12345, terrainRules: TerrainTestFixtures.AllLandRules);
        disposed.TryPlace(new(10, 10), BuildingKind.Road, default);
        Task pending = disposed.WaitForNavigationAsync(tree);
        disposed.Dispose();
        disposed.Dispose();
        try { await pending; return Fail("完整释放局没有取消实际道路准备"); }
        catch (System.OperationCanceledException) { }
        try { _ = disposed.IsNavigationReady; return Fail("完整释放后的导航查询没有明确拒绝"); }
        catch (ObjectDisposedException) { }
        try
        {
            disposed.Terrain.FindLandPath(LandPosition.FromCell(new(10, 10)), LandPosition.FromCell(new(11, 10)));
            return Fail("局释放后读取视图仍能返回路线");
        }
        catch (ObjectDisposedException) { }
        return true;
    }

    private static bool CheckTerrainReadCapability()
    {
        using var game = new FarmGame(12345, terrainRules: TerrainTestFixtures.AllLandRules);
        TerrainReadView terrain = game.Terrain;
        Type exposed = typeof(FarmGame).GetProperty(nameof(FarmGame.Terrain))!.PropertyType;
        if (exposed != terrain.GetType() || !exposed.IsSealed || typeof(IDisposable).IsAssignableFrom(exposed) ||
            exposed.GetConstructors().Length != 0 || exposed.GetFields().Length != 0 ||
            exposed.GetProperties().Any(property => property.CanWrite || property.PropertyType == typeof(TerrainMap)))
            return Fail("公开地形读取能力泄露拥有者、可变成员或资源释放能力");
        string[] queries = { "get_Seed", "get_IsAllLand", "GetCell", "IsFullLand", "FindLandPath" };
        if (exposed.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Any(method => !queries.Contains(method.Name)))
            return Fail("公开地形读取能力含有查询以外的操作");
        if (!ReferenceEquals(terrain, game.Terrain) || terrain.Seed != 12345 || !terrain.IsAllLand ||
            !terrain.GetCell(Vector2I.Zero).IsFullLand || !terrain.IsFullLand(Vector2I.Zero) ||
            terrain.IsFullLand(new(-1, 0)))
            return Fail("本局稳定读取视图未保留种子和完整陆地语义");
        try { terrain.GetCell(new(-1, 0)); return Fail("读取视图将越界地形查询吞成正常结果"); }
        catch (ArgumentOutOfRangeException) { }
        return true;
    }

    private static bool CheckMovementSowingPermission()
    {
        using var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        foreach (BuildingSpaceSnapshot space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        Vector2I farm = new(210, 211);
        if (!game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish).Success)
            return Fail("移动许可夹具建田失败");
        game.AdvanceTick();
        WorkerSnapshot worker = game.GetWorkers()[0];
        if (worker.Movement is not WorkerMovementSnapshot original || !game.IsWorkerMovementCurrent(original))
            return Fail("移动许可夹具没有开始真实播种行程");
        int plan = game.CreateCultivationPlan(new("移动许可回归", CultivationMode.Immediate,
            System.Array.Empty<CultivationEntry>())).Id;
        if (game.ApplyCultivationPlan(plan, new[] { farm }) != null || game.IsWorkerMovementCurrent(original))
            return Fail("休耕没有立即失效原播种移动凭据");
        if (game.DeleteCultivationPlan(plan) != null || game.IsWorkerMovementCurrent(original))
            return Fail("恢复自动播种复活了旧移动许可");
        game.AdvanceTick();
        WorkerSnapshot resumed = game.GetWorkers()[0];
        if (resumed.Path?.Points[0] != worker.ExactPosition ||
            resumed.Movement is not WorkerMovementSnapshot replacement || !game.IsWorkerMovementCurrent(replacement))
            return Fail("新播种许可没有从原连续位置领取有效行程");
        return true;
    }

    public static async Task<bool> RunWaterChecksAsync(SceneTree tree)
    {
        using var game = await FarmGame.CreateAsync(tree, 12345, weatherMode: WeatherMode.Explicit);
        TerrainReadView terrain = game.Terrain;
        Vector2I water = default;
        bool found = false;
        for (int y = 1; y < FarmGame.MapSize - 2 && !found; y++)
            for (int x = 1; x < FarmGame.MapSize - 2; x++)
                if (!terrain.IsFullLand(new Vector2I(x, y)))
                {
                    water = new Vector2I(x, y);
                    found = true;
                    break;
                }
        if (!found) return Fail("正式默认地形没有含水格");
        var spaces = game.GetBuildingSpaces();
        var workers = game.GetWorkers();
        int money = game.MoneyCents;
        foreach (BuildingKind building in new[] { BuildingKind.Farm, BuildingKind.Processor, BuildingKind.Road })
        {
            // 生产设施由九格末端触水，防止只检查锚点或工作中心。
            Vector2I anchor = building == BuildingKind.Road ? water : water - new Vector2I(2, 2);
            if (game.CheckPlacement(anchor, building, CropKind.Wheat).Failure != LandFailure.Water ||
                game.TryPlace(anchor, building, CropKind.Wheat) != new PlacementResult(LandFailure.Water, 0))
                return Fail("设施未按完整占地拒绝含水岸格，或预检与执行不一致");
        }
        Vector2I[] segment = { Vector2I.Zero, new(1, 0), water };
        if (game.CheckRoadPlacement(segment, RoadMaterial.Dirt).Failure != LandFailure.Water ||
            game.TryPlaceRoads(segment, RoadMaterial.Dirt).Failure != LandFailure.Water ||
            game.MoneyCents != money || !ReferenceEquals(spaces, game.GetBuildingSpaces()) ||
            !workers.SequenceEqual(game.GetWorkers()) || game.GetBuildingSpace(Vector2I.Zero) != null)
            return Fail("含水道路失败未保持资金、完整空间与工人零修改");
        try
        {
            game.FillWorldForBenchmark();
            return Fail("含水局被满图夹具替换为无水布局");
        }
        catch (System.InvalidOperationException)
        {
            if (!ReferenceEquals(terrain, game.Terrain) || !ReferenceEquals(spaces, game.GetBuildingSpaces()))
                return Fail("满图夹具失败前修改了地形或设施");
        }
        using var allLand = new FarmGame(12345, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        if (!game.GetBuildingSpaces().Select(space => (space.AnchorCell, space.Building, space.Orientation))
                .SequenceEqual(allLand.GetBuildingSpaces().Select(space => (space.AnchorCell, space.Building, space.Orientation))) ||
            game.GetBuildingSpaces().Any(space => game.GetPlot(space.AnchorCell) != allLand.GetPlot(space.AnchorCell)))
            return Fail("独立地形生成消耗了原开局作物随机序列");
        foreach (BuildingSpaceSnapshot space in spaces.Where(space => space.Building == BuildingKind.Farm))
            game.RemoveBuilding(space.AnchorCell);
        if (!game.TryPlace(Vector2I.Zero, BuildingKind.Farm, CropKind.Wheat).Success)
            return Fail("完整边缘陆地被误拒绝");
        game.AdvanceTick();
        WorkerSnapshot moving = game.GetWorkers()[0];
        if (moving.Movement is not WorkerMovementSnapshot movement || !game.IsWorkerMovementCurrent(movement) ||
            game.IsWorkerMovementCurrent(movement with { WorkActivity = WorkerActivity.Idle }) ||
            game.IsWorkerMovementCurrent(movement with { TargetCell = new Vector2I(-1, -1) }))
            return Fail("真实移动记录的目标或任务重验错误");
        game.SetFarmCrop(Vector2I.Zero, CropKind.Corn);
        if (game.IsWorkerMovementCurrent(movement) || game.GetWorkers()[0].ExactPosition != moving.ExactPosition)
            return Fail("改种未立即失效旧移动凭据或命令导致工人瞬移");
        game.AdvanceTick();
        WorkerSnapshot resumed = game.GetWorkers()[0];
        if (resumed.Path?.Points[0] != moving.ExactPosition || resumed.Movement is not WorkerMovementSnapshot replacement)
            return Fail("改种后工人没有从精确原位置重新求路");
        game.RemoveBuilding(Vector2I.Zero);
        if (game.IsWorkerMovementCurrent(replacement))
            return Fail("拆除未立即失效旧路线凭据");
        return true;
    }

    private static bool CheckBatchRoadPlacement()
    {
        using var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        game.SetPaused(true);
        var initial = game.GetBuildingSpaces();
        var workers = game.GetWorkers();
        int money = game.MoneyCents;
        int start = 0;
        foreach (RoadMaterial material in System.Enum.GetValues<RoadMaterial>())
        {
            Vector2I[] cells = { new(start, 0), new(start + 1, 0), new(start + 1, 1), new(start + 1, 2) };
            var before = game.GetBuildingSpaces();
            if (game.CheckRoadPlacement(cells, material) != new RoadPlacementCheck(LandFailure.None, 4, 400) ||
                !ReferenceEquals(before, game.GetBuildingSpaces()) ||
                game.TryPlaceRoads(cells, material) != new RoadPlacementResult(LandFailure.None, 4, 400))
                return Fail("道路批量预检改变正式状态或建造扣费不完整");
            foreach (Vector2I cell in cells)
            {
                BuildingSpaceSnapshot? space = game.GetBuildingSpace(cell);
                if (space?.Building != BuildingKind.Road || space.RoadMaterial != material ||
                    space.Orientation != BuildingOrientation.Q0 || space.Footprint.Offsets.Count != 1 ||
                    game.GetPlot(cell) != new PlotSnapshot(BuildingKind.Road, default, CropStage.None, 0))
                    return Fail("批量道路没有逐格登记正确材质或误建生产状态");
            }
            if (ReferenceEquals(before, game.GetBuildingSpaces()))
                return Fail("道路成功批量登记未发布新占地快照");
            start += 3;
        }
        if (game.MoneyCents != money - 2000 || initial.Count != 5 || game.Calendar.ElapsedSeconds != 0 ||
            !game.IsPaused || !game.GetWorkers().SequenceEqual(workers) || !game.HasConsistentState())
            return Fail("暂停批量道路改变时间、工人或正式状态一致性");
        return true;
    }

    private static bool CheckBatchRoadFailures()
    {
        using var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        Vector2I[] legal = { new(0, 0), new(1, 0), new(2, 0) };
        if (!game.CheckRoadPlacement(legal, RoadMaterial.Brick).Allowed ||
            !game.TryPlace(new(2, 0), BuildingKind.Road, CropKind.Wheat).Success)
            return Fail("批量重验夹具构造失败");
        var before = game.GetBuildingSpaces();
        int money = game.MoneyCents;
        var attempts = new[]
        {
            (legal, RoadMaterial.Brick, LandFailure.Occupied),
            (new[] { new Vector2I(0, 0), new Vector2I(189, 189) }, RoadMaterial.Brick, LandFailure.Occupied),
            (new[] { new Vector2I(0, 0), new Vector2I(190, 193) }, RoadMaterial.Brick, LandFailure.Occupied),
            (new[] { new Vector2I(0, 0), new Vector2I(-1, 0) }, RoadMaterial.Brick, LandFailure.OutOfBounds),
            (new[] { new Vector2I(0, 0), new Vector2I(FarmGame.MapSize, 0) }, RoadMaterial.Brick, LandFailure.OutOfBounds),
            (new[] { new Vector2I(0, 0), new Vector2I(0, 0) }, RoadMaterial.Brick, LandFailure.InvalidRoadRange),
            (System.Array.Empty<Vector2I>(), RoadMaterial.Brick, LandFailure.InvalidRoadRange),
            (new[] { new Vector2I(0, 0) }, (RoadMaterial)99, LandFailure.InvalidRoadMaterial),
            (new[] { new Vector2I(0, 0) }, (RoadMaterial)(-1), LandFailure.InvalidRoadMaterial),
            (Enumerable.Range(0, 50).Select(x => new Vector2I(x, 10)).ToArray(), RoadMaterial.Brick, LandFailure.InsufficientFunds),
        };
        foreach (var (cells, material, failure) in attempts)
        {
            RoadPlacementCheck check = game.CheckRoadPlacement(cells, material);
            RoadPlacementResult result = game.TryPlaceRoads(cells, material);
            if (check.Failure != failure || check.CostCents != (long)cells.Length * 100 || result.Failure != failure ||
                result.CellCount != 0 || result.ChargedCents != 0 || result.ErrorMessage == null ||
                game.MoneyCents != money || !ReferenceEquals(before, game.GetBuildingSpaces()) ||
                game.GetPlot(Vector2I.Zero).Building != BuildingKind.None ||
                game.GetBuildingSpace(new(2, 0))!.RoadMaterial != RoadMaterial.Dirt || !game.HasConsistentState())
                return Fail("道路整批失败留下部分占地、扣费、换材质或预检执行原因不一致");
        }
        foreach (System.Action action in new System.Action[]
        {
            () => game.TryPlaceRoads(legal, RoadMaterial.Dirt, (CommandOrigin)99),
            () => game.TryRemoveRoads(legal, (CommandOrigin)99),
        })
        {
            try { action(); return Fail("批量道路接受非法命令来源"); }
            catch (System.ArgumentOutOfRangeException) { }
        }
        return game.MoneyCents == money && ReferenceEquals(before, game.GetBuildingSpaces());
    }

    private static bool CheckBatchRoadFrozenMoney()
    {
        using var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var request = new TradeOrderRequest(new(CropKind.Radish, CommodityKind.Raw), TradeOrderSide.Buy,
            TradeOrderFrequency.Once, TradeOrderQuantityMode.Fixed, 0, TradeOrderBudgetMode.FixedBudget,
            4500, 1, CashReserveMode.Amount, 0, new IReadOnlyList<TradeOrderCondition>[]
            {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Less, 1) },
            });
        if (!game.CreateTradeOrder(request).Success || game.AvailableMoneyCents != 500)
            return Fail("道路冻结资金夹具失败");
        Vector2I[] six = Enumerable.Range(0, 6).Select(x => new Vector2I(x, 0)).ToArray();
        var before = game.GetBuildingSpaces();
        if (game.TryPlaceRoads(six, RoadMaterial.Slab).Failure != LandFailure.InsufficientFunds ||
            game.MoneyCents != 5000 || game.FrozenMoneyCents != 4500 || !ReferenceEquals(before, game.GetBuildingSpaces()) ||
            game.TryPlaceRoads(six.Take(5).ToArray(), RoadMaterial.Slab) != new RoadPlacementResult(LandFailure.None, 5, 500) ||
            game.MoneyCents != 4500 || game.FrozenMoneyCents != 4500 || game.AvailableMoneyCents != 0)
            return Fail("整段道路动用了冻结金币或没有允许准确耗尽可用余额");
        return true;
    }

    private static bool CheckBatchRoadRemoval()
    {
        using var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        game.AdvanceTicks(10);
        game.SetPaused(true);
        Vector2I first = new(188, 189);
        Vector2I last = new(198, 189);
        if (!game.TryPlaceRoads(new[] { first }, RoadMaterial.Gravel).Success ||
            !game.TryPlaceRoads(new[] { last }, RoadMaterial.Cobble).Success)
            return Fail("混合材质道路拆除夹具失败");
        var before = game.GetBuildingSpaces();
        var production = before.Where(space => space.Building != BuildingKind.Road).Select(space => game.GetPlot(space.AnchorCell)).ToArray();
        var workers = game.GetWorkers();
        var raw = FarmGame.Crops.Select(crop => game.GetRawStock(crop.Kind)).ToArray();
        var products = FarmGame.Crops.Select(crop => game.GetProductStock(crop.Kind)).ToArray();
        int money = game.MoneyCents;
        if (game.TryRemoveRoads(new[] { first, new Vector2I(-1, 0) }).Failure != LandFailure.OutOfBounds ||
            !ReferenceEquals(before, game.GetBuildingSpaces()))
            return Fail("道路拆除越界失败移除了有效范围道路");
        Vector2I[] range = Enumerable.Range(187, 13).Select(x => new Vector2I(x, 189)).Append(first).ToArray();
        if (game.TryRemoveRoads(range) != new RoadRemovalResult(LandFailure.None, 2) ||
            game.MoneyCents != money || game.GetPlot(first).Building != BuildingKind.None ||
            game.GetPlot(last).Building != BuildingKind.None || game.Calendar.ElapsedSeconds != 10 || !game.IsPaused ||
            !game.GetWorkers().SequenceEqual(workers) ||
            !production.SequenceEqual(game.GetBuildingSpaces().Select(space => game.GetPlot(space.AnchorCell))) ||
            !raw.SequenceEqual(FarmGame.Crops.Select(crop => game.GetRawStock(crop.Kind))) ||
            !products.SequenceEqual(FarmGame.Crops.Select(crop => game.GetProductStock(crop.Kind))) ||
            before.Single(space => space.AnchorCell == first).RoadMaterial != RoadMaterial.Gravel || !game.HasConsistentState())
            return Fail("范围拆路误删生产设施、重复计数、退款或改变生产和旧快照");
        var removed = game.GetBuildingSpaces();
        if (game.TryRemoveRoads(range).ErrorMessage != "没有可拆除的道路" ||
            game.TryRemoveRoads(System.Array.Empty<Vector2I>()).Failure != LandFailure.NoRoads ||
            !ReferenceEquals(removed, game.GetBuildingSpaces()) ||
            !game.TryPlaceRoads(new[] { first }, RoadMaterial.Brick).Success || game.RemoveBuilding(first) != null)
            return Fail("没有道路拒绝或正式逐格拆除回归");
        return true;
    }

    private static bool CheckStableSnapshotForwarding()
    {
        using var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var initialSpaces = game.GetBuildingSpaces();
        BuildingSpaceSnapshot[] savedSpaces = initialSpaces.ToArray();
        var initialWeather = game.ActiveWeatherRegions;
        Vector2I farm = new(189, 189);
        Rect2I rain = new(189, 189, 1, 1);
        if (!ReferenceEquals(initialSpaces, game.GetBuildingSpaces()) ||
            game.TryPlace(farm, BuildingKind.Farm, CropKind.Wheat).Success ||
            game.RemoveBuilding(Vector2I.Zero) == null ||
            !ReferenceEquals(initialSpaces, game.GetBuildingSpaces()) ||
            !game.TrySetWeatherRegion(rain, WeatherKind.Rain, out _) ||
            ReferenceEquals(initialWeather, game.ActiveWeatherRegions) || initialWeather.Count != 0)
            return Fail("占地／天气转发未保留稳定引用，或失败建拆改变了占地快照");
        var rainyWeather = game.ActiveWeatherRegions;
        game.AdvanceTick();
        if (!game.GetPlot(farm).HasWater ||
            !ReferenceEquals(initialSpaces, game.GetBuildingSpaces()) ||
            !ReferenceEquals(rainyWeather, game.ActiveWeatherRegions) ||
            game.TrySetWeatherRegion(rain, WeatherKind.Cloudy, out _) ||
            game.TryClearWeatherRegion(new(189, 189, 2, 2), out _) ||
            !ReferenceEquals(rainyWeather, game.ActiveWeatherRegions) ||
            !game.TryClearWeatherRegion(rain, out _) ||
            ReferenceEquals(rainyWeather, game.ActiveWeatherRegions) || rainyWeather.Count != 1 ||
            rainyWeather[0].Kind != WeatherKind.Rain)
            return Fail("动态生产错误失效占地快照，或天气失败／清除改变旧快照");
        if (game.RemoveBuilding(farm + Vector2I.One) != null ||
            ReferenceEquals(initialSpaces, game.GetBuildingSpaces()) ||
            !initialSpaces.SequenceEqual(savedSpaces) ||
            !game.TryPlace(farm, BuildingKind.Processor, CropKind.Wheat).Success)
            return Fail("成功建拆没有发布新空间快照或改写了已发布快照");
        var rebuilt = game.GetBuildingSpaces();
        if (rebuilt.Count != initialSpaces.Count || ReferenceEquals(rebuilt, initialSpaces) ||
            initialSpaces.Single(space => space.AnchorCell == farm).Building != BuildingKind.Farm ||
            rebuilt.Single(space => space.AnchorCell == farm).Building != BuildingKind.Processor)
            return Fail("同数量同锚点重建未以新引用发布真实空间变化");
        return true;
    }

    private static bool CheckBuildingOrientations()
    {
        foreach (BuildingOrientation orientation in System.Enum.GetValues<BuildingOrientation>())
        {
            using var baseline = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
            using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
            foreach (BuildingSpaceSnapshot space in game.GetBuildingSpaces())
                if (space.Orientation != BuildingOrientation.Q0)
                    return Fail("开局设施不是默认 q0 朝向");
            RemoveInitialBuildings(baseline);
            RemoveInitialBuildings(game);
            Vector2I farm = new(189, 189);
            Vector2I processor = new(192, 192);
            baseline.TryPlace(farm, BuildingKind.Farm, CropKind.Radish);
            baseline.TryPlace(processor, BuildingKind.Processor, CropKind.Radish);
            game.SetPaused(true);
            if (game.CheckPlacement(farm, BuildingKind.Farm, CropKind.Radish, orientation) !=
                    new PlacementCheck(LandFailure.None, 1000) ||
                game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish, orientation: orientation) !=
                    new PlacementResult(LandFailure.None, 1000) ||
                game.TryPlace(processor, BuildingKind.Processor, CropKind.Radish, orientation: orientation) !=
                    new PlacementResult(LandFailure.None, 1000) || game.MoneyCents != 3000)
                return Fail("暂停中合法朝向建造被拒绝或额外收费");
            foreach (Vector2I anchor in new[] { farm, processor })
            {
                BuildingSpaceSnapshot space = game.GetBuildingSpace(anchor)!;
                if (space.Orientation != orientation || space.WorkCell != anchor + Vector2I.One)
                    return Fail("经营登记没有保存朝向或改变工作中心");
                foreach (Vector2I offset in space.Footprint.Offsets)
                    if (!ReferenceEquals(space, game.GetBuildingSpace(anchor + offset)))
                        return Fail("经营查询没有统一子格朝向实例");
            }
            game.SetPaused(false);
            for (int tick = 0; tick < 260; tick++)
            {
                if (WeatherTestInput.AdvanceTick(baseline, tick == 0) != WeatherTestInput.AdvanceTick(game, tick == 0) ||
                    baseline.GetPlot(farm) != game.GetPlot(farm) ||
                    baseline.GetPlot(processor) != game.GetPlot(processor) ||
                    baseline.GetRawStock(CropKind.Radish) != game.GetRawStock(CropKind.Radish) ||
                    baseline.GetProductStock(CropKind.Radish) != game.GetProductStock(CropKind.Radish) ||
                    baseline.MoneyCents != game.MoneyCents)
                    return Fail("设施朝向改变真实农田、加工、库存或资金推进");
                var originalWorkers = baseline.GetWorkers();
                var workers = game.GetWorkers();
                for (int index = 0; index < workers.Count; index++)
                    if (originalWorkers[index] != workers[index])
                        return Fail("设施朝向改变工人位置、任务或动作");
            }
            if (game.GetProductStock(CropKind.Radish) == 0 || !game.HasConsistentState())
                return Fail("朝向等价测试没有覆盖真实完整收获和加工");
        }
        return true;
    }

    private static bool CheckOrientationFailures()
    {
        using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var before = game.GetBuildingSpaces();
        Vector2I anchor = new(0, 0);
        foreach (BuildingKind kind in new[] { BuildingKind.Farm, BuildingKind.Processor, BuildingKind.Road })
            foreach (BuildingOrientation orientation in new[] { (BuildingOrientation)(-1), (BuildingOrientation)4 })
                if (game.CheckPlacement(anchor, kind, CropKind.Wheat, orientation) !=
                        new PlacementCheck(LandFailure.InvalidOrientation, 0) ||
                    game.TryPlace(anchor, kind, CropKind.Wheat, orientation: orientation) !=
                        new PlacementResult(LandFailure.InvalidOrientation, 0))
                    return Fail("非法朝向预检与执行未统一拒绝");
        if (game.TryPlace(anchor, BuildingKind.Road, default, orientation: BuildingOrientation.Q1).Failure !=
                LandFailure.InvalidOrientation || game.MoneyCents != 5000 ||
            !ReferenceEquals(before, game.GetBuildingSpaces()) || game.GetBuildingSpace(anchor) != null)
            return Fail("朝向拒绝修改资金或空间实例");
        foreach (CropDefinition crop in FarmGame.Crops)
            if (game.GetRawStock(crop.Kind) != 0 || game.GetProductStock(crop.Kind) != 0)
                return Fail("朝向拒绝修改库存");
        foreach (BuildingOrientation orientation in System.Enum.GetValues<BuildingOrientation>())
            if (game.CheckPlacement(new(189, 189), BuildingKind.Farm, CropKind.Wheat, orientation).Failure != LandFailure.Occupied ||
                game.TryPlace(new(189, 189), BuildingKind.Farm, CropKind.Wheat, orientation: orientation).Failure != LandFailure.Occupied ||
                game.TryPlace(new(383, 383), BuildingKind.Farm, CropKind.Wheat, orientation: orientation).Failure != LandFailure.OutOfBounds ||
                game.MoneyCents != 5000 || !ReferenceEquals(before, game.GetBuildingSpaces()))
                return Fail("合法朝向绕过占用或范围检查，或失败留下修改");
        for (int col = 0; col < 15; col += 3)
            game.TryPlace(new(col, 0), BuildingKind.Farm, CropKind.Wheat);
        before = game.GetBuildingSpaces();
        if (game.TryPlace(new(18, 0), BuildingKind.Processor, CropKind.Radish, orientation: BuildingOrientation.Q3).Failure !=
                LandFailure.InsufficientFunds || game.MoneyCents != 0 ||
            !ReferenceEquals(before, game.GetBuildingSpaces()) || game.GetBuildingSpace(new(18, 0)) != null)
            return Fail("指定朝向建造绕过可用资金检查");
        return true;
    }

    private static bool CheckMultiCellFacilities()
    {
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        Vector2I anchor = new(5, 8); // 任意基础格锚点，不要求三格对齐。
        if (game.TryPlace(anchor, BuildingKind.Farm, CropKind.Radish).ChargedCents != 1000 ||
            game.MoneyCents != 4000 || game.GetBuildingSpaces().Count != 1)
            return Fail("多格农田没有按一座计费或登记为一个实例");
        BuildingSpaceSnapshot space = game.GetBuildingSpace(anchor)!;
        var savedSpaces = game.GetBuildingSpaces();
        foreach (Vector2I offset in space.Footprint.Offsets)
        {
            Vector2I child = anchor + offset;
            if (game.GetBuildingSpace(child) != space || game.GetPlot(child) != game.GetPlot(anchor) ||
                game.GetFarmDetails(child) != game.GetFarmDetails(anchor))
                return Fail("九个子格没有解析到同一农田空间、状态与详情");
        }
        if (space.AnchorCell != anchor || space.WorkCell != new Vector2I(6, 9) ||
            game.SetFarmCrop(anchor + new Vector2I(2, 2), CropKind.Wheat) != null ||
            game.GetPlot(anchor).CropKind != CropKind.Wheat || game.MoneyCents != 4000)
            return Fail("任意子格改种没有修改同一实例，或工作中心错误");
        if (game.RemoveBuilding(anchor + Vector2I.One) != null || game.GetBuildingSpaces().Count != 0 ||
            savedSpaces.Count != 1 || game.MoneyCents != 4000)
            return Fail("子格拆除没有整体移除，或旧空间快照改变、发生退款");
        foreach (Vector2I offset in space.Footprint.Offsets)
            if (game.GetPlot(anchor + offset).Building != BuildingKind.None ||
                game.GetBuildingSpace(anchor + offset) != null)
                return Fail("整座拆除后留下占用子格");

        Vector2I pending = new(10, 10);
        if (!game.CheckPlacement(pending, BuildingKind.Processor, CropKind.Radish).Allowed ||
            !game.TryPlace(pending + new Vector2I(2, 2), BuildingKind.Road, default).Success ||
            game.TryPlace(pending, BuildingKind.Processor, CropKind.Radish).Failure != LandFailure.Occupied ||
            game.GetPlot(pending).Building != BuildingKind.None || game.MoneyCents != 3900 ||
            game.TryPlace(new Vector2I(382, 10), BuildingKind.Farm, CropKind.Wheat).Failure != LandFailure.OutOfBounds ||
            game.MoneyCents != 3900 || !game.HasConsistentState())
            return Fail("完整末端子格冲突或部分越界没有在扣费前拒绝");

        game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        Vector2I farm = new(189, 189);
        Vector2I processor = new(192, 192);
        game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish);
        game.TryPlace(processor, BuildingKind.Processor, CropKind.Radish);
        WeatherTestInput.AdvanceTick(game, true);
        if (game.GetPlot(farm).RemainingSeconds != 206 || !game.GetPlot(farm).HasWater)
            return Fail("多格降雨或播种没有使用一个实例的完整时长");
        for (int second = 0; second < 205; second++)
            if (game.AdvanceTick().Harvested != 0)
                return Fail("九个子格导致农田进度被重复推进");
        TickResult harvest = game.AdvanceTick();
        if (harvest.Harvested < 36 || game.GetRawStock(CropKind.Radish) != harvest.Harvested - 9 ||
            game.GetPlot(processor).RemainingSeconds != 26)
            return Fail("多格农田收成或加工领取被按子格倍增");
        foreach (Vector2I offset in BuildingFootprint.Get(BuildingKind.Processor).Offsets)
            if (game.GetPlot(processor + offset) != game.GetPlot(processor) ||
                game.GetProcessorDetails(processor + offset) != game.GetProcessorDetails(processor))
                return Fail("加工子格未共享同一批次和详情");
        for (int second = 0; second < 25; second++)
            if (game.AdvanceTick().Produced != 0)
                return Fail("九个子格导致加工批次被重复推进");
        if (game.AdvanceTick().Produced != 9 || game.GetProductStock(CropKind.Radish) != 9 ||
            game.GetRawStock(CropKind.Radish) != harvest.Harvested - 18 || !game.HasConsistentState())
            return Fail("一座加工场地没有每批次产出、领取实际九份");
        return true;
    }

    private static bool CheckInitialCenter()
    {
        Vector2I[] farms = { new(189, 189), new(192, 189), new(195, 189) };
        Vector2I[] processors = { new(189, 192), new(192, 192) };
        bool sawSame = false;
        bool sawSplit = false;
        bool sawRadish = false;
        // 种子 0、2、4 在旧的七作物抽样中会选中春季禁生的甘蔗。
        for (int seed = 0; seed < 64; seed++)
        {
            var game = new FarmGame(seed, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
            var repeat = new FarmGame(seed, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
            if (game.MoneyCents != 5000 || game.Calendar.Season != Season.Spring ||
                game.GetBuildingSpaces().Count != 5 || !game.HasConsistentState())
                return Fail("开局季节、金币或五座设施状态错误");
            var workers = game.GetWorkers();
            if (workers.Count != 3)
                return Fail("开局没有三名真实经营工人");
            for (int i = 0; i < workers.Count; i++)
                if (workers[i] != new WorkerSnapshot(i + 1, (Vector2)BuildingFootprint.WorkCell(farms[i], BuildingKind.Farm), null, WorkerActivity.Idle)
                { ExactPosition = LandPosition.FromCell(BuildingFootprint.WorkCell(farms[i], BuildingKind.Farm)) } ||
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
                if ((FarmGame.GetCrop(plot.CropKind).GrowingSeasons & GrowingSeasons.Spring) == 0)
                    return Fail("春季开局随机到禁生作物");
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

            if (!game.AdvanceTick().WorkerActed)
                return Fail("开局三田首秒没有播种");
            foreach (Vector2I cell in farms)
                if (game.GetPlot(cell).Crop != CropStage.Seeded)
                    return Fail("开局赠送农田没有全部播种");
            if (!game.AdvanceTick().WorkerActed)
                return Fail("开局三田次秒没有浇水");
            foreach (Vector2I cell in farms)
                if (game.GetPlot(cell).Crop != CropStage.Growing || !game.GetPlot(cell).HasWater)
                    return Fail("开局赠送农田没有全部开始生长");
            int[] remaining = new int[farms.Length];
            for (int i = 0; i < farms.Length; i++)
                remaining[i] = game.GetPlot(farms[i]).RemainingSeconds;
            game.AdvanceTick();
            for (int i = 0; i < farms.Length; i++)
                if (remaining[i] <= 1 || game.GetPlot(farms[i]).RemainingSeconds != remaining[i] - 1)
                    return Fail("开局赠送农田没有继续生长");
        }
        if (!sawSame || !sawSplit || !sawRadish)
            return Fail("固定种子未覆盖两种开局配置");
        return true;
    }

    private static bool CheckInitialOtherSeasons()
    {
        Vector2I[] farms = { new(189, 189), new(192, 189), new(195, 189) };
        (uint seconds, Season season)[] starts =
        {
            (4320u, Season.Summer), (8640u, Season.Autumn), (12960u, Season.Winter),
        };
        foreach ((uint seconds, Season season) in starts)
            for (int seed = 0; seed < 16; seed++)
            {
                var game = new FarmGame(seed, seconds, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
                if (game.Calendar.Season != season)
                    return Fail("非春季开局日期错误");
                GrowingSeasons currentSeason = (GrowingSeasons)(1 << (int)season);
                foreach (Vector2I cell in farms)
                    if ((FarmGame.GetCrop(game.GetPlot(cell).CropKind).GrowingSeasons & currentSeason) == 0)
                        return Fail("非春季开局随机到禁生作物");
            }
        return true;
    }

    private static void RemoveInitialBuildings(FarmGame game)
    {
        game.RemoveBuilding(new Vector2I(189, 189));
        game.RemoveBuilding(new Vector2I(192, 189));
        game.RemoveBuilding(new Vector2I(195, 189));
        game.RemoveBuilding(new Vector2I(189, 192));
        game.RemoveBuilding(new Vector2I(192, 192));
    }

    private static bool CheckAllCrops()
    {
        int[] expectedGrowthSeconds = { 823, 1029, 618, 720, 875, 2058, 206 };
        int[] expectedProcessingSeconds = { 103, 155, 103, 52, 309, 103, 26 };
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            uint startSecond = crop.Kind == CropKind.Sugarcane ? 4320u : 0u;
            var game = new FarmGame(12345, startSecond, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
            Vector2I farm = new(189, 189);
            Vector2I processor = new(300, 300);
            RemoveInitialBuildings(game);
            if (game.MoneyCents != 5000 || game.CurrentDay != (int)(startSecond * 7 / 360) + 1 ||
                game.CurrentFlourPriceCents != new MarketQuotes(12345,
                    game.Calendar.ElapsedDays).GetQuote(new CommodityId(
                        CropKind.Wheat, CommodityKind.Product)).PriceCents)
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
            int batchQuantity = Math.Min(9, harvest.Harvested);
            if (harvest.Harvested < crop.HarvestQuantity * 6 || harvest.Harvested > crop.HarvestQuantity * 9 ||
                game.GetRawStock(crop.Kind) != harvest.Harvested - batchQuantity ||
                game.GetProcessorDetails(processor).BatchQuantity != batchQuantity ||
                game.GetPlot(processor).RemainingSeconds != processingSeconds)
                return Fail($"{crop.CropName}未按时收获并投入对应场地");
            SaleResult rawSale = game.SellRaw(crop.Kind);
            if (rawSale.Quantity != harvest.Harvested - batchQuantity)
                return Fail($"{crop.CropName}原料库存或加工中的原料出售错误");
            for (int i = 0; i < processingSeconds - 1; i++)
                game.AdvanceTick();
            if (game.GetProductStock(crop.Kind) != 0)
                return Fail($"{crop.ProductName}过早产出");
            TickResult finished = game.AdvanceTick();
            if (finished.Produced != batchQuantity || game.GetProductStock(crop.Kind) != batchQuantity)
                return Fail($"{crop.ProductName}未按时产出");
            int expectedRevenue = batchQuantity * game.GetProductPriceCents(crop.Kind);
            SaleResult sale = game.SellAll();
            if (sale.Quantity != batchQuantity || sale.RevenueCents != expectedRevenue ||
                game.MoneyCents != 3000 + expectedRevenue + rawSale.RevenueCents ||
                game.GetProductStock(crop.Kind) != 0 || game.SellAll().Quantity != 0)
                return Fail($"{crop.ProductName}出售或库存更新错误");
        }
        return true;
    }

    private static bool CheckBuildingCost()
    {
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        for (int col = 0; col < 5; col++)
        {
            Vector2I cell = new(col * 3, 0);
            if (game.BuildFarm(cell) != null || game.MoneyCents != 5000 - (col + 1) * FarmGame.BuildingCostCents)
                return Fail("农田建造未按每座 10 金币收费");
            if (game.BuildProcessor(cell, CropKind.Wheat) == null ||
                game.MoneyCents != 5000 - (col + 1) * FarmGame.BuildingCostCents)
                return Fail("占用地块仍可建造或重复扣费");
        }
        Vector2I another = new(15, 0);
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
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        Vector2I farm = new(189, 189);
        Vector2I processor = new(3, 0);
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
        int cornHarvest = game.GetRawStock(CropKind.Corn);
        if (game.GetRawStock(CropKind.Wheat) != 0 || cornHarvest < 24 || cornHarvest > 36 ||
            game.GetProductStock(CropKind.Rice) != 0 || game.GetPlot(processor).RemainingSeconds != 0)
            return Fail("加工场地消耗了不匹配原料，或切换前作物仍产出");
        if (game.SellAll().Quantity != 0)
            return Fail("原料被直接出售");
        game.RemoveBuilding(processor);
        game.BuildProcessor(processor, CropKind.Corn);
        if (game.GetRawStock(CropKind.Corn) != cornHarvest - 9 ||
            game.GetPlot(processor).RemainingSeconds != 155)
            return Fail("新建匹配场地没有自动处理库存原料");
        return true;
    }

    private static bool CheckRemoval()
    {
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        Vector2I farm = new(189, 189);
        Vector2I processor = new(3, 0);
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
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        Vector2I[] farms = { new(189, 189), new(192, 189), new(195, 189) };
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
                sowWorkers[i].TargetCell != BuildingFootprint.WorkCell(farms[i], BuildingKind.Farm) || sowWorkers[i].Activity != WorkerActivity.Watering)
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

        var remote = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(remote);
        Vector2I target = new(189, 192);
        remote.BuildFarm(target);
        if (remote.AdvanceTick().WorkerActed || remote.GetPlot(target).Crop != CropStage.None ||
            remote.GetWorkers()[0].GridPosition != (Vector2)BuildingFootprint.WorkCell(target, BuildingKind.Farm))
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
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        game.RemoveBuilding(new Vector2I(189, 192));
        game.RemoveBuilding(new Vector2I(192, 192));
        Vector2I[] farms = { new(189, 189), new(192, 189), new(195, 189) };
        foreach (Vector2I farm in farms)
            game.SetFarmCrop(farm, CropKind.Radish);
        int rounds = 0;
        int totalHarvested = 0;
        for (int second = 0; second < 624 && rounds < 3; second++)
        {
            TickResult tick = game.AdvanceTick();
            if (tick.Harvested == 0)
                continue;
            if (game.GetPresentationResults().Count(r => r.Kind == ProductionResultKind.Harvest) != 3 ||
                tick.Harvested != game.GetPresentationResults().Where(r => r.Kind == ProductionResultKind.Harvest).Sum(r => r.Quantity))
                return Fail("三个中心工人持续生产时有田块长期遗漏或重复收获");
            totalHarvested += tick.Harvested;
            rounds++;
        }
        if (rounds != 3 || game.GetRawStock(CropKind.Radish) != totalHarvested || game.MoneyCents != 5000)
            return Fail("三块中心田没有自动完成三轮，或收成与金币发生变化");
        return true;
    }

    private static bool CheckRainSupply()
    {
        var retained = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(retained);
        Vector2I first = new(189, 189);
        Vector2I second = new(201, 189);
        retained.BuildFarm(first);
        retained.BuildFarm(second);
        retained.SetFarmCrop(second, CropKind.Radish);
        retained.SetPaused(true);
        WeatherTestInput.AdvanceTick(retained, true);
        if (retained.GetPlot(second).HasWater || retained.Calendar.ElapsedSeconds != 0)
            return Fail("暂停期间降雨改变了农田或日历");
        retained.SetPaused(false);
        WeatherTestInput.AdvanceTick(retained, true);
        if (retained.GetPlot(second).Crop != CropStage.None ||
            !retained.GetPlot(second).HasWater ||
            retained.GetFarmDetails(second).Status != FarmStatus.WaitingForWorkerWithWater)
            return Fail("未播种农田没有在雨后留存水分");
        // 第二人从 (193,190) 走到工作中心 (202,190)，到田的秒不能同时播种。
        if (retained.AdvanceTick().WorkerActed || retained.AdvanceTick().WorkerActed ||
            retained.GetPlot(second).Crop != CropStage.None)
            return Fail("雨停后的移动秒被错误计为播种");
        TickResult usedRain = retained.AdvanceTick();
        if (!usedRain.WorkerActed || retained.GetPlot(second).Crop != CropStage.Growing ||
            retained.GetPlot(second).RemainingSeconds != 206)
            return Fail("雨停后播种没有使用留存水分开始生长");

        var seeded = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(seeded);
        Vector2I farm = new(189, 189);
        seeded.BuildFarm(farm);
        seeded.SetFarmCrop(farm, CropKind.Radish);
        seeded.AdvanceTick();
        if (seeded.GetPlot(farm).Crop != CropStage.Seeded || seeded.GetPlot(farm).HasWater)
            return Fail("无雨时农田未进入待水阶段");
        TickResult rainOnSeed = WeatherTestInput.AdvanceTick(seeded, true);
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

        var continuous = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(continuous);
        continuous.BuildFarm(farm);
        continuous.SetFarmCrop(farm, CropKind.Radish);
        continuous.AdvanceTick();
        continuous.AdvanceTick();
        TickResult repeatedRain = WeatherTestInput.AdvanceTick(continuous, true);
        if (repeatedRain.WorkerActed || continuous.GetPlot(farm).RemainingSeconds != 205)
            return Fail("人工浇水后遇雨重置了生长进度");
        TickResult harvest = default;
        for (int step = 0; step < 205; step++)
            harvest = WeatherTestInput.AdvanceTick(continuous, true);
        if (harvest.Harvested < 32 || continuous.GetRawStock(CropKind.Radish) != harvest.Harvested ||
            continuous.GetPlot(farm).Crop != CropStage.Seeded ||
            continuous.GetPlot(farm).HasWater)
            return Fail("持续降雨中收获没有清除本轮水分");
        TickResult nextRain = WeatherTestInput.AdvanceTick(continuous, true);
        if (nextRain.WorkerActed || continuous.GetPlot(farm).Crop != CropStage.Growing ||
            !continuous.GetPlot(farm).HasWater ||
            continuous.GetPlot(farm).RemainingSeconds != 205)
            return Fail("收获后的下一秒持续降雨没有重新供水");
        return true;
    }

    private static bool CheckSeasonalSowing()
    {
        var summer = new FarmGame(12345, 4320, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(summer);
        Vector2I farm = new(189, 189);
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

        var boundary = new FarmGame(12345, 4320 - 207, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(boundary);
        boundary.BuildFarm(farm);
        boundary.SetFarmCrop(farm, CropKind.Radish);
        if (boundary.GetFarmDetails(farm).Status != FarmStatus.InsufficientTime ||
            boundary.AdvanceTick().WorkerActed != true ||
            boundary.GetPlot(farm).Crop != CropStage.Seeded ||
            WeatherTestInput.AdvanceTick(boundary, true).WorkerActed != false ||
            boundary.GetPlot(farm).Crop != CropStage.Growing)
            return Fail("季末预计时间不足没有作为风险提示允许播种，或降雨重复触发浇水");
        return true;
    }

    private static bool CheckSeasonFailure()
    {
        // 春季 3 月 22 日起步，真实生产留下历史原料与成品。
        var game = new FarmGame(12345, 4000, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        Vector2I historyFarm = new(189, 189);
        Vector2I processor = new(300, 300);
        game.BuildFarm(historyFarm);
        game.SetFarmCrop(historyFarm, CropKind.Radish);
        for (int step = 0; step < 208; step++)
            game.AdvanceTick();
        int historyQuantity = game.GetRawStock(CropKind.Radish);
        if (historyQuantity < 36)
            return Fail("越季连接回归没有建立真实历史收成");
        game.BuildProcessor(processor, CropKind.Radish);
        int completedBatchQuantity = game.GetProcessorDetails(processor).BatchQuantity;
        for (int step = 0; step < 26; step++)
            game.AdvanceTick();
        int discardedBatchQuantity = game.GetProcessorDetails(processor).BatchQuantity;
        game.RemoveBuilding(processor); // 第一批九份入库；第二批九份投入随拆除丢弃，其余原料保留。
        while (game.Calendar.ElapsedSeconds < 4319)
            game.AdvanceTick();
        int rawStock = game.GetRawStock(CropKind.Radish);
        int productStock = game.GetProductStock(CropKind.Radish);
        int money = game.MoneyCents;
        if (completedBatchQuantity != 9 || discardedBatchQuantity != 9 ||
            rawStock != historyQuantity - completedBatchQuantity - discardedBatchQuantity ||
            productStock != completedBatchQuantity)
            return Fail("越季连接回归没有保留两类历史库存");

        // 复用现有负载夹具建立临界点进行中的作物，不人为延迟工人供水。
        // 正常调度实际播种后原地接续浇水，旧轮转排队失败场景已不再适用。
        game.FillWorldForBenchmark();
        Vector2I farm = new(36, 0); // 夹具既有布局中的萝卜田。
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
        Vector2I farm = new(189, 189);
        // 春末最后两秒播种玉米，春夏连续适宜，跨季后仍按原进度生长。
        var continuous = new FarmGame(12345, 4318, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(continuous);
        continuous.BuildFarm(farm);
        continuous.SetFarmCrop(farm, CropKind.Corn);
        WeatherTestInput.AdvanceTick(continuous, true);
        continuous.AdvanceTick();
        if (continuous.GetPlot(farm) !=
            new PlotSnapshot(BuildingKind.Farm, CropKind.Corn, CropStage.Growing, 1028, true,
                1d - 7193d / 7200))
            return Fail("相邻适宜季节误清了生长中的玉米");

        // 春末剩 208 秒播种干田萝卜，下一秒浇水，恰好在夏季起点成熟。
        var exact = new FarmGame(12345, 4112, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(exact);
        exact.BuildFarm(farm);
        exact.SetFarmCrop(farm, CropKind.Radish);
        for (int step = 0; step < 207; step++)
            exact.AdvanceTick();
        TickResult harvest = exact.AdvanceTick();
        if (!harvest.DayAdvanced || harvest.Harvested < 36 ||
            exact.GetRawStock(CropKind.Radish) != harvest.Harvested ||
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
                crop.Kind == CropKind.Sugarcane ? 4320u : 0u, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
            RemoveInitialBuildings(game);
            Vector2I farm = new(189, 189);
            game.BuildFarm(farm);
            game.SetFarmCrop(farm, crop.Kind);
            int growthSeconds = (crop.GrowthDays * 360 + 6) / 7;
            for (int i = 0; i < growthSeconds + 2; i++)
                game.AdvanceTick();
            int stock = game.GetRawStock(crop.Kind);
            int money = game.MoneyCents;
            int price = game.GetRawPriceCents(crop.Kind);
            if (stock < crop.HarvestQuantity * 6 || stock > crop.HarvestQuantity * 9 || game.GetProductStock(crop.Kind) != 0)
                return Fail($"{crop.CropName}原料未进入可出售库存");
            SaleResult sale = game.SellRaw(crop.Kind);
            if (sale.Quantity != stock || sale.RevenueCents != stock * price ||
                game.MoneyCents != money + sale.RevenueCents || game.GetRawStock(crop.Kind) != 0 ||
                game.SellRaw(crop.Kind).Quantity != 0 || game.SellAll().Quantity != 0)
                return Fail($"{crop.CropName}原料出售数量、当日收入或重复出售错误");
        }

        var mixed = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(mixed);
        Vector2I wheatFarm = new(189, 189);
        Vector2I cornFarm = new(192, 189);
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
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        CropKind invalid = (CropKind)999;
        Vector2I processor = new(12, 15);
        if (game.BuildProcessor(processor, invalid) == null ||
            game.GetPlot(processor).Building != BuildingKind.None || game.MoneyCents != 5000)
            return Fail("无效作物的加工场地建造修改了金币或土地");

        Vector2I farm = new(15, 15);
        if (game.BuildFarm(farm) != null || game.SetFarmCrop(farm, invalid) == null ||
            game.GetPlot(farm).CropKind != CropKind.Wheat || game.MoneyCents != 4000)
            return Fail("无效作物改种修改了农田或金币");
        return true;
    }

    private static bool CheckStateOwnership()
    {
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        if (!game.HasConsistentState())
            return Fail("开局占用与生产状态不一致");
        RemoveInitialBuildings(game);
        Vector2I farm = new(0, 0);
        Vector2I processor = new(3, 0);
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
        Vector2I corner = new(381, 381);
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
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        Vector2I target = new(0, 0);
        PlacementCheck preview = game.CheckPlacement(target, BuildingKind.Farm, CropKind.Wheat);
        if (!preview.Allowed || preview.CostCents != FarmGame.BuildingCostCents)
            return Fail("空地放置预检没有返回费用");
        if (game.CheckPlacement(new Vector2I(-3, 0), BuildingKind.Farm, CropKind.Wheat).Failure !=
                LandFailure.OutOfBounds ||
            game.CheckPlacement(new Vector2I(384, 0), BuildingKind.Farm, CropKind.Wheat).Failure !=
                LandFailure.OutOfBounds ||
            game.TryGetPlot(new Vector2I(0, 384), out _) != LandFailure.OutOfBounds ||
            game.RemoveBuilding(new Vector2I(-3, 0)) != "地图外地块" ||
            game.SetFarmCrop(new Vector2I(384, 0), CropKind.Corn) != "地图外地块")
            return Fail("地图外放置、查询、拆除或选种结果不一致");
        if (game.TryPlace(new Vector2I(-3, 0), BuildingKind.Farm, CropKind.Wheat).Failure !=
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

        Vector2I occupiedLater = new(3, 0);
        if (!game.CheckPlacement(occupiedLater, BuildingKind.Farm, CropKind.Wheat).Allowed ||
            game.BuildFarm(occupiedLater) != null ||
            game.TryPlace(occupiedLater, BuildingKind.Farm, CropKind.Wheat).Failure !=
                LandFailure.Occupied)
            return Fail("预检后占用变化未重新验证");
        for (int col = 2; col <= 3; col++)
            if (game.BuildFarm(new Vector2I(col * 3, 0)) != null)
                return Fail("余额准备失败");
        Vector2I insufficientLater = new(15, 0);
        if (!game.CheckPlacement(insufficientLater, BuildingKind.Farm, CropKind.Wheat).Allowed ||
            game.BuildFarm(new Vector2I(12, 0)) != null ||
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
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        foreach (CropDefinition crop in FarmGame.Crops)
            if (game.GetRawReserve(crop.Kind) != 0)
                return Fail("经营入口的保留底线默认值错误");
        if (game.SetRawReserve((CropKind)999, 1) != RawReserveFailure.InvalidCrop ||
            game.SetRawReserve(CropKind.Radish, -1) != RawReserveFailure.InvalidQuantity ||
            game.GetRawReserve(CropKind.Radish) != 0 || game.MoneyCents != 5000)
            return Fail("非法底线命令未拒绝，或修改了经营状态");

        Vector2I farm = new(189, 189);
        Vector2I first = new(381, 0);
        Vector2I second = new(0, 3);
        game.SetRawReserve(CropKind.Radish, int.MaxValue);
        game.BuildFarm(farm);
        game.SetFarmCrop(farm, CropKind.Radish);
        game.BuildProcessor(second, CropKind.Radish);
        game.BuildProcessor(first, CropKind.Radish);
        WeatherTestInput.AdvanceTick(game, true);
        for (int i = 0; i < 206; i++) game.AdvanceTick();
        game.RemoveBuilding(farm);
        int harvestQuantity = game.GetRawStock(CropKind.Radish);
        if (harvestQuantity < 36 || harvestQuantity > 54 ||
            game.GetProcessorDetails(first).Status != ProcessorStatus.WaitingForReserve ||
            game.GetProcessorDetails(second).Status != ProcessorStatus.WaitingForReserve)
            return Fail("原料保留底线阻塞了农田收获，或空闲场地错误领取");

        game.SetRawReserve(CropKind.Radish, harvestQuantity - 1);
        game.SetRawReserve(CropKind.Radish, harvestQuantity - 1);
        if (game.GetRawStock(CropKind.Radish) != harvestQuantity ||
            game.GetPlot(first).RemainingSeconds != 0 ||
            game.GetProcessorDetails(first).Status != ProcessorStatus.ReadyToProcess)
            return Fail("底线修改或查询立即启动了领取");
        game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != harvestQuantity - 1 ||
            game.GetPlot(first).RemainingSeconds != 26 || game.GetPlot(second).RemainingSeconds != 0)
            return Fail("下一经营领取阶段未按格序竞争，或取走了保留部分");

        game.SetRawReserve(CropKind.Radish, 99);
        if (game.GetRawStock(CropKind.Radish) != harvestQuantity - 1 || game.GetPlot(first).RemainingSeconds != 26 ||
            game.GetProcessorDetails(first).Status != ProcessorStatus.Processing)
            return Fail("提高底线退回投入物或重置了批次");
        SaleResult rawSale = game.SellRaw(CropKind.Radish);
        if (rawSale.Quantity != harvestQuantity - 1 || game.GetRawStock(CropKind.Radish) != 0 ||
            game.GetRawReserve(CropKind.Radish) != 99 ||
            game.GetProcessorDetails(second).Status != ProcessorStatus.WaitingForRaw)
            return Fail("出售未包含保留公共原料，或错误出售了投入物");
        for (int i = 0; i < 26; i++) game.AdvanceTick();
        if (game.GetProductStock(CropKind.Radish) != 1 ||
            game.GetPlot(first).RemainingSeconds != 0 ||
            rawSale.Quantity + game.GetProductStock(CropKind.Radish) != harvestQuantity)
            return Fail("收获、领取、出售与加工完成数量不守恒");
        SaleResult products = game.SellAll();
        if (products.Quantity != 1 || rawSale.Quantity + products.Quantity != harvestQuantity ||
            game.GetProductStock(CropKind.Radish) != 0)
            return Fail("成品出售后数量不守恒");
        return true;
    }

    private static bool CheckRawReserveConstruction()
    {
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        Vector2I farm = new(189, 189);
        Vector2I existing = new(0, 0);
        Vector2I later = new(0, 3);
        Vector2I earlier = new(3, 0);
        game.SetRawReserve(CropKind.Radish, int.MaxValue);
        game.BuildFarm(farm);
        game.SetFarmCrop(farm, CropKind.Radish);
        game.BuildProcessor(existing, CropKind.Radish);
        WeatherTestInput.AdvanceTick(game, true);
        for (int i = 0; i < 206; i++) game.AdvanceTick();
        game.RemoveBuilding(farm);
        int harvestQuantity = game.GetRawStock(CropKind.Radish);
        game.SetRawReserve(CropKind.Radish, harvestQuantity - 1);
        if (game.BuildProcessor(later, CropKind.Radish) != null ||
            game.GetPlot(existing).RemainingSeconds != 26 || game.GetPlot(later).RemainingSeconds != 0 ||
            game.GetRawStock(CropKind.Radish) != harvestQuantity - 1)
            return Fail("新建未立即执行全场格序领取，或取走底线库存");

        game.RemoveBuilding(existing); // 已投入的一份随拆除丢弃，不返回公共库存。
        game.SetRawReserve(CropKind.Radish, harvestQuantity - 2);
        if (game.BuildProcessor(earlier, CropKind.Radish) != null ||
            game.GetPlot(earlier).RemainingSeconds != 26 || game.GetPlot(later).RemainingSeconds != 0 ||
            game.GetRawStock(CropKind.Radish) != harvestQuantity - 2 || game.GetProductStock(CropKind.Radish) != 0 ||
            game.GetRawStock(CropKind.Radish) + 1 + 1 != harvestQuantity || !game.HasConsistentState())
            return Fail("新场地未按格序立即领取，或拆除损耗/投入/公共数量不守恒");
        return true;
    }

    private static async Task<bool> CheckRoadPlacement(SceneTree tree)
    {
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        Vector2I road = new(0, 0);
        PlacementCheck preview = game.CheckPlacement(road, BuildingKind.Road, default);
        if (!preview.Allowed || preview.CostCents != 100 || game.MoneyCents != 5000 ||
            game.GetPlot(road).Building != BuildingKind.None ||
            game.CheckPlacement(road, BuildingKind.None, default).Failure != LandFailure.InvalidBuilding ||
            game.CheckPlacement(road, (BuildingKind)999, default).Failure != LandFailure.InvalidBuilding ||
            game.TryPlace(road, BuildingKind.None, default) != new PlacementResult(LandFailure.InvalidBuilding, 0) ||
            game.TryPlace(road, (BuildingKind)999, default) != new PlacementResult(LandFailure.InvalidBuilding, 0))
            return Fail("道路预检修改状态、费用错误，或非法类型命令未稳定拒绝");
        PlacementResult built = game.TryPlace(road, BuildingKind.Road, (CropKind)999);
        PlotSnapshot expectedRoad = new(BuildingKind.Road, default, CropStage.None, 0);
        if (!built.Success || built.ChargedCents != 100 || game.MoneyCents != 4900 ||
            game.GetPlot(road) != expectedRoad || !game.HasConsistentState())
            return Fail("道路没有按100分占格，或错误创建了农田/加工/水分状态");
        foreach (BuildingKind kind in new[] { BuildingKind.Road, BuildingKind.Farm, BuildingKind.Processor })
            if (game.TryPlace(road, kind, default) != new PlacementResult(LandFailure.Occupied, 0) ||
                game.MoneyCents != 4900 || game.GetPlot(road) != expectedRoad)
                return Fail("道路重复铺设或被生产建筑覆盖，或失败仍扣费");
        foreach (Vector2I cell in new[] { new Vector2I(189, 189), new Vector2I(189, 192) })
        {
            PlotSnapshot before = game.GetPlot(cell);
            if (game.TryPlace(cell, BuildingKind.Road, default) != new PlacementResult(LandFailure.Occupied, 0) ||
                game.GetPlot(cell) != before || game.MoneyCents != 4900)
                return Fail("道路覆盖了农田或加工场地");
        }
        foreach (Vector2I cell in new[] { new Vector2I(-3, 0), new Vector2I(384, 0),
                     new Vector2I(0, -3), new Vector2I(0, 384) })
            if (game.CheckPlacement(cell, BuildingKind.Road, default).Failure != LandFailure.OutOfBounds ||
                game.TryPlace(cell, BuildingKind.Road, default) != new PlacementResult(LandFailure.OutOfBounds, 0) ||
                game.RemoveBuilding(cell) != "地图外地块" || game.MoneyCents != 4900)
                return Fail("越界铺路或拆路没有零修改拒绝");
        if (game.SetFarmCrop(road, CropKind.Radish) != "该土地没有农田" ||
            game.GetPlot(road) != expectedRoad || game.MoneyCents != 4900)
            return Fail("道路被改种命令当作农田");

        Vector2I changed = new(3, 0);
        if (!game.CheckPlacement(changed, BuildingKind.Road, default).Allowed ||
            game.BuildFarm(changed) != null ||
            game.TryPlace(changed, BuildingKind.Road, default) != new PlacementResult(LandFailure.Occupied, 0) ||
            game.MoneyCents != 3900 || game.GetPlot(changed).Building != BuildingKind.Farm)
            return Fail("道路预检后占用变化没有执行时重验");
        if (game.RemoveBuilding(road) != null || game.MoneyCents != 3900 ||
            game.GetPlot(road).Building != BuildingKind.None ||
            game.TryPlace(road, BuildingKind.Road, default).ChargedCents != 100 ||
            game.MoneyCents != 3800 || game.RemoveBuilding(road) != null ||
            game.BuildProcessor(road, CropKind.Radish) != null || game.MoneyCents != 2800 ||
            game.GetPlot(road).Building != BuildingKind.Processor || !game.HasConsistentState())
            return Fail("拆路退费、释放失败，或重铺/改建后的生产状态不一致");
        if (game.RemoveBuilding(road) != null || game.BuildFarm(road) != null ||
            game.GetPlot(road) != new PlotSnapshot(BuildingKind.Farm, CropKind.Wheat, CropStage.None, 0) ||
            game.MoneyCents != 1800 || !game.HasConsistentState())
            return Fail("道路释放后改建农田继承了旧状态，或扣费与生产占用不一致");

        using var poor = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(poor);
        for (int col = 0; col < 49; col++)
            if (poor.TryPlace(new Vector2I(col, 0), BuildingKind.Road, default).ChargedCents != 100)
                return Fail("余额边界夹具铺路失败");
        Vector2I last = new(300, 0);
        if (poor.MoneyCents != 100 || !poor.CheckPlacement(last, BuildingKind.Road, default).Allowed ||
            poor.TryPlace(new Vector2I(147, 0), BuildingKind.Road, default).ChargedCents != 100 ||
            poor.TryPlace(last, BuildingKind.Road, default) != new PlacementResult(LandFailure.InsufficientFunds, 0) ||
            poor.MoneyCents != 0 || poor.GetPlot(last).Building != BuildingKind.None || !poor.HasConsistentState())
            return Fail("道路预检后余额变化未重验，或不足100分仍铺路");
        await poor.WaitForNavigationAsync(tree);
        TickResult rain = WeatherTestInput.AdvanceTick(poor, true);
        if (rain.Harvested != 0 || rain.Produced != 0 || rain.WorkerActed ||
            poor.GetPlot(new Vector2I(0, 0)) != expectedRoad || poor.MoneyCents != 0)
            return Fail("道路参与了播种、供水或生产");
        return true;
    }

    private static async Task<bool> CheckRoadDoesNotClaimInventory(SceneTree tree)
    {
        using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        RemoveInitialBuildings(game);
        Vector2I farm = new(189, 189);
        Vector2I processor = new(30, 30);
        game.BuildFarm(farm);
        game.SetFarmCrop(farm, CropKind.Radish);
        game.SetRawReserve(CropKind.Radish, int.MaxValue);
        game.BuildProcessor(processor, CropKind.Radish);
        for (int step = 0; step < 208; step++)
            game.AdvanceTick();
        game.RemoveBuilding(farm);
        int harvestQuantity = game.GetRawStock(CropKind.Radish);
        game.SetRawReserve(CropKind.Radish, harvestQuantity - 1);
        int money = game.MoneyCents;
        if (game.GetRawStock(CropKind.Radish) != harvestQuantity ||
            game.GetProcessorDetails(processor).Status != ProcessorStatus.ReadyToProcess ||
            game.TryPlace(new Vector2I(0, 0), BuildingKind.Road, default).ChargedCents != 100 ||
            game.MoneyCents != money - 100 || game.GetRawStock(CropKind.Radish) != harvestQuantity ||
            game.GetPlot(processor).RemainingSeconds != 0 ||
            game.GetProcessorDetails(processor).Status != ProcessorStatus.ReadyToProcess)
            return Fail("铺路错误触发了空闲加工场地领取历史库存");
        await game.WaitForNavigationAsync(tree);
        game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != harvestQuantity - 1 || game.GetPlot(processor).RemainingSeconds != 26)
            return Fail("铺路后的正常经营领取阶段发生回归");
        return true;
    }

    private static async Task<bool> CheckRoadIndependentProduction(SceneTree tree)
    {
        using var bare = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        using var connected = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        using var broken = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        Vector2I farm = new(201, 189);
        foreach (FarmGame game in new[] { bare, connected, broken })
        {
            RemoveInitialBuildings(game);
            game.BuildFarm(farm);
            game.SetFarmCrop(farm, CropKind.Radish);
        }
        foreach (FarmGame game in new[] { connected, broken })
            for (int col = 193; col <= 200; col++)
                if (!game.TryPlace(new Vector2I(col, 190), BuildingKind.Road, default).Success)
                    return Fail("有路/断路验收布局铺设失败");
        broken.RemoveBuilding(new Vector2I(196, 190));
        await connected.WaitForNavigationAsync(tree);
        await broken.WaitForNavigationAsync(tree);
        FarmGame[] games = { bare, connected, broken };
        int[] harvestedRounds = new int[games.Length];
        int[] totalQuantity = new int[games.Length];
        for (int second = 0; second < 650; second++)
        {
            for (int index = 0; index < games.Length; index++)
            {
                TickResult result = games[index].AdvanceTick();
                if (result.Harvested > 0) { harvestedRounds[index]++; totalQuantity[index] += result.Harvested; }
                foreach (WorkerSnapshot worker in games[index].GetWorkers())
                    if (worker.Activity == WorkerActivity.Moving && worker.Path is LandPath path &&
                        worker.ExactPosition != path.GetPositionAtDistance(worker.TravelTicks * 3d))
                        return Fail("道路改变了每经营秒三格的真实路径采样");
            }
        }
        if (games.Where((game, index) => harvestedRounds[index] != 3 ||
                game.GetRawStock(CropKind.Radish) != totalQuantity[index]).Any() ||
            totalQuantity.Distinct().Count() != 1 ||
            connected.GetPlot(new Vector2I(196, 190)).Building != BuildingKind.Road ||
            broken.GetPlot(new Vector2I(196, 190)).Building != BuildingKind.None ||
            !bare.HasConsistentState() || !connected.HasConsistentState() || !broken.HasConsistentState())
            return Fail("三种道路布局未持续生产三轮，或道路占用状态异常");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
