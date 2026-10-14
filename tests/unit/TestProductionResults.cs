using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Time;
using FarmExchange.Weather;
using FarmExchange.Workers;

public partial class TestProductionResults : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);
    public static bool RunChecks() => CheckPlannedWorkContinuity() && CheckPlannedBatch() && CheckWorkAndInvalidation() &&
        CheckRainAndWaiting() && CheckHarvestAndBatch() && CheckImmediateRestart() && CheckSeasonResults();

    private static bool CheckPlannedWorkContinuity()
    {
        bool passed = true;
        foreach (int farmCount in new[] { 3, 4 })
            foreach (CultivationMode? mode in new CultivationMode?[] { null, CultivationMode.Immediate, CultivationMode.PrepareNext })
            {
                var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
                Vector2I[] farms = new[] { new Vector2I(189, 189), new(192, 189), new(195, 189), new(198, 189) }
                    .Take(farmCount).ToArray();
                if (farmCount == 4 && !game.TryPlace(farms[3], BuildingKind.Farm, CropKind.Radish).Success)
                    return Fail("供水竞争夹具未能建立第四田");
                if (mode is CultivationMode plannedMode)
                {
                    int planId = game.CreateCultivationPlan(new("供水续接", plannedMode,
                        new[] { new CultivationEntry(1, CropKind.Radish, 0) })).Id;
                    if (game.ApplyCultivationPlan(planId, farms) != null)
                        return Fail($"{mode}未能向{farmCount}田应用首日计划");
                }
                else
                    foreach (Vector2I farm in farms)
                        game.SetFarmCrop(farm, CropKind.Radish);
                string context = $"{mode?.ToString() ?? "无计划"}{farmCount}田";

                game.AdvanceTick();
                IReadOnlyList<ProductionResult> sows = game.GetPresentationResults();
                if (sows.Count != 3 || sows.Any(result => result.Kind != ProductionResultKind.Sow ||
                    !game.IsPresentationResultCurrent(result)))
                    passed = Fail($"{context}的三条真实播种结果同秒失效");
                IReadOnlyList<WorkerSnapshot> workers = game.GetWorkers();
                for (int worker = 0; worker < 3; worker++)
                    if (workers[worker].Activity != WorkerActivity.Watering ||
                        workers[worker].TargetCell != BuildingFootprint.WorkCell(farms[worker], BuildingKind.Farm))
                        passed = Fail($"{context}工人{worker + 1}播种后未认领原田供水");

                game.AdvanceTick();
                IReadOnlyList<ProductionResult> waters = game.GetPresentationResults();
                IReadOnlyList<WorkerSnapshot> wateredWorkers = game.GetWorkers();
                if (waters.Count != 3 || sows.Any(result => game.IsPresentationResultCurrent(result) ||
                    !game.IsPresentationTargetCurrent(result)))
                    passed = Fail($"{context}供水秒的结果数量或前一秒播种片段的有效性错误");
                for (int worker = 0; worker < 3; worker++)
                    if (!game.GetPlot(farms[worker]).HasWater ||
                        game.GetPlot(farms[worker]).Crop != CropStage.Growing ||
                        wateredWorkers[worker].GridPosition != workers[worker].GridPosition ||
                        wateredWorkers[worker].Activity != WorkerActivity.Idle || wateredWorkers[worker].TargetCell != null ||
                        !waters.Any(result => result.Kind == ProductionResultKind.Water &&
                            result.WorkerNumber == worker + 1 && result.AnchorCell == farms[worker] &&
                            game.IsPresentationResultCurrent(result)))
                        passed = Fail($"{context}工人{worker + 1}未在第2秒原地完成原田供水");
                if (farmCount == 4 && game.GetPlot(farms[3]).Crop != CropStage.None)
                    passed = Fail($"{context}在原田供水秒提前给第四田播种");
            }
        return passed;
    }

    private static bool CheckPlannedBatch()
    {
        foreach (CultivationMode mode in Enum.GetValues<CultivationMode>())
        {
            var single = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
            var batch = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
            Vector2I[] farms = { new(189, 189), new(192, 189), new(195, 189), new(198, 189) };
            foreach (FarmGame game in new[] { single, batch })
            {
                if (!game.TryPlace(farms[3], BuildingKind.Farm, CropKind.Radish).Success)
                    return Fail("计划批量对照未能建立第四田");
                int id = game.CreateCultivationPlan(new("计划批量对照", mode,
                    new[] { new CultivationEntry(1, CropKind.Radish, 0) })).Id;
                if (game.ApplyCultivationPlan(id, farms) != null)
                    return Fail("计划批量对照未能应用计划");
            }
            int rounds = 0;
            foreach (uint seconds in new uint[] { 1, 1, 6, 200, 20, 1000 })
            {
                long harvested = 0, produced = 0;
                for (uint second = 0; second < seconds; second++)
                {
                    TickResult tick = single.AdvanceTick();
                    harvested += tick.Harvested;
                    produced += tick.Produced;
                    rounds += single.GetPresentationResults().Count(result => result.Kind == ProductionResultKind.Harvest);
                }
                SimulationAdvanceResult advanced = batch.AdvanceTicks(seconds);
                if (advanced.AdvancedTicks != seconds || advanced.Harvested != harvested || advanced.Produced != produced ||
                    single.Calendar != batch.Calendar || !single.GetWorkers().SequenceEqual(batch.GetWorkers()) ||
                    farms.Any(farm => single.GetPlot(farm) != batch.GetPlot(farm) ||
                        single.GetFarmCultivation(farm) != batch.GetFarmCultivation(farm)) ||
                    !single.GetPresentationResults().Select(ResultFields).SequenceEqual(batch.GetPresentationResults().Select(ResultFields)) ||
                    single.GetPresentationResults().Any(result => !single.IsPresentationResultCurrent(result)) ||
                    batch.GetPresentationResults().Any(result => !batch.IsPresentationResultCurrent(result)))
                    return Fail($"{mode}计划单秒与批量的状态、工人、产出或真实结果不同");
            }
            if (rounds != 4 || farms.Any(farm => !single.GetFarmCultivation(farm).IsResting))
                return Fail($"{mode}四田年度条未各完成一轮，或条内自动复种");
        }
        return true;
    }

    private static (uint, Vector2I, CropKind, ProductionResultKind, int, int) ResultFields(ProductionResult result) =>
        (result.ElapsedSeconds, result.AnchorCell, result.CropKind, result.Kind, result.Quantity, result.WorkerNumber);

    private static FarmGame Empty(uint second = 0)
    {
        var game = new FarmGame(12345, second, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        return game;
    }

    private static bool CheckWorkAndInvalidation()
    {
        var game = Empty();
        Vector2I first = new(189, 189), second = new(192, 189);
        game.TryPlace(first, BuildingKind.Farm, CropKind.Radish);
        game.TryPlace(second, BuildingKind.Farm, CropKind.Radish);
        if (game.GetPresentationResults().Count != 0) return Fail("建造伪造了成功动作");
        game.AdvanceTick();
        var saved = game.GetPresentationResults();
        if (saved.Count != 2 || saved.Any(r => r.Kind != ProductionResultKind.Sow ||
            r.ElapsedSeconds != 1 || r.WorkerNumber == 0 || !game.IsPresentationResultCurrent(r)))
            return Fail("真正播种结果没有对应工人、锚点和经营秒");
        bool immutable = false;
        try { ((IList<ProductionResult>)saved)[0] = default; }
        catch (NotSupportedException) { immutable = true; }
        if (!immutable) return Fail("调用方能修改内部结果");
        game.SetFarmCrop(first, CropKind.Radish);
        if (game.IsPresentationResultCurrent(saved[0]) || !game.IsPresentationResultCurrent(saved[1]))
            return Fail("同种重启没有失效旧轮次，或污染了别的田结果");
        game.AdvanceTick();
        if (saved.Count != 2 || game.IsPresentationResultCurrent(saved[1]) ||
            !game.GetPresentationResults().Any(r => r.AnchorCell == second && r.Kind == ProductionResultKind.Water))
            return Fail("旧快照被修改，或真实供水结果丢失");
        ProductionResult water = game.GetPresentationResults().First(r => r.AnchorCell == second);
        game.SetPaused(true);
        game.AdvanceTicks(100);
        if (!game.IsPresentationResultCurrent(water)) return Fail("暂停推进修改了真实结果");
        game.RemoveBuilding(second);
        game.TryPlace(second, BuildingKind.Farm, CropKind.Radish);
        if (game.IsPresentationResultCurrent(water)) return Fail("拆除同格重建恢复了旧结果");
        if (game.IsPresentationTargetCurrent(water)) return Fail("拆除同格重建恢复了在播目标凭据");
        return true;
    }

    private static bool CheckRainAndWaiting()
    {
        foreach (CultivationMode? mode in new CultivationMode?[] { null, CultivationMode.Immediate, CultivationMode.PrepareNext })
        {
            var game = Empty();
            Vector2I farm = new(189, 189);
            game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish);
            if (mode is CultivationMode plannedMode)
            {
                int id = game.CreateCultivationPlan(new("雨水续接", plannedMode,
                    new[] { new CultivationEntry(1, CropKind.Radish, 0) })).Id;
                if (game.ApplyCultivationPlan(id, new[] { farm }) != null)
                    return Fail("雨水回归未能应用计划");
            }
            game.AdvanceTick();
            ProductionResult sow = game.GetPresentationResults().Single();
            WeatherTestInput.AdvanceTick(game, true);
            if (game.GetPresentationResults().Count != 0 || game.GetPlot(farm).Crop != CropStage.Growing ||
                !game.GetPlot(farm).HasWater || game.GetWorkers().Any(worker => worker.Activity != WorkerActivity.Idle) ||
                !game.IsPresentationTargetCurrent(sow))
                return Fail($"{mode?.ToString() ?? "无计划"}降雨未取消原供水认领，或影响了真实播种片段");
        }
        var distant = Empty();
        distant.TryPlace(new Vector2I(0, 0), BuildingKind.Farm, CropKind.Radish);
        distant.AdvanceTick();
        if (distant.GetPresentationResults().Count != 0) return Fail("移动/待执行任务伪造成功");
        return true;
    }

    private static bool CheckHarvestAndBatch()
    {
        var game = Empty();
        Vector2I farm = new(189, 189);
        game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish);
        game.AdvanceTicks(2);
        ProductionResult water = game.GetPresentationResults().Single(r => r.Kind == ProductionResultKind.Water);
        game.AdvanceTick();
        if (!Mathf.IsEqualApprox(game.GetPlot(farm).GrowthProgress, 7d / 1440))
            return Fail("实际生长进度退回向上取整秒数");
        game.AdvanceTicks(205);
        var harvest = game.GetPresentationResults().Single(r => r.Kind == ProductionResultKind.Harvest);
        if (harvest.Quantity < 36 || harvest.Quantity > 54 || harvest.AnchorCell != farm || harvest.ElapsedSeconds != 208 ||
            game.GetRawStock(CropKind.Radish) != harvest.Quantity || game.IsPresentationTargetCurrent(water))
            return Fail("成熟入库没有产生逐实例实际产量");
        game.AdvanceTicks(4);
        if (game.GetPresentationResults().Count != 0 || game.IsPresentationResultCurrent(harvest))
            return Fail("批量尾部平静区间保留了过期动作或收成");
        if (!game.IsPresentationTargetCurrent(harvest))
            return Fail("仅结果时间窗过期便使已开始的真实收获反馈目标失效");
        return true;
    }

    private static bool CheckImmediateRestart()
    {
        var game = Empty();
        Vector2I processor = new(189, 192);
        game.Buy(new CommodityId(CropKind.Radish, CommodityKind.Raw), 12);
        game.BuildProcessor(processor, CropKind.Radish);
        game.AdvanceTicks(26);
        var result = game.GetPresentationResults().Single(r => r.Kind == ProductionResultKind.Product);
        if (result.Quantity != 9 || game.GetPlot(processor).RemainingSeconds != 26 ||
            game.GetProcessorDetails(processor).BatchQuantity != 3 ||
            !game.IsPresentationResultCurrent(result) || game.GetProductStock(CropKind.Radish) != 9)
            return Fail("加工当秒完成再启动丢失本次产出证据");
        game.RemoveBuilding(processor);
        game.BuildProcessor(processor, CropKind.Radish);
        if (game.IsPresentationResultCurrent(result)) return Fail("新加工实例复用了旧实例产出");
        return true;
    }

    private static bool CheckSeasonResults()
    {
        var cleared = Empty(4319);
        cleared.TryPlace(new Vector2I(189, 189), BuildingKind.Farm, CropKind.Radish);
        cleared.AdvanceTick();
        var sow = cleared.GetPresentationResults().Single(r => r.Kind == ProductionResultKind.Sow);
        if (cleared.IsPresentationResultCurrent(sow)) return Fail("换季清理仍把旧播种当有效表现");
        var rescued = Empty(4320 - 200);
        Vector2I farm = new(189, 189);
        rescued.TryPlace(farm, BuildingKind.Farm, CropKind.Radish);
        rescued.AdvanceTicks(200);
        if (!rescued.GetPresentationResults().Any(r => r.Kind == ProductionResultKind.Harvest &&
            r.Quantity >= 36 && r.Quantity <= 54 && r.Quantity == rescued.GetRawStock(CropKind.Radish) && rescued.IsPresentationResultCurrent(r)))
            return Fail("换季促熟没有经过真实收成结果入口");
        return true;
    }

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
