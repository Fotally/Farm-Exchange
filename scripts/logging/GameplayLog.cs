using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Land;

namespace FarmExchange.Logging;

/**
 * <summary>建造、拆除与原料底线的领域 Adapter，投影真实请求及提交结果。</summary>
 */
public sealed class GameplayLog
{
    private const string Source = "FarmExchange.Gameplay.FarmGame";
    private static readonly CommandDescription PlaceCommand = new("PlaceBuilding", Source,
        "收到建造指令", "建造指令结束", "建造抛出异常", "建造指令异常结束");
    private static readonly CommandDescription RemoveCommand = new("RemoveBuilding", Source,
        "收到拆除指令", "拆除指令结束", "拆除抛出异常", "拆除指令异常结束");
    private static readonly CommandDescription ReserveCommand = new("SetRawReserve", Source,
        "收到原料底线指令", "原料底线指令结束", "原料底线设置抛出异常", "原料底线指令异常结束");
    private static readonly LogEventDescriptor Placed = new(40, "BuildingPlaced", Source);
    private static readonly LogEventDescriptor Removed = new(41, "BuildingRemoved", Source);
    private static readonly LogEventDescriptor ReserveChanged = new(42, "RawReserveChanged", Source);
    private readonly GameLog _context;
    private readonly FarmGame _game;

    internal GameplayLog(GameLog context, FarmGame game) { _context = context; _game = game; }

    /**
     * <summary>观察原建造输入和提交前资金，不执行放置预检或建造。</summary>
     * <param name="cell">原请求锚点，越界输入保持原值。</param>
     * <param name="building">原建筑类型，非法值保持原值。</param>
     * <param name="crop">原作物输入，道路也在原参数中保留。</param>
     * <param name="origin">真实 Player 或 Scenario 来源；其他值在记录前抛出异常。</param>
     * <returns>真实业务完成后终结的观察；关闭采集时为 null。</returns>
     */
    public BuildingLogOperation? BeginPlace(Vector2I cell, BuildingKind building, CropKind crop,
        CommandOrigin origin = CommandOrigin.Player) => BeginBuilding(cell, building, crop, false, origin);

    /**
     * <summary>观察原拆除子格，保存当前解析的整座设施和资金。</summary>
     * <param name="cell">原请求子格。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>真实业务完成后终结的观察；关闭采集时为 null。</returns>
     */
    public BuildingLogOperation? BeginRemove(Vector2I cell, CommandOrigin origin = CommandOrigin.Player) =>
        BeginBuilding(cell, null, null, true, origin);

    private BuildingLogOperation? BeginBuilding(Vector2I cell, BuildingKind? building, CropKind? crop,
        bool remove, CommandOrigin origin)
    {
        ValidateOrigin(origin);
        if (!_context.CanObserve) return null;
        var command = _context.NewCommand(remove ? RemoveCommand : PlaceCommand, origin);
        BuildingObservation? before = null;
        _context.Observe(() =>
        {
            var arguments = new Dictionary<string, object?> { ["Cell"] = Cell(cell) };
            if (!remove)
            {
                arguments["BuildingKind"] = building.ToString();
                arguments["Crop"] = crop.ToString();
            }
            command.Received(arguments);
            BuildingSpaceSnapshot? space = remove ? _game.GetBuildingSpace(cell) : null;
            before = new(_game.MoneyCents, _game.AvailableMoneyCents, _game.FrozenMoneyCents,
                space?.AnchorCell, remove ? space?.Building : building,
                remove ? space != null && space.Building != BuildingKind.Road ? _game.GetPlot(cell).CropKind : null : crop);
        });
        return new(this, command, cell, before);
    }

