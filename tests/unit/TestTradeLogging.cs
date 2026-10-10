using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using FarmExchange.Economy;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Market;
using FarmExchange.Trading;
using GoodsInventory = FarmExchange.Inventory.Inventory;
using static TestLogging;

public static class TestTradeLogging
{
    private static readonly CommodityId Raw = new(CropKind.Radish, CommodityKind.Raw);

    public static bool RunChecks()
    {
        try
        {
            CheckSingleSales();
            CheckProducts();
            CheckExecutedSaleFacts();
            CheckOriginsAndDisabledParity();
            CheckMergedTruncation();
            CheckFileEvidence();
            return true;
        }
        catch (Exception error) { GD.PrintErr("完整主动交易日志测试失败：" + error); return false; }
    }

    private static TradeOrderRequest WaitingOrder(CommodityId commodity, TradeOrderSide side, int limit = 1) => new(
        commodity, side, TradeOrderFrequency.Once, TradeOrderQuantityMode.Fixed, 1,
        side == TradeOrderSide.Buy ? TradeOrderBudgetMode.LimitPrice : TradeOrderBudgetMode.None,
        0, limit, CashReserveMode.Amount, 0,
        new IReadOnlyList<TradeOrderCondition>[]
        {
            new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.GreaterOrEqual, 0) },
        });

    private static void CheckSingleSales()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(17, logging);
        Require(game.Buy(Raw, 4).Success, "准备原料失败");
        int price = game.GetQuote(Raw).PriceCents;
        TradeResult fixedSale = game.Sell(Raw, 1, CommandOrigin.Scenario);
        Require(fixedSale.Success && fixedSale.UnitPriceCents == price, "固定卖出未返回实际价");
        Require(!game.Sell(Raw, -2).Success && !game.Sell(Raw, 10).Success &&
            !game.Sell(new((CropKind)999, CommodityKind.Product), 1).Success, "非法卖出未拒绝");
        int orderId = game.CreateTradeOrder(WaitingOrder(Raw, TradeOrderSide.Sell)).Id;
        Require(game.GetFrozenStock(Raw) == 1, "卖单未冻结夹具库存");
        TradeResult all = game.SellCommodityAll(Raw);
        Require(all.Success && all.Quantity == 2 && game.GetStock(Raw) == 1 && game.GetAvailableStock(Raw) == 0,
            "单商品全售动用冻结库存");
        Require(game.SellCommodityAll(Raw).Quantity == 0, "空可用库存全售未成功返回零");
        Require(game.CancelTradeOrder(orderId).Success && game.SellRaw(CropKind.Radish).Quantity == 1, "旧原料入口未复用单商品全售");
        Require(game.MoneyCents == 5000, "主动买卖不是即时同价零费");
        string[] sales = Lines(text.ToString()).Where(line => HasEvent(line, "TradeFinished") && line.Contains("Side: \"Sell\"")).ToArray();
        Require(sales.Length == 7 && sales[0].Contains("CommandOrigin: \"Scenario\"") &&
            sales[0].Contains("RequestedQuantity: 1") && sales[0].Contains("UnitPriceCents: " + price), "固定卖出字段或来源错误");
        Require(!sales[1].Contains("UnitPriceCents:") && !sales[2].Contains("UnitPriceCents:") &&
            sales[3].Contains("StockBefore: null"), "报价前拒绝伪造价格或非法商品库存");
        Require(sales[4].Contains("RequestMode: \"AllCommodity\"") && !sales[4].Contains("RequestedQuantity:") &&
            sales[4].Contains("StockBefore: 3") && sales[4].Contains("StockAfter: 1") &&
            sales[4].Contains("FrozenStockBefore: 1") && sales[4].Contains("FrozenStockAfter: 1"), "全售请求或冻结边界错误");
        Require(sales[5].Contains("Quantity: 0") && !sales[5].Contains("UnitPriceCents:"), "空库存全售补查了未读取单价");
        foreach (string line in Lines(text.ToString()).Where(line => HasEvent(line, "CommandFinished")))
            Require(!line.Contains("ValueCents:") && !line.Contains("StockBefore:"), "通用命令结束重复记账");
    }

    private static void CheckProducts()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(17, logging);
        long total = 0;
        foreach (var definition in CommodityCatalog.All.Where(item => item.Id.Kind == CommodityKind.Product))
        {
            TradeResult bought = game.Buy(definition.Id, 1);
            Require(bought.Success, "七加工品夹具买入失败");
            total += bought.TotalCents;
        }
        CommodityId wheat = new(CropKind.Wheat, CommodityKind.Product);
        int frozenOrder = game.CreateTradeOrder(WaitingOrder(wheat, TradeOrderSide.Sell)).Id;
        int frozenPrice = game.GetQuote(wheat).PriceCents;
        SaleResult first = game.SellAll(CommandOrigin.Scenario);
        Require(first.Success && first.Quantity == 6 && first.RevenueCents == total - frozenPrice && game.GetStock(wheat) == 1,
            "全加工品出售没有保留冻结品或合计错误");
        game.CancelTradeOrder(frozenOrder);
        Require(game.SellAll().Quantity == 1 && game.SellAll().Quantity == 0 && game.MoneyCents == 5000, "全加工品后续出售不守恒");
        string[] sales = Lines(text.ToString()).Where(line => HasEvent(line, "AllProductsSold")).ToArray();
        Require(sales.Length == 3 && sales[0].Contains("CommandName: \"SellAllProducts\"") &&
            sales[0].Contains("CommandOrigin: \"Scenario\"") && !sales[0].Contains("RequestedQuantity:"), "批量全售命令关联错误");
        Require(sales.All(line => Regex.Matches(line, "Commodity: ").Count == 7 && Regex.Matches(line, "UnitPriceCents:").Count == 7),
            "批量全售没有保留七条明细或伪造聚合单价");
        Require(sales[0].Contains("Commodity: \"Wheat.Product\", Quantity: 0, UnitPriceCents: " + frozenPrice) &&
            sales[0].Contains("ValueCents: 0, FeeCents: 0, StockBefore: 1, StockAfter: 1, AvailableStockBefore: 0, AvailableStockAfter: 0, FrozenStockBefore: 1, FrozenStockAfter: 1"),
            "冻结商品零成交行没有反映真实库存");
        Require(Lines(text.ToString()).Count(line => HasEvent(line, "TradeFinished")) == 7, "批量全售额外输出逐笔收支事件");
    }

    private static void CheckExecutedSaleFacts()
    {
        var inventory = new GoodsInventory();
        var wallet = new Wallet(int.MaxValue);
        var market = new MarketQuotes(17);
        var service = new TradingService(inventory, wallet, market);
        inventory.Add(Raw, 2);
        TradeResult capacity = service.Sell(Raw, 1);
        Require(capacity.Failure == TradeFailure.WalletCapacityExceeded && capacity.UnitPriceCents == market.GetQuote(Raw).PriceCents &&
            inventory.Get(Raw) == 2 && wallet.BalanceCents == int.MaxValue, "卖出容量拒绝丢失真实价或修改资源");
        wallet.TrySpend(100);
        TradeResult order = service.SellOrder(Raw, 1);
        Require(order.Success && order.UnitPriceCents == market.GetQuote(Raw).PriceCents && order.FeeCents == 1, "委托卖出丢失执行价/费用");
        CommodityId product = new(CropKind.Wheat, CommodityKind.Product);
        inventory.Add(product, 1);
        ProductSaleResult failed = service.SellAllProductsDetailed();
        Require(failed.Trade.Failure == TradeFailure.WalletCapacityExceeded && failed.Lines.Count == 7 &&
            failed.Lines.All(line => line.Quantity == 0 && line.ValueCents == 0 && line.UnitPriceCents > 0) && inventory.Get(product) == 1,
            "全售拒绝把候选数量当成交或没有保留实际价");
        wallet.TrySpend(1000);
        ProductSaleResult sold = service.SellAllProductsDetailed();
        Require(sold.Trade.Success && sold.Trade.Quantity == 1 && sold.Lines.Sum(line => line.ValueCents) == sold.Trade.TotalCents,
            "真实批量明细与原子合计不一致");
    }

    private static void CheckOriginsAndDisabledParity()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var traced = new FarmGame(17, log);
        using var plain = new FarmGame(17);
        foreach (FarmGame game in new[] { traced, plain })
        {
            game.Buy(Raw, 4);
            game.Sell(Raw, 1);
            game.SellCommodityAll(Raw);
            game.SellAll();
        }
        Require(traced.MoneyCents == plain.MoneyCents && traced.GetStock(Raw) == plain.GetStock(Raw), "日志改变主动交易资源");
        int length = text.ToString().Length;
        Action[] invalid =
        {
            () => traced.Sell(Raw, 1, (CommandOrigin)88),
            () => traced.SellCommodityAll(Raw, (CommandOrigin)88),
            () => traced.SellAll((CommandOrigin)88),
            () => traced.CreateTradeOrder(WaitingOrder(Raw, TradeOrderSide.Buy), (CommandOrigin)88),
            () => traced.UpdateTradeOrder(1, WaitingOrder(Raw, TradeOrderSide.Buy), (CommandOrigin)88),
            () => traced.CancelTradeOrder(1, (CommandOrigin)88),
            () => traced.SetTradeOrderEnabled(1, true, (CommandOrigin)88),
        };
        foreach (Action operation in invalid)
        {
            try { operation(); Require(false, "非法命令来源被接受"); }
            catch (ArgumentOutOfRangeException) { }
        }
        Require(text.ToString().Length == length && traced.MoneyCents == plain.MoneyCents, "非法来源记录或改变资源");
    }

    private static void CheckMergedTruncation()
    {
        using var text = new StringWriter();
        using var log = LogOutput.Capture(text);
        log.Submit(new(9101, "TestSampledProjection", "Tests.Logging"), new string('摘', 300), new()
        {
            ["Samples"] = new[] { 1, 2 },
            ["Label"] = new string('中', 600),
            ["Truncated"] = true,
            ["TruncatedFields"] = new[] { "Samples" },
            ["TruncatedOriginalCounts"] = new Dictionary<string, object?>
            {
                ["Samples"] = new Dictionary<string, object?> { ["ItemCount"] = 90L },
            },
        });
        string line = Lines(text.ToString()).Single();
        Require(Regex.Matches(line, "Truncated: true").Count == 1 && Regex.Matches(line, "TruncatedFields:").Count == 1 &&
            Regex.Matches(line, "TruncatedOriginalCounts:").Count == 1 && line.Contains("Samples: { ItemCount: 90 }") &&
            line.Contains("Message: { ScalarCount: 300 }") && line.Contains("Label: { ScalarCount: 600 }"),
            "集合与文本截断元数据重复或丢失原数量");
        Require(log.Health.Health == LoggingHealth.Healthy && HasEvent(line, "TestSampledProjection"), "合法采样事件未完整输出");
    }

    private static void CheckFileEvidence()
    {
        string root = ProjectSettings.GlobalizePath("res://build/test-results/logging/trading-" + Guid.NewGuid().ToString("N"));
        using (var log = RuntimeLog.OpenFile(root, true, new LogEnvironment(BuildKind: "Debug")))
        using (var game = new FarmGame(17, log))
        {
            foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
            Require(game.Buy(Raw, 4).Success && game.Sell(Raw, 1).Success && game.SellAll().Success, "文件夹具主动交易失败");
            TradeOrderRequest request = WaitingOrder(Raw, TradeOrderSide.Buy);
            int id = game.CreateTradeOrder(request).Id;
            game.AdvanceTicks(1);
            Require(game.UpdateTradeOrder(id, request with { LimitPriceCents = 100 }).Success, "文件夹具编辑失败");
            game.AdvanceTicks(1);
            int cancelled = game.CreateTradeOrder(request).Id;
            game.AdvanceTicks(1);
            game.CancelTradeOrder(cancelled);
            game.AdvanceTicks(2000);
            Require(log.Health.Health == LoggingHealth.Healthy, "完整业务 File 采集故障");
        }
        string[] Read(string subdirectory) => Directory.GetFiles(Path.Combine(root, subdirectory), "*.log")
            .OrderBy(path => path, StringComparer.Ordinal).SelectMany(File.ReadAllLines).ToArray();
        string[] runtime = Read("runtime"), debug = Read("debug");
        foreach (string name in new[] { "TradeFinished", "AllProductsSold", "OrderWaitChanged", "OrderFilled", "OrderCancelled", "NewsPublished", "QuoteUpdated", "GameEnded", "SessionEnded" })
            Require(runtime.Any(line => HasEvent(line, name)), "File 真实业务样例缺少 " + name);
        Require(runtime.SequenceEqual(debug) && runtime.All(line => line.Contains("SchemaVersion=1")), "File 双目标核心字段不一致");
        AssertSequence(runtime);
        Require(runtime.Select(line => Field(line, "Sequence")).Distinct().Count() == runtime.Length, "File 同一事实重复序号");
        GD.Print("完整交易订单行情真实 File 样例：" + root);
    }
}
