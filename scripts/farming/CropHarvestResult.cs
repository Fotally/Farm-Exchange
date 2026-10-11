using FarmExchange.Gameplay;

namespace FarmExchange.Farming;

/**
 * <summary>一轮正常成熟或换季促熟已经结算的实际收获，调用方按数量入库。</summary>
 */
internal readonly record struct CropHarvestResult(CropKind Crop, int Quantity);
