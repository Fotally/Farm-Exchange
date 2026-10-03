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
        CheckSowingControl() && CheckSeasonMaturity() && CheckExpectedRoundEnd() &&
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
            farms.Get(0) != new FarmSnapshot(CropKind.Wheat, CropStage.Growing, 823, true, 5760))
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

    private static bool CheckSowingControl()
    {
        var farms = new FarmingSystem(1);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        farms.Place(0, CropKind.Radish);
        FarmWorkRequest oldSow = farms.GetWorkNeed(0, spring)!.Value;
        farms.SetSowingEnabled(0, false);
        if (farms.Get(0).SowingEnabled || farms.GetWorkNeed(0, spring) != null ||
            farms.TryCompleteWork(oldSow, spring))
            return Fail("休耕未禁播或接受了旧播种凭据");
        farms.SupplyWater(0);
        farms.SetSowingEnabled(0, true);
        if (farms.TryCompleteWork(oldSow, spring) || !farms.TryWork(0, spring))
            return Fail("恢复播种复活旧任务或未保留空田水分");
        farms.RestartCrop(0, CropKind.Radish);
        if (farms.Get(0).Stage != CropStage.None || !farms.Get(0).HasWater ||
            farms.Get(0).RemainingTimeUnits != 0 || !farms.TryWork(0, spring))
            return Fail("同种立即改种未中断本轮或丢失水分");
        farms.RestartCrop(0, CropKind.Corn);
        if (farms.Get(0).CropKind != CropKind.Corn || farms.Get(0).Stage != CropStage.None ||
            !Throws<ArgumentOutOfRangeException>(() => farms.RestartCrop(0, (CropKind)999)))
            return Fail("立即改种未设置新作物或接受非法品种");

        farms.Remove(0);
        farms.Place(0, CropKind.Radish);
        farms.TryWork(0, spring);
        FarmWorkRequest oldWater = farms.GetWorkNeed(0, spring)!.Value;
        farms.SetSowingEnabled(0, false);
        FarmWorkRequest water = farms.GetWorkNeed(0, spring)!.Value;
        if (water.Kind != FarmWorkKind.Water || farms.TryCompleteWork(oldWater, spring) ||
            !farms.TryCompleteWork(water, spring) || farms.Get(0).Stage != CropStage.Growing)
            return Fail("禁播影响了待水本轮或旧工作凭据没有失效");
        farms.SetSowingEnabled(0, false);
        for (int second = 0; second < 206; second++)
            farms.AdvanceGrowth(0, out _);
        if (farms.GetWorkNeed(0, spring) != null || farms.Get(0).HasWater)
            return Fail("禁播田收获后自动复种或遗留水分");

        CalendarSnapshot riskySpring = new GameCalendar(4319).Snapshot;
        farms.SetSowingEnabled(0, true);
        if (PlantingRules.Check(CropKind.Radish, riskySpring, false) != PlantingFailure.InsufficientTime ||
            !PlantingRules.CanSow(CropKind.Radish, riskySpring, false) || !farms.TryWork(0, riskySpring) ||
            PlantingRules.CanSow(CropKind.Radish, new GameCalendar(4320).Snapshot, false))
            return Fail("预计时间不足仍拒绝播种或禁生季允许播种");
        return true;
    }

    private static bool CheckSeasonMaturity()
    {
        var farms = new FarmingSystem(6);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        // 马铃薯完整周期 5040 单位，648 次正常推进后剩 504，恰好十分之一。
        for (int index = 0; index < 4; index++)
        {
            farms.Place(index, CropKind.Potato);
            farms.TryWork(index, spring);
            if (index < 3)
            {
                farms.SupplyWater(index);
                for (int second = 0; second < 647 + index; second++)
                    if (farms.AdvanceGrowth(index, out _))
                        return Fail("禁生补救测试在建立阈值状态时已提前成熟");
            }
        }
        farms.Place(4, CropKind.Potato);
        farms.SupplyWater(4);
        FarmSnapshot nearMaturity = farms.Get(2);
        if (farms.Get(0).RemainingTimeUnits != 511 || farms.Get(1).RemainingTimeUnits != 504 ||
            nearMaturity.RemainingTimeUnits != 497 ||
            farms.TryMatureBeforeDisallowedSeason(2, Season.Autumn, out _) || farms.Get(2) != nearMaturity)
            return Fail("精确剩余时间错误或适宜换季错误触发成熟补救");
        if (farms.TryMatureBeforeDisallowedSeason(0, Season.Summer, out _) ||
            farms.TryMatureBeforeDisallowedSeason(1, Season.Summer, out _) ||
            farms.TryMatureBeforeDisallowedSeason(3, Season.Summer, out _) ||
            farms.TryMatureBeforeDisallowedSeason(4, Season.Summer, out _) ||
            farms.TryMatureBeforeDisallowedSeason(5, Season.Summer, out _) ||
            !farms.TryMatureBeforeDisallowedSeason(2, Season.Summer, out CropKind harvested) ||
            harvested != CropKind.Potato || farms.Get(2) != new FarmSnapshot(CropKind.Potato, CropStage.None, 0) ||
            farms.TryMatureBeforeDisallowedSeason(2, Season.Summer, out _) || farms.AdvanceGrowth(2, out _))
            return Fail("禁生阈值未严格小于十分之一，待水被补救，或收获重复发生");
        farms.ClearDisallowedCrops(Season.Summer);
        for (int index = 0; index < 4; index++)
            if (farms.Get(index) != new FarmSnapshot(CropKind.Potato, CropStage.None, 0))
                return Fail("未补救作物换季清理后遗留状态");
        if (!farms.Get(4).HasWater)
            return Fail("补救与清理影响了空田留水");

        farms.Remove(0);
        farms.Place(0, CropKind.Radish);
        farms.SupplyWater(0);
        farms.TryWork(0, spring);
        for (int second = 0; second < 205; second++)
            farms.AdvanceGrowth(0, out _);
        if (!farms.AdvanceGrowth(0, out harvested) || harvested != CropKind.Radish ||
            farms.TryMatureBeforeDisallowedSeason(0, Season.Summer, out _) || farms.AdvanceGrowth(0, out _))
            return Fail("正常边界收获后补救重复报告收成");
        return true;
    }

    private static bool CheckExpectedRoundEnd()
    {
        var farms = new FarmingSystem(1);
        farms.Place(0, CropKind.Radish);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        if (farms.GetExpectedRoundEndTimeUnits(0, 0) != null || !farms.TryWork(0, spring) ||
            farms.GetExpectedRoundEndTimeUnits(0, 7) != null)
            return Fail("空田或未知供水阶段虚构了本轮结束日期");
        farms.SupplyWater(0);
        FarmSnapshot growing = farms.Get(0);
        long expected = 14 + 206 * GameTimeUnits.PerSecond;
        if (farms.GetExpectedRoundEndTimeUnits(0, 14) != expected || farms.Get(0) != growing)
            return Fail("自然成熟预测未对齐实际 tick，或只读预测修改了生长状态");
        farms.AdvanceGrowth(0, out _);
        if (farms.GetExpectedRoundEndTimeUnits(0, 21) != expected)
            return Fail("正常推进使同一轮结束缓存每日漂移");

        farms.RestartCrop(0, CropKind.Sugarcane);
        CalendarSnapshot summer = new GameCalendar(8000).Snapshot;
        farms.TryWork(0, summer);
        growing = farms.Get(0);
        if (farms.GetExpectedRoundEndTimeUnits(0, 8000 * 7) != 8000 * 7 + 2058 * 7 ||
            farms.Get(0) != growing)
            return Fail("甘蔗夏秋适季跨季被错误截短，或查询重置了进度");
        if (farms.GetExpectedRoundEndTimeUnits(0, 88004) != 252 * GameTimeUnits.PerDay)
            return Fail("甘蔗秋冬未成熟清理结局未纳入结束日期");
        for (int tick = 0; tick < 2000; tick++)
            farms.AdvanceGrowth(0, out _);
        growing = farms.Get(0);
        if (farms.GetExpectedRoundEndTimeUnits(0, 12905 * 7) != 252 * GameTimeUnits.PerDay ||
            farms.Get(0) != growing || !farms.TryMatureBeforeDisallowedSeason(0, Season.Winter, out _))
            return Fail("甘蔗临近成熟的禁生补救结局与只读预测不一致");

        farms.RestartCrop(0, CropKind.Radish);
        farms.SupplyWater(0);
        farms.TryWork(0, spring);
        for (int tick = 0; tick < 205; tick++)
            farms.AdvanceGrowth(0, out _);
        if (farms.GetExpectedRoundEndTimeUnits(0, 4319 * 7) != 4320 * 7 ||
            !farms.AdvanceGrowth(0, out _) || farms.GetExpectedRoundEndTimeUnits(0, 4320 * 7) != null ||
            farms.TryMatureBeforeDisallowedSeason(0, Season.Summer, out _))
            return Fail("正常成熟恰在禁生边界时预测错误或重复补救收获");
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
