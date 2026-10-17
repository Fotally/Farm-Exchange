using System;
using System.Collections.Generic;
using FarmExchange.Gameplay;
using FarmExchange.Logging;
using FarmExchange.Time;
using FarmExchange.Weather;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Farming;

internal readonly record struct FarmSnapshot(
    CropKind CropKind, CropStage Stage, int RemainingSeconds, bool HasWater = false,
    int RemainingTimeUnits = 0, bool SowingEnabled = true);

internal enum FarmWorkKind { Sow, Water }

internal readonly record struct FarmWorkRequest(int CellIndex, int RoundRevision, FarmWorkKind Kind, int SowingRevision);

internal sealed class FarmingSystem
{
    private readonly Random _yieldRandom;
    private readonly SimulationDueIndex _due;
    private long _currentSecond;
    private readonly FarmState?[] _farms;
    private readonly int[] _roundRevisions;
    private readonly List<int> _indices = new();
    private IReadOnlyList<int>? _indicesSnapshot;

    internal FarmingSystem(int cellCount, SimulationDueIndex due, int seed = 0)
    {
        _due = due;
        _currentSecond = due.CurrentSecond;
        _yieldRandom = new Random(seed);
        _farms = new FarmState?[cellCount];
        _roundRevisions = new int[cellCount];
    }

    internal ProductionDiagnostics? Diagnostics { get; set; }

    internal int CellCount => _farms.Length;
    internal IReadOnlyList<int> Indices => _indicesSnapshot ??= Array.AsReadOnly(_indices.ToArray());
    /**
     * <summary>只读验证已成功作业仍属于当前农田轮次，不要求该动作再次可执行。</summary>
     * <param name="completedWork">本模块曾确认成功的完整工作凭据。</param>
     * <returns>原实例仍在且改种、拆建、收获或换季未失效其轮次；播种启停不影响已成功动作。</returns>
     */
    internal bool IsCompletedWorkCurrent(FarmWorkRequest completedWork) =>
        _farms[completedWork.CellIndex] != null &&
        _roundRevisions[completedWork.CellIndex] == completedWork.RoundRevision;
    internal bool HasFarm(int index) => _farms[index] != null;

