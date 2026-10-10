using System;
using FarmExchange.Economy;
using FarmExchange.Farming;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Market;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Trading;

internal sealed class TradingService
{
    private readonly GoodsInventory _inventory;
    private readonly Wallet _wallet;
    private readonly MarketQuotes _market;

    private TradingLog? _log;

    /**
     * <summary>接入本局交易观察，不改变结算依赖或规则。</summary>
     * <param name="log">本局领域入口；null 关闭观察。</param>
     */
    internal void SetLogging(TradingLog? log) => _log = log;

    internal TradingService(GoodsInventory inventory, Wallet wallet, MarketQuotes market)
    {
        _inventory = inventory;
        _wallet = wallet;
        _market = market;
    }

    internal TradeResult Buy(CommodityId commodity, int quantity) => BuyCore(commodity, quantity, 0, false, 0);

    /**
     * <summary>按执行时报价完整买入委托商品，并收取向上取整到分的 1% 手续费。</summary>
     * <param name="commodity">十四种合法商品之一。</param>
     * <param name="quantity">完整买入数量，须为正整数。</param>
     * <param name="reserveCents">设单时锁定的非负现金保留金额，单位为分。</param>
     * <param name="frozenCents">本单拥有的冻结金额，持续单为零。</param>
     * <returns>真实成交总额与手续费，或零修改的正常拒绝。</returns>
     * <remarks>费用计入支出；成交后余额不得低于现金保留金额。</remarks>
     */
    internal TradeResult BuyOrder(CommodityId commodity, int quantity, int reserveCents, int frozenCents = 0)
    {
        if (reserveCents < 0)
            throw new ArgumentOutOfRangeException(nameof(reserveCents));
        return BuyCore(commodity, quantity, reserveCents, true, frozenCents);
    }

    private TradeResult BuyCore(CommodityId commodity, int quantity, int reserveCents, bool chargeFee, int frozenCents)
    {
        TradeRuleObservation? checks = _log?.BeginRuleCheck(commodity, true, chargeFee);
        bool commodityRejected = !commodity.IsDefined;
        checks?.Checked(TradeRuleCheck.Commodity, !commodityRejected);
        if (commodityRejected)
            return Failed(TradeFailure.InvalidCommodity);
        bool quantityRejected = quantity <= 0;
        checks?.Checked(TradeRuleCheck.Quantity, !quantityRejected);
        if (quantityRejected)
            return Failed(TradeFailure.InvalidQuantity);
        bool capacityRejected = quantity > int.MaxValue - _inventory.Get(commodity);
        checks?.Checked(TradeRuleCheck.Capacity, !capacityRejected);
        if (capacityRejected)
            return Failed(TradeFailure.InventoryCapacityExceeded);
        int unitPriceCents = _market.GetQuote(commodity).PriceCents;
        long totalCents = (long)quantity * unitPriceCents;
        long feeCents = chargeFee ? (totalCents + 99) / 100 : 0;
        long expenseCents = totalCents + feeCents;
        long availableCents = (long)_wallet.AvailableCents + frozenCents;
        bool fundsRejected = expenseCents > availableCents;
        checks?.Checked(TradeRuleCheck.Funds, !fundsRejected);
        if (fundsRejected)
            return Failed(TradeFailure.InsufficientFunds, unitPriceCents);
        bool reserveRejected = availableCents - expenseCents < reserveCents;
        checks?.Checked(TradeRuleCheck.Reserve, !reserveRejected);
        if (reserveRejected)
            return Failed(TradeFailure.CashReserveNotMet, unitPriceCents);
        checks?.Accepted();
        _wallet.SpendForOrder((int)expenseCents, frozenCents);
        _inventory.Add(commodity, quantity);
        return new TradeResult(TradeFailure.None, quantity, totalCents)
        {
            FeeCents = feeCents,
            UnitPriceCents = unitPriceCents,
        };
    }

    internal TradeResult Sell(CommodityId commodity, int quantity) => SellCore(commodity, quantity, false, 0);

    /**
     * <summary>按执行时报价完整卖出委托商品，并从收入扣除向上取整到分的 1% 手续费。</summary>
     * <param name="commodity">十四种合法商品之一。</param>
     * <param name="quantity">完整卖出数量，须为正整数。</param>
     * <param name="frozenQuantity">本单拥有的冻结数量，持续单为零。</param>
     * <returns>真实成交总额与手续费，或零修改的正常拒绝。</returns>
     * <remarks>仅成功成交收费；先检查库存和净收入容量，再一次提交。</remarks>
     */
    internal TradeResult SellOrder(CommodityId commodity, int quantity, int frozenQuantity = 0) =>
        SellCore(commodity, quantity, true, frozenQuantity);

