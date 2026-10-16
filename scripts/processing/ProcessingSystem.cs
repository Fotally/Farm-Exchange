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
    private readonly SimulationDueIndex _due;
    private long _currentSecond;

    internal ProcessingSystem(int cellCount, SimulationDueIndex due)
    {
        _processors = new ProcessorState?[cellCount];
        _due = due;
        _currentSecond = due.CurrentSecond;
    }

    internal ProductionDiagnostics? Diagnostics { get; set; }

    internal bool HasProcessor(int index) => _processors[index] != null;

    internal ProcessorSnapshot Get(int index)
    {
        ProcessorState processor = _processors[index] ??
            throw new InvalidOperationException("土地没有加工状态");
        return new ProcessorSnapshot(processor.CropKind,
            GameTimeUnits.RemainingSeconds(RemainingTimeUnits(processor)), processor.BatchQuantity);
    }

    internal ProcessorStatus GetStatus(int index, GoodsInventory inventory)
    {
        ProcessorSnapshot processor = Get(index);
        if (processor.BatchQuantity > 0)
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
        _due.Cancel(_processors[index]!.DueHandle);
        _processors[index] = null;
    }

    /**
     * <summary>同步生产读取时钟，不遍历或改写各批次进度。</summary>
     * <param name="second">不早于当前时钟的绝对经营秒；产出阶段使用完成后日历秒。</param>
     */
    internal void AdvanceTo(long second)
    {
        if (second < _currentSecond)
            throw new ArgumentOutOfRangeException(nameof(second));
        _currentSecond = second;
    }

    /**
     * <summary>完成已到期的锁定批次；成品容量不足在修改批次和预约前抛出溢出异常。</summary>
     * <param name="index">到期索引交出的场地锚点。</param>
     * <param name="inventory">本局库存，只用于成品容量检查，实际入库由经营入口紧邻提交。</param>
     * <param name="productCrop">本次完成的对应作物；未完成为默认值。</param>
     * <param name="quantity">本次完成的实际份数；未完成为零。</param>
     * <returns>是否完成一批；失败保留本批精确目标、锁定量和预约。</returns>
     */
    internal bool Complete(int index, GoodsInventory inventory, out CropKind productCrop, out int quantity)
    {
        productCrop = default;
        quantity = 0;
        ProcessorState? processor = _processors[index];
        if (processor == null || processor.BatchQuantity == 0 || RemainingTimeUnits(processor) > 0)
            return false;
        _ = checked(inventory.GetProduct(processor.CropKind) + processor.BatchQuantity);
        _due.Cancel(processor.DueHandle);
        processor.DueTimeUnits = 0;
        productCrop = processor.CropKind;
        quantity = processor.BatchQuantity;
        processor.BatchQuantity = 0;
        return true;
    }

    /**
     * <summary>只读检查空闲场地是否可在下一领取阶段开批，不查询忙碌批次计时。</summary>
     * <param name="index">现有加工锚点。</param>
     * <param name="inventory">本局库存。</param>
     * <returns>空闲且原料可领取时为 true。</returns>
     */
    internal bool CanStart(int index, GoodsInventory inventory) =>
        _processors[index]!.BatchQuantity == 0 &&
        inventory.GetProcessingAvailability(_processors[index]!.CropKind) == RawProcessingAvailability.Available;

    private int RemainingTimeUnits(ProcessorState processor) => processor.BatchQuantity == 0 ? 0 :
        checked((int)Math.Max(0, processor.DueTimeUnits - _currentSecond * GameTimeUnits.PerSecond));

    /**
     * <summary>空闲场地一次领取最多九份可用原料，锁定实际量并开始完整时长的单批加工。</summary>
     * <param name="index">场地锚点。</param>
     * <param name="inventory">本局库存，原子领取时检查冻结和保留底线。</param>
     * <returns>成功开工；正在加工或无可领取量时状态及库存零修改。</returns>
     */
    internal bool TryStart(int index, GoodsInventory inventory)
    {
        ProcessorState? processor = _processors[index];
        if (processor == null || processor.BatchQuantity != 0)
            return false;
        if (inventory.GetProcessingAvailability(processor.CropKind) != RawProcessingAvailability.Available)
            return false;
        long dueTimeUnits = _currentSecond * GameTimeUnits.PerSecond +
            CropCatalog.Get(processor.CropKind).ProcessingHalfDays * GameTimeUnits.PerHalfDay;
        SimulationDueIndex.Handle handle = _due.Register(
            (dueTimeUnits + GameTimeUnits.PerSecond - 1) / GameTimeUnits.PerSecond, index);
        ProcessorDiagnosticState? before = Diagnostics?.BeginProcessor(index, this, inventory);
        inventory.TryTakeRawForProcessing(processor.CropKind, MaxBatchQuantity, out int quantity);
        Diagnostics?.RawClaimed(index, before, this, inventory, quantity);
        ProcessorDiagnosticState? beforeStart = Diagnostics?.BeginProcessor(index, this, inventory);
        processor.BatchQuantity = quantity;
        processor.DueTimeUnits = dueTimeUnits;
        processor.DueHandle = handle;
        Diagnostics?.ProcessingStarted(index, beforeStart, this, inventory);
        return true;
    }

    internal void SetProcessingForBenchmark(int index, CropKind crop)
    {
        Place(index, crop);
        _processors[index]!.BatchQuantity = MaxBatchQuantity;
        long dueTimeUnits = _currentSecond * GameTimeUnits.PerSecond +
            CropCatalog.Get(crop).ProcessingHalfDays * GameTimeUnits.PerHalfDay;
        _processors[index]!.DueTimeUnits = dueTimeUnits;
        _processors[index]!.DueHandle = _due.Register(
            (dueTimeUnits + GameTimeUnits.PerSecond - 1) / GameTimeUnits.PerSecond, index);
    }

    internal void Clear()
    {
        foreach (ProcessorState? processor in _processors)
            if (processor != null)
                _due.Cancel(processor.DueHandle);
        Array.Clear(_processors);
    }

    private sealed class ProcessorState
    {
        internal CropKind CropKind;
        internal long DueTimeUnits;
        internal SimulationDueIndex.Handle DueHandle;
        internal int BatchQuantity;

        internal ProcessorState(CropKind crop) => CropKind = crop;
    }
}
