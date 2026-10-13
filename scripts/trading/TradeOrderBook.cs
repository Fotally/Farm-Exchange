using System;
using System.Collections.Generic;
using FarmExchange.Logging;
using FarmExchange.Economy;
using FarmExchange.Inventory;
using FarmExchange.Market;
using FarmExchange.Time;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Trading;

internal sealed class TradeOrderBook
{
    private sealed class Order
    {
        internal int Id;
        internal TradeOrderRequest Request = null!;
        internal TradeOrderStatus Status;
        internal int CashBasisCents;
        internal int ReserveCents;
        internal int LockedQuantity;
        internal int FrozenCents;
        internal int FrozenQuantity;
        internal string? WaitingReason;
        internal TradeOrderFillSnapshot? LastFill;
    }

    private readonly List<Order> _orders = new();
    private readonly GoodsInventory _inventory;
    private readonly Wallet _wallet;
    private readonly MarketQuotes _market;
    private readonly TradingService _trading;
    private bool _nextCheckRequired = true;
    private TradeOrderLog? _log;

    internal void AttachLog(TradeOrderLog? log) => _log = log;

    /**
     * <summary>只查询现有订单是否仍可执行或重新启用，不创建快照或求值。</summary>
     * <param name="id">本局订单编号。</param>
     * <returns>等待或停用的订单为 true，不存在及终态为 false。</returns>
     */
    internal bool IsDiagnosticOrderActive(int id) => Find(id)?.Status is TradeOrderStatus.Waiting or TradeOrderStatus.Disabled;

    /**
     * <summary>新批量请求重新检查等待订单，纳入请求之间的正式经营命令。</summary>
     */
    internal void BeginAdvanceRequest() => _nextCheckRequired = true;

    /**
     * <summary>查询是否必须在下一经营秒重新检查活动订单。</summary>
     * <returns>有等待订单且新请求或上秒任一订单成交时返回 true。</returns>
     */
    internal bool NeedsNextTickCheck => _nextCheckRequired &&
        _orders.Exists(order => order.Status == TradeOrderStatus.Waiting);

    internal TradeOrderBook(GoodsInventory inventory, Wallet wallet, MarketQuotes market, TradingService trading)
    {
        _inventory = inventory;
        _wallet = wallet;
        _market = market;
        _trading = trading;
    }

    /**
     * <summary>读取独立只读委托快照；查询不求值或执行交易。</summary>
     * <returns>按原建单顺序排列的全部委托，包括成交及撤销记录。</returns>
     */
    internal IReadOnlyList<TradeOrderSnapshot> GetSnapshots()
    {
        var snapshots = new TradeOrderSnapshot[_orders.Count];
        for (int i = 0; i < snapshots.Length; i++)
        {
            Order order = _orders[i];
            snapshots[i] = new TradeOrderSnapshot(order.Id, order.Request, order.Status,
                order.CashBasisCents, order.ReserveCents, order.LockedQuantity,
                order.FrozenCents, order.FrozenQuantity, order.WaitingReason,
                order.LastFill);
        }
        return Array.AsReadOnly(snapshots);
    }

    /**
     * <summary>创建完整委托，并为一次单冻结资源。</summary>
     * <param name="request">完整配置，金额为分，数量为份，比例为整数百分比。</param>
     * <returns>新 ID 或中文拒绝原因；失败不修改单据与资源。</returns>
     * <remarks>以设单时总现金锁定保留基准；提交本身不执行。</remarks>
     */
    internal TradeOrderCommandResult Create(TradeOrderRequest request)
    {
        int basis = _wallet.BalanceCents;
        string? error = Prepare(request, basis, null, out int reserve, out int quantity,
            out int frozenCents, out int frozenQuantity);
        if (error != null)
            return new TradeOrderCommandResult(false, 0, error);
        var order = new Order
        {
            Id = _orders.Count + 1,
            Request = CopyRequest(request),
            Status = TradeOrderStatus.Waiting,
            CashBasisCents = basis,
            ReserveCents = reserve,
            LockedQuantity = quantity,
            FrozenCents = frozenCents,
            FrozenQuantity = frozenQuantity,
            WaitingReason = "等待下一经营秒检查",
        };
        ReplaceResources(null, order);
        _orders.Add(order);
        return new TradeOrderCommandResult(true, order.Id, null);
    }

