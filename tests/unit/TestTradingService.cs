using System;
using Godot;
using FarmExchange.Economy;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;
using FarmExchange.Time;
using FarmExchange.Trading;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public partial class TestTradingService : Node
{
    public override void _Ready()
    {
        GetTree().Quit(RunChecks() ? 0 : 1);
    }

    public static bool RunChecks() => CheckCommodityTrades() && CheckFailures() &&
        CheckCapacities() && CheckAllProducts() && CheckCurrentQuote() && CheckGameplayPhases() &&
        CheckOrderFeesAndReserves() && CheckOrderFailuresAndCapacities();

    private static bool CheckCommodityTrades()
    {
        var inventory = new GoodsInventory();
        var wallet = new Wallet(100000);
        var market = new MarketQuotes(12345);
        var trading = new TradingService(inventory, wallet, market);
        foreach (CommodityDefinition commodity in CommodityCatalog.All)
        {
            int before = wallet.BalanceCents;
            int price = market.GetQuote(commodity.Id).PriceCents;
            TradeResult buy = trading.Buy(commodity.Id, 3);
            if (!buy.Success || buy.Quantity != 3 || buy.TotalCents != price * 3 || buy.FeeCents != 0 ||
                inventory.Get(commodity.Id) != 3 || wallet.BalanceCents != before - price * 3)
                return Fail("十四种商品未按同一当前报价买入公共库存");
            TradeResult sell = trading.Sell(commodity.Id, 1);
            if (!sell.Success || sell.Quantity != 1 || sell.TotalCents != price || sell.FeeCents != 0 ||
                inventory.Get(commodity.Id) != 2 || wallet.BalanceCents != before - price * 2)
                return Fail("指定数量卖出不守恒");
            TradeResult all = trading.SellAll(commodity.Id);
            if (!all.Success || all.Quantity != 2 || all.TotalCents != price * 2 ||
                inventory.Get(commodity.Id) != 0 || wallet.BalanceCents != before ||
                trading.SellAll(commodity.Id) != new TradeResult(TradeFailure.None, 0, 0))
                return Fail("单商品全售或空库存全售结果错误");
        }
        foreach (CommodityDefinition commodity in CommodityCatalog.All)
            if (inventory.Get(commodity.Id) != 0)
                return Fail("交易改变了其他商品的库存");
        return true;
    }

    private static bool CheckFailures()
    {
        CommodityId raw = new(CropKind.Radish, CommodityKind.Raw);
        var inventory = new GoodsInventory();
        inventory.Add(raw, 2);
        var wallet = new Wallet(25);
        var trading = new TradingService(inventory, wallet, new MarketQuotes(12345));
        CommodityId[] invalid = { new((CropKind)(-1), CommodityKind.Raw),
            new((CropKind)999, CommodityKind.Product), new(CropKind.Wheat, (CommodityKind)999) };
        foreach (CommodityId commodity in invalid)
            if (!Rejected(trading.Buy(commodity, 1), TradeFailure.InvalidCommodity) ||
                !Rejected(trading.Sell(commodity, 1), TradeFailure.InvalidCommodity) ||
                !Rejected(trading.SellAll(commodity), TradeFailure.InvalidCommodity))
                return Fail("非法商品未正常拒绝");
        foreach (int quantity in new[] { 0, -1, int.MinValue })
            if (!Rejected(trading.Buy(raw, quantity), TradeFailure.InvalidQuantity) ||
                !Rejected(trading.Sell(raw, quantity), TradeFailure.InvalidQuantity))
                return Fail("非正整数交易数量未正常拒绝");
        if (!Rejected(trading.Buy(raw, 2), TradeFailure.InsufficientFunds) ||
            !Rejected(trading.Buy(raw, int.MaxValue), TradeFailure.InventoryCapacityExceeded) ||
            !Rejected(trading.Sell(raw, 3), TradeFailure.InsufficientStock) ||
            inventory.Get(raw) != 2 || wallet.BalanceCents != 25)
            return Fail("交易失败改变了金币或公共库存");
        inventory.SetRawReserve(CropKind.Radish, int.MaxValue);
        if (!trading.SellAll(raw).Success || inventory.Get(raw) != 0 ||
            inventory.GetRawReserve(CropKind.Radish) != int.MaxValue)
            return Fail("手动卖出受原料底线限制或清除了底线");
        foreach (TradeFailure failure in Enum.GetValues<TradeFailure>())
        {
            var result = new TradeResult(failure, 0, 0);
            if (result.Success != (failure == TradeFailure.None) ||
                (failure == TradeFailure.None ? result.ErrorMessage != null : string.IsNullOrWhiteSpace(result.ErrorMessage)))
                return Fail("交易结果失败标志或中文反馈缺失");
        }
        return true;
    }

    private static bool CheckCapacities()
    {
        CommodityId raw = new(CropKind.Radish, CommodityKind.Raw);
        var inventory = new GoodsInventory();
        inventory.Add(raw, int.MaxValue - 1);
        var wallet = new Wallet(25);
        var trading = new TradingService(inventory, wallet, new MarketQuotes(12345));
        if (!Rejected(trading.Buy(raw, 2), TradeFailure.InventoryCapacityExceeded) ||
            wallet.BalanceCents != 25 || inventory.Get(raw) != int.MaxValue - 1 ||
            !trading.Buy(raw, 1).Success || wallet.BalanceCents != 0 || inventory.Get(raw) != int.MaxValue)
            return Fail("库存容量边界或精确余额买入错误");
        if (!Rejected(trading.Sell(raw, int.MaxValue), TradeFailure.WalletCapacityExceeded) ||
            wallet.BalanceCents != 0 || inventory.Get(raw) != int.MaxValue)
            return Fail("长整数收入超过钱包容量时未零修改拒绝");
        var empty = new GoodsInventory();
        var rich = new Wallet(int.MaxValue);
        var huge = new TradingService(empty, rich, new MarketQuotes(12345));
        if (!Rejected(huge.Buy(raw, int.MaxValue), TradeFailure.InsufficientFunds) ||
            empty.Get(raw) != 0 || rich.BalanceCents != int.MaxValue)
            return Fail("长整数买入金额溢出为可支付数值");
        empty.Add(raw, 1);
        var nearLimit = new Wallet(int.MaxValue - 25);
        var limitTrading = new TradingService(empty, nearLimit, new MarketQuotes(12345));
        if (!limitTrading.Sell(raw, 1).Success || nearLimit.BalanceCents != int.MaxValue || empty.Get(raw) != 0)
            return Fail("收入恰好到达钱包上限被拒绝");
        empty.Add(raw, 1);
        if (!Rejected(limitTrading.SellAll(raw), TradeFailure.WalletCapacityExceeded) ||
            empty.Get(raw) != 1 || nearLimit.BalanceCents != int.MaxValue)
            return Fail("钱包已满时全售未保持库存");
        return true;
    }

    private static bool CheckAllProducts()
    {
        var inventory = new GoodsInventory();
        var wallet = new Wallet(int.MaxValue);
        var market = new MarketQuotes(12345);
        var trading = new TradingService(inventory, wallet, market);
        if (trading.SellAllProducts() != new TradeResult(TradeFailure.None, 0, 0))
            return Fail("满钱包空库存全售应成功返回零");
        long expectedRevenue = 0;
        long expectedQuantity = 0;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            int quantity = (int)crop.Kind + 1;
            inventory.AddProduct(crop.Kind, quantity);
            inventory.AddRaw(crop.Kind, 9);
            expectedQuantity += quantity;
            expectedRevenue += quantity * market.GetQuote(new CommodityId(crop.Kind, CommodityKind.Product)).PriceCents;
        }
        wallet.TrySpend((int)expectedRevenue - 1);
        int beforeFailedSale = wallet.BalanceCents;
        if (!Rejected(trading.SellAllProducts(), TradeFailure.WalletCapacityExceeded) ||
            wallet.BalanceCents != beforeFailedSale)
            return Fail("全部成品出售未在聚合容量检查时拒绝");
        foreach (CropDefinition crop in FarmGame.Crops)
            if (inventory.GetProduct(crop.Kind) != (int)crop.Kind + 1 || inventory.GetRaw(crop.Kind) != 9)
                return Fail("全售失败发生部分扣库存");
        wallet.TrySpend(1);
        TradeResult sale = trading.SellAllProducts();
        if (!sale.Success || sale.Quantity != expectedQuantity || sale.TotalCents != expectedRevenue ||
            wallet.BalanceCents != int.MaxValue)
            return Fail("全部成品聚合收入或数量不守恒");
        foreach (CropDefinition crop in FarmGame.Crops)
            if (inventory.GetProduct(crop.Kind) != 0 || inventory.GetRaw(crop.Kind) != 9)
                return Fail("成品全售未清空成品或影响了原料");
        inventory.AddProduct(CropKind.Wheat, int.MaxValue);
        inventory.AddProduct(CropKind.Corn, int.MaxValue);
        if (!Rejected(trading.SellAllProducts(), TradeFailure.WalletCapacityExceeded) ||
            inventory.GetProduct(CropKind.Wheat) != int.MaxValue || inventory.GetProduct(CropKind.Corn) != int.MaxValue)
            return Fail("多商品累计超过整数上限后未完整拒绝");
        return true;
    }

    private static bool CheckCurrentQuote()
    {
        CommodityId raw = new(CropKind.Radish, CommodityKind.Raw);
        var inventory = new GoodsInventory();
        var wallet = new Wallet(10000);
        var market = new MarketQuotes(12345);
        var trading = new TradingService(inventory, wallet, market);
        int oldPrice = market.GetQuote(raw).PriceCents;
        trading.Buy(raw, 3);
        market.Advance(new GameCalendar(720).Snapshot);
        CommodityId selected = raw;
        bool foundChange = market.GetQuote(raw).PriceCents != oldPrice;
        foreach (CommodityDefinition commodity in CommodityCatalog.All)
            if (market.GetQuote(commodity.Id).PriceCents != commodity.InitialPriceCents)
            {
                selected = commodity.Id;
                foundChange = true;
                break;
            }
        if (!foundChange)
            return Fail("固定市场种子未覆盖报价变化");
        int price = market.GetQuote(selected).PriceCents;
        int before = wallet.BalanceCents;
        TradeResult buy = trading.Buy(selected, 2);
        TradeResult sell = trading.Sell(selected, 1);
        if (buy.TotalCents != price * 2 || sell.TotalCents != price || wallet.BalanceCents != before - price)
            return Fail("报价更新后的执行仍使用旧报价");
        int rawPrice = market.GetQuote(raw).PriceCents;
        int rawStock = inventory.Get(raw);
        TradeResult oldStockSale = trading.SellAll(raw);
        if (oldStockSale.TotalCents != (long)rawStock * rawPrice)
            return Fail("历史买入库存未按成交时当前价出售");
        return true;
    }

    private static bool CheckGameplayPhases()
    {
        var game = new FarmGame(12345);
        foreach (Vector2I cell in new[] { new Vector2I(189, 189), new(192, 189), new(195, 189), new(189, 192), new(192, 192) })
            game.RemoveBuilding(cell);
        CommodityId raw = new(CropKind.Radish, CommodityKind.Raw);
        CommodityId product = new(CropKind.Radish, CommodityKind.Product);
        Vector2I first = new(0, 0);
        Vector2I second = new(3, 0);
        game.BuildProcessor(first, CropKind.Radish);
        game.SetRawReserve(CropKind.Radish, 1);
        game.SetPaused(true);
        int before = game.MoneyCents;
        if (!game.Buy(raw, 2).Success || !game.Buy(product, 1).Success ||
            game.GetRawStock(CropKind.Radish) != 2 || game.GetProductStock(CropKind.Radish) != 1 ||
            game.GetPlot(first).RemainingSeconds != 0 || game.AdvanceTick() != default ||
            game.Calendar.ElapsedSeconds != 0 || game.GetQuote(raw).PriceCents != 25)
            return Fail("暂停买入未进入同一公共库存或操作触发了经营");
        SaleResult products = game.SellAll();
        if (!products.Success || products.Quantity != 1 || products.RevenueCents != 50 ||
            game.MoneyCents != before - 50)
            return Fail("旧全成品入口未统一到暂停交易结算");
        game.SetPaused(false);
        game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != 1 || game.GetPlot(first).RemainingSeconds != 26)
            return Fail("买入原料未在下一领取阶段按底线开始加工");
        game.SetRawReserve(CropKind.Radish, 0);
        game.SetPaused(true);
        if (game.BuildProcessor(second, CropKind.Radish) != null ||
            game.GetRawStock(CropKind.Radish) != 0 || game.GetPlot(second).RemainingSeconds != 26)
            return Fail("买入原料未走新场地既有即时领取路径");
        if (!Rejected(game.Sell(raw, 1), TradeFailure.InsufficientStock) ||
            game.SellRaw((CropKind)999).Failure != TradeFailure.InvalidCommodity)
            return Fail("已投入原料仍可出售或旧入口非法作物未正常拒绝");
        game.SetPaused(false);
        for (int i = 0; i < 26; i++)
            game.AdvanceTick();
        if (game.GetStock(product) != 2 || game.GetStock(raw) != 0)
            return Fail("买入原料加工后的公共库存产出不守恒");
        return true;
    }

    private static bool Rejected(TradeResult result, TradeFailure failure) =>
        !result.Success && result.Failure == failure && result.Quantity == 0 && result.TotalCents == 0 && result.FeeCents == 0;

    private static bool CheckOrderFeesAndReserves()
    {
        CommodityId raw = new(CropKind.Radish, CommodityKind.Raw);
        var inventory = new GoodsInventory();
        var wallet = new Wallet(126);
        var trading = new TradingService(inventory, wallet, new MarketQuotes(12345));
        TradeResult buy = trading.BuyOrder(raw, 1, 100);
        if (!buy.Success || buy.TotalCents != 25 || buy.FeeCents != 1 || buy.Quantity != 1 ||
            inventory.Get(raw) != 1 || wallet.BalanceCents != 100)
            return Fail("小额委托费用未向上取整到 1 分或精确保留现金买入错误");
        TradeResult sell = trading.SellOrder(raw, 1);
        if (!sell.Success || sell.TotalCents != 25 || sell.FeeCents != 1 || sell.Quantity != 1 ||
            inventory.Get(raw) != 0 || wallet.BalanceCents != 124)
            return Fail("委托卖出未从成交总额扣除费用或往返收支错误");
        if (!Rejected(trading.BuyOrder(raw, 1, 99), TradeFailure.CashReserveNotMet) ||
            inventory.Get(raw) != 0 || wallet.BalanceCents != 124)
            return Fail("现金保留线缺 1 分仍然成交或拒绝后有资源修改");
        wallet.TrySpend(98);
        if (!trading.BuyOrder(raw, 1, 0).Success || wallet.BalanceCents != 0 || inventory.Get(raw) != 1)
            return Fail("含费余额恰足未完整成交");
        if (!trading.SellOrder(raw, 1).Success || wallet.BalanceCents != 24 ||
            !Rejected(trading.BuyOrder(raw, 1, 0), TradeFailure.InsufficientFunds) ||
            wallet.BalanceCents != 24 || inventory.Get(raw) != 0)
            return Fail("含费余额不足时未零修改等待");
        var exact = new TradingService(new GoodsInventory(), new Wallet(1000), new MarketQuotes(12345));
        TradeResult hundred = exact.BuyOrder(raw, 4, 0);
        TradeResult overHundred = exact.BuyOrder(raw, 5, 0);
        if (hundred.TotalCents != 100 || hundred.FeeCents != 1 ||
            overHundred.TotalCents != 125 || overHundred.FeeCents != 2)
            return Fail("整百分金额或非整百分金额的委托手续费错误");
        return true;
    }

    private static bool CheckOrderFailuresAndCapacities()
    {
        CommodityId raw = new(CropKind.Radish, CommodityKind.Raw);
        CommodityId invalid = new(CropKind.Radish, (CommodityKind)999);
        var inventory = new GoodsInventory();
        var wallet = new Wallet(25);
        var trading = new TradingService(inventory, wallet, new MarketQuotes(12345));
        if (!Rejected(trading.BuyOrder(raw, 1, 0), TradeFailure.InsufficientFunds) ||
            !Rejected(trading.BuyOrder(invalid, 1, 0), TradeFailure.InvalidCommodity) ||
            !Rejected(trading.SellOrder(invalid, 1), TradeFailure.InvalidCommodity) ||
            !Rejected(trading.BuyOrder(raw, 0, 0), TradeFailure.InvalidQuantity) ||
            !Rejected(trading.SellOrder(raw, -1), TradeFailure.InvalidQuantity) ||
            !Rejected(trading.SellOrder(raw, 1), TradeFailure.InsufficientStock) ||
            wallet.BalanceCents != 25 || inventory.Get(raw) != 0)
            return Fail("委托非法输入或含费余额缺 1 分未零修改拒绝");
        try
        {
            trading.BuyOrder(raw, 1, -1);
            return Fail("委托现金保留金额允许负数");
        }
        catch (ArgumentOutOfRangeException) { }
        inventory.Add(raw, int.MaxValue - 1);
        if (!Rejected(trading.BuyOrder(raw, 2, 0), TradeFailure.InventoryCapacityExceeded) ||
            !Rejected(trading.SellOrder(raw, int.MaxValue - 1), TradeFailure.WalletCapacityExceeded) ||
            wallet.BalanceCents != 25 || inventory.Get(raw) != int.MaxValue - 1)
            return Fail("委托库存容量或巨大卖出收入未零修改拒绝");
        var empty = new GoodsInventory();
        var rich = new Wallet(int.MaxValue);
        var huge = new TradingService(empty, rich, new MarketQuotes(12345));
        if (!Rejected(huge.BuyOrder(raw, int.MaxValue, 0), TradeFailure.InsufficientFunds) ||
            rich.BalanceCents != int.MaxValue || empty.Get(raw) != 0)
            return Fail("巨大委托含费金额未用宽整数预检");
        empty.Add(raw, 1);
        var nearLimit = new Wallet(int.MaxValue - 24);
        var limited = new TradingService(empty, nearLimit, new MarketQuotes(12345));
        TradeResult exactSale = limited.SellOrder(raw, 1);
        if (!exactSale.Success || exactSale.TotalCents != 25 || exactSale.FeeCents != 1 ||
            nearLimit.BalanceCents != int.MaxValue || empty.Get(raw) != 0)
            return Fail("委托卖出未按净收入检查钱包上限");
        empty.Add(raw, 1);
        if (!Rejected(limited.SellOrder(raw, 1), TradeFailure.WalletCapacityExceeded) ||
            empty.Get(raw) != 1 || nearLimit.BalanceCents != int.MaxValue)
            return Fail("满钱包委托卖出失败改变了资源");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
