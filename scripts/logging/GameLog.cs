using System;
using System.Collections.Generic;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;
using FarmExchange.Trading;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>绑定一局真实经营的领域观察上下文，封装局身份、指令关联及业务投影。</summary>
 */
public sealed class GameLog : IDisposable
{
    private const string Source = "FarmExchange.Gameplay.FarmGame";
    private readonly RuntimeLog _owner;
    private readonly FarmGame _game;
    private readonly string _gameId = Guid.NewGuid().ToString("N");
    private long _commandId;
    private bool _ended;

    internal GameLog(RuntimeLog owner, FarmGame game) { _owner = owner; _game = game; }

    internal void Initialized(int seed) => Observe(() =>
    {
        var fields = Context("Initialization");
        fields["GamePurpose"] = "Main";
        fields["Seed"] = seed;
        var facilities = new List<Dictionary<string, object?>>();
        foreach (var space in _game.GetBuildingSpaces())
            facilities.Add(new()
            {
                ["Anchor"] = new Dictionary<string, object?> { ["X"] = space.AnchorCell.X, ["Y"] = space.AnchorCell.Y },
                ["BuildingKind"] = space.Building.ToString(),
                ["Crop"] = _game.GetPlot(space.AnchorCell).CropKind.ToString(),
            });
        fields["InitialFacilities"] = facilities;
        var inventory = new Dictionary<string, object?>();
        foreach (var commodity in CommodityCatalog.All)
            inventory[CommodityName(commodity.Id)] = new Dictionary<string, object?>
            {
                ["Total"] = _game.GetStock(commodity.Id),
                ["Available"] = _game.GetAvailableStock(commodity.Id),
                ["Frozen"] = _game.GetFrozenStock(commodity.Id),
            };
        fields["Inventory"] = inventory;
        fields["MoneyCents"] = _game.MoneyCents;
        fields["AvailableMoneyCents"] = _game.AvailableMoneyCents;
        fields["FrozenMoneyCents"] = _game.FrozenMoneyCents;
        _owner.Emit("GameInitialized", "经营局初始化完成", Source, fields);
    });

    /**
     * <summary>观察原买入请求并取得本笔提交前的真实资源。</summary>
     * <param name="commodity">原请求商品，非法商品保持原值并交给业务判断。</param>
     * <param name="quantity">原请求份数，不用实际成交数量覆盖。</param>
     * <param name="origin">真实 Player 或 Scenario 来源；其他值在记录前抛出 ArgumentOutOfRangeException。</param>
     * <returns>在业务完成后终结的观察对象；已释放或关闭采集时为 null。</returns>
     * <remarks>调用方随后只执行一次原业务命令，再 Complete 或 Faulted；本入口不执行业务。</remarks>
     */
    public BuyLogOperation? BeginBuy(CommodityId commodity, int quantity, CommandOrigin origin = CommandOrigin.Player)
    {
        if (origin is not CommandOrigin.Player and not CommandOrigin.Scenario)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (_ended || !_owner.IsEnabled) return null;
        var command = new BuyCommand(++_commandId, origin);
        TradeObservation? before = null;
        Observe(() =>
        {
            var fields = Command(command);
            fields["CommandArguments"] = new Dictionary<string, object?>
            {
                ["Commodity"] = CommodityName(commodity),
                ["RequestMode"] = "Fixed",
                ["RequestedQuantity"] = quantity,
            };
            _owner.Emit("CommandReceived", "收到商品买入指令", Source, fields);
            before = Snapshot(commodity);
        });
        return new BuyLogOperation(this, command, commodity, quantity, before);
    }