    /**
     * <summary>同 ID 整体替换活动委托，保留原现金基准和创建顺序。</summary>
     * <param name="id">已有活动委托 ID。</param>
     * <param name="request">替换后的完整配置。</param>
     * <returns>成功结果或中文拒绝原因；失败保留原配置、结果与冻结额度。</returns>
     */
    internal TradeOrderCommandResult Update(int id, TradeOrderRequest request)
    {
        Order? order = Find(id);
        if (order == null || IsTerminal(order))
            return Rejected(id, "委托不存在或已经结束");
        string? error = Prepare(request, order.CashBasisCents, order, out int reserve,
            out int quantity, out int frozenCents, out int frozenQuantity);
        if (error != null)
            return Rejected(id, error);
        var replacement = new Order
        {
            Id = id,
            Request = CopyRequest(request),
            CashBasisCents = order.CashBasisCents,
            ReserveCents = reserve,
            LockedQuantity = quantity,
            FrozenCents = frozenCents,
            FrozenQuantity = frozenQuantity,
            Status = request.Frequency == TradeOrderFrequency.Continuous && order.Status == TradeOrderStatus.Disabled
                ? TradeOrderStatus.Disabled : TradeOrderStatus.Waiting,
            WaitingReason = "等待下一经营秒检查",
            LastFill = order.LastFill,
        };
        ReplaceResources(order, replacement);
        _orders[id - 1] = replacement;
        return new TradeOrderCommandResult(true, id, null);
    }

    /**
     * <summary>撤销活动委托并释放该单全部冻结资源，记录仍可查询。</summary>
     * <param name="id">活动委托 ID。</param>
     * <returns>成功或委托已结束的正常拒绝。</returns>
     */
    internal TradeOrderCommandResult Cancel(int id)
    {
        Order? order = Find(id);
        if (order == null || IsTerminal(order))
            return Rejected(id, "委托不存在或已经结束");
        Release(order);
        order.Status = TradeOrderStatus.Cancelled;
        order.WaitingReason = null;
        return new TradeOrderCommandResult(true, id, null);
    }

    /**
     * <summary>启用或停用持续策略；一次单通过撤销结束。</summary>
     * <param name="id">未结束的持续策略 ID。</param>
     * <param name="enabled">是否在后续经营秒执行。</param>
     * <returns>成功或不支持启停的正常拒绝。</returns>
     */
    internal TradeOrderCommandResult SetEnabled(int id, bool enabled)
    {
        Order? order = Find(id);
        if (order == null || IsTerminal(order) || order.Request.Frequency != TradeOrderFrequency.Continuous)
            return Rejected(id, "仅活动持续策略可以启用或停用");
        order.Status = enabled ? TradeOrderStatus.Waiting : TradeOrderStatus.Disabled;
        order.WaitingReason = enabled ? "等待下一经营秒检查" : "策略已停用";
        return new TradeOrderCommandResult(true, id, null);
    }

