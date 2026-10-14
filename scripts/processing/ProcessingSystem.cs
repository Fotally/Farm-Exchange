using System;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Time;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Processing;

internal readonly record struct ProcessorSnapshot(CropKind CropKind, int RemainingSeconds, int BatchQuantity);

internal sealed class ProcessingSystem
{
    internal const int MaxBatchQuantity = 9;
    private readonly ProcessorState?[] _processors;

    internal ProcessingSystem(int cellCount) => _processors = new ProcessorState?[cellCount];

    internal ProductionDiagnostics? Diagnostics { get; set; }

    internal bool HasProcessor(int index) => _processors[index] != null;

    internal ProcessorSnapshot Get(int index)
    {
        ProcessorState processor = _processors[index] ??
            throw new InvalidOperationException("土地没有加工状态");
        return new ProcessorSnapshot(processor.CropKind,
            GameTimeUnits.RemainingSeconds(processor.RemainingTimeUnits), processor.BatchQuantity);
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

    /**
     * <summary>推进已有批次，完工返回锁定量并清空批次；成品容量不足在修改前抛出溢出异常。</summary>
     * <param name="index">场地锚点。</param>
     * <param name="inventory">本局库存，只用于末秒成品容量检查，实际入库由经营入口提交。</param>
     * <param name="productCrop">本次完成的对应作物；未完成为默认值。</param>
     * <param name="quantity">本次完成的实际份数；未完成为零。</param>
     * <returns>是否完成一批；失败时保留本批剩余时间和锁定投入量。</returns>
     */
    internal bool Advance(int index, GoodsInventory inventory, out CropKind productCrop, out int quantity)
    {
        productCrop = default;
        quantity = 0;
        ProcessorState? processor = _processors[index];
        if (processor == null || processor.RemainingTimeUnits == 0)
            return false;
        if (processor.RemainingTimeUnits <= GameTimeUnits.PerSecond)
            _ = checked(inventory.GetProduct(processor.CropKind) + processor.BatchQuantity);
        processor.RemainingTimeUnits -= GameTimeUnits.PerSecond;
        if (processor.RemainingTimeUnits > 0)
            return false;
        processor.RemainingTimeUnits = 0;
        productCrop = processor.CropKind;
        quantity = processor.BatchQuantity;
        processor.BatchQuantity = 0;
        return true;
    }

    /**
     * <summary>查询当前场地下一次完工或可领取的秒距离。</summary>
     * <param name="index">现有加工锚点。</param>
     * <param name="inventory">本局库存，用于只读检查空闲领取。</param>
     * <returns>忙碌按单批向上取整；可领取为一秒，无事件为 uint.MaxValue。</returns>
     */
    internal uint GetNextEventSeconds(int index, GoodsInventory inventory)
    {
        ProcessorState processor = _processors[index]!;
        if (processor.RemainingTimeUnits > 0)
            return (uint)GameTimeUnits.RemainingSeconds(processor.RemainingTimeUnits);
        return inventory.GetProcessingAvailability(processor.CropKind) == RawProcessingAvailability.Available
            ? 1u : uint.MaxValue;
    }

    /**
     * <summary>一次累计当前批次完工之前的无事件区间。</summary>
     * <param name="index">现有加工锚点。</param>
     * <param name="seconds">严格小于当前批次剩余经营秒数的区间。</param>
     */
    internal void AdvanceQuietSeconds(int index, uint seconds)
    {
        ProcessorState processor = _processors[index]!;
        if (processor.RemainingTimeUnits > 0)
            processor.RemainingTimeUnits -= checked((int)(seconds * GameTimeUnits.PerSecond));
    }

    /**
     * <summary>空闲场地一次领取最多九份可用原料，锁定实际量并开始完整时长的单批加工。</summary>
     * <param name="index">场地锚点。</param>
     * <param name="inventory">本局库存，原子领取时检查冻结和保留底线。</param>
     * <returns>成功开工；正在加工或无可领取量时状态及库存零修改。</returns>
     */
    internal bool TryStart(int index, GoodsInventory inventory)
    {
        ProcessorState? processor = _processors[index];
        if (processor == null || processor.RemainingTimeUnits != 0)
            return false;
        ProcessorDiagnosticState? before = Diagnostics?.BeginProcessor(index, this, inventory);
        if (!inventory.TryTakeRawForProcessing(processor.CropKind, MaxBatchQuantity, out int quantity))
            return false;
        Diagnostics?.RawClaimed(index, before, this, inventory, quantity);
        ProcessorDiagnosticState? beforeStart = Diagnostics?.BeginProcessor(index, this, inventory);
        processor.BatchQuantity = quantity;
        processor.RemainingTimeUnits = CropCatalog.Get(processor.CropKind).ProcessingHalfDays *
            GameTimeUnits.PerHalfDay;
        Diagnostics?.ProcessingStarted(index, beforeStart, this, inventory);
        return true;
    }

    internal void SetProcessingForBenchmark(int index, CropKind crop)
    {
        Place(index, crop);
        _processors[index]!.BatchQuantity = MaxBatchQuantity;
        _processors[index]!.RemainingTimeUnits = CropCatalog.Get(crop).ProcessingHalfDays *
            GameTimeUnits.PerHalfDay;
    }

    internal void Clear() => Array.Clear(_processors);

    private sealed class ProcessorState
    {
        internal CropKind CropKind;
        internal int RemainingTimeUnits;
        internal int BatchQuantity;

        internal ProcessorState(CropKind crop) => CropKind = crop;
    }
}
