using System.Collections.Generic;
using FarmExchange.Inventory;

namespace FarmExchange.Trading;

public enum TradeOrderSide { Buy, Sell }
public enum TradeOrderFrequency { Once, Continuous }
public enum TradeOrderQuantityMode { Fixed, BuyToTarget, SellToTarget }
public enum TradeOrderBudgetMode { None, FixedBudget, LimitPrice }
public enum CashReserveMode { Amount, Percent }
public enum TradeConditionFactor { Price, Stock, Season }
public enum TradeConditionComparison { Less, LessOrEqual, Equal, GreaterOrEqual, Greater }
public enum TradeOrderStatus { Waiting, Disabled, Completed, Cancelled }

public sealed record TradeOrderCondition(
    TradeConditionFactor Factor, TradeConditionComparison Comparison, int Value);

/** <summary>玩家完整设置；条件组内全部满足，组间任意满足。</summary> */
public sealed record TradeOrderRequest(
    CommodityId Commodity, TradeOrderSide Side, TradeOrderFrequency Frequency,
    TradeOrderQuantityMode QuantityMode, int Quantity, TradeOrderBudgetMode BudgetMode,
    int BudgetCents, int LimitPriceCents, CashReserveMode ReserveMode, int ReserveValue,
    IReadOnlyList<IReadOnlyList<TradeOrderCondition>> ConditionGroups);
