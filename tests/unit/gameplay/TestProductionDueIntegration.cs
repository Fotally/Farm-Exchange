using System;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Weather;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public static class TestProductionDueIntegration
{
    public static bool RunChecks()
    {
        try
        {
            CheckMixedFifoAndCapacity();
            CheckReservationFailure();
            CheckCancellationAndPromotion();
            CheckCalendarLimit();
            CheckGameFifo();
            GD.Print("生产到期索引混合FIFO、容量保留、撤销与绝对时钟边界通过");
            return true;
        }
        catch (Exception error) { GD.PushError("生产到期接入失败：" + error); return false; }
    }

    private static void CheckMixedFifoAndCapacity()
    {
        var due = new SimulationDueIndex(3);
        var farming = new FarmingSystem(3, due, 65);
        var processing = new ProcessingSystem(3, due);
        var inventory = new GoodsInventory();
        processing.Place(2, CropKind.Sunflower);
        inventory.AddRaw(CropKind.Sunflower, 9);
        Require(processing.TryStart(2, inventory), "首个加工预约失败");
        Move(103);
        farming.Place(1, CropKind.Radish, WeatherKind.Sunny);
        farming.SupplyWater(1);
        Require(farming.TryWork(1, new GameCalendar(103).Snapshot), "中间农田预约失败");
        Move(200);
        farming.SetWeather(1, WeatherKind.Rain);
        Move(257);
        processing.Place(0, CropKind.Potato);
        inventory.AddRaw(CropKind.Potato, 4);
        Require(processing.TryStart(0, inventory), "末个加工预约失败");
        Move(309);
        Require(due.TryPeekDue(out _, out int first) && first == 2, "同秒产出被锚点或类型排序");
        Require(processing.Complete(first, inventory, out CropKind product, out int amount), "首项未完成");
        inventory.AddProduct(product, amount);
        Require(due.TryPeekDue(out var failedHandle, out int second) && second == 1, "农田未保持混合登记次序");
        inventory.AddRaw(CropKind.Radish, int.MaxValue);
        FarmSnapshot before = farming.Get(1);
        Require(before.RemainingTimeUnits == 0 && before.HasWater, "到期未结算快照错误");
        Throws<OverflowException>(() => farming.CompleteGrowth(1, inventory, out _));
        Require(farming.Get(1) == before && due.Count == 2 && due.TryPeekDue(out var retained, out second) &&
            retained == failedHandle && second == 1 && inventory.GetProduct(CropKind.Sunflower) == 9 &&
            processing.Get(0).BatchQuantity == 4, "容量异常丢弃当前/后续项或回滚成功项");
        farming.SetWeather(1, WeatherKind.Cloudy); // 已到期，切天气不能把未生长时间计入原轮。
        inventory.Remove(new(CropKind.Radish, CommodityKind.Raw), 200);
        Require(farming.CompleteGrowth(1, inventory, out CropHarvestResult harvest), "容量释放后原轮未完成");
        double basis = 6 * (6 + new Random(65).NextDouble() * 3);
        int expected = (int)Math.Round(basis * (1 - 0.1 * 761 / 1440), MidpointRounding.AwayFromZero);
        Require(harvest.Quantity == expected, "失败重复天气积分、重抽随机量或计入到期后天气");
        inventory.AddRaw(harvest.Crop, harvest.Quantity);
        Require(due.TryPeekDue(out _, out int last) && last == 0 &&
            processing.Complete(last, inventory, out product, out amount) && amount == 4, "尾项未保留或锁量改变");
        inventory.AddProduct(product, amount);
        Require(!due.TryPeekDue(out _, out _) && !farming.CompleteGrowth(1, inventory, out _) &&
            !processing.Complete(2, inventory, out _, out _) && inventory.GetProduct(CropKind.Sunflower) == 9,
            "显式续读重复提交成功项");

        void Move(long second)
        {
            due.AdvanceTo(second);
            farming.AdvanceTo(second);
            processing.AdvanceTo(second);
        }
    }

    private static void CheckReservationFailure()
    {
        var due = new SimulationDueIndex(1);
        var farming = new FarmingSystem(3, due, 78);
        var processing = new ProcessingSystem(3, due);
        var inventory = new GoodsInventory();
        processing.Place(0, CropKind.Radish);
        inventory.AddRaw(CropKind.Radish, 18);
        Require(processing.TryStart(0, inventory), "预约容量夹具开批失败");
        processing.Place(1, CropKind.Radish);
        Throws<InvalidOperationException>(() => processing.TryStart(1, inventory));
        Require(inventory.GetRaw(CropKind.Radish) == 9 && processing.Get(1).BatchQuantity == 0 && due.Count == 1,
            "预约容量失败扣除了原料或建立了无预约批次");
        farming.Place(2, CropKind.Radish, WeatherKind.Sunny);
        farming.TryWork(2, new GameCalendar().Snapshot);
        FarmSnapshot seeded = farming.Get(2);
        Throws<InvalidOperationException>(() => farming.SupplyWater(2));
        Require(farming.Get(2) == seeded && !seeded.HasWater, "供水预约失败仍修改轮次或水分");
        farming.RestartCrop(2, CropKind.Radish);
        farming.SupplyWater(2);
        FarmSnapshot wet = farming.Get(2);
        Throws<InvalidOperationException>(() => farming.TryWork(2, new GameCalendar().Snapshot));
        Require(farming.Get(2) == wet, "湿田播种预约失败仍播种");
        processing.Remove(0);
        Require(farming.TryWork(2, new GameCalendar().Snapshot) && due.Count == 1, "释放槽位后不能启动原轮");
        due.AdvanceTo(206);
        farming.AdvanceTo(206);
        Require(farming.CompleteGrowth(2, inventory, out CropHarvestResult harvest) &&
            harvest.Quantity == (int)Math.Round(6 * (6 + new Random(78).NextDouble() * 3), MidpointRounding.AwayFromZero),
            "失败预约消耗了农田随机抽取");
    }

    private static void CheckCancellationAndPromotion()
    {
        var due = new SimulationDueIndex(2);
        var farming = new FarmingSystem(2, due, 91);
        var inventory = new GoodsInventory();
        foreach (int index in new[] { 0, 1 })
        {
            farming.Place(index, CropKind.Potato, WeatherKind.Cloudy);
            farming.SupplyWater(index);
            farming.TryWork(index, new GameCalendar().Snapshot);
        }
        farming.SetSowingEnabled(0, false);
        Require(due.Count == 2, "关闭复种取消当前轮");
        farming.RestartCrop(1, CropKind.Potato);
        Require(due.Count == 1, "同种重启未撤销成熟预约");
        due.AdvanceTo(649);
        farming.AdvanceTo(649);
        Require(farming.Get(0).RemainingTimeUnits == 497, "精确目标丢失促熟阈值精度");
        inventory.AddRaw(CropKind.Potato, int.MaxValue);
        FarmSnapshot before = farming.Get(0);
        Throws<OverflowException>(() => farming.TryMatureBeforeDisallowedSeason(0, Season.Summer, inventory, out _));
        Require(farming.Get(0) == before && due.Count == 1, "促熟容量异常提前清轮或撤销预约");
        inventory.Remove(new(CropKind.Potato, CommodityKind.Raw), 200);
        Require(farming.TryMatureBeforeDisallowedSeason(0, Season.Summer, inventory, out CropHarvestResult harvest) &&
            harvest.Quantity == (int)Math.Round(8 * (6 + new Random(91).NextDouble() * 3) * 1.1, MidpointRounding.AwayFromZero) &&
            due.Count == 0 && !farming.Get(0).HasWater && !farming.Get(0).SowingEnabled,
            "促熟重算未来天气、丢失复种状态或没有取消正常成熟");
        farming.TryWork(1, new GameCalendar().Snapshot);
        Require(due.Count == 1, "重启保留水分后未重登");
        farming.Remove(1);
        farming.Place(1, CropKind.Radish, WeatherKind.Sunny);
        Require(due.Count == 0 && farming.Get(1).Stage == CropStage.None, "拆建继承旧预约");
        farming.SupplyWater(1);
        farming.TryWork(1, new GameCalendar().Snapshot);
        farming.ClearDisallowedCrops(Season.Summer);
        Require(due.Count == 0, "禁生清理遗留预约");
        farming.SupplyWater(1);
        farming.TryWork(1, new GameCalendar().Snapshot);
        farming.Clear();
        Require(due.Count == 0, "清局遗留成熟预约");
    }

    private static void CheckCalendarLimit()
    {
        var due = new SimulationDueIndex(1, uint.MaxValue - 1L);
        var processing = new ProcessingSystem(1, due);
        var inventory = new GoodsInventory();
        processing.Place(0, CropKind.Radish);
        inventory.AddRaw(CropKind.Radish, 1);
        Require(processing.TryStart(0, inventory) && due.TryGetNext(out long target) && target == (long)uint.MaxValue + 25,
            "uint终点附近开工错误拒绝或目标截断");
        due.AdvanceTo(uint.MaxValue);
        processing.AdvanceTo(uint.MaxValue);
        Require(processing.Get(0).RemainingSeconds == 25, "uint终点附近精确剩余错误");
        using var game = new FarmGame(65, uint.MaxValue - 1, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        game.AdvanceTick();
        Require(game.Calendar.ElapsedSeconds == uint.MaxValue, "最后合法秒未完成");
        Throws<InvalidOperationException>(() => game.AdvanceTick());
        Throws<InvalidOperationException>(() => game.AdvanceTicks(1));
        Require(game.Calendar.ElapsedSeconds == uint.MaxValue, "uint终点拒绝推进改变日历");
    }

    private static void CheckGameFifo()
    {
        using var game = new FarmGame(65, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        var raw = new CommodityId(CropKind.Radish, CommodityKind.Raw);
        Require(game.Buy(raw, 18).Success, "正式FIFO夹具买入失败");
        Vector2I first = new(210, 210), second = new(180, 180);
        Require(game.BuildProcessor(first, CropKind.Radish) == null &&
            game.BuildProcessor(second, CropKind.Radish) == null, "正式FIFO夹具建造失败");
        Require(game.AdvanceTicks(25).Produced == 0, "命令开批提前完工");
        TickResult result = game.AdvanceTick();
        var output = game.GetPresentationResults();
        Require(result.Produced == 18 && output.Count == 2 && output[0].AnchorCell == first &&
            output[1].AnchorCell == second && output[0].Quantity == 9 && output[1].Quantity == 9 &&
            game.GetProductStock(CropKind.Radish) == 18, "正式产出未按登记FIFO或重复入库");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("没有抛出预期异常：" + typeof(T).Name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
