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

    internal int GetRaw(CropKind crop) => _rawStock[IndexOf(crop)];
    internal int GetProduct(CropKind crop) => _productStock[IndexOf(crop)];
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

    internal void AddRaw(CropKind crop, int quantity)
    {
        int index = IndexOf(crop);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        _rawStock[index] = checked(_rawStock[index] + quantity);
    }

    internal void AddProduct(CropKind crop, int quantity)
    {
        int index = IndexOf(crop);
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        _productStock[index] = checked(_productStock[index] + quantity);
    }

    internal bool TryTakeRawForProcessing(CropKind crop)
    {
        int index = IndexOf(crop);
        if (GetProcessingAvailability(crop) != RawProcessingAvailability.Available)
            return false;
        _rawStock[index]--;
        return true;
    }

    internal void TakeAllRaw(CropKind crop) => _rawStock[IndexOf(crop)] = 0;

    internal void TakeAllProducts() => Array.Clear(_productStock);

    private static int IndexOf(CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        return (int)crop;
    }
}
