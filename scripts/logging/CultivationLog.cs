using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;

namespace FarmExchange.Logging;

/**
 * <summary>年度表与手动接管的领域观察，保存有界原请求并投影真实前后状态。</summary>
 * <remarks>不验证排程、不解析整批目标、不执行或重试耕作命令。</remarks>
 */
public sealed class CultivationLog
{
    private const string Source = "FarmExchange.Gameplay.FarmGame";
    private const int SampleLimit = 32;
    private static readonly LogEventDescriptor Created = new(60, "CultivationPlanCreated", Source);
    private static readonly LogEventDescriptor Updated = new(61, "CultivationPlanUpdated", Source);
    private static readonly LogEventDescriptor Deleted = new(62, "CultivationPlanDeleted", Source);
    private static readonly LogEventDescriptor Applied = new(63, "CultivationPlanApplied", Source);
    private static readonly LogEventDescriptor ManualChanged = new(64, "FarmManualControlChanged", Source);
    private readonly GameLog _context;
    private readonly FarmGame _game;

    internal CultivationLog(GameLog context, FarmGame game) { _context = context; _game = game; }

    /**
     * <summary>观察年度表创建原请求。</summary>
     * <param name="request">原始表配置，不把非法输入当作有效配置。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null。</returns>
     */
    public CultivationPlanLogOperation? BeginCreate(CultivationPlanRequest request, CommandOrigin origin = CommandOrigin.Player) =>
        BeginPlan("CreateCultivationPlan", Created, 0, request, origin);

    /**
     * <summary>观察年度表完整更新及原有效配置。</summary>
     * <param name="id">原请求表编号。</param>
     * <param name="request">原请求替换配置。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null。</returns>
     */
    public CultivationPlanLogOperation? BeginUpdate(int id, CultivationPlanRequest request, CommandOrigin origin = CommandOrigin.Player) =>
        BeginPlan("UpdateCultivationPlan", Updated, id, request, origin);

    /**
     * <summary>观察删除前有效表与真实引用数。</summary>
     * <param name="id">原请求表编号。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null。</returns>
     */
    public CultivationPlanLogOperation? BeginDelete(int id, CommandOrigin origin = CommandOrigin.Player) =>
        BeginPlan("DeleteCultivationPlan", Deleted, id, null, origin);

    private CultivationPlanLogOperation? BeginPlan(string name, LogEventDescriptor descriptor, int id,
        CultivationPlanRequest? request, CommandOrigin origin)
    {
        ValidateOrigin(origin);
        if (!_context.CanObserve) return null;
        CommandObservation command = NewCommand(name, origin);
        PlanObservation? before = null;
        _context.Observe(() =>
        {
            PlanProjection? input = Project(request?.Name, request?.Mode, request?.Entries, request != null);
            var arguments = new Dictionary<string, object?>();
            var metadata = new Dictionary<string, object?>();
            if (descriptor != Created) arguments["PlanId"] = id;
            if (descriptor != Deleted) arguments["RequestedPlan"] = AddProjection(input, "CommandArguments.RequestedPlan", metadata);
            command.Received(arguments, metadata);
            before = new(descriptor, id, input, descriptor == Created ? null : _game.GetCultivationPlan(id));
        });
        return new(this, command, before);
    }

    internal void PlanCompleted(CommandObservation command, PlanObservation? before, CultivationCommandResult result) =>
        command.Complete(fields =>
        {
            if (before == null) return new(null, null, result.Success, result.Error);
            CultivationPlanSnapshot? after = before.Event == Created
                ? result.Success ? _game.GetCultivationPlan(result.Id) : null
                : _game.GetCultivationPlan(before.Id);
            Outcome(fields, result.Error);
            int? actualId = after?.Id ?? before.Plan?.Id;
            if (actualId.HasValue) fields["PlanId"] = actualId.Value;
            if (before.Event != Deleted) fields["RequestedPlan"] = AddProjection(before.Request, "RequestedPlan", fields);
            fields["PlanBefore"] = AddProjection(Project(before.Plan), "PlanBefore", fields);
            fields["PlanAfter"] = AddProjection(Project(after), "PlanAfter", fields);
            if (before.Event == Deleted) fields["DetachedFarmCount"] = result.Success ? before.Plan?.ReferencingFarms ?? 0 : 0;
            return new(before.Event, result.Success ? "年度表命令完成" : "年度表命令被拒绝", result.Success, result.Error);
        });

