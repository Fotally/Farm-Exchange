using System;

namespace FarmExchange.Trading;

public enum TradeFailure
{
    None, InvalidCommodity, InvalidQuantity, InsufficientFunds, InsufficientStock,
    InventoryCapacityExceeded, WalletCapacityExceeded, CashReserveNotMet,
}

public readonly record struct TradeResult(TradeFailure Failure, long Quantity, long TotalCents)
{
    public long FeeCents { get; init; }
    /**
     * <summary>买入结算实际读取的单价，单位为分；未执行报价读取时为 null。</summary>
     * <remarks>当前由 Buy 与 BuyOrder 提供；不是记录时另查的报价，其他交易路径本阶段保持 null。</remarks>
     */
    public int? UnitPriceCents { get; init; }
    public bool Success => Failure == TradeFailure.None;
    public string? ErrorMessage => Failure switch
    {
        TradeFailure.None => null,
        TradeFailure.InvalidCommodity => "无效商品",
        TradeFailure.InvalidQuantity => "交易数量须为 1 到 2147483647 之间的整数",
        TradeFailure.InsufficientFunds => "金币不足，无法买入",
        TradeFailure.InsufficientStock => "公共库存不足，无法卖出",
        TradeFailure.InventoryCapacityExceeded => "商品库存容量不足",
        TradeFailure.WalletCapacityExceeded => "金币余额容量不足",
        TradeFailure.CashReserveNotMet => "买入后现金低于委托保留金额",
        _ => throw new ArgumentOutOfRangeException(nameof(Failure)),
    };
}
