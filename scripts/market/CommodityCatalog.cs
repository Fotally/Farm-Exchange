using System;
using System.Collections.Generic;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;

namespace FarmExchange.Market;

public static class CommodityCatalog
{
    private static readonly int[] RawInitialPricesCents = { 250, 250, 300, 200, 400, 500, 25 };
    private static readonly int[] ProductInitialPricesCents = { 500, 500, 600, 400, 800, 1000, 50 };

    public static IReadOnlyList<CommodityDefinition> All { get; } = BuildDefinitions();

    public static bool IsDefined(CommodityId commodity) => commodity.IsDefined;

    public static CommodityDefinition Get(CommodityId commodity)
    {
        if (!IsDefined(commodity))
            throw new ArgumentOutOfRangeException(nameof(commodity));
        return All[(int)commodity.Crop * 2 + (int)commodity.Kind];
    }

    private static IReadOnlyList<CommodityDefinition> BuildDefinitions()
    {
        var definitions = new CommodityDefinition[CropCatalog.Crops.Count * 2];
        foreach (CropDefinition crop in CropCatalog.Crops)
        {
            int index = (int)crop.Kind;
            definitions[index * 2] = new CommodityDefinition(new CommodityId(crop.Kind, CommodityKind.Raw),
                crop.CropName + "原料", RawInitialPricesCents[index]);
            definitions[index * 2 + 1] = new CommodityDefinition(new CommodityId(crop.Kind, CommodityKind.Product),
                crop.ProductName + "加工品", ProductInitialPricesCents[index]);
        }
        return Array.AsReadOnly(definitions);
    }
}
