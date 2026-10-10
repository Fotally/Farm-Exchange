using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Time;
using FarmExchange.Trading;

public static class TestOrderLogging
{
    private static readonly CommodityId Raw = new(CropKind.Radish, CommodityKind.Raw);
    private static readonly CommodityId Product = new(CropKind.Radish, CommodityKind.Product);

    public static bool RunChecks()
    {
        try
        {
            CheckLifecycleAndResources();
            CheckActualWaitingAndShortCircuit();
            CheckBoundedTransitions();
            CheckLifecycleTails();
            CheckEditingWaitBoundary();
            CheckLargeAndInvalidRequests();
            CheckIsolationAndParity();
            return true;
        }
        catch (Exception error) { GD.PrintErr("订单日志测试失败：" + error); return false; }
    }

    private static TradeOrderRequest Request(TradeOrderSide side = TradeOrderSide.Buy,
        TradeOrderFrequency frequency = TradeOrderFrequency.Once) => new(Raw, side, frequency,
        TradeOrderQuantityMode.Fixed, 1,
        side == TradeOrderSide.Buy && frequency == TradeOrderFrequency.Once ? TradeOrderBudgetMode.LimitPrice : TradeOrderBudgetMode.None,
        0, 30, CashReserveMode.Amount, 0,
        new IReadOnlyList<TradeOrderCondition>[] { new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.GreaterOrEqual, 0) } });

    private static void RemoveFacilities(FarmGame game)
    {
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
    }

    private static void CheckLifecycleAndResources()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, logging);
        RemoveFacilities(game);
        Require(game.CreateTradeOrder(Request()).Success, "限价单创建失败");
        Require(Events(text, "OrderWaitChanged").Length == 0, "创建伪造实际等待");
        string created = Events(text, "OrderCreated").Single();
        Require(created.Contains("OrderStatusBefore: null") && created.Contains("OrderFrozenCentsAfter: 31") &&
            created.Contains("FrozenMoneyAfterCents: 31") && created.Contains("MoneyBeforeCents: 5000"), "创建冻结采样错误");
        game.AdvanceTick();
        string filled = Events(text, "OrderFilled").Single();
        Require(filled.Contains("UnitPriceCents: 25") && filled.Contains("ValueCents: 25") && filled.Contains("FeeCents: 1") &&
            filled.Contains("OrderFrozenCentsBefore: 31") && filled.Contains("OrderFrozenCentsAfter: 0") &&
            filled.Contains("MoneyAfterCents: 4974") && filled.Contains("StockAfter: 1") &&
            filled.Contains("OrderStatusAfter: \"Completed\"") && !filled.Contains("CommandId:"), "实际成交价、费用、释放与自动身份错误");
        Require(Events(text, "TradeFinished").Length == 0, "自动成交重复记为主动交易");
        Require(!game.CancelTradeOrder(1).Success, "终态撤销意外成功");
        Require(game.CreateTradeOrder(Request(TradeOrderSide.Sell)).Success, "一次卖单创建失败");
        Require(game.Buy(Product, 1).Success, "测试加工品准备失败");
        Require(game.UpdateTradeOrder(2, Request(TradeOrderSide.Sell) with { Commodity = Product }).Success, "换商品编辑失败");
        string edited = Events(text, "OrderEdited").Single();
        Require(edited.Contains("Commodity: \"Radish.Raw\"") && edited.Contains("OrderCommodityStocks: [") &&
            edited.Contains("Commodity: \"Radish.Product\"") && edited.Contains("FrozenStockBefore: 1, FrozenStockAfter: 0") &&
            edited.Contains("FrozenStockBefore: 0, FrozenStockAfter: 1"), "跨商品编辑丢失任一冻结归属");
        Require(!game.UpdateTradeOrder(2, Request(TradeOrderSide.Sell) with { Quantity = 100 }).Success, "失败编辑意外成功");
        string rejected = Events(text, "OrderEdited").Last();
        Require(rejected.Contains("Outcome: \"Rejected\"") && rejected.Contains("OrderFrozenQuantityBefore: 1") &&
            rejected.Contains("OrderFrozenQuantityAfter: 1") && game.GetFrozenStock(Product) == 1, "编辑拒绝污染有效配置或冻结");
        Require(game.CancelTradeOrder(2).Success && game.GetFrozenStock(Product) == 0, "撤销未释放冻结");
        Require(game.CreateTradeOrder(Request(TradeOrderSide.Sell)).Success, "实际卖出单准备失败");
        game.AdvanceTick();
        string sale = Events(text, "OrderFilled").Last();
        Require(sale.Contains("Side: \"Sell\"") && sale.Contains("UnitPriceCents: 25") && sale.Contains("FeeCents: 1") &&
            sale.Contains("OrderFrozenQuantityBefore: 1") && sale.Contains("OrderFrozenQuantityAfter: 0"), "自动卖出事实错误");
        Require(game.CreateTradeOrder(Request() with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 100, Quantity = 0 }).Success,
            "预算单创建失败");
        game.AdvanceTick();
        string budget = Events(text, "OrderFilled").Last();
        Require(budget.Contains("Quantity: 3") && budget.Contains("ValueCents: 75") && budget.Contains("FeeCents: 1") &&
            budget.Contains("OrderFrozenCentsBefore: 100") && budget.Contains("OrderFrozenCentsAfter: 0"), "预算成交不是实际量或未释放差额");
        Require(!game.CancelTradeOrder(-4).Success, "非法 ID 未拒绝");
        string invalidId = Events(text, "OrderCancelled").Last();
        Require(!invalidId.Contains("OrderId:"), "不存在订单伪造领域身份");
        Require(Events(text, "CommandReceived").Last().Contains("OrderId: -4"), "原输入 ID 被丢弃");
    }

    private static void CheckActualWaitingAndShortCircuit()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, logging);
        RemoveFacilities(game);
        var conditions = new IReadOnlyList<TradeOrderCondition>[]
        {
            new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Less, 0),
                new TradeOrderCondition(TradeConditionFactor.Season, TradeConditionComparison.Equal, (int)Season.Winter) },
            new[] { new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.Greater, 10) },
        };
        Require(game.CreateTradeOrder(Request(frequency: TradeOrderFrequency.Continuous) with { ConditionGroups = conditions }).Success,
            "条件单创建失败");
        game.SetPaused(true);
        game.AdvanceTicks(10);
        Require(Events(text, "OrderWaitChanged").Length == 0, "暂停补造检查");
        game.SetPaused(false);
        game.AdvanceTick();
        string waiting = Events(text, "OrderWaitChanged").Single();
        Require(waiting.Contains("WaitingCategories: [\"Price\", \"Stock\", \"Season\"]") &&
            waiting.Contains("PrimaryReason: \"Price\"") && waiting.Contains("PreviousWaitingCategories: null"), "实际 AND/OR 原因集合错误");
        game.Buy(Raw, 1);
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Length == 1, "同原因数值变化刷屏");
        game.Buy(Raw, 11);
        game.AdvanceTick();
        Require(Events(text, "OrderFilled").Length == 1 && Events(text, "OrderWaitChanged").Last().Contains("Resolution: \"Filled\""),
            "OR 后组真实满足未结束等待");
        Require(game.SetTradeOrderEnabled(1, false).Success, "停用失败");
        int waits = Events(text, "OrderWaitChanged").Length;
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Length == waits, "停用伪造求值");
        game.UpdateTradeOrder(1, Request(frequency: TradeOrderFrequency.Continuous) with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[]
            {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.GreaterOrEqual, 0) },
                new[] { new TradeOrderCondition(TradeConditionFactor.Season, TradeConditionComparison.Equal, (int)Season.Winter) },
            },
        });
        game.SetTradeOrderEnabled(1, true);
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Length == waits, "未求值的 OR 后组被补造成阻塞");
        game.CancelTradeOrder(1);
        Require(game.CreateTradeOrder(Request() with { LimitPriceCents = 24 }).Success, "限价等待单失败");
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Last().Contains("PrimaryReason: \"LimitPrice\""), "缺少实际限价原因");
        game.UpdateTradeOrder(2, Request() with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 25, Quantity = 0 });
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Last().Contains("PrimaryReason: \"BudgetInsufficient\""), "缺少实际预算原因");
        game.CancelTradeOrder(2);
        Require(game.CreateTradeOrder(Request(frequency: TradeOrderFrequency.Continuous) with
        { QuantityMode = TradeOrderQuantityMode.BuyToTarget, Quantity = 1 }).Success, "达标策略失败");
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Last().Contains("PrimaryReason: \"TargetReached\""), "缺少实际目标达标原因");
        int resolved = Events(text, "OrderWaitChanged").Count(line => line.Contains("WaitingState: \"Resolved\""));
        game.SetTradeOrderEnabled(3, false);
        game.SetTradeOrderEnabled(3, true);
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Last().Contains("PreviousWaitingCategories: null"), "重新启用未建立新基线");
        game.CancelTradeOrder(3);
        Require(Events(text, "OrderWaitChanged").Count(line => line.Contains("WaitingState: \"Resolved\"")) == resolved,
            "取消或停用伪造成交恢复");
        game.CreateTradeOrder(Request(frequency: TradeOrderFrequency.Continuous) with { ReserveValue = 5000 });
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Last().Contains("TradeFailure.CashReserveNotMet"), "未从实际交易失败提供稳定原因");
    }

    private static void CheckBoundedTransitions()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(3);
        GameLog context = logging.BindGame(game, 3)!;
        long uptime = 0;
        var orders = new TradeOrderLog(context, game, () => uptime);
        for (uint i = 0; i < 12; i++)
            orders.Evaluated(1, Waiting(i), new GameCalendar(i).Snapshot);
        Require(Events(text, "OrderWaitChanged").Length == 9 && Events(text, "OrderWaitSummary").Length == 0,
            "首次或每窗中间变化预算错误");
        orders.Evaluated(1, Waiting(11), new GameCalendar(12).Snapshot);
        uptime = 60_000;
        orders.Evaluated(1, Waiting(12), new GameCalendar(13).Snapshot);
        string summary = Events(text, "OrderWaitSummary").Single();
        Require(summary.Contains("SuppressedTransitionCount: 3") && summary.Contains("FirstObservedSimulationSeconds: 9") &&
            summary.Contains("LastObservedSimulationSeconds: 12") && summary.Contains("Price: 3") && summary.Contains("Stock: 1"),
            "合并尾段计数混同经营秒或漏掉实际同类观察");
        Require(Events(text, "OrderWaitChanged").Length == 10, "封窗后预算没有重置");
        for (uint i = 13; i < 24; i++) orders.Evaluated(1, Waiting(i), new GameCalendar(i).Snapshot);
        orders.Evaluated(1, new(TradeOrderBlocker.None, TradeOrderBlocker.None, TradeFailure.None, true), new GameCalendar(24).Snapshot);
        Require(Events(text, "OrderWaitChanged").Last().Contains("Resolution: \"Filled\"") &&
            Events(text, "OrderWaitSummary").Length == 2, "成交恢复受预算压制或丢尾段");
        for (uint i = 0; i < 12; i++) orders.Evaluated(2, Waiting(i), new GameCalendar(i).Snapshot);
        orders.End();
        int count = Events(text, "OrderWaitSummary").Length;
        orders.End();
        Require(count == 3 && Events(text, "OrderWaitSummary").Length == count, "关闭漏尾段或重复输出");

        static TradeOrderEvaluation Waiting(uint i)
        {
            var reason = i % 2 == 0 ? TradeOrderBlocker.Stock : TradeOrderBlocker.Price;
            return new(reason, reason, TradeFailure.None, false);
        }
    }

    private static void CheckLargeAndInvalidRequests()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, logging);
        var groups = Enumerable.Range(0, 20).Select(_ => (IReadOnlyList<TradeOrderCondition>)Enumerable.Range(0, 100)
            .Select(_ => new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.GreaterOrEqual, 0)).ToArray()).ToArray();
        Require(game.CreateTradeOrder(Request() with { ConditionGroups = groups }).Success, "日志限额拒绝了合法大配置");
        string received = Events(text, "CommandReceived").Last();
        string created = Events(text, "OrderCreated").Single();
        Require(received.Contains("CommandArguments.RequestedOrderRequest.ConditionGroups[0]") && received.Contains("ItemCount: 100") &&
            received.Contains("ItemCount: 20") && created.Contains("OrderRequestAfter.ConditionGroups[0]"), "大配置截断路径或原数量缺失");
        Require(!text.ToString().Contains("EventName=EventPayloadRejected") &&
            Lines(text).All(line => Encoding.UTF8.GetByteCount(line) <= 32 * 1024), "核心结果被大配置挤出预算");
        Require(game.GetTradeOrders()[0].Request.ConditionGroups.Count == 20 &&
            game.GetTradeOrders()[0].Request.ConditionGroups[0].Count == 100, "投影截断修改了生效配置");
        var shortGroups = Enumerable.Range(0, 12).Select(_ => (IReadOnlyList<TradeOrderCondition>)new[]
            { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.GreaterOrEqual, 0) }).ToArray();
        Require(game.UpdateTradeOrder(1, Request() with { ConditionGroups = shortGroups }).Success, "多组配置更新失败");
        Require(Events(text, "OrderEdited").Last().Contains("ItemCount: 12"), "前八组预算没有标原组数");
        Require(!game.UpdateTradeOrder(1, Request() with { Commodity = new((CropKind)123, (CommodityKind)456) }).Success,
            "非法编辑未拒绝");
        Require(Events(text, "OrderEdited").Last().Contains("Commodity: \"123.456\", StockBefore: null, StockAfter: null"),
            "非法商品被查询或伪造库存");
        Require(!game.CreateTradeOrder(null!).Success, "null 配置没有原业务拒绝");
        Require(Events(text, "OrderCreated").Last().Contains("RequestedOrderRequest: null"), "null 原输入丢失");
        Require(!game.CreateTradeOrder(Request() with { ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] { null! } }).Success,
            "null 条件组未被原业务拒绝");
        Require(!game.CreateTradeOrder(Request() with { ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] { new TradeOrderCondition[] { null! } } }).Success,
            "null 条件未被原业务拒绝");
        Require(logging.Health.FailureCount == 0, "合法/非法配置投影产生日志故障");
    }

    private static void CheckLifecycleTails()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        var game = new FarmGame(12345, logging);
        RemoveFacilities(game);
        Require(game.CreateTradeOrder(ChangingWaitRequest()).Success, "尾段策略创建失败");
        DriveWaitingChanges(game);
        game.SetTradeOrderEnabled(1, false);
        Require(Events(text, "OrderWaitSummary").Length == 1 && Events(text, "OrderWaitChanged").Length == 9,
            "真实停用未输出合并尾段或伪造恢复");
        string output = text.ToString();
        Require(output.LastIndexOf("EventName=OrderWaitSummary", StringComparison.Ordinal) <
            output.LastIndexOf("EventName=OrderEnabledChanged", StringComparison.Ordinal), "停用尾段顺序错误");
        game.SetTradeOrderEnabled(1, true);
        DriveWaitingChanges(game);
        game.CancelTradeOrder(1);
        Require(Events(text, "OrderWaitSummary").Length == 2, "真实取消未输出合并尾段");
        output = text.ToString();
        Require(output.LastIndexOf("EventName=OrderWaitSummary", StringComparison.Ordinal) <
            output.LastIndexOf("EventName=OrderCancelled", StringComparison.Ordinal), "取消尾段顺序错误");
        game.CreateTradeOrder(ChangingWaitRequest());
        DriveWaitingChanges(game);
        game.Dispose();
        Require(Events(text, "OrderWaitSummary").Length == 3, "局关闭未刷新真实订单尾段");
        output = text.ToString();
        Require(output.LastIndexOf("EventName=OrderWaitSummary", StringComparison.Ordinal) <
            output.LastIndexOf("EventName=GameEnded", StringComparison.Ordinal), "关闭尾段晚于局结束");

    }

    private static void CheckEditingWaitBoundary()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(12345, logging);
        RemoveFacilities(game);
        Require(game.CreateTradeOrder(ChangingWaitRequest()).Success, "编辑观察策略创建失败");
        DriveWaitingChanges(game);
        Require(Events(text, "OrderWaitChanged").Length == 9 && Events(text, "OrderWaitSummary").Length == 0,
            "编辑前未积累真实待合并变化");
        Require(!game.UpdateTradeOrder(1, ChangingWaitRequest() with { Quantity = 0 }).Success, "非法编辑意外成功");
        game.AdvanceTick();
        game.SellCommodityAll(Raw);
        game.AdvanceTick();
        Require(Events(text, "OrderWaitChanged").Length == 9 && Events(text, "OrderWaitSummary").Length == 0,
            "拒绝编辑清除了原基线、变化额度或待合并尾段");
        Require(game.UpdateTradeOrder(1, ChangingWaitRequest() with { Commodity = Product }).Success, "跨商品编辑失败");
        string summary = Events(text, "OrderWaitSummary").Single();
        Require(summary.Contains("SuppressedTransitionCount: 4") &&
            summary.Contains("LastWaitingCategories: [\"TradeFailure.InsufficientStock\"]"),
            "成功编辑未保留旧配置完整尾段");
        string output = text.ToString();
        Require(output.LastIndexOf("EventName=OrderWaitSummary", StringComparison.Ordinal) <
            output.LastIndexOf("EventName=OrderEdited", StringComparison.Ordinal), "旧配置尾段晚于编辑领域事件");
        Require(Events(text, "OrderWaitChanged").Length == 9, "成功编辑伪造实际求值或恢复");
        game.AdvanceTick();
        string first = Events(text, "OrderWaitChanged").Last();
        Require(Events(text, "OrderWaitChanged").Length == 10 && first.Contains("PreviousWaitingCategories: null") &&
            first.Contains("WaitingState: \"Waiting\"") && first.Contains("TradeFailure.InsufficientStock"),
            "同 ID 新配置相同原因被旧配置基线去重");
        Require(!Events(text, "OrderWaitChanged").Any(line => line.Contains("WaitingState: \"Resolved\"")),
            "配置替换伪造成交恢复");
    }

    private static TradeOrderRequest ChangingWaitRequest() => Request(TradeOrderSide.Sell, TradeOrderFrequency.Continuous) with
    {
        Quantity = 2,
        ConditionGroups = new IReadOnlyList<TradeOrderCondition>[]
        { new[] { new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.Equal, 0) } },
    };

    private static void DriveWaitingChanges(FarmGame game)
    {
        game.SellCommodityAll(Raw);
        for (int i = 0; i < 12; i++)
        {
            if (i % 2 == 1) Require(game.Buy(Raw, 1).Success, "等待变化库存准备失败");
            else if (i > 0) game.SellCommodityAll(Raw);
            game.AdvanceTick();
        }
    }

    private static void CheckIsolationAndParity()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var first = new FarmGame(31, logging);
        using var second = new FarmGame(31, logging);
        using var plain = new FarmGame(31);
        foreach (FarmGame game in new[] { first, second, plain })
        {
            RemoveFacilities(game);
            game.CreateTradeOrder(Request() with { LimitPriceCents = 24 });
        }
        Require(first.AdvanceTicks(100) == plain.AdvanceTicks(100), "日志改变批量推进结果");
        second.AdvanceTick();
        string[] waiting = Events(text, "OrderWaitChanged");
        Require(waiting.Length == 2 && Regex.Match(waiting[0], "GameInstanceId: \"([^\"]+)\"").Value !=
            Regex.Match(waiting[1], "GameInstanceId: \"([^\"]+)\"").Value, "两局同号订单互相去重");
        Require(first.MoneyCents == plain.MoneyCents && first.FrozenMoneyCents == plain.FrozenMoneyCents &&
            first.GetTradeOrders()[0].WaitingReason == plain.GetTradeOrders()[0].WaitingReason, "日志改变资源或等待业务状态");
        first.Dispose();
        int length = text.ToString().Length;
        first.AdvanceTick();
        first.CreateTradeOrder(Request());
        Require(text.ToString().Length == length, "局结束后仍输出订单事件");
    }

    private static string[] Lines(StringWriter text) => text.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    private static string[] Events(StringWriter text, string name) => Lines(text).Where(line => line.Contains("EventName=" + name + " ")).ToArray();
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
