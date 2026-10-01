using System;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Processing;

internal readonly record struct ProcessorSnapshot(CropKind CropKind, int RemainingSeconds);

internal sealed class ProcessingSystem
{
    private readonly ProcessorState?[] _processors;

    internal ProcessingSystem(int cellCount) => _processors = new ProcessorState?[cellCount];

    internal bool HasProcessor(int index) => _processors[index] != null;

    internal ProcessorSnapshot Get(int index)
    {
        ProcessorState processor = _processors[index] ??
            throw new InvalidOperationException("土地没有加工状态");
        return new ProcessorSnapshot(processor.CropKind,
            GameTimeUnits.RemainingSeconds(processor.RemainingTimeUnits));
    }

    internal ProcessorStatus GetStatus(int index, GoodsInventory inventory)
    {
        ProcessorSnapshot processor = Get(index);
        if (processor.RemainingSeconds > 0)
            return ProcessorStatus.Processing;
        return inventory.GetProcessingAvailability(processor.CropKind) switch
        {
            RawProcessingAvailability.NoRaw => ProcessorStatus.WaitingForRaw,
            RawProcessingAvailability.Reserved => ProcessorStatus.WaitingForReserve,
            _ => ProcessorStatus.ReadyToProcess,
        };
    }

    internal void Place(int index, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        if (_processors[index] != null)
            throw new InvalidOperationException("土地已有加工状态");
        _processors[index] = new ProcessorState(crop);
    }

    internal void Remove(int index)
    {
        if (_processors[index] == null)
            throw new InvalidOperationException("土地没有加工状态");
        _processors[index] = null;
    }

    internal bool Advance(int index, out CropKind productCrop)
    {
        productCrop = default;
        ProcessorState? processor = _processors[index];
        if (processor == null || processor.RemainingTimeUnits == 0)
            return false;
        processor.RemainingTimeUnits -= GameTimeUnits.PerSecond;
        if (processor.RemainingTimeUnits > 0)
            return false;
        processor.RemainingTimeUnits = 0;
        productCrop = processor.CropKind;
        return true;
    }

    internal bool TryStart(int index, GoodsInventory inventory)
    {
        ProcessorState? processor = _processors[index];
        if (processor == null || processor.RemainingTimeUnits != 0 ||
            !inventory.TryTakeRawForProcessing(processor.CropKind))
            return false;
        processor.RemainingTimeUnits = CropCatalog.Get(processor.CropKind).ProcessingHalfDays *
            GameTimeUnits.PerHalfDay;
        return true;
    }

    internal void SetProcessingForBenchmark(int index, CropKind crop)
    {
        Place(index, crop);
        _processors[index]!.RemainingTimeUnits = CropCatalog.Get(crop).ProcessingHalfDays *
            GameTimeUnits.PerHalfDay;
    }

    internal void Clear() => Array.Clear(_processors);

    private sealed class ProcessorState
    {
        internal CropKind CropKind;
        internal int RemainingTimeUnits;

        internal ProcessorState(CropKind crop) => CropKind = crop;
    }
}