    /**
     * <summary>按建单顺序执行一个未暂停经营秒的委托检查。</summary>
     * <param name="calendar">生产、领取、工人与行情推进后的日历快照。</param>
     * <remarks>每单最多成交一次，后单读取前单成交后的库存和现金；失败不改变资源。</remarks>
     */
    internal void Execute(CalendarSnapshot calendar)
    {
        if (calendar.IsPaused)
            return;
        _nextCheckRequired = false;
        foreach (Order order in _orders)
        {
            if (order.Status != TradeOrderStatus.Waiting)
                continue;
            TradeOrderRequest request = order.Request;
            int price = _market.GetQuote(request.Commodity).PriceCents;
            OrderEvaluationObservation? detail = _log?.BeginEvaluation(order.Id, request.ConditionGroups.Count);
            string? reason = UnmetConditions(request, price, calendar.Season, detail,
                out TradeOrderBlocker blockers, out TradeOrderBlocker primary, out bool allGroupsChecked);
            bool conditionGroupsSatisfied = reason == null;
            if (reason == null && request.BudgetMode == TradeOrderBudgetMode.LimitPrice && price > request.LimitPriceCents)
            {
                reason = "当前报价高于最高买入限价";
                blockers = primary = TradeOrderBlocker.LimitPrice;
            }
            int quantity = request.Frequency == TradeOrderFrequency.Once ? order.LockedQuantity :
                QuantityFor(request);
            if (request.BudgetMode == TradeOrderBudgetMode.FixedBudget)
                quantity = BudgetQuantity(request.BudgetCents, price);
            if (reason == null && quantity <= 0)
            {
                reason = request.BudgetMode == TradeOrderBudgetMode.FixedBudget
                    ? "预算不足以完整买入一份商品及手续费" : "当前库存已达到数量目标";
                blockers = primary = request.BudgetMode == TradeOrderBudgetMode.FixedBudget
                    ? TradeOrderBlocker.BudgetInsufficient : TradeOrderBlocker.TargetReached;
            }
            if (reason != null)
            {
                order.WaitingReason = reason;
                detail?.Complete(new(blockers, primary, TradeFailure.None, false), conditionGroupsSatisfied, quantity,
                    request.BudgetMode == TradeOrderBudgetMode.FixedBudget ? request.BudgetCents : null);
                _log?.Evaluated(order.Id, new(blockers, primary, TradeFailure.None, false), calendar);
                continue;
            }
            OrderFillObservation? observation = _log?.BeforeFill(Snapshot(order));
            TradeResult result = request.Side == TradeOrderSide.Buy
                ? _trading.BuyOrder(request.Commodity, quantity, order.ReserveCents, order.FrozenCents)
                : _trading.SellOrder(request.Commodity, quantity, order.FrozenQuantity);
            if (!result.Success)
            {
                order.WaitingReason = result.ErrorMessage;
                detail?.Complete(new(TradeOrderBlocker.None, TradeOrderBlocker.None, result.Failure, false), conditionGroupsSatisfied, quantity,
                    request.BudgetMode == TradeOrderBudgetMode.FixedBudget ? request.BudgetCents : null);
                _log?.Evaluated(order.Id, new(TradeOrderBlocker.None, TradeOrderBlocker.None, result.Failure, false), calendar);
                continue;
            }
            order.LastFill = new TradeOrderFillSnapshot(request.Commodity, request.Side, result, _wallet.BalanceCents);
            order.WaitingReason = null;
            _nextCheckRequired = true;
            if (request.Frequency == TradeOrderFrequency.Once)
            {
                order.FrozenCents = order.FrozenQuantity = 0;
                order.Status = TradeOrderStatus.Completed;
            }
            _log?.Filled(observation, Snapshot(order), result);
            detail?.Complete(new(TradeOrderBlocker.None, TradeOrderBlocker.None, TradeFailure.None, allGroupsChecked), conditionGroupsSatisfied, quantity,
                request.BudgetMode == TradeOrderBudgetMode.FixedBudget ? request.BudgetCents : null);
            _log?.Evaluated(order.Id, new(TradeOrderBlocker.None, TradeOrderBlocker.None, TradeFailure.None, allGroupsChecked), calendar);
        }
    }

    private string? Prepare(TradeOrderRequest request, int basis, Order? previous,
        out int reserve, out int quantity, out int frozenCents, out int frozenQuantity)
    {
        reserve = quantity = frozenCents = frozenQuantity = 0;
        string? error = Validate(request);
        if (error != null)
            return error;
        reserve = request.Side == TradeOrderSide.Buy
            ? request.ReserveMode == CashReserveMode.Amount ? request.ReserveValue
                : (int)(((long)basis * request.ReserveValue + 99) / 100) : 0;
        if (request.Frequency == TradeOrderFrequency.Continuous)
            return null;
        bool keepLockedQuantity = previous != null && previous.Request.Frequency == TradeOrderFrequency.Once &&
            previous.Request.Commodity == request.Commodity && previous.Request.Side == request.Side &&
            previous.Request.QuantityMode == request.QuantityMode && previous.Request.Quantity == request.Quantity &&
            previous.Request.BudgetMode == request.BudgetMode;
        quantity = request.BudgetMode == TradeOrderBudgetMode.FixedBudget ? 0 :
            keepLockedQuantity ? previous!.LockedQuantity : QuantityFor(request);
        if (request.BudgetMode != TradeOrderBudgetMode.FixedBudget && quantity <= 0)
            return "一次委托的目标差额须为正数";
        if (request.Side == TradeOrderSide.Buy)
        {
            long total = (long)quantity * request.LimitPriceCents;
            long allocation = request.BudgetMode == TradeOrderBudgetMode.FixedBudget
                ? request.BudgetCents : total + (total + 99) / 100;
            long available = (long)_wallet.AvailableCents + (previous?.FrozenCents ?? 0);
            if (allocation > available)
                return "可用金币不足以冻结委托预算";
            if (available - allocation < reserve)
                return "冻结委托预算后现金低于保留金额";
            frozenCents = (int)allocation;
        }
        else
        {
            int own = previous?.Request.Commodity == request.Commodity ? previous.FrozenQuantity : 0;
            if (quantity > (long)_inventory.GetAvailable(request.Commodity) + own)
                return "可用库存不足以冻结委托数量";
            frozenQuantity = quantity;
        }
        return null;
    }

