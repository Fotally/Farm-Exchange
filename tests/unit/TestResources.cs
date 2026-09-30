using System;
using Godot;
using FarmExchange.Economy;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public partial class TestResources : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        if (passed)
            GD.Print("资源模块：作物定义、库存与金币检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks() => CheckCropDefinitions() && CheckInventory() && CheckWallet();

    private static bool CheckCropDefinitions()
    {
        (CropKind Kind, string Crop, string Building, string Product,
            int GrowthDays, int Harvest, int ProcessingHalfDays, int ProductPercent,
            int RawPercent, GrowingSeasons Seasons)[] expected =
        {
            (CropKind.Wheat, "小麦", "磨坊", "面粉", 16, 2, 4, 100, 50,
                GrowingSeasons.Spring | GrowingSeasons.Autumn | GrowingSeasons.Winter),
            (CropKind.Corn, "玉米", "玉米加工坊", "玉米粉", 20, 4, 6, 100, 50,
                GrowingSeasons.Spring | GrowingSeasons.Summer),
            (CropKind.Rice, "水稻", "碾米坊", "大米", 12, 3, 4, 120, 50,
                GrowingSeasons.Spring | GrowingSeasons.Summer),
            (CropKind.Potato, "马铃薯", "淀粉坊", "淀粉", 14, 8, 2, 80, 50,
                GrowingSeasons.Spring | GrowingSeasons.Autumn),
            (CropKind.Sunflower, "向日葵", "榨油坊", "葵花籽油", 17, 1, 12, 160, 50,
                GrowingSeasons.Spring | GrowingSeasons.Summer),
            (CropKind.Sugarcane, "甘蔗", "制糖坊", "蔗糖", 40, 12, 4, 200, 50,
                GrowingSeasons.Summer | GrowingSeasons.Autumn),
            (CropKind.Radish, "萝卜", "腌制坊", "腌萝卜", 4, 6, 1, 10, 50,
                GrowingSeasons.Spring | GrowingSeasons.Autumn | GrowingSeasons.Winter),
        };
        if (FarmGame.Crops.Count != expected.Length)
            return Fail("迁移后作物数量改变");
        foreach (var row in expected)
        {
            CropDefinition crop = FarmGame.GetCrop(row.Kind);
            if (crop.Kind != row.Kind || crop.CropName != row.Crop ||
                crop.BuildingName != row.Building || crop.ProductName != row.Product ||
                crop.GrowthDays != row.GrowthDays || crop.HarvestQuantity != row.Harvest ||
                crop.ProcessingHalfDays != row.ProcessingHalfDays ||
                crop.PricePercent != row.ProductPercent || crop.RawPricePercent != row.RawPercent ||
                crop.GrowingSeasons != row.Seasons)
                return Fail($"{row.Kind} 的名称、时长、售价倍率或适宜季节改变");
        }
        if (!Throws<ArgumentOutOfRangeException>(() => CropCatalog.Get((CropKind)999)))
            return Fail("无效作物标识没有被作物定义入口拒绝");
        return true;
    }

    private static bool CheckInventory()
    {
        var stock = new GoodsInventory();
        stock.AddRaw(CropKind.Wheat, 3);
        if (!stock.TryTakeRawForProcessing(CropKind.Wheat) || stock.GetRaw(CropKind.Wheat) != 2 ||
            stock.GetRaw(CropKind.Corn) != 0)
            return Fail("加工领取改变了错误的原料库存");
        stock.TakeAllRaw(CropKind.Wheat);
        stock.AddProduct(CropKind.Wheat, 2);
        if (stock.GetRaw(CropKind.Wheat) != 0 || stock.GetProduct(CropKind.Wheat) != 2)
            return Fail("原料出售或成品入库改变了错误的分类库存");
        stock.TakeAllProducts();
        if (stock.GetProduct(CropKind.Wheat) != 0)
            return Fail("成品出售后库存没有清零");

        if (!Throws<ArgumentOutOfRangeException>(() => stock.AddRaw(CropKind.Wheat, -1)) ||
            !Throws<ArgumentOutOfRangeException>(() => stock.AddProduct((CropKind)999, 1)) ||
            stock.GetRaw(CropKind.Wheat) != 0 || stock.GetProduct(CropKind.Wheat) != 0)
            return Fail("非法数量或作物标识修改了库存");
        stock.AddRaw(CropKind.Wheat, int.MaxValue);
        if (!Throws<OverflowException>(() => stock.AddRaw(CropKind.Wheat, 1)) ||
            stock.GetRaw(CropKind.Wheat) != int.MaxValue)
            return Fail("库存溢出未在修改前拒绝");
        return true;
    }

    private static bool CheckWallet()
    {
        var wallet = new Wallet(5000);
        if (!wallet.TrySpend(1000) || wallet.TrySpend(5000) || wallet.BalanceCents != 4000)
            return Fail("金币扣款或余额不足处理错误");
        wallet.Credit(150);
        if (wallet.BalanceCents != 4150 ||
            !Throws<ArgumentOutOfRangeException>(() => wallet.TrySpend(-1)) ||
            !Throws<ArgumentOutOfRangeException>(() => wallet.Credit(-1)) ||
            wallet.BalanceCents != 4150)
            return Fail("非法金币金额修改了余额");
        var full = new Wallet(int.MaxValue);
        if (!Throws<OverflowException>(() => full.Credit(1)) || full.BalanceCents != int.MaxValue)
            return Fail("金币溢出未在修改前拒绝");
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
