using System;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Processing;

internal readonly record struct ProcessorSnapshot(CropKind CropKind, int RemainingTicks);

internal sealed class ProcessingSystem
{
    private readonly ProcessorState?[] _processors;

    internal ProcessingSystem(int cellCount) => _processors = new ProcessorState?[cellCount];

    internal bool HasProcessor(int index) => _processors[index] != null;

    internal ProcessorSnapshot Get(int index)
    {
        ProcessorState processor = _processors[index] ??
            throw new InvalidOperationException("土地没有加工状态");
        return new ProcessorSnapshot(processor.CropKind, processor.RemainingTicks);
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
        if (processor == null || processor.RemainingTicks == 0)
            return false;
        processor.RemainingTicks--;
        if (processor.RemainingTicks != 0)
            return false;
        productCrop = processor.CropKind;
        return true;
    }

    internal bool TryStart(int index, GoodsInventory inventory)
    {
        ProcessorState? processor = _processors[index];
        if (processor == null || processor.RemainingTicks != 0 ||
            !inventory.TryTakeRawForProcessing(processor.CropKind))
            return false;
        processor.RemainingTicks = CropCatalog.Get(processor.CropKind).ProcessingTicks;
        return true;
    }

    internal void SetProcessingForBenchmark(int index, CropKind crop)
    {
        Place(index, crop);
        _processors[index]!.RemainingTicks = CropCatalog.Get(crop).ProcessingTicks;
    }

    internal void Clear() => Array.Clear(_processors);

    private sealed class ProcessorState
    {
        internal CropKind CropKind;
        internal int RemainingTicks;

        internal ProcessorState(CropKind crop) => CropKind = crop;
    }
}
