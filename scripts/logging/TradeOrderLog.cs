using System;
using System.Collections.Generic;
using System.Linq;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;
using FarmExchange.Trading;

namespace FarmExchange.Logging;

/**
 * <summary>订单领域观察，封装配置投影、资源采样及真实等待的有界变化记录。</summary>
 * <remarks>所有判断与执行归订单业务；本入口不执行条件、不重读结算价格。</remarks>
 */
public sealed class TradeOrderLog
{
    private const string Source = "FarmExchange.Trading.TradeOrderBook";
    private const long WindowMs = 60_000;
    private const int TransitionLimit = 8;
    private static readonly LogEventDescriptor Created = new(20, "OrderCreated", Source);
    private static readonly LogEventDescriptor Edited = new(21, "OrderEdited", Source);
    private static readonly LogEventDescriptor Cancelled = new(22, "OrderCancelled", Source);
    private static readonly LogEventDescriptor EnabledChanged = new(23, "OrderEnabledChanged", Source);
    private static readonly LogEventDescriptor Fill = new(24, "OrderFilled", Source);
    private static readonly LogEventDescriptor WaitChanged = new(25, "OrderWaitChanged", Source);
    private static readonly LogEventDescriptor WaitSummary = new(26, "OrderWaitSummary", Source);
    private readonly GameLog _context;
    private readonly FarmGame _game;
    private readonly Func<long> _uptime;
    private readonly Dictionary<int, WaitingObservation> _waiting = new();

    internal TradeOrderLog(GameLog context, FarmGame game) : this(context, game, () => context.Output.UptimeMs) { }
    internal TradeOrderLog(GameLog context, FarmGame game, Func<long> uptime)
    { _context = context; _game = game; _uptime = uptime; }

    /**
     * <summary>记录创建原输入并采集修改前资源。</summary>
     * <param name="request">原始配置，非法或 null 输入仍保留。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null，否则返回只观察真实结果的关联。</returns>
     */
    public OrderLogOperation? BeginCreate(TradeOrderRequest request, CommandOrigin origin = CommandOrigin.Player) =>
        Begin("CreateTradeOrder", Created, 0, request, null, origin);

    /**
     * <summary>记录同 ID 编辑原输入及原有效订单。</summary>
     * <param name="id">原输入订单 ID。</param>
     * <param name="request">原输入替换配置。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null。</returns>
     */
    public OrderLogOperation? BeginEdit(int id, TradeOrderRequest request, CommandOrigin origin = CommandOrigin.Player) =>
        Begin("UpdateTradeOrder", Edited, id, request, null, origin);

    /**
     * <summary>观察撤销命令，不释放业务资源。</summary>
     * <param name="id">原输入订单 ID。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null。</returns>
     */
    public OrderLogOperation? BeginCancel(int id, CommandOrigin origin = CommandOrigin.Player) =>
        Begin("CancelTradeOrder", Cancelled, id, null, null, origin);

    /**
     * <summary>观察启停原输入，不改变订单状态。</summary>
     * <param name="id">原输入订单 ID。</param>
     * <param name="enabled">请求启用状态。</param>
     * <param name="origin">真实命令来源。</param>
     * <returns>关闭采集时为 null。</returns>
     */
    public OrderLogOperation? BeginSetEnabled(int id, bool enabled, CommandOrigin origin = CommandOrigin.Player) =>
        Begin("SetTradeOrderEnabled", EnabledChanged, id, null, enabled, origin);

