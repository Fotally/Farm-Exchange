using System;
using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Farming;

internal readonly record struct FarmSnapshot(CropKind CropKind, CropStage Stage, int RemainingSeconds);

internal sealed class FarmingSystem
{
    private readonly FarmState?[] _farms;

    internal FarmingSystem(int cellCount) => _farms = new FarmState?[cellCount];

    internal int CellCount => _farms.Length;
    internal bool HasFarm(int index) => _farms[index] != null;

    internal FarmSnapshot Get(int index)
    {
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        return new FarmSnapshot(farm.CropKind, farm.Stage,
            GameTimeUnits.RemainingSeconds(farm.RemainingTimeUnits));
    }

    internal void Place(int index, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        if (_farms[index] != null)
            throw new InvalidOperationException("土地已有农田状态");
        _farms[index] = new FarmState(crop);
    }

    internal void Remove(int index)
    {
        if (_farms[index] == null)
            throw new InvalidOperationException("土地没有农田状态");
        _farms[index] = null;
    }

    internal void SetCrop(int index, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        FarmState farm = _farms[index] ?? throw new InvalidOperationException("土地没有农田状态");
        if (farm.CropKind == crop)
            return;
        farm.CropKind = crop;
        farm.Stage = CropStage.None;
        farm.RemainingTimeUnits = 0;
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
        harvestedCrop = farm.CropKind;
        return true;
    }

    internal bool TryWork(int index)
    {
        FarmState? farm = _farms[index];
        if (farm == null || farm.Stage == CropStage.Growing)
            return false;
        if (farm.Stage == CropStage.None)
            farm.Stage = CropStage.Seeded;
        else
        {
            farm.Stage = CropStage.Growing;
            farm.RemainingTimeUnits = CropCatalog.Get(farm.CropKind).GrowthDays *
                GameTimeUnits.PerDay;
        }
        return true;
    }

    internal void SetGrowingForBenchmark(int index, CropKind crop)
    {
        Place(index, crop);
        FarmState farm = _farms[index]!;
        farm.Stage = CropStage.Growing;
        farm.RemainingTimeUnits = CropCatalog.Get(crop).GrowthDays * GameTimeUnits.PerDay;
    }

    internal void Clear() => Array.Clear(_farms);

    private sealed class FarmState
    {
        internal CropKind CropKind;
        internal CropStage Stage;
        internal int RemainingTimeUnits;

        internal FarmState(CropKind crop) => CropKind = crop;
    }
}
