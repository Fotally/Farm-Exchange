using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;

public partial class TestProductionResults : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);
    public static bool RunChecks() => CheckWorkAndInvalidation() && CheckRainAndWaiting() &&
        CheckHarvestAndBatch() && CheckImmediateRestart() && CheckSeasonResults();

    private static FarmGame Empty(uint second = 0)
    {
        var game = new FarmGame(12345, second);
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
        var game = Empty();
        game.TryPlace(new Vector2I(189, 189), BuildingKind.Farm, CropKind.Radish);
        game.AdvanceTick();
        game.AdvanceTick(isRaining: true);
        if (game.GetPresentationResults().Any(r => r.Kind == ProductionResultKind.Water))
            return Fail("降雨代替供水后仍伪造工人浇水");
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
        game.AdvanceTick();
        if (!Mathf.IsEqualApprox(game.GetPlot(farm).GrowthProgress, 7d / 1440))
            return Fail("实际生长进度退回向上取整秒数");
        game.AdvanceTicks(205);
        var harvest = game.GetPresentationResults().Single(r => r.Kind == ProductionResultKind.Harvest);
        if (harvest.Quantity != 6 || harvest.AnchorCell != farm || harvest.ElapsedSeconds != 208 ||
            game.GetRawStock(CropKind.Radish) != 6)
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
        game.Buy(new CommodityId(CropKind.Radish, CommodityKind.Raw), 3);
        game.BuildProcessor(processor, CropKind.Radish);
        game.AdvanceTicks(26);
        var result = game.GetPresentationResults().Single(r => r.Kind == ProductionResultKind.Product);
        if (result.Quantity != 1 || game.GetPlot(processor).RemainingSeconds != 26 ||
            !game.IsPresentationResultCurrent(result) || game.GetProductStock(CropKind.Radish) != 1)
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
            r.Quantity == 6 && rescued.IsPresentationResultCurrent(r)))
            return Fail("换季促熟没有经过真实收成结果入口");
        return true;
    }

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
