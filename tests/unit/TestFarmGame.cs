using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Market;

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
        CheckWorkerRotation() && CheckRawSales();

    private static bool CheckInitialCenter()
    {
        Vector2I[] farms = { new(63, 63), new(64, 63), new(65, 63) };
        Vector2I[] processors = { new(63, 64), new(64, 64) };
        bool sawSame = false;
        bool sawSplit = false;
        for (int seed = 0; seed < 64; seed++)
        {
            var game = new FarmGame(seed);
            var repeat = new FarmGame(seed);
            if (game.MoneyCents != 5000)
                return Fail("开局金币错误");
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
            if ((primary == secondary && !sawSame) || (primary != secondary && !sawSplit))
            {
                for (int tick = 0; tick < 60; tick++)
                    game.AdvanceTick();
                if (game.GetProductStock(primary) == 0 ||
                    game.GetProductStock(secondary) == 0)
                    return Fail("中心预置建筑没有形成自动生产循环");
            }
            sawSame |= primary == secondary;
            sawSplit |= primary != secondary;
        }
        if (!sawSame || !sawSplit)
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
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            var game = new FarmGame(12345);
            Vector2I farm = new(3, 4);
            Vector2I processor = new(100, 100);
            RemoveInitialBuildings(game);
            if (game.MoneyCents != 5000 || game.CurrentDay != 1 ||
                game.CurrentFlourPriceCents != MarketPriceCurve.InitialPriceCents)
                return Fail("初始资源错误");
            if (game.BuildFarm(farm) != null ||
                game.SetFarmCrop(farm, crop.Kind) != null ||
                game.BuildProcessor(processor, crop.Kind) != null)
                return Fail($"{crop.CropName}的农田或加工场地建造失败");
            if (game.MoneyCents != 3000)
                return Fail($"{crop.CropName}建造费用错误");
            if (crop.ProcessingTicks >= crop.GrowthTicks)
                return Fail($"{crop.CropName}加工未快于成熟");

            TickResult sow = game.AdvanceTick();
            TickResult water = game.AdvanceTick();
            if (!sow.WorkerActed || !water.WorkerActed ||
                game.GetPlot(farm).Crop != CropStage.Growing ||
                game.GetPlot(farm).RemainingTicks != crop.GrowthTicks)
                return Fail($"{crop.CropName}播种或浇水时序错误");
            for (int i = 0; i < crop.GrowthTicks - 1; i++)
                game.AdvanceTick();
            if (game.GetRawStock(crop.Kind) != 0)
                return Fail($"{crop.CropName}过早成熟");
            TickResult harvest = game.AdvanceTick();
            if (harvest.Harvested != 1 || game.GetRawStock(crop.Kind) != 0 ||
                game.GetPlot(processor).RemainingTicks != crop.ProcessingTicks)
                return Fail($"{crop.CropName}未按时收获并投入对应场地");
            if (game.SellRaw(crop.Kind).Quantity != 0)
                return Fail($"{crop.CropName}加工中的原料仍可出售");
            for (int i = 0; i < crop.ProcessingTicks - 1; i++)
                game.AdvanceTick();
            if (game.GetProductStock(crop.Kind) != 0)
                return Fail($"{crop.ProductName}过早产出");
            TickResult finished = game.AdvanceTick();
            if (finished.Produced != 1 || game.GetProductStock(crop.Kind) != 1)
                return Fail($"{crop.ProductName}未按时产出");
            int expectedRevenue = game.GetProductPriceCents(crop.Kind);
            SaleResult sale = game.SellAll();
            if (sale.Quantity != 1 || sale.RevenueCents != expectedRevenue ||
                game.MoneyCents != 3000 + expectedRevenue ||
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
        Vector2I farm = new(0, 0);
        Vector2I processor = new(1, 0);
        game.BuildFarm(farm);
        game.BuildProcessor(processor, CropKind.Rice);
        game.AdvanceTick();
        game.AdvanceTick();
        if (game.SetFarmCrop(farm, CropKind.Corn) != null ||
            game.GetPlot(farm).Crop != CropStage.None ||
            game.GetPlot(farm).RemainingTicks != 0)
            return Fail("切换作物未丢弃生长中的旧作物");
        if (game.SetFarmCrop(farm, CropKind.Corn) != null ||
            game.SetFarmCrop(processor, CropKind.Corn) == null)
            return Fail("重复选择或非农田选择处理错误");
        game.AdvanceTick();
        game.AdvanceTick();
        for (int i = 0; i < FarmGame.GetCrop(CropKind.Corn).GrowthTicks; i++)
            game.AdvanceTick();
        if (game.GetRawStock(CropKind.Wheat) != 0 || game.GetRawStock(CropKind.Corn) != 1 ||
            game.GetProductStock(CropKind.Rice) != 0 || game.GetPlot(processor).RemainingTicks != 0)
            return Fail("加工场地消耗了不匹配原料，或切换前作物仍产出");
        if (game.SellAll().Quantity != 0)
            return Fail("原料被直接出售");
        game.RemoveBuilding(processor);
        game.BuildProcessor(processor, CropKind.Corn);
        if (game.GetRawStock(CropKind.Corn) != 0 ||
            game.GetPlot(processor).RemainingTicks != FarmGame.GetCrop(CropKind.Corn).ProcessingTicks)
            return Fail("新建匹配场地没有自动处理库存原料");
        return true;
    }

    private static bool CheckRemoval()
    {
        var game = new FarmGame(12345);
        RemoveInitialBuildings(game);
        Vector2I farm = new(0, 0);
        Vector2I processor = new(1, 0);
        game.BuildFarm(farm);
        game.BuildProcessor(processor, CropKind.Wheat);
        game.AdvanceTick();
        game.AdvanceTick();
        for (int i = 0; i < FarmGame.GetCrop(CropKind.Wheat).GrowthTicks; i++)
            game.AdvanceTick();
        if (game.GetPlot(farm).Crop != CropStage.Seeded ||
            game.GetPlot(processor).RemainingTicks == 0 ||
            game.RemoveBuilding(farm) != null || game.RemoveBuilding(processor) != null)
            return Fail("移除农田或加工中的场地失败");
        for (int i = 0; i < FarmGame.GetCrop(CropKind.Wheat).ProcessingTicks; i++)
            game.AdvanceTick();
        if (game.GetProductStock(CropKind.Wheat) != 0 ||
            game.GetPlot(farm).Building != BuildingKind.None || game.MoneyCents != 3000)
            return Fail("移除建筑后仍产出或返还建造费");
        return true;
    }

    private static bool CheckWorkerRotation()
    {
        var game = new FarmGame(12345);
        RemoveInitialBuildings(game);
        Vector2I first = new(0, 0);
        Vector2I second = new(1, 0);
        game.BuildFarm(first);
        game.BuildFarm(second);
        game.SetFarmCrop(second, CropKind.Sunflower);
        game.AdvanceTick();
        if (game.GetPlot(first).Crop != CropStage.Seeded || game.GetPlot(second).Crop != CropStage.None)
            return Fail("单工人第一 tick 完成了多次操作");
        game.AdvanceTick();
        if (game.GetPlot(second).Crop != CropStage.Seeded)
            return Fail("工人未轮流照料第二块农田");
        game.AdvanceTick();
        game.AdvanceTick();
        if (game.GetPlot(first).RemainingTicks != 4 || game.GetPlot(second).RemainingTicks != 9)
            return Fail("不同作物未按各自成熟时间生长");
        return true;
    }

    private static bool CheckRawSales()
    {
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            var game = new FarmGame(12345);
            RemoveInitialBuildings(game);
            Vector2I farm = new(3, 4);
            game.BuildFarm(farm);
            game.SetFarmCrop(farm, crop.Kind);
            for (int i = 0; i < 30; i++)
                game.AdvanceTick();
            int stock = game.GetRawStock(crop.Kind);
            int money = game.MoneyCents;
            int price = game.GetRawPriceCents(crop.Kind);
            if (stock == 0 || game.GetProductStock(crop.Kind) != 0)
                return Fail($"{crop.CropName}原料未进入可出售库存");
            SaleResult sale = game.SellRaw(crop.Kind);
            if (sale.Quantity != stock || sale.RevenueCents != stock * price ||
                game.MoneyCents != money + sale.RevenueCents || game.GetRawStock(crop.Kind) != 0 ||
                game.SellRaw(crop.Kind).Quantity != 0 || game.SellAll().Quantity != 0)
                return Fail($"{crop.CropName}原料出售数量、当日收入或重复出售错误");
        }

        var mixed = new FarmGame(12345);
        RemoveInitialBuildings(mixed);
        Vector2I wheatFarm = new(3, 4);
        Vector2I cornFarm = new(4, 4);
        mixed.BuildFarm(wheatFarm);
        mixed.BuildFarm(cornFarm);
        mixed.SetFarmCrop(cornFarm, CropKind.Corn);
        for (int i = 0; i < 30; i++)
            mixed.AdvanceTick();
        int cornStock = mixed.GetRawStock(CropKind.Corn);
        if (mixed.GetRawStock(CropKind.Wheat) == 0 || cornStock == 0)
            return Fail("两种原料没有分别进入库存");
        mixed.SellRaw(CropKind.Wheat);
        if (mixed.GetRawStock(CropKind.Corn) != cornStock)
            return Fail("出售小麦原料改变了玉米原料库存");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
