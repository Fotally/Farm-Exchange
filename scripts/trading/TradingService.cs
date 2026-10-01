using FarmExchange.Economy;
using FarmExchange.Farming;
using FarmExchange.Inventory;
using FarmExchange.Market;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Trading;

internal sealed class TradingService
{
    private readonly GoodsInventory _inventory;
    private readonly Wallet _wallet;
    private readonly MarketQuotes _market;

    internal TradingService(GoodsInventory inventory, Wallet wallet, MarketQuotes market)
    {
        _inventory = inventory;
        _wallet = wallet;
        _market = market;
    }

    internal TradeResult Buy(CommodityId commodity, int quantity)
    {
        if (!commodity.IsDefined)
            return Failed(TradeFailure.InvalidCommodity);
        if (quantity <= 0)
            return Failed(TradeFailure.InvalidQuantity);
        if (quantity > int.MaxValue - _inventory.Get(commodity))
            return Failed(TradeFailure.InventoryCapacityExceeded);
        long totalCents = (long)quantity * _market.GetQuote(commodity).PriceCents;
        if (totalCents > _wallet.BalanceCents)
            return Failed(TradeFailure.InsufficientFunds);
        _wallet.TrySpend((int)totalCents);
        _inventory.Add(commodity, quantity);
        return new TradeResult(TradeFailure.None, quantity, totalCents);
    }

    internal TradeResult Sell(CommodityId commodity, int quantity)
    {
        if (!commodity.IsDefined)
            return Failed(TradeFailure.InvalidCommodity);
        if (quantity <= 0)
            return Failed(TradeFailure.InvalidQuantity);
        if (quantity > _inventory.Get(commodity))
            return Failed(TradeFailure.InsufficientStock);
        long totalCents = (long)quantity * _market.GetQuote(commodity).PriceCents;
        if (totalCents > int.MaxValue - _wallet.BalanceCents)
            return Failed(TradeFailure.WalletCapacityExceeded);
        _inventory.Remove(commodity, quantity);
        _wallet.Credit((int)totalCents);
        return new TradeResult(TradeFailure.None, quantity, totalCents);
    }

    internal TradeResult SellAll(CommodityId commodity)
    {
        if (!commodity.IsDefined)
            return Failed(TradeFailure.InvalidCommodity);
        int quantity = _inventory.Get(commodity);
        return quantity == 0 ? new TradeResult(TradeFailure.None, 0, 0) : Sell(commodity, quantity);
    }

    internal TradeResult SellAllProducts()
    {
        var quantities = new int[CropCatalog.Crops.Count];
        long sold = 0;
        long totalCents = 0;
        foreach (var crop in CropCatalog.Crops)
        {
            CommodityId commodity = new(crop.Kind, CommodityKind.Product);
            int quantity = _inventory.Get(commodity);
            quantities[(int)crop.Kind] = quantity;
            sold += quantity;
            totalCents += (long)quantity * _market.GetQuote(commodity).PriceCents;
        }
        if (totalCents > int.MaxValue - _wallet.BalanceCents)
            return Failed(TradeFailure.WalletCapacityExceeded);
        foreach (var crop in CropCatalog.Crops)
            _inventory.Remove(new CommodityId(crop.Kind, CommodityKind.Product), quantities[(int)crop.Kind]);
        _wallet.Credit((int)totalCents);
        return new TradeResult(TradeFailure.None, sold, totalCents);
    }

    private static TradeResult Failed(TradeFailure failure) => new(failure, 0, 0);
}
