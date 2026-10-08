using System.Collections.Generic;
using FarmExchange.Inventory;

namespace FarmExchange.Trading;

/**
 * <summary>全部加工品原子结算的真实合计与按作物目录排列的七条商品明细。</summary>
 * <remarks>拒绝时合计和每行实际数量、货值为零，保留预检实际读取的单价；不把请求量当成交量。</remarks>
 */
public sealed record ProductSaleResult(TradeResult Trade, IReadOnlyList<TradeLineResult> Lines);

/**
 * <summary>全部加工品结算中单个商品的实际份数、执行单价与货值，金额单位为分。</summary>
 */
public readonly record struct TradeLineResult(CommodityId Commodity, int Quantity, int UnitPriceCents, long ValueCents);
