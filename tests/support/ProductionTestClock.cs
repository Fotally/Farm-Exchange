using System.Runtime.CompilerServices;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Weather;
using GoodsInventory = FarmExchange.Inventory.Inventory;

// 模块测试显式组装生产时钟、到期索引和晴天天气；不在正式 Module 保留旧逐田计时路径。
internal static class ProductionTestClock
{
    private sealed record Context(SimulationDueIndex Due, GoodsInventory Inventory);
    private static readonly ConditionalWeakTable<FarmingSystem, Context> Farms = new();
    private static readonly ConditionalWeakTable<ProcessingSystem, Context> Processors = new();

    internal static FarmingSystem CreateFarms(int cellCount, int seed = 0, long currentSecond = 0)
    {
        var due = new SimulationDueIndex(cellCount, currentSecond);
        var farming = new FarmingSystem(cellCount, due, seed);
        Farms.Add(farming, new Context(due, new GoodsInventory()));
        return farming;
    }

    internal static ProcessingSystem CreateProcessors(int cellCount)
    {
        var due = new SimulationDueIndex(cellCount);
        var processing = new ProcessingSystem(cellCount, due);
        Processors.Add(processing, new Context(due, new GoodsInventory()));
        return processing;
    }

    internal static void Place(this FarmingSystem farming, int index, CropKind crop) =>
        farming.Place(index, crop, WeatherKind.Sunny);

    internal static void AdvanceTimeTo(this FarmingSystem farming, long second)
    {
        Farms.GetValue(farming, _ => throw new System.InvalidOperationException()).Due.AdvanceTo(second);
        farming.AdvanceTo(second);
    }

    internal static void AdvanceSecond(this FarmingSystem farming)
    {
        Context context = Farms.GetValue(farming, _ => throw new System.InvalidOperationException());
        farming.AdvanceTimeTo(context.Due.CurrentSecond + 1);
    }

    internal static bool CompleteGrowth(this FarmingSystem farming, int index, out CropHarvestResult harvest) =>
        farming.CompleteGrowth(index, Farms.GetValue(farming, _ => throw new System.InvalidOperationException()).Inventory, out harvest);

    internal static bool AdvanceGrowth(this FarmingSystem farming, int index, out CropHarvestResult harvest, WeatherKind weather)
    {
        if (farming.HasFarm(index)) farming.SetWeather(index, weather);
        farming.AdvanceSecond();
        return farming.CompleteGrowth(index, out harvest);
    }

    internal static void AdvanceQuietSeconds(this FarmingSystem farming, int index, uint seconds, WeatherKind weather)
    {
        farming.SetWeather(index, weather);
        Context context = Farms.GetValue(farming, _ => throw new System.InvalidOperationException());
        farming.AdvanceTimeTo(context.Due.CurrentSecond + seconds);
    }

    internal static bool TryMatureBeforeDisallowedSeason(this FarmingSystem farming, int index, Season season,
        out CropHarvestResult harvest) => farming.TryMatureBeforeDisallowedSeason(index, season,
            Farms.GetValue(farming, _ => throw new System.InvalidOperationException()).Inventory, out harvest);

    internal static void AdvanceQuietSeconds(this ProcessingSystem processing, int index, uint seconds)
    {
        Context context = Processors.GetValue(processing, _ => throw new System.InvalidOperationException());
        context.Due.AdvanceTo(context.Due.CurrentSecond + seconds);
        processing.AdvanceTo(context.Due.CurrentSecond);
    }

    internal static bool Advance(this ProcessingSystem processing, int index, GoodsInventory inventory,
        out CropKind crop, out int quantity)
    {
        Context context = Processors.GetValue(processing, _ => throw new System.InvalidOperationException());
        if (!context.Due.TryGetNext(out long next) || next > context.Due.CurrentSecond)
            context.Due.AdvanceTo(context.Due.CurrentSecond + 1);
        processing.AdvanceTo(context.Due.CurrentSecond);
        return processing.Complete(index, inventory, out crop, out quantity);
    }
}
