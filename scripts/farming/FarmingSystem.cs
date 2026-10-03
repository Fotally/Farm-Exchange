using System;
using System.Collections.Generic;
using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Farming;

internal readonly record struct FarmSnapshot(
    CropKind CropKind, CropStage Stage, int RemainingSeconds, bool HasWater = false,
    int RemainingTimeUnits = 0, bool SowingEnabled = true);

internal enum FarmWorkKind { Sow, Water }

internal readonly record struct FarmWorkRequest(int CellIndex, int Revision, FarmWorkKind Kind);

internal sealed class FarmingSystem
{
    private readonly FarmState?[] _farms;
    private readonly int[] _farmRevisions;
    private readonly List<int> _indices = new();
    private IReadOnlyList<int>? _indicesSnapshot;

    internal FarmingSystem(int cellCount)
    {
        _farms = new FarmState?[cellCount];
        _farmRevisions = new int[cellCount];
    }

    internal int CellCount => _farms.Length;
    internal IReadOnlyList<int> Indices => _indicesSnapshot ??= Array.AsReadOnly(_indices.ToArray());
    internal bool HasFarm(int index) => _farms[index] != null;

    internal FarmSnapshot Get(int index)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        return new FarmSnapshot(farm.CropKind, farm.Stage,
            GameTimeUnits.RemainingSeconds(farm.RemainingTimeUnits), farm.HasWater,
            farm.RemainingTimeUnits, farm.SowingEnabled);
    }

    internal void Place(int index, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        if (_farms[index] != null)
            throw new InvalidOperationException("土地已有农田状态");
        _farmRevisions[index]++;
        _farms[index] = new FarmState(crop);
        int insertion = _indices.BinarySearch(index);
        _indices.Insert(~insertion, index);
        _indicesSnapshot = null;
    }

    internal void Remove(int index)
    {
        if (_farms[index] == null)
            throw new InvalidOperationException("土地没有农田状态");
        _farmRevisions[index]++;
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
        _farmRevisions[index]++;
        farm.CropKind = crop;
        farm.Stage = CropStage.None;
        farm.RemainingTimeUnits = 0;
    }

    /**
     * <summary>启停空田播种，已播种作物仍可浇水生长。</summary>
     * <remarks>启停变化使旧工人任务凭据失效，重复设置不修改版本。</remarks>
     * <param name="index">现有农田的锚点索引。</param>
     * <param name="enabled">是否允许空田播种。</param>
     */
    internal void SetSowingEnabled(int index, bool enabled)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        if (farm.SowingEnabled == enabled)
            return;
        farm.SowingEnabled = enabled;
        _farmRevisions[index]++;
    }

    internal void SupplyWater(int index)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        farm.HasWater = true;
        if (farm.Stage == CropStage.Seeded)
            StartGrowth(farm);
    }

    internal bool AdvanceGrowth(int index, out CropKind harvestedCrop)
    {
        harvestedCrop = default;
        FarmState? farm = _farms[index];
        if (farm == null || farm.Stage != CropStage.Growing)
            return false;
        farm.RemainingTimeUnits -= GameTimeUnits.PerSecond;
        if (farm.RemainingTimeUnits > 0)
            return false;
        harvestedCrop = FinishHarvest(index, farm);
        return true;
    }

    /**
     * <summary>禁生换季前促成接近完成的一轮成熟，并返回正常收获的作物。</summary>
     * <remarks>仅生长中且精确剩余时间严格小于完整周期的十分之一。入库仍由经营入口处理。</remarks>
     * <param name="index">农田锚点索引。</param>
     * <param name="season">实际换季即将进入的季节。</param>
     * <param name="harvestedCrop">成功时需要沿正常路径入库的作物。</param>
     * <returns>促成成熟并完成本轮收获返回 true，其余状态零修改返回 false。</returns>
     */
    internal bool TryMatureBeforeDisallowedSeason(int index, Season season, out CropKind harvestedCrop)
    {
        harvestedCrop = default;
        FarmState? farm = _farms[index];
        if (farm == null || farm.Stage != CropStage.Growing)
            return false;
        CropDefinition crop = CropCatalog.Get(farm.CropKind);
        GrowingSeasons nextSeason = (GrowingSeasons)(1 << (int)season);
        if ((crop.GrowingSeasons & nextSeason) != 0 ||
            (long)farm.RemainingTimeUnits * 10 >= crop.GrowthDays * GameTimeUnits.PerDay)
            return false;
        harvestedCrop = FinishHarvest(index, farm);
        return true;
    }

    private CropKind FinishHarvest(int index, FarmState farm)
    {
        farm.Stage = CropStage.None;
        farm.RemainingTimeUnits = 0;
        farm.HasWater = false;
        _farmRevisions[index]++;
        return farm.CropKind;
    }

    internal FarmWorkRequest? GetWorkNeed(int index, CalendarSnapshot calendar)
    {
        FarmState? farm = _farms[index];
        if (farm == null || farm.Stage == CropStage.Growing)
            return null;
        if (farm.Stage == CropStage.None)
        {
            if (!farm.SowingEnabled || !PlantingRules.CanSow(farm.CropKind, calendar, farm.HasWater))
                return null;
            return new FarmWorkRequest(index, _farmRevisions[index], FarmWorkKind.Sow);
        }
        return new FarmWorkRequest(index, _farmRevisions[index], FarmWorkKind.Water);
    }

    internal bool TryCompleteWork(FarmWorkRequest request, CalendarSnapshot calendar)
    {
        if (GetWorkNeed(request.CellIndex, calendar) != request)
            return false;
        FarmState farm = _farms[request.CellIndex]!;
        if (request.Kind == FarmWorkKind.Sow)
        {
            farm.Stage = CropStage.Seeded;
            if (farm.HasWater)
                StartGrowth(farm);
        }
        else
            SupplyWater(request.CellIndex);
        return true;
    }

    internal bool TryWork(int index, CalendarSnapshot calendar) =>
        GetWorkNeed(index, calendar) is FarmWorkRequest work && TryCompleteWork(work, calendar);

    internal void ClearDisallowedCrops(Season season)
    {
        GrowingSeasons currentSeason = (GrowingSeasons)(1 << (int)season);
        foreach (int index in _indices)
        {
            FarmState? farm = _farms[index];
            if (farm == null || farm.Stage == CropStage.None ||
                (CropCatalog.Get(farm.CropKind).GrowingSeasons & currentSeason) != 0)
                continue;
            farm.Stage = CropStage.None;
            farm.RemainingTimeUnits = 0;
            farm.HasWater = false;
            _farmRevisions[index]++;
        }
    }

    private static void StartGrowth(FarmState farm)
    {
        farm.Stage = CropStage.Growing;
        farm.RemainingTimeUnits = CropCatalog.Get(farm.CropKind).GrowthDays * GameTimeUnits.PerDay;
    }

    internal void SetGrowingForBenchmark(int index, CropKind crop)
    {
        Place(index, crop);
        FarmState farm = _farms[index]!;
        farm.Stage = CropStage.Growing;
        farm.HasWater = true;
        farm.RemainingTimeUnits = CropCatalog.Get(crop).GrowthDays * GameTimeUnits.PerDay;
    }

    internal void Clear()
    {
        foreach (int index in _indices)
            _farmRevisions[index]++;
        Array.Clear(_farms);
        _indices.Clear();
        _indicesSnapshot = null;
    }

    private sealed class FarmState
    {
        internal CropKind CropKind;
        internal CropStage Stage;
        internal int RemainingTimeUnits;
        internal bool HasWater;
        internal bool SowingEnabled = true;

        internal FarmState(CropKind crop) => CropKind = crop;
    }
}
