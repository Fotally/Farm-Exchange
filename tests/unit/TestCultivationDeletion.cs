using System;
using System.Linq;
using System.Text.Json;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;

public partial class TestCultivationDeletion : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
        foreach (CultivationMode mode in new[] { CultivationMode.Immediate, CultivationMode.PrepareNext })
            foreach ((CropStage stage, bool wet) in new[]
            {
                (CropStage.Growing, false), (CropStage.Seeded, false),
                (CropStage.None, false), (CropStage.None, true),
            })
                if (!CheckPreservedFarms(mode, stage, wet)) return false;
        return true;
    }

    private static bool CheckPreservedFarms(CultivationMode mode, CropStage stage, bool wet)
    {
        var game = new FarmGame(12345);
        foreach (var space in game.GetBuildingSpaces().Where(space => space.Building == BuildingKind.Processor))
            game.RemoveBuilding(space.AnchorCell);
        Vector2I[] targets = { new(189, 189), new(192, 189) };
        Vector2I manual = new(195, 189), other = new(198, 189);
        foreach (Vector2I farm in targets.Append(manual)) game.SetFarmCrop(farm, CropKind.Radish);
        if (game.BuildFarm(other) != null || game.SetFarmCrop(other, CropKind.Radish) != null ||
            !game.Buy(new CommodityId(CropKind.Radish, CommodityKind.Raw), 1).Success)
            return Fail("删除夹具未建立独立农田和非零公共库存");
        var otherRequest = new CultivationPlanRequest("保留的另一张表", CultivationMode.PrepareNext,
            new[] { new CultivationEntry(1, CropKind.Wheat, 20) });
        int otherId = game.CreateCultivationPlan(otherRequest).Id;
        game.ApplyCultivationPlan(otherId, new[] { other });
        var request = new CultivationPlanRequest("待删除的多田共享表", mode,
            stage == CropStage.None ? new[] { new CultivationEntry(1, CropKind.Wheat, 10) } :
                new[] { new CultivationEntry(1, CropKind.Radish, 0), new CultivationEntry(2, CropKind.Wheat, 5) });
        int id = game.CreateCultivationPlan(request).Id;
        if (game.ApplyCultivationPlan(id, targets) != null) return Fail("多田共享表夹具应用失败");
        int ticks = stage == CropStage.Growing ? 2 : stage == CropStage.Seeded ? 1 : 10;
        for (int tick = 0; tick < ticks; tick++) game.AdvanceTick(isRaining: wet);
        if (targets.Any(farm => game.GetPlot(farm).Crop != stage ||
            stage == CropStage.None && (game.GetPlot(farm).HasWater != wet || !game.GetFarmCultivation(farm).IsResting)) ||
            game.GetPlot(manual).Crop == CropStage.None || game.PrepareFarmCrop(manual, CropKind.Corn) != null ||
            game.GetFarmCultivation(manual).PreparedCrop != CropKind.Corn)
            return Fail($"删除夹具未真实达到{stage}/湿润{wet}及独立手动预备");
        game.SetPaused(true);
        Vector2I[] farms = targets.Concat(new[] { manual, other }).ToArray();
        PlotSnapshot[] plots = farms.Select(game.GetPlot).ToArray();
        FarmCultivationSnapshot[] arrangements = farms.Select(game.GetFarmCultivation).ToArray();
        var stocks = FarmGame.Crops.Select(crop => (game.GetRawStock(crop.Kind), game.GetProductStock(crop.Kind))).ToArray();
        var money = (game.MoneyCents, game.AvailableMoneyCents, game.FrozenMoneyCents);
        uint seconds = game.Calendar.ElapsedSeconds;
        var oldSnapshots = game.GetCultivationPlans();
        string plans = JsonSerializer.Serialize(oldSnapshots);
        if (game.DeleteCultivationPlan(int.MaxValue) != "耕作表不存在" ||
            JsonSerializer.Serialize(game.GetCultivationPlans()) != plans ||
            !plots.SequenceEqual(farms.Select(game.GetPlot)) || !arrangements.SequenceEqual(farms.Select(game.GetFarmCultivation)) ||
            !stocks.SequenceEqual(FarmGame.Crops.Select(crop => (game.GetRawStock(crop.Kind), game.GetProductStock(crop.Kind)))) ||
            money != (game.MoneyCents, game.AvailableMoneyCents, game.FrozenMoneyCents) ||
            game.Calendar.ElapsedSeconds != seconds || !game.IsPaused)
            return Fail("删除不存在表没有完整零修改拒绝");
        if (game.DeleteCultivationPlan(id) != null || game.GetCultivationPlans().Any(plan => plan.Id == id) ||
            !plots.SequenceEqual(farms.Select(game.GetPlot)) || targets.Any(farm => game.GetFarmCultivation(farm) != default) ||
            game.GetFarmCultivation(manual) != arrangements[2] || game.GetFarmCultivation(other) != arrangements[3] ||
            !stocks.SequenceEqual(FarmGame.Crops.Select(crop => (game.GetRawStock(crop.Kind), game.GetProductStock(crop.Kind)))) ||
            money != (game.MoneyCents, game.AvailableMoneyCents, game.FrozenMoneyCents) ||
            game.Calendar.ElapsedSeconds != seconds || !game.IsPaused ||
            oldSnapshots.First(plan => plan.Id == id).ReferencingFarms != 2)
            return Fail("删除引用表没有保留全部当前轮、水分、独立安排、资源、日期或旧快照");
        string remainingPlans = JsonSerializer.Serialize(game.GetCultivationPlans());
        if (game.DeleteCultivationPlan(id) != "耕作表不存在" || game.UpdateCultivationPlan(id, request).Success ||
            game.ApplyCultivationPlan(id, targets) == null || JsonSerializer.Serialize(game.GetCultivationPlans()) != remainingPlans ||
            !plots.SequenceEqual(farms.Select(game.GetPlot)))
            return Fail("已删除表仍可删除、编辑、应用，或拒绝时修改当前轮");
        int emptyId = game.CreateCultivationPlan(new CultivationPlanRequest("无引用空表", CultivationMode.PrepareNext,
            Array.Empty<CultivationEntry>())).Id;
        if (emptyId <= id || game.DeleteCultivationPlan(emptyId) != null || game.GetCultivationPlans().Count != 1 ||
            game.GetCultivationPlans()[0].Id != otherId)
            return Fail("无引用空表不能删除、表编号被复用或误删另一张表");
        int unreferencedId = game.CreateCultivationPlan(new CultivationPlanRequest("无引用非空表", mode,
            new[] { new CultivationEntry(1, CropKind.Radish, 0) })).Id;
        if (unreferencedId <= emptyId || game.DeleteCultivationPlan(unreferencedId) != null ||
            JsonSerializer.Serialize(game.GetCultivationPlans()) != remainingPlans ||
            !plots.SequenceEqual(farms.Select(game.GetPlot)) ||
            game.GetFarmCultivation(manual) != arrangements[2] || game.GetFarmCultivation(other) != arrangements[3] ||
            !stocks.SequenceEqual(FarmGame.Crops.Select(crop => (game.GetRawStock(crop.Kind), game.GetProductStock(crop.Kind)))) ||
            money != (game.MoneyCents, game.AvailableMoneyCents, game.FrozenMoneyCents) ||
            game.Calendar.ElapsedSeconds != seconds || !game.IsPaused)
            return Fail("删除无引用非空表修改了农田、其它表或资源，或删除空表后编号回退");
        game.SetPaused(false);
        bool harvested = false, replanted = false;
        for (int tick = 0; tick < 600; tick++)
        {
            PlotSnapshot before = game.GetPlot(targets[0]);
            int beforeStock = game.GetRawStock(CropKind.Radish);
            TickResult result = game.AdvanceTick();
            PlotSnapshot current = game.GetPlot(targets[0]);
            // 收获在工人相位前，同一秒可能已重新播种；同时检查本田轮次和真实入库，避免把其它田收成当作本田收成。
            bool roundEnded = before.Crop == CropStage.Growing &&
                (current.Crop != CropStage.Growing || current.RemainingSeconds > before.RemainingSeconds);
            if (roundEnded && result.Harvested > 0 && game.GetRawStock(CropKind.Radish) > beforeStock)
                harvested = true;
            if (harvested && current.Crop != CropStage.None) replanted = true;
            if (targets.Any(farm => game.GetPlot(farm).CropKind != CropKind.Radish || game.GetFarmCultivation(farm).PlanId != null))
                return Fail("删除表后的农田仍执行旧表异种安排");
        }
        return harvested && replanted && game.GetRawStock(CropKind.Radish) > 1 ||
            Fail($"删除表后的当前作物没有完成真实收获并由工人自动复种：{mode}/{stage}/wet={wet} harvested={harvested} replanted={replanted} stock={game.GetRawStock(CropKind.Radish)} final={game.GetPlot(targets[0])}");
    }

    private static bool Fail(string message) { GD.PushError("共享年度表删除：" + message); return false; }
}
