using System.Collections.Generic;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Processing;
using Microsoft.Extensions.Logging;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Logging;

/**
 * <summary>投影选定生产实例的真实转换与播种检查，不执行或累计业务。</summary>
 */
internal sealed class ProductionDiagnostics
{
    private static readonly LogEventDescriptor FarmEvent = new(101, "ProductionStateChanged", "FarmExchange.Farming.FarmingSystem", LogLevel.Debug, false);
    private static readonly LogEventDescriptor ProcessorEvent = new(101, "ProductionStateChanged", "FarmExchange.Processing.ProcessingSystem", LogLevel.Debug, false);
    private static readonly LogEventDescriptor RuleEvent = new(100, "RuleChecked", "FarmExchange.Farming.PlantingRules", LogLevel.Debug, false);
    private readonly GameLog _context;

    internal ProductionDiagnostics(GameLog context) => _context = context;

    internal FarmSnapshot? BeginFarm(int index, FarmingSystem farming)
    {
        if (!_context.Diagnostics.ShouldCapture(FarmEvent.Name, anchorIndex: index)) return null;
        return CaptureFarm(index, farming);
    }

    private FarmSnapshot? CaptureFarm(int index, FarmingSystem farming)
    {
        FarmSnapshot? before = null;
        _context.Observe(() => before = farming.Get(index));
        return before;
    }

    internal ProcessorDiagnosticState? BeginProcessor(int index, ProcessingSystem processing, GoodsInventory inventory)
    {
        if (!_context.Diagnostics.ShouldCapture(ProcessorEvent.Name, anchorIndex: index)) return null;
        return CaptureProcessor(index, processing, inventory);
    }

    private ProcessorDiagnosticState? CaptureProcessor(int index, ProcessingSystem processing, GoodsInventory inventory)
    {
        ProcessorDiagnosticState? before = null;
        _context.Observe(() => before = ProcessorState(index, processing, inventory));
        return before;
    }

    internal void Sown(int index, FarmSnapshot? before, FarmingSystem farming) => FarmChanged(index, before, farming, "Sown");
    internal void Watered(int index, FarmSnapshot? before, FarmingSystem farming) => FarmChanged(index, before, farming, "Watered");
    internal void GrowthStarted(int index, FarmSnapshot? before, FarmingSystem farming) => FarmChanged(index, before, farming, "GrowthStarted");
    internal void Cleared(int index, FarmSnapshot? before, FarmingSystem farming) => FarmChanged(index, before, farming, "Cleared");
    internal void Harvested(int index, FarmSnapshot? before, FarmingSystem farming, int quantity) => FarmChanged(index, before, farming, "Harvested", quantity);

    private void FarmChanged(int index, FarmSnapshot? before, FarmingSystem farming, string transition, int? quantity = null)
    {
        if (!before.HasValue || !_context.Diagnostics.ShouldCapture(FarmEvent.Name, anchorIndex: index)) return;
        CaptureFarmChanged(index, before.Value, farming, transition, quantity);
    }

    private void CaptureFarmChanged(int index, FarmSnapshot before, FarmingSystem farming, string transition, int? quantity)
    {
        _context.Observe(() =>
        {
            FarmSnapshot after = farming.Get(index);
            var fields = Fields(index, "Farm", before.CropKind, transition);
            fields["StateBefore"] = before.Stage.ToString();
            fields["StateAfter"] = after.Stage.ToString();
            fields["HasWaterBefore"] = before.HasWater;
            fields["HasWaterAfter"] = after.HasWater;
            fields["RemainingSeconds"] = after.RemainingSeconds;
            if (quantity.HasValue) fields["Quantity"] = quantity.Value;
            _context.Diagnostics.Capture(FarmEvent, "选定农田实际状态转换", fields);
        });
    }

