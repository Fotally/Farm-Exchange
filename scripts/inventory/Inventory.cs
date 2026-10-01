using System;
using FarmExchange.Farming;
using FarmExchange.Gameplay;

namespace FarmExchange.Inventory;

internal enum RawProcessingAvailability { Available, NoRaw, Reserved }

internal sealed class Inventory
{
    private readonly int[] _rawStock = new int[CropCatalog.Crops.Count];
    private readonly int[] _productStock = new int[CropCatalog.Crops.Count];
    private readonly int[] _rawReserve = new int[CropCatalog.Crops.Count];

    internal int Get(CommodityId commodity) => StocksFor(commodity)[(int)commodity.Crop];
    internal int GetRaw(CropKind crop) => Get(new CommodityId(crop, CommodityKind.Raw));
    internal int GetProduct(CropKind crop) => Get(new CommodityId(crop, CommodityKind.Product));
    internal int GetRawReserve(CropKind crop) => _rawReserve[IndexOf(crop)];

    internal void SetRawReserve(CropKind crop, int quantity)
    {
        int index = IndexOf(crop);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        _rawReserve[index] = quantity;
    }

    internal RawProcessingAvailability GetProcessingAvailability(CropKind crop)
    {
        int index = IndexOf(crop);
        if (_rawStock[index] == 0)
            return RawProcessingAvailability.NoRaw;
        return _rawStock[index] > _rawReserve[index]
            ? RawProcessingAvailability.Available : RawProcessingAvailability.Reserved;
    }

    internal void Add(CommodityId commodity, int quantity)
    {
        int[] stocks = StocksFor(commodity);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        int index = (int)commodity.Crop;
        stocks[index] = checked(stocks[index] + quantity);
    }

    internal void Remove(CommodityId commodity, int quantity)
    {
        int[] stocks = StocksFor(commodity);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        int index = (int)commodity.Crop;
        if (stocks[index] < quantity)
            throw new InvalidOperationException("公共库存不足");
        stocks[index] -= quantity;
    }

    internal void AddRaw(CropKind crop, int quantity) =>
        Add(new CommodityId(crop, CommodityKind.Raw), quantity);
    internal void AddProduct(CropKind crop, int quantity) =>
        Add(new CommodityId(crop, CommodityKind.Product), quantity);

    internal bool TryTakeRawForProcessing(CropKind crop)
    {
        int index = IndexOf(crop);
        if (GetProcessingAvailability(crop) != RawProcessingAvailability.Available)
            return false;
        _rawStock[index]--;
        return true;
    }

    internal void TakeAllRaw(CropKind crop) =>
        Remove(new CommodityId(crop, CommodityKind.Raw), GetRaw(crop));

    internal void TakeAllProducts()
    {
        foreach (CropDefinition crop in CropCatalog.Crops)
            Remove(new CommodityId(crop.Kind, CommodityKind.Product), GetProduct(crop.Kind));
    }

    private int[] StocksFor(CommodityId commodity)
    {
        if (!commodity.IsDefined)
            throw new ArgumentOutOfRangeException(nameof(commodity));
        return commodity.Kind == CommodityKind.Raw ? _rawStock : _productStock;
    }

    private static int IndexOf(CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        return (int)crop;
    }
}