    private static string? Validate(TradeOrderRequest request)
    {
        if (request == null || !request.Commodity.IsDefined)
            return "无效委托商品";
        if (!Enum.IsDefined(request.Side) || !Enum.IsDefined(request.Frequency) ||
            !Enum.IsDefined(request.QuantityMode) || !Enum.IsDefined(request.BudgetMode) ||
            !Enum.IsDefined(request.ReserveMode))
            return "无效委托配置";
        if (request.ReserveValue < 0 || request.ReserveMode == CashReserveMode.Percent && request.ReserveValue > 100)
            return "现金保留须为非负金额或 0 到 100 的整数百分比";
        bool fixedBudget = request.BudgetMode == TradeOrderBudgetMode.FixedBudget;
        bool onceBuy = request.Side == TradeOrderSide.Buy && request.Frequency == TradeOrderFrequency.Once;
        if (onceBuy ? request.BudgetMode == TradeOrderBudgetMode.None : request.BudgetMode != TradeOrderBudgetMode.None)
            return "一次买单须选择固定预算或最高限价，其他委托不设置冻结预算";
        if (fixedBudget && (request.BudgetCents <= 0 || request.QuantityMode != TradeOrderQuantityMode.Fixed))
            return "固定预算须为正金额，并在成交时按预算计算数量";
        if (request.BudgetMode == TradeOrderBudgetMode.LimitPrice && request.LimitPriceCents <= 0)
            return "最高买入限价须为正金额";
        if (!fixedBudget && (request.Quantity < 0 || request.QuantityMode != TradeOrderQuantityMode.SellToTarget && request.Quantity == 0))
            return "固定数量及补货目标须为正数，卖出剩余目标可为零";
        if (request.Side == TradeOrderSide.Buy && request.QuantityMode == TradeOrderQuantityMode.SellToTarget ||
            request.Side == TradeOrderSide.Sell && request.QuantityMode == TradeOrderQuantityMode.BuyToTarget)
            return "数量模式与买卖方向不符";
        if (request.ConditionGroups == null || request.ConditionGroups.Count == 0)
            return "至少设置一组触发条件";
        foreach (IReadOnlyList<TradeOrderCondition> group in request.ConditionGroups)
        {
            if (group == null || group.Count == 0)
                return "每组至少设置一个条件";
            foreach (TradeOrderCondition condition in group)
            {
                if (condition == null || !Enum.IsDefined(condition.Factor) || !Enum.IsDefined(condition.Comparison) || condition.Value < 0)
                    return "无效触发条件";
                if (condition.Factor == TradeConditionFactor.Season &&
                    (condition.Comparison != TradeConditionComparison.Equal || !Enum.IsDefined((Season)condition.Value)))
                    return "季节条件须等于春、夏、秋、冬之一";
            }
        }
        return null;
    }

