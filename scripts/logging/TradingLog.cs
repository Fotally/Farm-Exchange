using System;
using System.Collections.Generic;
using FarmExchange.Gameplay;
using FarmExchange.Farming;
using FarmExchange.Inventory;
using FarmExchange.Trading;

namespace FarmExchange.Logging;

/**
 * <summary>主动交易的领域 Adapter，拥有请求、真实资源快照与结果字段投影。</summary>
 */
public sealed class TradingLog
{
    private const string Source = "FarmExchange.Gameplay.FarmGame";
    private static readonly CommandDescription BuyCommand = new("BuyCommodity", Source,
        "收到商品买入指令", "商品买入指令结束", "商品买入抛出异常", "商品买入指令异常结束");
    private static readonly LogEventDescriptor TradeFinished = new(7, "TradeFinished", Source);
    private static readonly LogEventDescriptor AllProductsSold = new(11, "AllProductsSold", Source);
    private static readonly CommandDescription SellCommand = new("SellCommodity", Source,
        "收到商品卖出指令", "商品卖出指令结束", "商品卖出抛出异常", "商品卖出指令异常结束");
    private static readonly CommandDescription SellAllCommand = new("SellCommodityAll", Source,
        "收到单商品全售指令", "单商品全售指令结束", "单商品全售抛出异常", "单商品全售指令异常结束");
    private static readonly CommandDescription SellProductsCommand = new("SellAllProducts", Source,
        "收到全部加工品出售指令", "全部加工品出售指令结束", "全部加工品出售抛出异常", "全部加工品出售指令异常结束");
    private readonly GameLog _context;
    private readonly FarmGame _game;

    internal TradingLog(GameLog context, FarmGame game) { _context = context; _game = game; }

    /**
     * <summary>观察原买入请求并取得本笔提交前的真实资源。</summary>
     * <param name="commodity">原请求商品，非法商品保持原值并交给业务判断。</param>
     * <param name="quantity">原请求份数，不用实际成交数量覆盖。</param>
     * <param name="origin">真实 Player 或 Scenario 来源；其他值在记录前抛出 ArgumentOutOfRangeException。</param>
     * <returns>在业务完成后终结的观察对象；已释放或关闭采集时为 null。</returns>
     * <remarks>调用方随后只执行一次原业务命令，再 Complete 或 Faulted；本入口不执行业务。</remarks>
     */
    public TradeLogOperation? BeginBuy(CommodityId commodity, int quantity, CommandOrigin origin = CommandOrigin.Player) =>
        BeginTrade(BuyCommand, commodity, quantity, true, origin);

    /**
     * <summary>观察固定数量卖出的原请求及提交前资源。</summary>
     * <param name="commodity">原请求商品。</param>
     * <param name="quantity">原请求份数。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>一次完成观察；结束或关闭采集时为 null。</returns>
     */
    public TradeLogOperation? BeginSell(CommodityId commodity, int quantity, CommandOrigin origin = CommandOrigin.Player) =>
        BeginTrade(SellCommand, commodity, quantity, false, origin);

    /**
     * <summary>观察某商品可用库存全部卖出的请求，不把当前数量改写为固定请求。</summary>
     * <param name="commodity">原请求商品。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>一次完成观察；结束或关闭采集时为 null。</returns>
     */
    public TradeLogOperation? BeginSellAll(CommodityId commodity, CommandOrigin origin = CommandOrigin.Player) =>
        BeginTrade(SellAllCommand, commodity, null, false, origin);