    /**
     * <summary>观察批量应用原输入，不验证或解析目标。</summary>
     * <param name="id">原请求表编号。</param>
     * <param name="cells">原始子格列表，仅采样前 32 项。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null。</returns>
     */
    public CultivationApplyLogOperation? BeginApply(int id, IReadOnlyList<Vector2I> cells, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateOrigin(origin);
        if (!_context.CanObserve) return null;
        CommandObservation command = NewCommand("ApplyCultivationPlan", origin);
        ApplyObservation? before = null;
        _context.Observe(() =>
        {
            var arguments = new Dictionary<string, object?> { ["PlanId"] = id };
            var metadata = new Dictionary<string, object?>();
            List<object?>? sample = cells == null ? null : new();
            if (cells != null)
            {
                for (int i = 0; i < cells.Count && i < SampleLimit; i++) sample!.Add(Cell(cells[i]));
                if (cells.Count > SampleLimit) Truncated(metadata, "CommandArguments.Cells", cells.Count);
            }
            arguments["Cells"] = sample;
            command.Received(arguments, metadata);
            before = new(_game.GetCultivationPlan(id)?.Id, cells?.Count);
        });
        return new(this, command, before);
    }

    internal void ApplyCompleted(CommandObservation command, ApplyObservation? before, string? error, IReadOnlyList<int>? indices) =>
        command.Complete(fields =>
        {
            if (before == null) return new(null, null, error == null, error);
            Outcome(fields, error);
            if (before.PlanId.HasValue) fields["PlanId"] = before.PlanId.Value;
            fields["InputCellCount"] = before.InputCount;
            fields["ResolvedUniqueTargetCount"] = indices?.Count;
            fields["AppliedTargetCount"] = error == null ? indices?.Count : 0;
            var targets = new List<object?>();
            if (indices != null)
                for (int i = 0; i < indices.Count && i < SampleLimit; i++)
                    targets.Add(Cell(new(indices[i] % FarmGame.MapSize, indices[i] / FarmGame.MapSize)));
            fields["TargetAnchors"] = targets;
            fields["TargetsTruncated"] = indices?.Count > SampleLimit;
            if (indices?.Count > SampleLimit) Truncated(fields, "TargetAnchors", indices.Count);
            return new(Applied, error == null ? "年度表应用完成" : "年度表应用被拒绝", error == null, error);
        });