    internal void RawClaimed(int index, ProcessorDiagnosticState? before, ProcessingSystem processing, GoodsInventory inventory) =>
        ProcessorChanged(index, before, processing, inventory, "RawClaimed", 1);
    internal void ProcessingStarted(int index, ProcessorDiagnosticState? before, ProcessingSystem processing, GoodsInventory inventory) =>
        ProcessorChanged(index, before, processing, inventory, "ProcessingStarted");
    internal void Processed(int index, ProcessorDiagnosticState? before, ProcessingSystem processing, GoodsInventory inventory, int quantity) =>
        ProcessorChanged(index, before, processing, inventory, "Processed", quantity);

    private void ProcessorChanged(int index, ProcessorDiagnosticState? before, ProcessingSystem processing,
        GoodsInventory inventory, string transition, int? quantity = null)
    {
        if (!before.HasValue || !_context.Diagnostics.ShouldCapture(ProcessorEvent.Name, anchorIndex: index)) return;
        CaptureProcessorChanged(index, before.Value, processing, inventory, transition, quantity);
    }

    private void CaptureProcessorChanged(int index, ProcessorDiagnosticState before, ProcessingSystem processing,
        GoodsInventory inventory, string transition, int? quantity)
    {
        _context.Observe(() =>
        {
            ProcessorDiagnosticState after = ProcessorState(index, processing, inventory);
            var fields = Fields(index, "Processor", before.Crop, transition);
            fields["StateBefore"] = before.Status.ToString();
            fields["StateAfter"] = after.Status.ToString();
            fields["RemainingSeconds"] = after.RemainingSeconds;
            if (quantity.HasValue) fields["Quantity"] = quantity.Value;
            _context.Diagnostics.Capture(ProcessorEvent, "选定加工场地实际状态转换", fields);
        });
    }

    internal void PlantingChecked(int? anchorIndex, CropKind crop, PlantingFailure result)
    {
        if (!anchorIndex.HasValue || !_context.Diagnostics.ShouldCapture(RuleEvent.Name, anchorIndex: anchorIndex)) return;
        CapturePlantingChecked(anchorIndex.Value, crop, result);
    }

    private void CapturePlantingChecked(int anchorIndex, CropKind crop, PlantingFailure result)
    {
        _context.Observe(() =>
        {
            var fields = _context.Context("Workers");
            fields["RuleKind"] = "Planting";
            fields["Anchor"] = Anchor(anchorIndex);
            fields["Crop"] = crop.ToString();
            var checks = new Dictionary<string, object?> { ["Season"] = result != PlantingFailure.WrongSeason };
            if (result != PlantingFailure.WrongSeason) checks["Time"] = result != PlantingFailure.InsufficientTime;
            fields["CheckResults"] = checks;
            fields["Outcome"] = result == PlantingFailure.WrongSeason ? "Rejected" : "Success";
            _context.Diagnostics.Capture(RuleEvent, "选定农田实际播种检查", fields);
        });
    }

    private Dictionary<string, object?> Fields(int index, string kind, CropKind crop, string transition)
    {
        var fields = _context.Context("Processing");
        // 供水可能来自降雨，不把全部生产转换伪归到工人相位。
        fields.Remove("Phase");
        fields["ProductionKind"] = kind;
        fields["Anchor"] = Anchor(index);
        fields["Crop"] = crop.ToString();
        fields["Transition"] = transition;
        return fields;
    }

    private static ProcessorDiagnosticState ProcessorState(int index, ProcessingSystem processing, GoodsInventory inventory)
    {
        ProcessorSnapshot state = processing.Get(index);
        return new(state.CropKind, processing.GetStatus(index, inventory), state.RemainingSeconds);
    }

    private static Dictionary<string, object?> Anchor(int index) => new()
    {
        ["X"] = index % FarmGame.MapSize,
        ["Y"] = index / FarmGame.MapSize,
    };
}

internal readonly record struct ProcessorDiagnosticState(CropKind Crop, ProcessorStatus Status, int RemainingSeconds);
