using FarmExchange.Inventory;

namespace FarmExchange.Market;

public readonly record struct CommodityDefinition(CommodityId Id, string Name, int InitialPriceCents);