    private OrderLogOperation? Begin(string name, LogEventDescriptor descriptor, int id,
        TradeOrderRequest? request, bool? enabled, CommandOrigin origin)
    {
        if (origin is not CommandOrigin.Player and not CommandOrigin.Scenario)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!_context.CanObserve) return null;
        var command = _context.NewCommand(new(name, Source, "收到订单指令", "订单指令结束", "订单指令抛出异常", "订单指令异常结束"), origin);
        OrderCommandObservation? observation = null;
        _context.Observe(() =>
        {
            var arguments = new Dictionary<string, object?>();
            var metadata = new Dictionary<string, object?>();
            if (descriptor != Created) arguments["OrderId"] = id;
            if (descriptor == Created || descriptor == Edited)
                arguments["RequestedOrderRequest"] = ProjectRequest(request, "CommandArguments.RequestedOrderRequest", metadata);
            if (enabled.HasValue) arguments["Enabled"] = enabled.Value;
            command.Received(arguments, metadata);
            TradeOrderSnapshot? before = Find(id);
            CommodityId? commodity = before?.Request.Commodity ?? request?.Commodity;
            CommodityId? other = before != null && request != null && before.Request.Commodity != request.Commodity
                ? request.Commodity : null;
            observation = new(descriptor, request, before, commodity, other,
                commodity.HasValue ? _context.Trading.Snapshot(commodity.Value) : null,
                other.HasValue ? _context.Trading.Snapshot(other.Value) : null);
        });
        return observation == null ? null : new(this, command, observation);
    }

    internal void Completed(CommandObservation command, OrderCommandObservation before, TradeOrderCommandResult result) =>
        command.Complete(fields =>
        {
            TradeOrderSnapshot? after = Find(result.Id);
            if (result.Success && (before.Event == Edited || before.Event == Cancelled ||
                before.Event == EnabledChanged && after?.Status == TradeOrderStatus.Disabled))
                Forget(result.Id);
            fields["Outcome"] = result.Success ? "Success" : "Rejected";
            fields["RejectionReason"] = result.ErrorMessage;
            if (after != null) fields["OrderId"] = after.Id;
            if (before.Event == Created || before.Event == Edited)
            {
                fields["RequestedOrderRequest"] = ProjectRequest(before.Request, "RequestedOrderRequest", fields);
                if (after != null)
                {
                    fields["OrderRequestBefore"] = ProjectRequest(before.Order?.Request, "OrderRequestBefore", fields);
                    fields["OrderRequestAfter"] = ProjectRequest(after.Request, "OrderRequestAfter", fields);
                    fields["CashBasisCents"] = after.CashBasisCents;
                    fields["ReserveCents"] = after.ReserveCents;
                    fields["LockedQuantity"] = after.LockedQuantity;
                }
            }
            if (after != null) AddOrder(fields, before.Order, after);
            if (before.Commodity.HasValue && before.Resources.HasValue)
            {
                fields["Commodity"] = CommodityName(before.Commodity.Value);
                TradingLog.AddResources(fields, before.Resources.Value, _context.Trading.Snapshot(before.Commodity.Value));
                if (before.OtherCommodity.HasValue && before.OtherResources.HasValue)
                {
                    fields["OrderCommodityStocks"] = new[]
                    {
                        StockFields(before.Commodity.Value, before.Resources.Value),
                        StockFields(before.OtherCommodity.Value, before.OtherResources.Value),
                    };
                }
            }
            return new(before.Event, result.Success ? "订单指令已完成" : "订单指令被拒绝", result.Success, result.ErrorMessage);
        });

    internal OrderFillObservation? BeforeFill(TradeOrderSnapshot order)
    {
        if (!_context.CanObserve) return null;
        OrderFillObservation? before = null;
        _context.Observe(() => before = new(order, _context.Trading.Snapshot(order.Request.Commodity)));
        return before;
    }

    internal void Filled(OrderFillObservation? before, TradeOrderSnapshot after, TradeResult result) => _context.Observe(() =>
    {
        if (before == null) return;
        FlushIfExpired(after.Id);
        if (_waiting.TryGetValue(after.Id, out WaitingObservation? waiting)) Flush(after.Id, waiting);
        var fields = _context.Context("Orders");
        fields["OrderId"] = after.Id;
        fields["Commodity"] = CommodityName(after.Request.Commodity);
        fields["Side"] = after.Request.Side.ToString();
        fields["Quantity"] = result.Quantity;
        fields["UnitPriceCents"] = result.UnitPriceCents;
        fields["ValueCents"] = result.TotalCents;
        fields["FeeCents"] = result.FeeCents;
        TradingLog.AddResources(fields, before.Resources, _context.Trading.Snapshot(after.Request.Commodity));
        AddOrder(fields, before.Order, after);
        _context.Output.Submit(Fill, "订单实际成交", fields);
    });

    internal void Evaluated(int id, TradeOrderEvaluation evaluation, CalendarSnapshot calendar) => _context.Observe(() =>
    {
        string[] categories = Categories(evaluation);
        bool resolved = categories.Length == 0;
        FlushIfExpired(id);
        if (!_waiting.TryGetValue(id, out WaitingObservation? state))
        {
            if (resolved) return;
            state = new() { WindowStart = _uptime() };
            _waiting.Add(id, state);
            EmitWait(id, categories, null, evaluation, calendar);
            state.Categories = categories;
            return;
        }
        if (resolved)
        {
            Flush(id, state);
            EmitWait(id, categories, state.Categories, evaluation, calendar);
            _waiting.Remove(id);
            return;
        }
        bool changed = !state.Categories.SequenceEqual(categories);
        if (changed && state.EmittedTransitions < TransitionLimit)
        {
            EmitWait(id, categories, state.Categories, evaluation, calendar);
            state.EmittedTransitions++;
        }
        else if (changed || state.SuppressedTransitions > 0)
        {
            if (state.SuppressedTransitions == 0) state.FirstSeconds = calendar.ElapsedSeconds;
            if (changed) state.SuppressedTransitions++;
            state.LastSeconds = calendar.ElapsedSeconds;
            foreach (string category in categories)
                state.Counts[category] = state.Counts.GetValueOrDefault(category) + 1;
        }
        state.Categories = categories;
    });

    internal void End() => _context.Observe(() =>
    {
        foreach (var pair in _waiting) Flush(pair.Key, pair.Value);
        _waiting.Clear();
    });

    private void EmitWait(int id, string[] categories, string[]? previous, TradeOrderEvaluation evaluation, CalendarSnapshot calendar)
    {
        var fields = _context.Context("Orders");
        fields["OrderId"] = id;
        fields["WaitingState"] = categories.Length == 0 ? "Resolved" : "Waiting";
        fields["PrimaryReason"] = evaluation.Failure != TradeFailure.None ? "TradeFailure." + evaluation.Failure :
            evaluation.Primary == TradeOrderBlocker.None ? null : evaluation.Primary.ToString();
        fields["WaitingCategories"] = categories;
        fields["PreviousWaitingCategories"] = previous;
        fields["EvaluationCoverage"] = evaluation.Complete ? "Complete" : "ActualShortCircuit";
        if (categories.Length == 0) fields["Resolution"] = "Filled";
        fields["SimulationSeconds"] = calendar.ElapsedSeconds;
        fields["Season"] = calendar.Season.ToString();
        _context.Output.Submit(WaitChanged, categories.Length == 0 ? "订单等待已实际结束" : "订单实际等待原因变化", fields);
    }

    private void FlushIfExpired(int id)
    {
        if (!_waiting.TryGetValue(id, out WaitingObservation? state) || _uptime() - state.WindowStart < WindowMs) return;
        Flush(id, state);
        state.WindowStart = _uptime();
        state.EmittedTransitions = 0;
    }

    private void Forget(int id)
    {
        if (!_waiting.Remove(id, out WaitingObservation? state)) return;
        Flush(id, state);
    }

    private void Flush(int id, WaitingObservation state)
    {
        if (state.SuppressedTransitions == 0) return;
        var fields = _context.Context("Orders");
        fields["OrderId"] = id;
        fields["IntervalStartUptimeMs"] = state.WindowStart;
        fields["IntervalEndUptimeMs"] = _uptime();
        fields["Coalesced"] = true;
        fields["SuppressedTransitionCount"] = state.SuppressedTransitions;
        fields["FirstObservedSimulationSeconds"] = state.FirstSeconds;
        fields["LastObservedSimulationSeconds"] = state.LastSeconds;
        fields["ReasonObservationCounts"] = new Dictionary<string, long>(state.Counts);
        fields["LastWaitingState"] = "Waiting";
        fields["LastWaitingCategories"] = state.Categories;
        _context.Output.Submit(WaitSummary, "订单等待中间变化合并", fields);
        state.SuppressedTransitions = 0;
        state.Counts.Clear();
    }

    private static string[] Categories(TradeOrderEvaluation evaluation)
    {
        if (evaluation.Failure != TradeFailure.None) return new[] { "TradeFailure." + evaluation.Failure };
        var categories = new List<string>(3);
        foreach (TradeOrderBlocker reason in Enum.GetValues<TradeOrderBlocker>())
            if (reason != TradeOrderBlocker.None && (evaluation.Blockers & reason) != 0) categories.Add(reason.ToString());
        return categories.ToArray();
    }

    private TradeOrderSnapshot? Find(int id) => id <= 0 ? null : _game.GetTradeOrders().FirstOrDefault(order => order.Id == id);
    private static string CommodityName(CommodityId commodity) => commodity.Crop + "." + commodity.Kind;

    private Dictionary<string, object?> StockFields(CommodityId commodity, TradeObservation before)
    {
        TradeObservation after = _context.Trading.Snapshot(commodity);
        return new()
        {
            ["Commodity"] = CommodityName(commodity),
            ["StockBefore"] = before.Stock,
            ["StockAfter"] = after.Stock,
            ["AvailableStockBefore"] = before.AvailableStock,
            ["AvailableStockAfter"] = after.AvailableStock,
            ["FrozenStockBefore"] = before.FrozenStock,
            ["FrozenStockAfter"] = after.FrozenStock,
        };
    }

    private static void AddOrder(Dictionary<string, object?> fields, TradeOrderSnapshot? before, TradeOrderSnapshot after)
    {
        fields["OrderStatusBefore"] = before?.Status.ToString();
        fields["OrderStatusAfter"] = after.Status.ToString();
        fields["OrderFrozenCentsBefore"] = before?.FrozenCents ?? 0;
        fields["OrderFrozenCentsAfter"] = after.FrozenCents;
        fields["OrderFrozenQuantityBefore"] = before?.FrozenQuantity ?? 0;
        fields["OrderFrozenQuantityAfter"] = after.FrozenQuantity;
    }

    private static Dictionary<string, object?>? ProjectRequest(TradeOrderRequest? request, string path, Dictionary<string, object?> fields)
    {
        if (request == null) return null;
        List<object?>? groups = null;
        if (request.ConditionGroups != null)
        {
            groups = new();
            int remaining = 32;
            for (int i = 0; i < request.ConditionGroups.Count && i < 8 && remaining > 0; i++)
            {
                IReadOnlyList<TradeOrderCondition>? group = request.ConditionGroups[i];
                if (group == null) { groups.Add(null); continue; }
                var conditions = new List<object?>();
                for (int j = 0; j < group.Count && remaining > 0; j++, remaining--)
                {
                    TradeOrderCondition? condition = group[j];
                    conditions.Add(condition == null ? null : new Dictionary<string, object?>
                    { ["Factor"] = condition.Factor.ToString(), ["Comparison"] = condition.Comparison.ToString(), ["Value"] = condition.Value });
                }
                groups.Add(conditions);
                if (conditions.Count < group.Count) Truncated(fields, path + ".ConditionGroups[" + i + "]", group.Count);
            }
            if (groups.Count < request.ConditionGroups.Count) Truncated(fields, path + ".ConditionGroups", request.ConditionGroups.Count);
        }
        return new()
        {
            ["Commodity"] = CommodityName(request.Commodity),
            ["Side"] = request.Side.ToString(),
            ["Frequency"] = request.Frequency.ToString(),
            ["QuantityMode"] = request.QuantityMode.ToString(),
            ["Quantity"] = request.Quantity,
            ["BudgetMode"] = request.BudgetMode.ToString(),
            ["BudgetCents"] = request.BudgetCents,
            ["LimitPriceCents"] = request.LimitPriceCents,
            ["ReserveMode"] = request.ReserveMode.ToString(),
            ["ReserveValue"] = request.ReserveValue,
            ["ConditionGroups"] = groups,
        };
    }

    private static void Truncated(Dictionary<string, object?> fields, string path, long count)
    {
        fields["Truncated"] = true;
        if (!fields.TryGetValue("TruncatedFields", out object? paths)) fields["TruncatedFields"] = paths = new List<string>();
        if (!fields.TryGetValue("TruncatedOriginalCounts", out object? counts))
            fields["TruncatedOriginalCounts"] = counts = new Dictionary<string, object?>();
        ((List<string>)paths!).Add(path);
        ((Dictionary<string, object?>)counts!)[path] = new Dictionary<string, object?> { ["ItemCount"] = count };
    }

    private sealed class WaitingObservation
    {
        internal long WindowStart;
        internal int EmittedTransitions;
        internal string[] Categories = Array.Empty<string>();
        internal long SuppressedTransitions;
        internal uint FirstSeconds;
        internal uint LastSeconds;
        internal Dictionary<string, long> Counts = new();
    }
}

internal sealed record OrderCommandObservation(LogEventDescriptor Event, TradeOrderRequest? Request,
    TradeOrderSnapshot? Order, CommodityId? Commodity, CommodityId? OtherCommodity,
    TradeObservation? Resources, TradeObservation? OtherResources);

internal sealed record OrderFillObservation(TradeOrderSnapshot Order, TradeObservation Resources);
