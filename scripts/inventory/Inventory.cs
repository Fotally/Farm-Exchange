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
    private readonly int[] _frozen = new int[CropCatalog.Crops.Count * 2];

    internal int Get(CommodityId commodity) => StocksFor(commodity)[(int)commodity.Crop];
    internal int GetFrozen(CommodityId commodity)
    {
        StocksFor(commodity);
        return _frozen[(int)commodity.Crop * 2 + (int)commodity.Kind];
    }
    internal int GetAvailable(CommodityId commodity) => Get(commodity) - GetFrozen(commodity);
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
        int available = GetAvailable(new CommodityId(crop, CommodityKind.Raw));
        if (available == 0)
            return RawProcessingAvailability.NoRaw;
        return available > _rawReserve[index]
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
        if (GetAvailable(commodity) < quantity)
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
        Remove(new CommodityId(crop, CommodityKind.Raw), GetAvailable(new CommodityId(crop, CommodityKind.Raw)));

    internal void TakeAllProducts()
    {
        foreach (CropDefinition crop in CropCatalog.Crops)
            Remove(new CommodityId(crop.Kind, CommodityKind.Product), GetAvailable(new CommodityId(crop.Kind, CommodityKind.Product)));
    }

    /**
     * <summary>完整替换一张委托对同一商品的冻结数量。</summary>
     * <param name="commodity">合法公共商品。</param>
     * <param name="previousQuantity">该单原冻结数量。</param>
     * <param name="nextQuantity">新冻结数量。</param>
     * <returns>可用库存加原单额度足够时成功；失败不修改。</returns>
     */
    internal bool TryReplaceFrozen(CommodityId commodity, int previousQuantity, int nextQuantity)
    {
        int frozen = GetFrozen(commodity);
        if (previousQuantity < 0 || previousQuantity > frozen || nextQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(previousQuantity));
        if (nextQuantity > GetAvailable(commodity) + previousQuantity)
            return false;
        _frozen[(int)commodity.Crop * 2 + (int)commodity.Kind] = frozen - previousQuantity + nextQuantity;
        return true;
    }

    /**
     * <summary>提交已完整预检的委托出售，并释放该单全部冻结库存。</summary>
     * <param name="commodity">合法公共商品。</param>
     * <param name="quantity">本次完整卖出数量。</param>
     * <param name="frozenQuantity">本单拥有的冻结数量，持续单为零。</param>
     * <remarks>不消费其他委托冻结的库存。</remarks>
     */
    internal void RemoveForOrder(CommodityId commodity, int quantity, int frozenQuantity)
    {
        if (quantity < 0 || frozenQuantity < 0 || frozenQuantity > GetFrozen(commodity) ||
            quantity > GetAvailable(commodity) + frozenQuantity)
            throw new InvalidOperationException("委托库存与预检不一致");
        _frozen[(int)commodity.Crop * 2 + (int)commodity.Kind] -= frozenQuantity;
        StocksFor(commodity)[(int)commodity.Crop] -= quantity;
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