    internal void BuildingCompleted(CommandObservation command, Vector2I cell, BuildingObservation? before,
        PlacementResult? placement, string? rejectionReason) => command.Complete(fields =>
    {
        bool success = placement?.Success ?? rejectionReason == null;
        if (before.HasValue)
        {
            BuildingObservation prior = before.Value;
            fields["Outcome"] = success ? "Success" : "Rejected";
            fields["RejectionReason"] = rejectionReason;
            fields["Cell"] = Cell(cell);
            Vector2I? anchor = placement.HasValue
                ? success ? _game.GetBuildingSpace(cell)?.AnchorCell : null
                : prior.Anchor;
            fields["Anchor"] = anchor.HasValue ? Cell(anchor.Value) : null;
            fields["BuildingKind"] = prior.Building?.ToString();
            if (prior.Building != BuildingKind.Road) fields["Crop"] = prior.Crop?.ToString();
            fields["ChargedCents"] = (long)(placement?.ChargedCents ?? 0);
            if (placement.HasValue) fields["FailureCode"] = "LandFailure." + placement.Value.Failure;
            fields["MoneyBeforeCents"] = prior.Money;
            fields["MoneyAfterCents"] = _game.MoneyCents;
            fields["AvailableMoneyBeforeCents"] = prior.AvailableMoney;
            fields["AvailableMoneyAfterCents"] = _game.AvailableMoneyCents;
            fields["FrozenMoneyBeforeCents"] = prior.FrozenMoney;
            fields["FrozenMoneyAfterCents"] = _game.FrozenMoneyCents;
        }
        return new(before.HasValue ? placement.HasValue ? Placed : Removed : null,
            (placement.HasValue ? "建造" : "拆除") + (success ? "成功" : "被拒绝"), success, rejectionReason);
    });

    /**
     * <summary>观察原料底线原请求和修改前的真实底线。</summary>
     * <param name="crop">原作物输入。</param>
     * <param name="quantity">原请求底线份数，负数保持原值。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>真实业务完成后终结的观察；关闭采集时为 null。</returns>
     */
    public RawReserveLogOperation? BeginSetRawReserve(CropKind crop, int quantity,
        CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateOrigin(origin);
        if (!_context.CanObserve) return null;
        var command = _context.NewCommand(ReserveCommand, origin);
        int? before = null;
        _context.Observe(() =>
        {
            command.Received(new() { ["Crop"] = crop.ToString(), ["RequestedQuantity"] = quantity });
            if (CropCatalog.IsDefined(crop)) before = _game.GetRawReserve(crop);
        });
        return new(this, command, crop, quantity, before);
    }

    internal void ReserveCompleted(CommandObservation command, CropKind crop, int quantity, int? before,
        RawReserveFailure result) => command.Complete(fields =>
    {
        bool success = result == RawReserveFailure.None;
        string? reason = result switch
        {
            RawReserveFailure.None => null,
            RawReserveFailure.InvalidCrop => "无效作物",
            RawReserveFailure.InvalidQuantity => "原料底线不能为负数",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        fields["Outcome"] = success ? "Success" : "Rejected";
        fields["FailureCode"] = "RawReserveFailure." + result;
        fields["RejectionReason"] = reason;
        fields["Crop"] = crop.ToString();
        fields["RequestedQuantity"] = quantity;
        fields["PreviousReserveQuantity"] = before;
        fields["ReserveQuantity"] = CropCatalog.IsDefined(crop) ? _game.GetRawReserve(crop) : null;
        return new(ReserveChanged, success ? "原料底线设置完成" : "原料底线设置被拒绝", success, reason);
    });

    private static Dictionary<string, object?> Cell(Vector2I cell) => new() { ["X"] = cell.X, ["Y"] = cell.Y };
    private static void ValidateOrigin(CommandOrigin origin)
    {
        if (origin is not CommandOrigin.Player and not CommandOrigin.Scenario)
            throw new ArgumentOutOfRangeException(nameof(origin));
    }
}

internal readonly record struct BuildingObservation(int Money, int AvailableMoney, int FrozenMoney,
    Vector2I? Anchor, BuildingKind? Building, CropKind? Crop);
