using System.Collections.Generic;
using FarmExchange.Inventory;
using FarmExchange.Time;

namespace FarmExchange.Market;

public readonly record struct MarketQuoteSnapshot(
    CommodityId Id, int PriceCents, int PreviousPriceCents, decimal ChangePercent);

public readonly record struct MarketNewsSnapshot(
    GameDate PublishedDate, GameDate QuoteDate, IReadOnlyList<string> Lines);

public readonly record struct MarketSnapshot(
    GameDate LastQuoteDate, GameDate NextQuoteDate, IReadOnlyList<MarketQuoteSnapshot> Quotes,
    MarketNewsSnapshot? News);
