using Godot;

public partial class TestFarmGame : Node
{
    public override void _Ready()
    {
        bool passed = CheckProductionLoop() && CheckWorkerRotation();
        if (passed)
            GD.Print("工人、生产与交易检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    private static bool CheckProductionLoop()
    {
        var game = new FarmGame();
        Vector2I farm = new(3, 4);
        Vector2I mill = new(100, 100);
        Vector2I extraLand = new(0, 127);
        if (game.Money != 0 || game.WheatStock != 0 || game.FlourStock != 0 || game.FreeLandGrants != 2)
            return Fail("初始资源错误");
        if (game.UnlockLand(farm) != null || game.UnlockLand(mill) != null || game.FreeLandGrants != 0)
            return Fail("两次任意位置免费选地失败");
        if (game.UnlockLand(extraLand) == null || game.BuildFarm(extraLand) == null)
            return Fail("金币不足时仍可购地或建农田");
        if (game.BuildFarm(farm) != null || game.BuildMill(mill) != null)
            return Fail("农田或磨坊建造失败");

        TickResult sow = game.AdvanceTick();
        if (!sow.WorkerActed || game.GetPlot(farm).Crop != CropStage.Seeded)
            return Fail("工人未在第一 tick 自动播种");
        TickResult water = game.AdvanceTick();
        if (!water.WorkerActed || game.GetPlot(farm).Crop != CropStage.Growing ||
            game.GetPlot(farm).RemainingTicks != FarmGame.GrowthTicks)
            return Fail("工人未在第二 tick 自动浇水");
        for (int i = 0; i < FarmGame.GrowthTicks - 1; i++)
            game.AdvanceTick();
        if (game.WheatStock != 0 || game.GetPlot(farm).RemainingTicks != 1)
            return Fail("小麦过早成熟");
        TickResult harvest = game.AdvanceTick();
        if (harvest.WheatHarvested != 1 || game.WheatStock != 0 ||
            game.GetPlot(mill).RemainingTicks != FarmGame.MillingTicks ||
            game.GetPlot(farm).Crop != CropStage.Seeded)
            return Fail("第 7 tick 未自动收获、交给磨坊并开始下一轮播种");
        if (game.SellAll() != 0 || game.Money != 0)
            return Fail("原小麦被直接出售");

        for (int i = 0; i < 6; i++)
            game.AdvanceTick();
        if (game.WheatStock != 1 || game.FlourStock != 0 || game.GetPlot(mill).RemainingTicks != 4)
            return Fail("磨坊忙时未保存后续小麦");
        for (int i = 0; i < 4; i++)
            game.AdvanceTick();
        if (game.FlourStock != 1 || game.WheatStock != 0 || game.GetPlot(mill).RemainingTicks != FarmGame.MillingTicks)
            return Fail("磨坊完成后未自动加工下一份小麦");
        for (int i = 0; i < FarmGame.MillingTicks; i++)
            game.AdvanceTick();
        if (game.FlourStock != 2 || game.SellAll() != 2 || game.Money != 10 ||
            game.UnlockLand(extraLand) != null || game.Money != 0)
            return Fail("面粉出售后购地流程错误");

        int wheatBeforeRemoval = game.WheatStock;
        if (game.GetPlot(farm).Crop != CropStage.Growing || game.GetPlot(mill).RemainingTicks == 0 ||
            game.RemoveBuilding(farm) != null || game.RemoveBuilding(mill) != null)
            return Fail("移除生长中的农田或加工中的磨坊失败");
        for (int i = 0; i < FarmGame.MillingTicks; i++)
            game.AdvanceTick();
        if (game.FlourStock != 0 || game.WheatStock != wheatBeforeRemoval ||
            game.GetPlot(farm).Building != BuildingKind.None || game.GetPlot(mill).Building != BuildingKind.None ||
            !game.GetPlot(farm).IsUnlocked)
            return Fail("移除建筑后仍产出，或已投入原料被退回");
        return true;
    }

    private static bool CheckWorkerRotation()
    {
        var game = new FarmGame();
        Vector2I first = new(0, 0);
        Vector2I second = new(1, 0);
        game.UnlockLand(first);
        game.UnlockLand(second);
        game.BuildFarm(first);
        game.BuildFarm(second);
        game.AdvanceTick();
        if (game.GetPlot(first).Crop != CropStage.Seeded || game.GetPlot(second).Crop != CropStage.None)
            return Fail("单工人第一 tick 完成了多次操作");
        game.AdvanceTick();
        if (game.GetPlot(first).Crop != CropStage.Seeded || game.GetPlot(second).Crop != CropStage.Seeded)
            return Fail("工人未轮流照料第二块农田");
        game.AdvanceTick();
        game.AdvanceTick();
        if (game.GetPlot(first).Crop != CropStage.Growing || game.GetPlot(second).Crop != CropStage.Growing)
            return Fail("工人未轮流浇水");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