    private int QuantityFor(TradeOrderRequest request)
    {
        int stock = _inventory.Get(request.Commodity);
        return request.QuantityMode switch
        {
            TradeOrderQuantityMode.Fixed => request.Quantity,
            TradeOrderQuantityMode.BuyToTarget => Math.Max(0, request.Quantity - stock),
            TradeOrderQuantityMode.SellToTarget => Math.Max(0, stock - request.Quantity),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    private static int BudgetQuantity(int budget, int price)
    {
        int low = 0;
        int high = budget / price;
        while (low < high)
        {
            int middle = low + (int)(((long)high - low + 1) / 2);
            long total = (long)middle * price;
            if (total + (total + 99) / 100 <= budget)
                low = middle;
            else
                high = middle - 1;
        }
        return low;
    }

    private string? UnmetConditions(TradeOrderRequest request, int price, Season season, OrderEvaluationObservation? detail,
        out TradeOrderBlocker blockers, out TradeOrderBlocker primary, out bool allGroupsChecked)
    {
        blockers = primary = TradeOrderBlocker.None;
        allGroupsChecked = false;
        int visitedGroups = 0;
        var groups = new List<string>();
        foreach (IReadOnlyList<TradeOrderCondition> group in request.ConditionGroups)
        {
            visitedGroups++;
            detail?.BeginGroup(group.Count);
            var reasons = new List<string>();
            foreach (TradeOrderCondition condition in group)
            {
                int actual = condition.Factor switch
                {
                    TradeConditionFactor.Price => price,
                    TradeConditionFactor.Stock => _inventory.Get(request.Commodity),
                    TradeConditionFactor.Season => (int)season,
                    _ => throw new ArgumentOutOfRangeException(nameof(request)),
                };
                bool satisfied = condition.Comparison switch
                {
                    TradeConditionComparison.Less => actual < condition.Value,
                    TradeConditionComparison.LessOrEqual => actual <= condition.Value,
                    TradeConditionComparison.Equal => actual == condition.Value,
                    TradeConditionComparison.GreaterOrEqual => actual >= condition.Value,
                    TradeConditionComparison.Greater => actual > condition.Value,
                    _ => throw new ArgumentOutOfRangeException(nameof(request)),
                };
                detail?.Condition(condition, actual, satisfied);
                if (!satisfied)
                {
                    reasons.Add($"{FactorName(condition.Factor)}条件未满足（当前 {actual}）");
                    TradeOrderBlocker blocker = condition.Factor switch
                    {
                        TradeConditionFactor.Price => TradeOrderBlocker.Price,
                        TradeConditionFactor.Stock => TradeOrderBlocker.Stock,
                        TradeConditionFactor.Season => TradeOrderBlocker.Season,
                        _ => throw new ArgumentOutOfRangeException(nameof(request)),
                    };
                    blockers |= blocker;
                    if (primary == TradeOrderBlocker.None) primary = blocker;
                }
            }
            if (reasons.Count == 0)
            {
                blockers = primary = TradeOrderBlocker.None;
                allGroupsChecked = visitedGroups == request.ConditionGroups.Count;
                return null;
            }
            groups.Add(string.Join("，", reasons));
        }
        return string.Join("；或：", groups);
    }

    private static TradeOrderSnapshot Snapshot(Order order) => new(order.Id, order.Request, order.Status,
        order.CashBasisCents, order.ReserveCents, order.LockedQuantity, order.FrozenCents,
        order.FrozenQuantity, order.WaitingReason, order.LastFill);

    private static string FactorName(TradeConditionFactor factor) => factor switch
    {
        TradeConditionFactor.Price => "价格",
        TradeConditionFactor.Stock => "库存",
        TradeConditionFactor.Season => "季节",
        _ => throw new ArgumentOutOfRangeException(nameof(factor)),
    };

    private void ReplaceResources(Order? previous, Order next)
    {
        if (previous != null)
            Release(previous);
        if (!_wallet.TryReplaceFrozen(0, next.FrozenCents) ||
            !_inventory.TryReplaceFrozen(next.Request.Commodity, 0, next.FrozenQuantity))
            throw new InvalidOperationException("委托冻结与完整预检不一致");
    }

    private void Release(Order order)
    {
        _wallet.TryReplaceFrozen(order.FrozenCents, 0);
        _inventory.TryReplaceFrozen(order.Request.Commodity, order.FrozenQuantity, 0);
        order.FrozenCents = order.FrozenQuantity = 0;
    }

    private Order? Find(int id) => id > 0 && id <= _orders.Count ? _orders[id - 1] : null;
    private static bool IsTerminal(Order order) => order.Status is TradeOrderStatus.Completed or TradeOrderStatus.Cancelled;
    private static TradeOrderCommandResult Rejected(int id, string error) => new(false, id, error);

    private static TradeOrderRequest CopyRequest(TradeOrderRequest request)
    {
        var groups = new IReadOnlyList<TradeOrderCondition>[request.ConditionGroups.Count];
        for (int i = 0; i < groups.Length; i++)
        {
            var conditions = new TradeOrderCondition[request.ConditionGroups[i].Count];
            for (int j = 0; j < conditions.Length; j++)
                conditions[j] = request.ConditionGroups[i][j];
            groups[i] = Array.AsReadOnly(conditions);
        }
        return request with { ConditionGroups = Array.AsReadOnly(groups) };
    }
}