    /**
     * <summary>观察两种手动接管及单田前状态，不改变当前轮。</summary>
     * <param name="cell">原请求子格。</param>
     * <param name="crop">原请求作物。</param>
     * <param name="mode">Immediate 重启当前轮；PrepareNext 保留已有轮。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null。</returns>
     */
    public FarmManualControlLogOperation? BeginManualControl(Vector2I cell, CropKind crop, CultivationMode mode,
        CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateOrigin(origin);
        if (mode is not CultivationMode.Immediate and not CultivationMode.PrepareNext) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!_context.CanObserve) return null;
        CommandObservation command = NewCommand(mode == CultivationMode.Immediate ? "SetFarmCrop" : "PrepareFarmCrop", origin);
        ManualObservation? before = null;
        _context.Observe(() =>
        {
            command.Received(new() { ["Cell"] = Cell(cell), ["Crop"] = crop.ToString() });
            bool valid = _game.TryGetPlot(cell, out PlotSnapshot plot) == FarmExchange.Land.LandFailure.None && plot.Building == BuildingKind.Farm;
            before = new(cell, crop, mode, valid ? plot : null,
                valid ? _game.GetFarmCultivation(cell) : null, valid ? _game.GetBuildingSpace(cell)!.AnchorCell : null);
        });
        return new(this, command, before);
    }

    internal void ManualCompleted(CommandObservation command, ManualObservation? before, string? error) =>
        command.Complete(fields =>
        {
            if (before == null) return new(null, null, error == null, error);
            bool valid = before.Plot.HasValue;
            PlotSnapshot? after = valid ? _game.GetPlot(before.Cell) : null;
            FarmCultivationSnapshot? arrangement = valid ? _game.GetFarmCultivation(before.Cell) : null;
            Outcome(fields, error);
            fields["Cell"] = Cell(before.Cell);
            fields["Anchor"] = before.Anchor.HasValue ? Cell(before.Anchor.Value) : null;
            fields["Crop"] = before.Crop.ToString();
            fields["TakeoverMode"] = before.Mode.ToString();
            fields["PreviousPlanId"] = before.Arrangement?.PlanId;
            fields["PlanDetached"] = valid && error == null ? before.Arrangement?.PlanId != null && arrangement?.PlanId == null : null;
            fields["CurrentCyclePreserved"] = valid && error == null
                ? before.Mode == CultivationMode.PrepareNext && before.Plot!.Value.Crop != CropStage.None : null;
            fields["PreviousSelectedCrop"] = before.Plot?.CropKind.ToString();
            fields["EffectiveSelectedCrop"] = after?.CropKind.ToString();
            fields["NextCycleCrop"] = arrangement?.PreparedCrop?.ToString();
            return new(ManualChanged, error == null ? "农田手动接管完成" : "农田手动接管被拒绝", error == null, error);
        });

    private CommandObservation NewCommand(string name, CommandOrigin origin) => _context.NewCommand(
        new(name, Source, "收到耕作指令", "耕作指令结束", "耕作指令抛出异常", "耕作指令异常结束"), origin);

    private static PlanProjection? Project(CultivationPlanSnapshot? plan) => Project(plan?.Name, plan?.Mode, plan?.Entries, plan != null);

    private static PlanProjection? Project(string? name, CultivationMode? mode, IReadOnlyList<CultivationEntry>? entries, bool exists)
    {
        if (!exists) return null;
        List<object?>? sample = entries == null ? null : new();
        if (entries != null)
            for (int i = 0; i < entries.Count && i < SampleLimit; i++)
                sample!.Add(new Dictionary<string, object?>
                { ["Id"] = entries[i].Id, ["Crop"] = entries[i].Crop.ToString(), ["StartDay"] = entries[i].StartDay });
        return new(new() { ["Name"] = name, ["Mode"] = mode?.ToString(), ["Entries"] = sample }, entries?.Count);
    }

    private static Dictionary<string, object?>? AddProjection(PlanProjection? plan, string path, Dictionary<string, object?> fields)
    {
        if (plan?.EntryCount > SampleLimit) Truncated(fields, path + ".Entries", plan.EntryCount.Value);
        return plan?.Fields;
    }

    private static void Truncated(Dictionary<string, object?> fields, string path, long count)
    {
        fields["Truncated"] = true;
        if (!fields.TryGetValue("TruncatedFields", out object? paths)) fields["TruncatedFields"] = paths = new List<string>();
        if (!fields.TryGetValue("TruncatedOriginalCounts", out object? counts)) fields["TruncatedOriginalCounts"] = counts = new Dictionary<string, object?>();
        ((List<string>)paths!).Add(path);
        ((Dictionary<string, object?>)counts!)[path] = new Dictionary<string, object?> { ["ItemCount"] = count };
    }

    private static Dictionary<string, object?> Cell(Vector2I cell) => new() { ["X"] = cell.X, ["Y"] = cell.Y };
    private static void Outcome(Dictionary<string, object?> fields, string? error)
    { fields["Outcome"] = error == null ? "Success" : "Rejected"; fields["RejectionReason"] = error; }
    private static void ValidateOrigin(CommandOrigin origin)
    { if (origin is not CommandOrigin.Player and not CommandOrigin.Scenario) throw new ArgumentOutOfRangeException(nameof(origin)); }
}

internal sealed record PlanProjection(Dictionary<string, object?> Fields, int? EntryCount);
internal sealed record PlanObservation(LogEventDescriptor Event, int Id, PlanProjection? Request, CultivationPlanSnapshot? Plan);
internal sealed record ApplyObservation(int? PlanId, int? InputCount);
internal sealed record ManualObservation(Vector2I Cell, CropKind Crop, CultivationMode Mode, PlotSnapshot? Plot,
    FarmCultivationSnapshot? Arrangement, Vector2I? Anchor);
