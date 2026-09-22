using Godot;

public partial class TestFarmGame : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        if (passed)
            GD.Print("工人、生产、市场与交易检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks()
    {
        return CheckProductionLoop() && CheckWorkerRotation() &&
            CheckMarketCurve() && CheckDayTiming();
    }

    private static bool CheckProductionLoop()
    {
        var game = new FarmGame(12345);
        Vector2I farm = new(3, 4);
        Vector2I mill = new(100, 100);
        Vector2I extraLand = new(0, 127);
        if (game.MoneyCents != 0 || game.WheatStock != 0 || game.FlourStock != 0 || game.FreeLandGrants != 2 ||
            game.CurrentDay != 1 || game.CurrentFlourPriceCents != MarketPriceCurve.InitialPriceCents)
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
        SaleResult emptySale = game.SellAll();
        if (emptySale.Quantity != 0 || emptySale.RevenueCents != 0 || game.MoneyCents != 0)
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
        int salePrice = game.CurrentFlourPriceCents;
        SaleResult sale = game.SellAll();
        if (game.FlourStock != 0 || sale.Quantity != 2 || sale.RevenueCents != 2 * salePrice ||
            game.MoneyCents != sale.RevenueCents || game.UnlockLand(extraLand) != null ||
            game.MoneyCents != sale.RevenueCents - FarmGame.LandCostCents)
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
        var game = new FarmGame(12345);
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

    private static bool CheckMarketCurve()
    {
        var curve = new MarketPriceCurve(24680);
        var sameCurve = new MarketPriceCurve(24680);
        var otherCurve = new MarketPriceCurve(13579);
        int previousPrice = curve.GetPriceCents(1);
        bool differentSeedChangedPrice = false;
        int smallChanges = 0;
        int mediumChanges = 0;
        int largeChanges = 0;
        if (previousPrice != MarketPriceCurve.InitialPriceCents)
            return Fail("市场曲线未从 5.00 金币开始");

        for (int day = 2; day <= 100000; day++)
        {
            int price = curve.GetPriceCents(day);
            if (price < MarketPriceCurve.MinimumPriceCents || price > MarketPriceCurve.MaximumPriceCents)
                return Fail($"第 {day} 天价格超出 1.00～20.00 金币范围");
            if (price != sameCurve.GetPriceCents(day))
                return Fail("相同市场种子没有生成相同价格");
            if (price != otherCurve.GetPriceCents(day))
                differentSeedChangedPrice = true;

            double change = System.Math.Abs(price - previousPrice) * 100.0 / previousPrice;
            if (change > 20.0)
                return Fail($"第 {day} 天价格变化超过 20%：{change:0.00}%");
            if (change < 5.0)
                smallChanges++;
            else if (change < 15.0)
                mediumChanges++;
            else
                largeChanges++;
            previousPrice = price;
        }

        if (!differentSeedChangedPrice)
            return Fail("不同市场种子生成了完全相同的价格曲线");
        if (smallChanges == 0 || mediumChanges == 0 || largeChanges == 0)
            return Fail($"长期曲线没有覆盖三种涨跌结果：小幅 {smallChanges}，中幅 {mediumChanges}，大幅 {largeChanges}");
        return true;
    }

    private static bool CheckDayTiming()
    {
        var game = new FarmGame(12345);
        for (int i = 0; i < FarmGame.TicksPerDay - 1; i++)
        {
            TickResult result = game.AdvanceTick();
            if (result.DayAdvanced || game.CurrentDay != 1)
                return Fail("未满 10 tick 就提前进入下一天");
        }

        TickResult nextDay = game.AdvanceTick();
        if (!nextDay.DayAdvanced || game.CurrentDay != 2 || game.DailyPriceChangePercent == 0.0)
            return Fail("第 10 tick 未进入下一天并更新价格");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
