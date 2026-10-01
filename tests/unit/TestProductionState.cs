using System;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Processing;
using FarmExchange.Time;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public partial class TestProductionState : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks() => CheckLand() && CheckPlacementMessages() &&
        CheckFarming() && CheckWater() && CheckPlantingRules() && CheckSeasonFailure() &&
        CheckProcessing() && CheckProcessingReserve();

    private static bool CheckLand()
    {
        var land = new LandOccupancy(FarmGame.MapSize * FarmGame.MapSize);
        if (land.Get(0) != BuildingKind.None ||
            !Throws<ArgumentOutOfRangeException>(() => land.Place(0, BuildingKind.None)) ||
            !Throws<ArgumentOutOfRangeException>(() => land.Place(0, (BuildingKind)999)) ||
            !Throws<InvalidOperationException>(() => land.Remove(0)) ||
            land.Get(0) != BuildingKind.None)
            return Fail("非法建筑或空地拆除改变了占用状态");

        land.Place(0, BuildingKind.Farm);
        land.Place(3, BuildingKind.Processor);
        land.Place(6, BuildingKind.Road);
        if (!Throws<InvalidOperationException>(() => land.Place(0, BuildingKind.Processor)) ||
            !Throws<InvalidOperationException>(() => land.Place(6, BuildingKind.Farm)) ||
            !Throws<InvalidOperationException>(() => land.Place(6, BuildingKind.Road)) ||
            land.Get(0) != BuildingKind.Farm || land.Get(3) != BuildingKind.Processor ||
            land.Get(6) != BuildingKind.Road)
            return Fail("重复放置覆盖了已有建筑");
        land.Remove(0);
        land.Remove(3);
        land.Remove(6);
        if (land.Get(0) != BuildingKind.None || land.Get(3) != BuildingKind.None ||
            land.Get(6) != BuildingKind.None)
            return Fail("拆除后建筑仍占用土地");
        return true;
    }

    private static bool CheckPlacementMessages()
    {
        if (FarmGame.GetBuildingCostCents(BuildingKind.Farm) != 1000 ||
            FarmGame.GetBuildingCostCents(BuildingKind.Processor) != 1000 ||
            FarmGame.GetBuildingCostCents(BuildingKind.Road) != 100 ||
            !Throws<ArgumentOutOfRangeException>(() => FarmGame.GetBuildingCostCents(BuildingKind.None)) ||
            !Throws<ArgumentOutOfRangeException>(() => FarmGame.GetBuildingCostCents((BuildingKind)999)))
            return Fail("统一类型费用查询错误，或非法类型被解释成免费建筑");
        if (new PlacementResult(LandFailure.None, 1000).ErrorMessage != null ||
            new PlacementResult(LandFailure.InvalidBuilding, 0).ErrorMessage != "无效建筑" ||
            new PlacementResult(LandFailure.InvalidCrop, 0).ErrorMessage != "无效作物" ||
            new PlacementResult(LandFailure.OutOfBounds, 0).ErrorMessage != "地图外地块" ||
            new PlacementResult(LandFailure.Occupied, 0).ErrorMessage != "该土地已有建筑" ||
            new PlacementResult(LandFailure.InsufficientFunds, 0).ErrorMessage != "金币不足，无法建造建筑")
            return Fail("放置结果的成功状态或失败提示不一致");
        return true;
    }

    private static bool CheckFarming()
    {
        var farms = new FarmingSystem(2);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        CropKind invalid = (CropKind)999;
        if (farms.TryWork(0, spring) || farms.AdvanceGrowth(0, out _) ||
            !Throws<InvalidOperationException>(() => farms.Get(0)) ||
            !Throws<InvalidOperationException>(() => farms.Remove(0)) ||
            !Throws<InvalidOperationException>(() => farms.SetCrop(0, CropKind.Radish)) ||
            !Throws<ArgumentOutOfRangeException>(() => farms.Place(0, invalid)) ||
            farms.HasFarm(0))
            return Fail("无农田或无效作物时农田状态发生改变");

        farms.Place(0, CropKind.Wheat);
        if (!Throws<InvalidOperationException>(() => farms.Place(0, CropKind.Corn)) ||
            !Throws<ArgumentOutOfRangeException>(() => farms.SetCrop(0, invalid)) ||
            farms.Get(0) != new FarmSnapshot(CropKind.Wheat, CropStage.None, 0))
            return Fail("重复放置或无效改种改变了农田状态");

        if (!farms.TryWork(0, spring))
            return Fail("空农田没有进入已播种阶段");
        farms.SetCrop(0, CropKind.Wheat);
        if (farms.Get(0) != new FarmSnapshot(CropKind.Wheat, CropStage.Seeded, 0) ||
            farms.AdvanceGrowth(0, out _))
            return Fail("选择相同作物清除了播种状态，或播种后提前生长");
        farms.SetCrop(0, CropKind.Radish);
        if (farms.Get(0) != new FarmSnapshot(CropKind.Radish, CropStage.None, 0) ||
            !farms.TryWork(0, spring) || !farms.TryWork(0, spring) ||
            farms.TryWork(0, spring) ||
            farms.Get(0).RemainingSeconds != 206)
            return Fail("改种没有清除旧进度，或生长中仍允许工人工作");

        for (int second = 1; second < 206; second++)
            if (farms.AdvanceGrowth(0, out _) || farms.Get(0).Stage != CropStage.Growing)
                return Fail("萝卜在目标秒数之前成熟");
        if (!farms.AdvanceGrowth(0, out CropKind harvested) || harvested != CropKind.Radish ||
            farms.Get(0) != new FarmSnapshot(CropKind.Radish, CropStage.None, 0))
            return Fail("萝卜到期后没有一次性收获并清除进度");
        farms.Remove(0);
        if (farms.HasFarm(0) || farms.AdvanceGrowth(0, out _) || farms.TryWork(0, spring))
            return Fail("拆除后仍能推进或照料农田");
        return true;
    }

    private static bool CheckProcessing()
    {
        var processors = new ProcessingSystem(2);
        var inventory = new GoodsInventory();
        CropKind invalid = (CropKind)999;
        if (processors.Advance(0, out _) || processors.TryStart(0, inventory) ||
            !Throws<InvalidOperationException>(() => processors.Get(0)) ||
            !Throws<InvalidOperationException>(() => processors.Remove(0)) ||
            !Throws<ArgumentOutOfRangeException>(() => processors.Place(0, invalid)) ||
            processors.HasProcessor(0))
            return Fail("无场地或无效作物时加工状态发生改变");

        processors.Place(0, CropKind.Radish);
        if (!Throws<InvalidOperationException>(() => processors.Place(0, CropKind.Wheat)) ||
            processors.Get(0) != new ProcessorSnapshot(CropKind.Radish, 0) ||
            processors.TryStart(0, inventory) || processors.Advance(0, out _))
            return Fail("重复放置或无原料时加工状态错误");

        inventory.AddRaw(CropKind.Radish, 2);
        if (!processors.TryStart(0, inventory) || inventory.GetRaw(CropKind.Radish) != 1 ||
            processors.TryStart(0, inventory) || inventory.GetRaw(CropKind.Radish) != 1 ||
            processors.Get(0).RemainingSeconds != 26)
            return Fail("加工领取、重复启动或半日时长错误");
        for (int second = 1; second < 26; second++)
            if (processors.Advance(0, out _) || processors.Get(0).RemainingSeconds <= 0)
                return Fail("腌制坊在目标秒数之前完成加工");
        if (!processors.Advance(0, out CropKind product) || product != CropKind.Radish ||
            processors.Get(0) != new ProcessorSnapshot(CropKind.Radish, 0) ||
            processors.Advance(0, out _) || inventory.GetRaw(CropKind.Radish) != 1)
            return Fail("加工到期后没有一次性完成或库存被重复领取");
        processors.Remove(0);
        if (processors.HasProcessor(0) || processors.TryStart(0, inventory))
            return Fail("拆除后仍能启动加工");
        return true;
    }

    private static bool CheckProcessingReserve()
    {
        var processors = new ProcessingSystem(2);
        var inventory = new GoodsInventory();
        processors.Place(0, CropKind.Radish);
        processors.Place(1, CropKind.Radish);
        if (processors.GetStatus(0, inventory) != ProcessorStatus.WaitingForRaw)
            return Fail("空库存场地没有返回缺少原料状态");
        inventory.AddRaw(CropKind.Radish, 3);
        inventory.SetRawReserve(CropKind.Radish, 3);
        if (processors.TryStart(0, inventory) ||
            processors.GetStatus(0, inventory) != ProcessorStatus.WaitingForReserve)
            return Fail("保留底线未阻止启动或等待原因错误");
        inventory.SetRawReserve(CropKind.Radish, 2);
        if (processors.GetStatus(0, inventory) != ProcessorStatus.ReadyToProcess ||
            processors.Get(0).RemainingSeconds != 0 || inventory.GetRaw(CropKind.Radish) != 3)
            return Fail("只读待领取查询消费了原料或启动批次");
        if (!processors.TryStart(0, inventory) || processors.TryStart(1, inventory) ||
            inventory.GetRaw(CropKind.Radish) != 2)
            return Fail("多场地竞争取走了保留原料");
        inventory.SetRawReserve(CropKind.Radish, 99);
        inventory.TakeAllRaw(CropKind.Radish);
        if (processors.GetStatus(0, inventory) != ProcessorStatus.Processing ||
            processors.GetStatus(1, inventory) != ProcessorStatus.WaitingForRaw ||
            processors.Get(0).RemainingSeconds != 26 || processors.TryStart(0, inventory))
            return Fail("提高底线或出售改变了进行中批次，或等待原因未实时更新");
        return true;
    }

    private static bool CheckWater()
    {
        var farms = new FarmingSystem(1);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        farms.Place(0, CropKind.Wheat);
        farms.SupplyWater(0);
        if (farms.Get(0) != new FarmSnapshot(CropKind.Wheat, CropStage.None, 0, true) ||
            !farms.TryWork(0, spring) ||
            farms.Get(0) != new FarmSnapshot(CropKind.Wheat, CropStage.Growing, 823, true))
            return Fail("空田留水后播种没有直接开始生长");

        farms.AdvanceGrowth(0, out _);
        farms.SupplyWater(0);
        if (farms.Get(0).RemainingSeconds != 822)
            return Fail("生长中重复供水重置了进度");

        farms.SetCrop(0, CropKind.Radish);
        if (farms.Get(0) != new FarmSnapshot(CropKind.Radish, CropStage.None, 0, true) ||
            !farms.TryWork(0, spring) || farms.Get(0).RemainingSeconds != 206)
            return Fail("改种没有保留水分供下一轮播种使用");
        for (int second = 0; second < 205; second++)
            if (farms.AdvanceGrowth(0, out _))
                return Fail("获水后萝卜提前成熟");
        if (!farms.AdvanceGrowth(0, out _) ||
            farms.Get(0) != new FarmSnapshot(CropKind.Radish, CropStage.None, 0))
            return Fail("收获没有清除本轮水分");

        farms.Remove(0);
        farms.Place(0, CropKind.Wheat);
        if (farms.Get(0).HasWater)
            return Fail("拆除后重建的农田继承了旧水分");
        return true;
    }

    private static bool CheckPlantingRules()
    {
        static PlantingFailure Check(CropKind crop, uint second, bool wet = false) =>
            PlantingRules.Check(crop, new GameCalendar(second).Snapshot, wet);

        const uint season = 4320;
        if (Check(CropKind.Sugarcane, 0) != PlantingFailure.WrongSeason ||
            Check(CropKind.Sugarcane, season) != PlantingFailure.None ||
            Check(CropKind.Potato, season) != PlantingFailure.WrongSeason ||
            Check(CropKind.Radish, season * 2) != PlantingFailure.None)
            return Fail("作物适宜季节表没有参与播种检查");

        // 萝卜需 206 秒生长，工人动作后再过 1 秒才开始按秒结算。
        // 干田多预留 1 秒供水；恰在边界成熟允许播种。
        if (Check(CropKind.Radish, season - 208) != PlantingFailure.None ||
            Check(CropKind.Radish, season - 207) != PlantingFailure.InsufficientTime ||
            Check(CropKind.Radish, season - 207, true) != PlantingFailure.None ||
            Check(CropKind.Radish, season - 206) != PlantingFailure.InsufficientTime ||
            Check(CropKind.Radish, season - 206, true) != PlantingFailure.InsufficientTime ||
            Check(CropKind.Radish, season - 205, true) != PlantingFailure.InsufficientTime)
            return Fail("干湿农田预计成熟或季节临界秒错误");

        if (Check(CropKind.Corn, season - 2) != PlantingFailure.None ||
            Check(CropKind.Corn, season * 2 - 1031) != PlantingFailure.None ||
            Check(CropKind.Corn, season * 2 - 1030) != PlantingFailure.InsufficientTime ||
            Check(CropKind.Wheat, season * 5 - 825) != PlantingFailure.None ||
            Check(CropKind.Wheat, season * 5 - 824) != PlantingFailure.InsufficientTime)
            return Fail("连续适宜季节或跨年边界没有按完整区间计算");
        return true;
    }

    private static bool CheckSeasonFailure()
    {
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CalendarSnapshot plantingDate = new GameCalendar(
                crop.Kind == CropKind.Sugarcane ? 4320u : 0u).Snapshot;
            foreach (Season season in Enum.GetValues<Season>())
            {
                var farms = new FarmingSystem(4);
                farms.Place(0, crop.Kind);
                farms.Place(1, crop.Kind);
                farms.Place(2, crop.Kind);
                farms.SupplyWater(2);
                if (!farms.TryWork(0, plantingDate) || !farms.TryWork(1, plantingDate) ||
                    !farms.TryWork(1, plantingDate))
                    return Fail("越季测试没有建立待水与生长中的作物");
                farms.AdvanceGrowth(1, out _);
                FarmSnapshot seeded = farms.Get(0);
                FarmSnapshot growing = farms.Get(1);
                FarmSnapshot empty = farms.Get(2);
                bool allowed = (crop.GrowingSeasons & (GrowingSeasons)(1 << (int)season)) != 0;

                farms.ClearDisallowedCrops(season);
                FarmSnapshot cleared = new(crop.Kind, CropStage.None, 0);
                if (farms.Get(0) != (allowed ? seeded : cleared) ||
                    farms.Get(1) != (allowed ? growing : cleared) ||
                    farms.Get(2) != empty || farms.HasFarm(3))
                    return Fail($"{crop.CropName}进入{season}时未按阶段清理，或清理了空田留水");
                if (!allowed)
                {
                    if (farms.AdvanceGrowth(0, out _) || farms.AdvanceGrowth(1, out _) ||
                        farms.TryWork(0, new GameCalendar((uint)season * 4320).Snapshot))
                        return Fail("越季失败仍收获，或禁生季节清理后反复播种");
                    farms.SupplyWater(1);
                    farms.ClearDisallowedCrops(season);
                    if (farms.Get(1) != new FarmSnapshot(crop.Kind, CropStage.None, 0, true))
                        return Fail("重复越季清理影响了已清空农田的新留水");
                    if (!farms.TryWork(0, plantingDate) || farms.Get(0).Stage != CropStage.Seeded)
                        return Fail("适宜条件恢复后没有重新播种");
                }
            }
        }
        return true;
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