    private TradeResult SellCore(CommodityId commodity, int quantity, bool chargeFee, int frozenQuantity,
        TradeRuleScope scope = TradeRuleScope.Fixed)
    {
        TradeRuleObservation? checks = _log?.BeginRuleCheck(commodity, false, chargeFee, scope);
        bool commodityRejected = !commodity.IsDefined;
        checks?.Checked(TradeRuleCheck.Commodity, !commodityRejected);
        if (commodityRejected)
            return Failed(TradeFailure.InvalidCommodity);
        bool quantityRejected = quantity <= 0;
        checks?.Checked(TradeRuleCheck.Quantity, !quantityRejected);
        if (quantityRejected)
            return Failed(TradeFailure.InvalidQuantity);
        bool stockRejected = quantity > (long)_inventory.GetAvailable(commodity) + frozenQuantity;
        checks?.Checked(TradeRuleCheck.Stock, !stockRejected);
        if (stockRejected)
            return Failed(TradeFailure.InsufficientStock);
        int unitPriceCents = _market.GetQuote(commodity).PriceCents;
        long totalCents = (long)quantity * unitPriceCents;
        long feeCents = chargeFee ? (totalCents + 99) / 100 : 0;
        long incomeCents = totalCents - feeCents;
        bool capacityRejected = incomeCents > int.MaxValue - _wallet.BalanceCents;
        checks?.Checked(TradeRuleCheck.Capacity, !capacityRejected);
        if (capacityRejected)
            return Failed(TradeFailure.WalletCapacityExceeded, unitPriceCents);
        checks?.Accepted();
        _inventory.RemoveForOrder(commodity, quantity, frozenQuantity);
        _wallet.Credit((int)incomeCents);
        return new TradeResult(TradeFailure.None, quantity, totalCents) { FeeCents = feeCents, UnitPriceCents = unitPriceCents };
    }

    internal TradeResult SellAll(CommodityId commodity)
    {
        if (!commodity.IsDefined)
        {
            _log?.BeginRuleCheck(commodity, false, false, TradeRuleScope.AllCommodity)?.Checked(TradeRuleCheck.Commodity, false);
            return Failed(TradeFailure.InvalidCommodity);
        }
        int quantity = _inventory.GetAvailable(commodity);
        if (quantity == 0)
        {
            TradeRuleObservation? checks = _log?.BeginRuleCheck(commodity, false, false, TradeRuleScope.AllCommodity);
            checks?.Checked(TradeRuleCheck.Commodity, true);
            checks?.Accepted();
            return new TradeResult(TradeFailure.None, 0, 0);
        }
        return SellCore(commodity, quantity, false, 0, TradeRuleScope.AllCommodity);
    }

    internal TradeResult SellAllProducts() => SellAllProductsDetailed().Trade;

    /**
     * <summary>完整结算全部加工品并返回原汇总过程取得的七商品实际明细。</summary>
     * <returns>真实合计和只读明细；容量拒绝保持资源不变、逐行实际数量与货值为零。</returns>
     */
    internal ProductSaleResult SellAllProductsDetailed()
    {
        var quantities = new int[CropCatalog.Crops.Count];
        var prices = new int[CropCatalog.Crops.Count];
        long sold = 0;
        long totalCents = 0;
        foreach (var crop in CropCatalog.Crops)
        {
            CommodityId commodity = new(crop.Kind, CommodityKind.Product);
            int quantity = _inventory.GetAvailable(commodity);
            quantities[(int)crop.Kind] = quantity;
            sold += quantity;
            int unitPriceCents = _market.GetQuote(commodity).PriceCents;
            prices[(int)crop.Kind] = unitPriceCents;
            totalCents += (long)quantity * unitPriceCents;
        }
        var lines = new TradeLineResult[CropCatalog.Crops.Count];
        bool rejected = totalCents > int.MaxValue - _wallet.BalanceCents;
        TradeRuleObservation? checks = _log?.BeginRuleCheck(null, false, false, TradeRuleScope.AllProducts);
        checks?.Checked(TradeRuleCheck.Capacity, !rejected);
        if (!rejected) checks?.Accepted();
        foreach (var crop in CropCatalog.Crops)
        {
            int index = (int)crop.Kind;
            int quantity = rejected ? 0 : quantities[index];
            lines[index] = new(new(crop.Kind, CommodityKind.Product), quantity, prices[index], (long)quantity * prices[index]);
        }
        if (rejected)
            return new(Failed(TradeFailure.WalletCapacityExceeded), Array.AsReadOnly(lines));
        foreach (var crop in CropCatalog.Crops)
            _inventory.Remove(new CommodityId(crop.Kind, CommodityKind.Product), quantities[(int)crop.Kind]);
        _wallet.Credit((int)totalCents);
        return new(new TradeResult(TradeFailure.None, sold, totalCents), Array.AsReadOnly(lines));
    }

    private static TradeResult Failed(TradeFailure failure, int? unitPriceCents = null) =>
        new(failure, 0, 0) { UnitPriceCents = unitPriceCents };
}
