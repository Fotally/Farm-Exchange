using FarmExchange.Inventory;

namespace FarmExchange.Trading;

/** <summary>最近一次真实成交的商品、方向、结算结果与成交后总余额。</summary> */
public sealed record TradeOrderFillSnapshot(
    CommodityId Commodity, TradeOrderSide Side, TradeResult Trade, int BalanceCents);

/** <summary>独立只读委托快照，保留原现金基准、冻结归属和最近一次真实成交。</summary> */
public sealed record TradeOrderSnapshot(
    int Id, TradeOrderRequest Request, TradeOrderStatus Status,
    int CashBasisCents, int ReserveCents, int LockedQuantity,
    int FrozenCents, int FrozenQuantity, string? WaitingReason,
    TradeOrderFillSnapshot? LastFill);

public readonly record struct TradeOrderCommandResult(bool Success, int Id, string? ErrorMessage);