    private TradeLogOperation? BeginTrade(CommandDescription description, CommodityId commodity, int? quantity, bool buy, CommandOrigin origin)
    {
        if (origin is not CommandOrigin.Player and not CommandOrigin.Scenario)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!_context.CanObserve) return null;
        var command = _context.NewCommand(description, origin);
        TradeObservation? before = null;
        _context.Observe(() =>
        {
            var arguments = new Dictionary<string, object?>
            {
                ["Commodity"] = CommodityName(commodity),
                ["RequestMode"] = quantity.HasValue ? "Fixed" : "AllCommodity",
            };
            if (quantity.HasValue) arguments["RequestedQuantity"] = quantity.Value;
            command.Received(arguments);
            before = Snapshot(commodity);
        });
        return new TradeLogOperation(this, command, commodity, quantity, buy, before);
    }

    internal void TradeCompleted(CommandObservation command, CommodityId commodity, int? requestedQuantity, bool buy, TradeResult result, TradeObservation? before)
        => command.Complete(fields =>
        {
            if (before.HasValue)
            {
                TradeObservation after = Snapshot(commodity);
                fields["Outcome"] = result.Success ? "Success" : "Rejected";
                fields["FailureCode"] = "TradeFailure." + result.Failure;
                fields["RejectionReason"] = result.ErrorMessage;
                fields["Commodity"] = CommodityName(commodity);
                fields["Side"] = buy ? "Buy" : "Sell";
                fields["RequestMode"] = requestedQuantity.HasValue ? "Fixed" : "AllCommodity";
                if (requestedQuantity.HasValue) fields["RequestedQuantity"] = requestedQuantity.Value;
                fields["Quantity"] = result.Quantity;
                fields["ValueCents"] = result.TotalCents;
                fields["FeeCents"] = result.FeeCents;
                if (result.UnitPriceCents.HasValue) fields["UnitPriceCents"] = result.UnitPriceCents.Value;
                AddResources(fields, before.Value, after);
            }
            return new(before.HasValue ? TradeFinished : null,
                (buy ? "买入商品" : "卖出商品") + (result.Success ? "成功" : "被拒绝"), result.Success, result.ErrorMessage);
        });

    /**
     * <summary>观察一次全部加工品出售请求及七商品提交前资源。</summary>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>完成后消费原结算明细的观察对象；关闭采集时为 null。</returns>
     */
    public ProductSaleLogOperation? BeginSellAllProducts(CommandOrigin origin = CommandOrigin.Player)
    {
        if (origin is not CommandOrigin.Player and not CommandOrigin.Scenario)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!_context.CanObserve) return null;
        var command = _context.NewCommand(SellProductsCommand, origin);
        TradeObservation[]? before = null;
        _context.Observe(() =>
        {
            command.Received(new() { ["RequestMode"] = "AllProducts" });
            before = new TradeObservation[CropCatalog.Crops.Count];
            foreach (var crop in CropCatalog.Crops)
                before[(int)crop.Kind] = Snapshot(new(crop.Kind, CommodityKind.Product));
        });
        return new(this, command, before);
    }

    internal void ProductsCompleted(CommandObservation command, ProductSaleResult result, TradeObservation[]? before) =>
        command.Complete(fields =>
        {
            TradeResult trade = result.Trade;
            if (before != null)
            {
                fields["Outcome"] = trade.Success ? "Success" : "Rejected";
                fields["FailureCode"] = "TradeFailure." + trade.Failure;
                fields["RejectionReason"] = trade.ErrorMessage;
                fields["RequestMode"] = "AllProducts";
                fields["Quantity"] = trade.Quantity;
                fields["ValueCents"] = trade.TotalCents;
                fields["FeeCents"] = trade.FeeCents;
                AddMoney(fields, before[0], Snapshot(result.Lines[0].Commodity));
                var lines = new List<Dictionary<string, object?>>();
                foreach (TradeLineResult line in result.Lines)
                {
                    var item = new Dictionary<string, object?>
                    {
                        ["Commodity"] = CommodityName(line.Commodity),
                        ["Quantity"] = line.Quantity,
                        ["UnitPriceCents"] = line.UnitPriceCents,
                        ["ValueCents"] = line.ValueCents,
                        ["FeeCents"] = 0,
                    };
                    AddStock(item, before[(int)line.Commodity.Crop], Snapshot(line.Commodity));
                    lines.Add(item);
                }
                fields["TradeLines"] = lines;
            }
            return new(before == null ? null : AllProductsSold,
                trade.Success ? "全部加工品出售完成" : "全部加工品出售被拒绝", trade.Success, trade.ErrorMessage);
        });

    internal TradeObservation Snapshot(CommodityId commodity) => new(
        _game.MoneyCents, _game.AvailableMoneyCents, _game.FrozenMoneyCents,
        commodity.IsDefined ? _game.GetStock(commodity) : null,
        commodity.IsDefined ? _game.GetAvailableStock(commodity) : null,
        commodity.IsDefined ? _game.GetFrozenStock(commodity) : null);

    private static string CommodityName(CommodityId commodity) => commodity.Crop + "." + commodity.Kind;

    internal static void AddResources(Dictionary<string, object?> fields, TradeObservation before, TradeObservation after)
    {
        AddMoney(fields, before, after);
        AddStock(fields, before, after);
    }

    private static void AddMoney(Dictionary<string, object?> fields, TradeObservation before, TradeObservation after)
    {
        fields["MoneyBeforeCents"] = before.Money;
        fields["MoneyAfterCents"] = after.Money;
        fields["AvailableMoneyBeforeCents"] = before.AvailableMoney;
        fields["AvailableMoneyAfterCents"] = after.AvailableMoney;
        fields["FrozenMoneyBeforeCents"] = before.FrozenMoney;
        fields["FrozenMoneyAfterCents"] = after.FrozenMoney;
    }

    private static void AddStock(Dictionary<string, object?> fields, TradeObservation before, TradeObservation after)
    {
        fields["StockBefore"] = before.Stock;
        fields["StockAfter"] = after.Stock;
        fields["AvailableStockBefore"] = before.AvailableStock;
        fields["AvailableStockAfter"] = after.AvailableStock;
        fields["FrozenStockBefore"] = before.FrozenStock;
        fields["FrozenStockAfter"] = after.FrozenStock;
    }
}

internal readonly record struct TradeObservation(
    int Money, int AvailableMoney, int FrozenMoney, int? Stock, int? AvailableStock, int? FrozenStock);
