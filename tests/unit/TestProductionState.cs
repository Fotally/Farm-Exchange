using System;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Processing;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public partial class TestProductionState : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks() => CheckLand() && CheckPlacementMessages() &&
        CheckFarming() && CheckWater() && CheckProcessing();

    private static bool CheckLand()
    {
        var land = new LandOccupancy(3);
        if (land.Get(0) != BuildingKind.None ||
            !Throws<ArgumentOutOfRangeException>(() => land.Place(0, BuildingKind.None)) ||
            !Throws<ArgumentOutOfRangeException>(() => land.Place(0, (BuildingKind)999)) ||
            !Throws<InvalidOperationException>(() => land.Remove(0)) ||
            land.Get(0) != BuildingKind.None)
            return Fail("非法建筑或空地拆除改变了占用状态");

        land.Place(0, BuildingKind.Farm);
        land.Place(1, BuildingKind.Processor);
        if (!Throws<InvalidOperationException>(() => land.Place(0, BuildingKind.Processor)) ||
            land.Get(0) != BuildingKind.Farm || land.Get(1) != BuildingKind.Processor)
            return Fail("重复放置覆盖了已有建筑");
        land.Remove(0);
        land.Remove(1);
        if (land.Get(0) != BuildingKind.None || land.Get(1) != BuildingKind.None)
            return Fail("拆除后建筑仍占用土地");
        return true;
    }

    private static bool CheckPlacementMessages()
    {
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
        CropKind invalid = (CropKind)999;
        if (farms.TryWork(0) || farms.AdvanceGrowth(0, out _) ||
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

        if (!farms.TryWork(0))
            return Fail("空农田没有进入已播种阶段");
        farms.SetCrop(0, CropKind.Wheat);
        if (farms.Get(0) != new FarmSnapshot(CropKind.Wheat, CropStage.Seeded, 0) ||
            farms.AdvanceGrowth(0, out _))
            return Fail("选择相同作物清除了播种状态，或播种后提前生长");
        farms.SetCrop(0, CropKind.Radish);
        if (farms.Get(0) != new FarmSnapshot(CropKind.Radish, CropStage.None, 0) ||
            !farms.TryWork(0) || !farms.TryWork(0) || farms.TryWork(0) ||
            farms.Get(0).RemainingSeconds != 206)
            return Fail("改种没有清除旧进度，或生长中仍允许工人工作");

        for (int second = 1; second < 206; second++)
            if (farms.AdvanceGrowth(0, out _) || farms.Get(0).Stage != CropStage.Growing)
                return Fail("萝卜在目标秒数之前成熟");
        if (!farms.AdvanceGrowth(0, out CropKind harvested) || harvested != CropKind.Radish ||
            farms.Get(0) != new FarmSnapshot(CropKind.Radish, CropStage.None, 0))
            return Fail("萝卜到期后没有一次性收获并清除进度");
        farms.Remove(0);
        if (farms.HasFarm(0) || farms.AdvanceGrowth(0, out _) || farms.TryWork(0))
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

    private static bool CheckWater()
    {
        var farms = new FarmingSystem(1);
        farms.Place(0, CropKind.Wheat);
        farms.SupplyWater(0);
        if (farms.Get(0) != new FarmSnapshot(CropKind.Wheat, CropStage.None, 0, true) ||
            !farms.TryWork(0) ||
            farms.Get(0) != new FarmSnapshot(CropKind.Wheat, CropStage.Growing, 823, true))
            return Fail("空田留水后播种没有直接开始生长");

        farms.AdvanceGrowth(0, out _);
        farms.SupplyWater(0);
        if (farms.Get(0).RemainingSeconds != 822)
            return Fail("生长中重复供水重置了进度");

        farms.SetCrop(0, CropKind.Radish);
        if (farms.Get(0) != new FarmSnapshot(CropKind.Radish, CropStage.None, 0, true) ||
            !farms.TryWork(0) || farms.Get(0).RemainingSeconds != 206)
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