    internal FarmSnapshot Get(int index)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        return new FarmSnapshot(farm.CropKind, farm.Stage,
            GameTimeUnits.RemainingSeconds(RemainingTimeUnits(farm)), farm.HasWater,
            RemainingTimeUnits(farm), farm.SowingEnabled);
    }

    internal void Place(int index, CropKind crop, WeatherKind weather)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        if (_farms[index] != null)
            throw new InvalidOperationException("土地已有农田状态");
        _roundRevisions[index]++;
        _farms[index] = new FarmState(crop, weather);
        int insertion = _indices.BinarySearch(index);
        _indices.Insert(~insertion, index);
        _indicesSnapshot = null;
    }

    internal void Remove(int index)
    {
        if (_farms[index] == null)
            throw new InvalidOperationException("土地没有农田状态");
        _due.Cancel(_farms[index]!.DueHandle);
        _roundRevisions[index]++;
        _farms[index] = null;
        _indices.Remove(index);
        _indicesSnapshot = null;
    }

    internal void SetCrop(int index, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        if (farm.CropKind == crop)
            return;
        RestartCrop(index, crop);
    }

    /**
     * <summary>中断本轮并设置作物，同品种也重新开始，保留田块水分。</summary>
     * <param name="index">现有农田的锚点索引。</param>
     * <param name="crop">有效作物种类。</param>
     */
    internal void RestartCrop(int index, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        _due.Cancel(farm.DueHandle);
        _roundRevisions[index]++;
        farm.CropKind = crop;
        farm.Stage = CropStage.None;
        farm.DueTimeUnits = 0;
    }

    /**
     * <summary>启停空田播种，已播种作物仍可浇水生长。</summary>
     * <remarks>启停变化仅使旧播种凭据失效，保留本轮浇水及已成功动作；重复设置不修改版本。</remarks>
     * <param name="index">现有农田的锚点索引。</param>
     * <param name="enabled">是否允许空田播种。</param>
     */
    internal void SetSowingEnabled(int index, bool enabled)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        if (farm.SowingEnabled == enabled)
            return;
        farm.SowingEnabled = enabled;
        farm.SowingRevision++;
    }

    internal void SupplyWater(int index)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        SimulationDueIndex.Handle handle = farm.Stage == CropStage.Seeded ? ReserveGrowth(index, farm) : default;
        FarmSnapshot? before = !farm.HasWater ? Diagnostics?.BeginFarm(index, this) : null;
        farm.HasWater = true;
        Diagnostics?.Watered(index, before, this);
        if (farm.Stage == CropStage.Seeded)
            StartGrowth(index, farm, handle);
    }

    /**
     * <summary>同步生产读取时钟，不遍历或改写各田生长进度。</summary>
     * <param name="second">不早于当前时钟的绝对经营秒；产出阶段使用完成后日历秒。</param>
     */
    internal void AdvanceTo(long second)
    {
        if (second < _currentSecond)
            throw new ArgumentOutOfRangeException(nameof(second));
        _currentSecond = second;
    }

    /**
     * <summary>在权威天气实际改变时结清本田旧天气段并开始新段。</summary>
     * <param name="index">现有农田锚点。</param>
     * <param name="weather">当前时钟开始生效的整田九格权威天气。</param>
     */
    internal void SetWeather(int index, WeatherKind weather)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        if (farm.Weather == weather)
            return;
        if (farm.Stage == CropStage.Growing)
            SettleWeather(farm);
        farm.Weather = weather;
    }

    /**
     * <summary>结算已到期的一轮，原料容量检查通过后才终结轮次和撤销预约。</summary>
     * <param name="index">到期索引交出的农田锚点。</param>
     * <param name="inventory">本局库存，仅用于检查实际产量容量；入库由经营入口紧邻提交。</param>
     * <param name="harvest">成功时本轮作物及实际数量。</param>
     * <returns>本轮到期并成功终结为 true；其余状态为 false，容量溢出保留原轮和预约。</returns>
     */
    internal bool CompleteGrowth(int index, GoodsInventory inventory, out CropHarvestResult harvest)
    {
        harvest = default;
        FarmState? farm = _farms[index];
        if (farm == null || farm.Stage != CropStage.Growing || RemainingTimeUnits(farm) > 0)
            return false;
        harvest = FinishHarvest(index, farm, inventory);
        return true;
    }

    /**
     * <summary>只读预测已开始生长的一轮实际结束时刻，包含正常成熟与禁生换季结局。</summary>
     * <param name="index">现有农田的锚点索引。</param>
     * <param name="now">完成本步推进后的累计精确时间单位。</param>
     * <returns>实际经营 tick 对齐的结束时刻；空田或待水阶段返回 null。</returns>
     * <remarks>禁生边界可能促熟或清理，预测不报告收成、不改变状态，也不猜测未来供水。</remarks>
     */
    internal long? GetExpectedRoundEndTimeUnits(int index, long now)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        if (farm.Stage != CropStage.Growing)
            return null;
        long normalEnd = ((farm.DueTimeUnits + GameTimeUnits.PerSecond - 1) /
            GameTimeUnits.PerSecond) * GameTimeUnits.PerSecond;
        return Math.Min(normalEnd, PlantingRules.GetGrowingWindowEndTimeUnits(farm.CropKind, now));
    }

    /**
     * <summary>禁生换季前促成接近完成的一轮成熟，并返回已结算的作物与数量。</summary>
     * <remarks>仅生长中且精确剩余时间严格小于完整周期的十分之一。入库仍由经营入口处理。</remarks>
     * <param name="index">农田锚点索引。</param>
     * <param name="season">实际换季即将进入的季节。</param>
     * <param name="inventory">本局库存，用于终结轮次前的原料容量检查。</param>
     * <param name="harvest">成功时需要沿正常路径入库的作物及实际数量。</param>
     * <returns>促成成熟并完成本轮收获返回 true，其余状态零修改返回 false。</returns>
     */
    internal bool TryMatureBeforeDisallowedSeason(int index, Season season, GoodsInventory inventory, out CropHarvestResult harvest)
    {
        harvest = default;
        FarmState? farm = _farms[index];
        if (farm == null || farm.Stage != CropStage.Growing)
            return false;
        CropDefinition crop = CropCatalog.Get(farm.CropKind);
        GrowingSeasons nextSeason = (GrowingSeasons)(1 << (int)season);
        if ((crop.GrowingSeasons & nextSeason) != 0 ||
            (long)RemainingTimeUnits(farm) * 10 >= crop.GrowthDays * GameTimeUnits.PerDay)
            return false;
        harvest = FinishHarvest(index, farm, inventory);
        return true;
    }

    private int RemainingTimeUnits(FarmState farm) => farm.Stage == CropStage.Growing
        ? checked((int)Math.Max(0, farm.DueTimeUnits - _currentSecond * GameTimeUnits.PerSecond)) : 0;

    private long WeatherEnd(FarmState farm) => Math.Min(
        _currentSecond * GameTimeUnits.PerSecond, farm.DueTimeUnits);

    private void SettleWeather(FarmState farm)
    {
        long end = WeatherEnd(farm);
        int elapsed = checked((int)(end - farm.WeatherStartTimeUnits));
        farm.GrownTimeUnits += elapsed;
        farm.WeatherPercentTimeUnits += (long)CropYieldRules.GetWeatherPercent(farm.CropKind, farm.Weather) * elapsed;
        farm.WeatherStartTimeUnits = end;
    }

    private CropHarvestResult FinishHarvest(int index, FarmState farm, GoodsInventory inventory)
    {
        int elapsed = checked((int)(WeatherEnd(farm) - farm.WeatherStartTimeUnits));
        int quantity = CropYieldRules.CalculateQuantity(farm.RandomBaseQuantity,
            farm.WeatherPercentTimeUnits + (long)CropYieldRules.GetWeatherPercent(farm.CropKind, farm.Weather) * elapsed,
            farm.GrownTimeUnits + elapsed);
        _ = checked(inventory.GetRaw(farm.CropKind) + quantity);
        _due.Cancel(farm.DueHandle);
        farm.Stage = CropStage.None;
        farm.DueTimeUnits = 0;
        farm.HasWater = false;
        _roundRevisions[index]++;
        return new CropHarvestResult(farm.CropKind, quantity);
    }

    internal FarmWorkRequest? GetWorkNeed(int index, CalendarSnapshot calendar) =>
        GetWorkNeedCore(index, calendar, null);

    private FarmWorkRequest? GetWorkNeedCore(int index, CalendarSnapshot calendar, ProductionDiagnostics? diagnostics)
    {
        FarmState? farm = _farms[index];
        if (farm == null || farm.Stage == CropStage.Growing)
            return null;
        if (farm.Stage == CropStage.None)
        {
            if (!farm.SowingEnabled || !PlantingRules.CanSow(farm.CropKind, calendar, farm.HasWater, diagnostics, index))
                return null;
            return new FarmWorkRequest(index, _roundRevisions[index], FarmWorkKind.Sow, farm.SowingRevision);
        }
        return new FarmWorkRequest(index, _roundRevisions[index], FarmWorkKind.Water, 0);
    }

    internal bool TryCompleteWork(FarmWorkRequest request, CalendarSnapshot calendar)
    {
        if (GetWorkNeedCore(request.CellIndex, calendar, Diagnostics) != request)
            return false;
        FarmState farm = _farms[request.CellIndex]!;
        if (request.Kind == FarmWorkKind.Sow)
        {
            SimulationDueIndex.Handle handle = farm.HasWater ? ReserveGrowth(request.CellIndex, farm) : default;
            FarmSnapshot? before = Diagnostics?.BeginFarm(request.CellIndex, this);
            farm.Stage = CropStage.Seeded;
            Diagnostics?.Sown(request.CellIndex, before, this);
            if (farm.HasWater)
                StartGrowth(request.CellIndex, farm, handle);
        }
        else
            SupplyWater(request.CellIndex);
        return true;
    }

    internal bool TryWork(int index, CalendarSnapshot calendar) =>
        GetWorkNeed(index, calendar) is FarmWorkRequest work && TryCompleteWork(work, calendar);

    /**
     * <summary>清理当前季节禁生且尚未结束的作物轮次。</summary>
     * <param name="season">日历已切换到的季节。</param>
     * <returns>原遍历实际清理的逐作物轮数，不包含空田或已收获轮次。</returns>
     */
    internal CropClearResult ClearDisallowedCrops(Season season)
    {
        var result = new CropClearResult();
        GrowingSeasons currentSeason = (GrowingSeasons)(1 << (int)season);
        foreach (int index in _indices)
        {
            FarmState? farm = _farms[index];
            if (farm == null || farm.Stage == CropStage.None ||
                (CropCatalog.Get(farm.CropKind).GrowingSeasons & currentSeason) != 0)
                continue;
            FarmSnapshot? before = Diagnostics?.BeginFarm(index, this);
            _due.Cancel(farm.DueHandle);
            farm.Stage = CropStage.None;
            farm.DueTimeUnits = 0;
            farm.HasWater = false;
            _roundRevisions[index]++;
            result.Record(farm.CropKind);
            Diagnostics?.Cleared(index, before, this);
        }
        return result;
    }

    private SimulationDueIndex.Handle ReserveGrowth(int index, FarmState farm)
    {
        long dueTimeUnits = _currentSecond * GameTimeUnits.PerSecond +
            CropCatalog.Get(farm.CropKind).GrowthDays * GameTimeUnits.PerDay;
        return _due.Register((dueTimeUnits + GameTimeUnits.PerSecond - 1) / GameTimeUnits.PerSecond, index);
    }

    private void StartGrowth(int index, FarmState farm, SimulationDueIndex.Handle handle)
    {
        FarmSnapshot? before = Diagnostics?.BeginFarm(index, this);
        long dueTimeUnits = _currentSecond * GameTimeUnits.PerSecond +
            CropCatalog.Get(farm.CropKind).GrowthDays * GameTimeUnits.PerDay;
        farm.DueHandle = handle;
        farm.DueTimeUnits = dueTimeUnits;
        farm.Stage = CropStage.Growing;
        farm.WeatherStartTimeUnits = _currentSecond * GameTimeUnits.PerSecond;
        farm.RandomBaseQuantity = CropCatalog.Get(farm.CropKind).HarvestQuantity *
            (CropYieldRules.MinimumMultiplier + _yieldRandom.NextDouble() *
                (CropYieldRules.MaximumMultiplier - CropYieldRules.MinimumMultiplier));
        farm.GrownTimeUnits = 0;
        farm.WeatherPercentTimeUnits = 0;
        Diagnostics?.GrowthStarted(index, before, this);
    }

    internal void SetGrowingForBenchmark(int index, CropKind crop, WeatherKind weather)
    {
        Place(index, crop, weather);
        FarmState farm = _farms[index]!;
        SimulationDueIndex.Handle handle = ReserveGrowth(index, farm);
        farm.HasWater = true;
        StartGrowth(index, farm, handle);
    }

    internal void Clear()
    {
        foreach (int index in _indices)
        {
            _due.Cancel(_farms[index]!.DueHandle);
            _roundRevisions[index]++;
        }
        Array.Clear(_farms);
        _indices.Clear();
        _indicesSnapshot = null;
    }

    private sealed class FarmState
    {
        internal CropKind CropKind;
        internal CropStage Stage;
        internal long DueTimeUnits;
        internal SimulationDueIndex.Handle DueHandle;
        internal long WeatherStartTimeUnits;
        internal WeatherKind Weather;
        internal bool HasWater;
        internal bool SowingEnabled = true;
        internal int SowingRevision;
        internal double RandomBaseQuantity;
        internal int GrownTimeUnits;
        internal long WeatherPercentTimeUnits;

        internal FarmState(CropKind crop, WeatherKind weather) { CropKind = crop; Weather = weather; }
    }
}
