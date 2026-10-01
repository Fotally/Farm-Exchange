using FarmExchange.Farming;
using FarmExchange.Gameplay;

namespace FarmExchange.Inventory;

public enum CommodityKind { Raw, Product }

public readonly record struct CommodityId(CropKind Crop, CommodityKind Kind)
{
    public bool IsDefined => CropCatalog.IsDefined(Crop) &&
        Kind is CommodityKind.Raw or CommodityKind.Product;
}
