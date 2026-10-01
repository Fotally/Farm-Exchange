using System;
using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Farming;

internal readonly record struct FarmSnapshot(
    CropKind CropKind, CropStage Stage, int RemainingSeconds, bool HasWater = false);

internal enum FarmWorkKind { Sow, Water }

internal readonly record struct FarmWorkRequest(int CellIndex, int Revision, FarmWorkKind Kind);

internal sealed class FarmingSystem
{
    private readonly FarmState?[] _farms;
    private readonly int[] _farmRevisions;

    internal FarmingSystem(int cellCount)
    {
        _farms = new FarmState?[cellCount];
        _farmRevisions = new int[cellCount];
    }

    internal int CellCount => _farms.Length;
    internal bool HasFarm(int index) => _farms[index] != null;

    internal FarmSnapshot Get(int index)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        return new FarmSnapshot(farm.CropKind, farm.Stage,
            GameTimeUnits.RemainingSeconds(farm.RemainingTimeUnits), farm.HasWater);
    }

    internal void Place(int index, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        if (_farms[index] != null)
            throw new InvalidOperationException("土地已有农田状态");
        _farmRevisions[index]++;
        _farms[index] = new FarmState(crop);
    }

    internal void Remove(int index)
    {
        if (_farms[index] == null)
            throw new InvalidOperationException("土地没有农田状态");
        _farmRevisions[index]++;
        _farms[index] = null;
    }

    internal void SetCrop(int index, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        if (farm.CropKind == crop)
            return;
        _farmRevisions[index]++;
        farm.CropKind = crop;
        farm.Stage = CropStage.None;
        farm.RemainingTimeUnits = 0;
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
        farm.Stage = CropStage.None;
        farm.RemainingTimeUnits = 0;
        farm.HasWater = false;
        _farmRevisions[index]++;
        harvestedCrop = farm.CropKind;
        return true;
    }

    internal FarmWorkRequest? GetWorkNeed(int index, CalendarSnapshot calendar)
    {
        FarmState? farm = _farms[index];
        if (farm == null || farm.Stage == CropStage.Growing)
            return null;
        if (farm.Stage == CropStage.None)
        {
            if (PlantingRules.Check(farm.CropKind, calendar, farm.HasWater) != PlantingFailure.None)
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
        for (int index = 0; index < _farms.Length; index++)
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
        for (int index = 0; index < _farms.Length; index++)
            if (_farms[index] != null)
                _farmRevisions[index]++;
        Array.Clear(_farms);
    }

    private sealed class FarmState
    {
        internal CropKind CropKind;
        internal CropStage Stage;
        internal int RemainingTimeUnits;
        internal bool HasWater;

        internal FarmState(CropKind crop) => CropKind = crop;
    }
}