    internal void BuyFinished(BuyCommand command, CommodityId commodity, int requestedQuantity, TradeResult result, TradeObservation? before)
        => Observe(() =>
        {
            if (before.HasValue)
            {
                TradeObservation after = Snapshot(commodity);
                var fields = Command(command);
                fields["Outcome"] = result.Success ? "Success" : "Rejected";
                fields["FailureCode"] = "TradeFailure." + result.Failure;
                fields["RejectionReason"] = result.ErrorMessage;
                fields["Commodity"] = CommodityName(commodity);
                fields["Side"] = "Buy";
                fields["RequestMode"] = "Fixed";
                fields["RequestedQuantity"] = requestedQuantity;
                fields["Quantity"] = result.Quantity;
                fields["ValueCents"] = result.TotalCents;
                fields["FeeCents"] = result.FeeCents;
                if (result.UnitPriceCents.HasValue) fields["UnitPriceCents"] = result.UnitPriceCents.Value;
                fields["MoneyBeforeCents"] = before.Value.Money;
                fields["MoneyAfterCents"] = after.Money;
                fields["AvailableMoneyBeforeCents"] = before.Value.AvailableMoney;
                fields["AvailableMoneyAfterCents"] = after.AvailableMoney;
                fields["FrozenMoneyBeforeCents"] = before.Value.FrozenMoney;
                fields["FrozenMoneyAfterCents"] = after.FrozenMoney;
                fields["StockBefore"] = before.Value.Stock;
                fields["StockAfter"] = after.Stock;
                fields["AvailableStockBefore"] = before.Value.AvailableStock;
                fields["AvailableStockAfter"] = after.AvailableStock;
                fields["FrozenStockBefore"] = before.Value.FrozenStock;
                fields["FrozenStockAfter"] = after.FrozenStock;
                _owner.Emit("TradeFinished", result.Success ? "买入商品成功" : "买入商品被拒绝", Source, fields);
            }
            var finished = Command(command);
            finished["CommandStatus"] = result.Success ? "Succeeded" : "Rejected";
            finished["RejectionReason"] = result.ErrorMessage;
            _owner.Emit("CommandFinished", "商品买入指令结束", Source, finished);
        });

    internal void BuyFaulted(BuyCommand command, Exception error) => Observe(() =>
    {
        var fields = Command(command);
        fields["ExceptionType"] = error.GetType().FullName;
        fields["Exception"] = error.ToString();
        _owner.Emit("BusinessException", "商品买入抛出异常", Source, fields, LogLevel.Error);
        var finished = Command(command);
        finished["CommandStatus"] = "Faulted";
        _owner.Emit("CommandFinished", "商品买入指令异常结束", Source, finished);
    });

    internal void End(string reason)
    {
        if (_ended) return;
        _ended = true;
        _owner.Observe(() =>
        {
            var fields = Context("Shutdown");
            fields["EndReason"] = reason;
            _owner.Emit("GameEnded", "经营局结束", Source, fields);
        });
        _owner.Release(this);
    }

    /**
     * <summary>幂等结束本局观察关联，不修改经营资源。</summary>
     */
    public void Dispose() => End("Released");

    private Dictionary<string, object?> Command(BuyCommand command)
    {
        var fields = Context("Command");
        fields["CommandId"] = command.Id;
        fields["CommandName"] = "BuyCommodity";
        fields["CommandOrigin"] = command.Origin.ToString();
        return fields;
    }

    private void Observe(Action observation)
    {
        if (!_ended) _owner.Observe(observation);
    }

    private Dictionary<string, object?> Context(string phase)
    {
        var calendar = _game.Calendar;
        return new()
        {
            ["GameInstanceId"] = _gameId,
            ["SimulationSeconds"] = calendar.ElapsedSeconds,
            ["GameDate"] = new Dictionary<string, object?> { ["Year"] = calendar.Year, ["Month"] = calendar.Month, ["Day"] = calendar.Day },
            ["Season"] = calendar.Season.ToString(),
            ["IsPaused"] = calendar.IsPaused,
            ["Phase"] = phase,
        };
    }

    private TradeObservation Snapshot(CommodityId commodity) => new(
        _game.MoneyCents, _game.AvailableMoneyCents, _game.FrozenMoneyCents,
        commodity.IsDefined ? _game.GetStock(commodity) : null,
        commodity.IsDefined ? _game.GetAvailableStock(commodity) : null,
        commodity.IsDefined ? _game.GetFrozenStock(commodity) : null);

    private static string CommodityName(CommodityId commodity) => commodity.Crop + "." + commodity.Kind;
}

internal readonly record struct TradeObservation(
    int Money, int AvailableMoney, int FrozenMoney, int? Stock, int? AvailableStock, int? FrozenStock);

internal readonly record struct BuyCommand(long Id, CommandOrigin Origin);
