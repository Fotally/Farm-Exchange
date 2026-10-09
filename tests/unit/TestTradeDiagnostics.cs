using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FarmExchange.Economy;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Market;
using FarmExchange.Trading;
using Godot;
using static TestLogging;

public static class TestTradeDiagnostics
{
    private static readonly CommodityId Raw = new(CropKind.Radish, CommodityKind.Raw);
    private static readonly TradeOrderCondition TruePrice = new(TradeConditionFactor.Price, TradeConditionComparison.GreaterOrEqual, 0);
    private static readonly TradeOrderCondition FalsePrice = new(TradeConditionFactor.Price, TradeConditionComparison.Less, 0);

    public static bool RunChecks()
    {
        try
        {
            CheckRealTradeBranches();
            CheckAllSaleRules();
            CheckOrderSamplesAndShortCircuit();
            CheckCalculatedQuotes();
            CheckProfilesAndProvider();
            return true;
        }
        catch (Exception error) { GD.PrintErr("交易专项诊断检查失败：" + error); return false; }
    }

    private static void CheckRealTradeBranches()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(73, logging);
        RemoveFacilities(game);
        game.Buy(Raw, 1);
        Require(Events(text, "RuleChecked").Length == 0, "未选择专项仍构造交易明细");
        Require(game.Log!.Diagnostics.Start(new(new[] { "RuleChecked" }, IncludeGameEvents: true)), "无法选择全局交易检查");
        game.Buy(new CommodityId((CropKind)99, CommodityKind.Raw), 1);
        game.Buy(Raw, 0);
        game.Buy(Raw, int.MaxValue);
        game.Buy(Raw, 1000000);
        game.Buy(Raw, 1);
        game.Sell(Raw, 0);
        game.Sell(Raw, 3);
        game.Sell(Raw, 1);
        string[] checks = Events(text, "RuleChecked");
        Require(checks.Length == 8, "真实交易检查没有逐次唯一记录");
        string[] expected =
        {
            "{ Commodity: false }", "{ Commodity: true, Quantity: false }",
            "{ Commodity: true, Quantity: true, Capacity: false }",
            "{ Commodity: true, Quantity: true, Capacity: true, Funds: false }",
            "{ Commodity: true, Quantity: true, Capacity: true, Funds: true, Reserve: true }",
            "{ Commodity: true, Quantity: false }",
            "{ Commodity: true, Quantity: true, Stock: false }",
            "{ Commodity: true, Quantity: true, Stock: true, Capacity: true }",
        };
        for (int index = 0; index < checks.Length; index++)
            Require(checks[index].Contains("CheckResults: " + expected[index]), "失败后补跑了检查或实际顺序变化");
        Require(checks.All(line => line.Contains("RuleKind: \"Trade\"") && line.Contains("CaptureId:")), "交易明细未关联捕获");
        game.Log.Diagnostics.Stop();
        game.Buy(Raw, 1);
        Require(Events(text, "RuleChecked").Length == 8, "停止后仍采集交易明细");
    }

    private static void CheckOrderSamplesAndShortCircuit()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(79, logging);
        RemoveFacilities(game);
        var shortCircuit = Request() with
        {
            Frequency = TradeOrderFrequency.Once,
            BudgetMode = TradeOrderBudgetMode.LimitPrice,
            LimitPriceCents = 1,
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] { new[] { TruePrice }, new[] { FalsePrice } },
        };
        int limitId = game.CreateTradeOrder(shortCircuit).Id;
        int budgetId = game.CreateTradeOrder(shortCircuit with
        { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 25 }).Id;
        int settlementId = game.CreateTradeOrder(Request() with { Quantity = 1000000 }).Id;
        var groups = Enumerable.Range(0, 10).Select(_ =>
            (IReadOnlyList<TradeOrderCondition>)Enumerable.Repeat(FalsePrice, 7).ToArray()).ToArray();
        int largeId = game.CreateTradeOrder(Request() with { ConditionGroups = groups }).Id;
        int fillId = game.CreateTradeOrder(shortCircuit with { LimitPriceCents = 30 }).Id;
        int unselectedId = game.CreateTradeOrder(Request() with { ConditionGroups = new[] { new[] { FalsePrice } } }).Id;
        Require(limitId > 0 && budgetId > 0 && settlementId > 0 && largeId > 0 && fillId > 0 && unselectedId > 0, "诊断订单准备失败");
        game.Log!.Diagnostics.Start(new(new[] { "OrderEvaluated" }, OrderIds: new[] { limitId, budgetId, settlementId, largeId, fillId }));
        game.AdvanceTick();
        string[] events = Events(text, "OrderEvaluated");
        Require(events.Length == 5 && !events.Any(line => Field(line, "OrderId") == unselectedId.ToString(CultureInfo.InvariantCulture)),
            "未选订单采集了明细或真实求值丢失");
        string limit = events.Single(line => Field(line, "OrderId") == limitId.ToString(CultureInfo.InvariantCulture));
        Require(limit.Contains("ConditionSatisfied: false") && limit.Contains("ConditionGroupsSatisfied: true") &&
            limit.Contains("WaitingCategories: [\"LimitPrice\"]") &&
            limit.Contains("ConfiguredConditionGroupCount: 2") && limit.Contains("EvaluatedConditionGroupCount: 1") &&
            limit.Contains("EvaluatedConditionCount: 1") && Regex.Matches(limit, @"\bSatisfied: true\b").Count == 1 &&
            !Regex.IsMatch(limit, @"\bSatisfied: false\b") && !limit.Contains("ComputedBudgetCents:"),
            "条件通过与限价等待混淆或补算了后续 OR 组");
        string budget = events.Single(line => Field(line, "OrderId") == budgetId.ToString(CultureInfo.InvariantCulture));
        Require(budget.Contains("ConditionSatisfied: false") && budget.Contains("ConditionGroupsSatisfied: true") &&
            budget.Contains("ComputedQuantity: 0") &&
            budget.Contains("ComputedBudgetCents: 25") && budget.Contains("BudgetInsufficient"), "真实零预算数量未保留");
        string settlement = events.Single(line => Field(line, "OrderId") == settlementId.ToString(CultureInfo.InvariantCulture));
        Require(settlement.Contains("ConditionSatisfied: false") && settlement.Contains("ConditionGroupsSatisfied: true") &&
            settlement.Contains("TradeFailure.InsufficientFunds"),
            "条件与结算失败混淆");
        string large = events.Single(line => Field(line, "OrderId") == largeId.ToString(CultureInfo.InvariantCulture));
        Require(large.Contains("ConditionSatisfied: false") && large.Contains("ConditionGroupsSatisfied: false") &&
            large.Contains("EvaluatedConditionGroupCount: 10") &&
            large.Contains("EvaluatedConditionCount: 70") && Regex.Matches(large, "Actual: ").Count == 32 &&
            large.Contains("Truncated: true") && large.Contains("Conditions[4]") && large.Contains("ItemCount: 7") &&
            large.Contains("ItemCount: 10"), "条件样本未有界或实际原数量丢失");
        Require(new[] { limit, budget, settlement, large }.All(line => line.Contains("EvaluationCoverage: \"ActualShortCircuit\"")),
            "所有配置组已访问不代表后续交易检查完整执行");
        string filled = events.Single(line => Field(line, "OrderId") == fillId.ToString(CultureInfo.InvariantCulture));
        Require(filled.Contains("ConditionSatisfied: true") && filled.Contains("ConditionGroupsSatisfied: true") &&
            filled.Contains("EvaluationCoverage: \"ActualShortCircuit\"") && filled.Contains("EvaluatedConditionGroupCount: 1"),
            "真实成交前提通过与 OR 未访问后组的覆盖标记混淆");
        Require(game.GetTradeOrders().Single(order => order.Id == fillId).Status == TradeOrderStatus.Completed,
            "订单诊断影响真实成交");
        int before = events.Length;
        var advanced = game.AdvanceTicks(200);
        int after = Events(text, "OrderEvaluated").Length;
        Require(advanced.QuietTicks > 0 && after - before < 200 * 4, "专项诊断破坏平静推进并重跑逐秒判断");
    }

    private static void CheckAllSaleRules()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(77, logging);
        RemoveFacilities(game);
        game.Log!.Diagnostics.Start(new(new[] { "RuleChecked" }, IncludeGameEvents: true));
        game.SellCommodityAll(new CommodityId((CropKind)99, CommodityKind.Raw));
        game.SellCommodityAll(Raw);
        game.SellAll();
        CommodityId product = new(CropKind.Radish, CommodityKind.Product);
        game.Buy(product, 2);
        game.SellAll();
        string[] rules = Events(text, "RuleChecked");
        Require(rules.Length == 5 && rules[0].Contains("CheckResults: { Commodity: false }") &&
            rules[1].Contains("CheckResults: { Commodity: true }") && rules[1].Contains("Outcome: \"Success\""),
            "非法或空库存单商品全售伪造数量、库存检查");
        Require(new[] { rules[2], rules[4] }.All(line => line.Contains("RequestMode: \"AllProducts\"") &&
            line.Contains("CheckResults: { Capacity: true }") && !line.Contains("Commodity:")),
            "聚合全售伪造单商品或遗漏真实容量检查");
        Require(game.GetStock(product) == 0 && Events(text, "AllProductsSold").Length == 2,
            "全售专项诊断重复正式结果或改变库存");

        // 完整交易模块的容量边界夹具只投影真实规则，不读取此独立状态以外的资金或库存。
        var stock = new FarmExchange.Inventory.Inventory();
        var wallet = new Wallet(int.MaxValue);
        var trading = new TradingService(stock, wallet, new MarketQuotes(77));
        trading.SetLogging(game.Log.Trading);
        stock.Add(product, 1);
        TradeResult rejected = trading.SellAllProducts();
        Require(rejected.Failure == TradeFailure.WalletCapacityExceeded && wallet.BalanceCents == int.MaxValue &&
            stock.Get(product) == 1 && Events(text, "RuleChecked").Last().Contains("CheckResults: { Capacity: false }"),
            "聚合容量拒绝未保留资源或伪造检查");
        wallet.TrySpend(100);
        Require(trading.SellAllProducts().Success && stock.Get(product) == 0, "聚合检查改变完整提交");
    }

    private static void CheckCalculatedQuotes()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(83, logging);
        using var plain = new FarmGame(83);
        RemoveFacilities(game);
        RemoveFacilities(plain);
        game.Log!.Diagnostics.Start(new(new[] { "QuoteCalculated" }, IncludeGameEvents: true));
        uint quoteDay = game.GetMarketSnapshot().NextQuoteDate.ElapsedDays;
        uint newsSeconds = SecondsAtDay(quoteDay - 1);
        game.AdvanceTicks(newsSeconds);
        plain.AdvanceTicks(newsSeconds);
        string[] calculated = Events(text, "QuoteCalculated");
        Require(calculated.Length == 14 && Events(text, "QuoteUpdated").Length == 0, "计算提前变成正式价或缺少商品");
        Require(game.GetMarketSnapshot().Quotes.SequenceEqual(plain.GetMarketSnapshot().Quotes), "计算观察改变随机或旧价");
        foreach (string line in calculated)
        {
            long number = long.Parse(Field(line, "Numerator"), CultureInfo.InvariantCulture);
            long denominator = long.Parse(Field(line, "Denominator"), CultureInfo.InvariantCulture);
            int rounded = int.Parse(Field(line, "RoundedPriceCents"), CultureInfo.InvariantCulture);
            int minimum = int.Parse(Field(line, "MinimumPriceCents"), CultureInfo.InvariantCulture);
            int maximum = int.Parse(Field(line, "MaximumPriceCents"), CultureInfo.InvariantCulture);
            Require(rounded == (number + denominator / 2) / denominator &&
                int.Parse(Field(line, "FinalPriceCents"), CultureInfo.InvariantCulture) == Math.Clamp(rounded, minimum, maximum),
                "投影不是本次实际整数限幅结果");
            bool raw = line.Contains("Formula: \"RawFormula\"");
            Require(raw == line.Contains("SeasonPercent:") && raw != line.Contains("PendingRawPriceCents:") &&
                raw != line.Contains("RawInitialPriceCents:"), "原料与加工品公式成员错配");
        }
        uint delta = SecondsAtDay(quoteDay) - newsSeconds;
        game.AdvanceTicks(delta);
        plain.AdvanceTicks(delta);
        Require(game.GetMarketSnapshot().Quotes.SequenceEqual(plain.GetMarketSnapshot().Quotes), "诊断改变正式行情");
        foreach (string line in Events(text, "QuoteUpdated"))
        {
            string commodity = Regex.Match(line, "Commodity: \"([^\"]+)\"").Groups[1].Value;
            string same = calculated.Single(item => item.Contains("Commodity: \"" + commodity + "\""));
            Require(Field(line, "PriceCents") == Field(same, "FinalPriceCents"), "正式价与此前真实计算不符");
        }
        Require(Events(text, "QuoteCalculated").Length == 14, "正式更新重复计算明细");
    }

    private static void CheckProfilesAndProvider()
    {
        string root = ProjectSettings.GlobalizePath("res://build/issue130-validation/trade-provider-" + Guid.NewGuid().ToString("N"));
        using var disabled = RuntimeLog.Disabled();
        using var runtime = RuntimeLog.OpenFile(Path.Combine(root, "runtime"), false, new LogEnvironment());
        using var development = RuntimeLog.OpenFile(Path.Combine(root, "development"), true, new LogEnvironment());
        using var badText = new FailingWriter();
        using var bad = RuntimeLog.Capture(badText, diagnostic: _ => { });
        var results = new List<string>();
        foreach (RuntimeLog log in new[] { disabled, runtime, development, bad })
        {
            using var game = new FarmGame(89, log);
            RemoveFacilities(game);
            int order = game.CreateTradeOrder(Request() with { ConditionGroups = new[] { new[] { FalsePrice } } }).Id;
            bool capture = game.Log?.Diagnostics.Start(new(new[] { "RuleChecked", "OrderEvaluated", "QuoteCalculated" },
                OrderIds: new[] { order }, IncludeGameEvents: true)) ?? false;
            Require(capture == (log == development || log == bad), "关闭或 runtime 采集了明细");
            TradeResult bought = game.Buy(Raw, 3);
            TradeResult sold = game.Sell(Raw, 1);
            CommodityId product = new(CropKind.Radish, CommodityKind.Product);
            game.Buy(product, 1);
            var allSold = game.SellAll();
            var emptySold = game.SellAll();
            var invalidAll = game.SellCommodityAll(new CommodityId((CropKind)99, CommodityKind.Raw));
            bool threw = false;
            try { game.Buy(Raw, 1, (CommandOrigin)99); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Require(threw, "日志状态改变原业务参数异常");
            var advance = game.AdvanceTicks(1500);
            results.Add($"{bought}|{sold}|{allSold}|{emptySold}|{invalidAll}|{game.MoneyCents}|{game.GetStock(Raw)}|{game.Calendar.ElapsedSeconds}|" +
                $"{advance.AdvancedTicks}|{advance.QuietTicks}|{advance.EventTicks}|" +
                string.Join(',', game.GetMarketSnapshot().Quotes.Select(quote => quote.PriceCents)) + "|" +
                game.GetTradeOrders().Single().WaitingReason);
        }
        Require(results.Distinct().Count() == 1 && bad.Health.FailureCount > 0, "采集资格或输出故障改变经营、平静秒或随机序列");
        runtime.Dispose();
        development.Dispose();
        string runtimeText = string.Join('\n', Directory.GetFiles(Path.Combine(root, "runtime"), "*.log", SearchOption.AllDirectories).Select(File.ReadAllText));
        string developmentText = string.Join('\n', Directory.GetFiles(Path.Combine(root, "development"), "*.log", SearchOption.AllDirectories).Select(File.ReadAllText));
        Require(!runtimeText.Contains("EventName=QuoteCalculated") && !runtimeText.Contains("EventName=RuleChecked") &&
            developmentText.Contains("EventName=RuleChecked") && developmentText.Contains("EventName=OrderEvaluated") &&
            developmentText.Contains("EventName=QuoteCalculated"), "真实 File provider 分流证据缺失");
    }

    private static TradeOrderRequest Request() => new(Raw, TradeOrderSide.Buy, TradeOrderFrequency.Continuous,
        TradeOrderQuantityMode.Fixed, 1, TradeOrderBudgetMode.None, 0, 0, CashReserveMode.Amount, 0,
        new IReadOnlyList<TradeOrderCondition>[] { new[] { TruePrice } });

    private static void RemoveFacilities(FarmGame game)
    { foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell); }

    private static string[] Events(StringWriter text, string name) => Lines(text.ToString()).Where(line => HasEvent(line, name)).ToArray();
    private static uint SecondsAtDay(uint day) => checked((uint)(((ulong)day * 360 + 6) / 7));

    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("交易诊断写盘故障夹具");
    }
}
