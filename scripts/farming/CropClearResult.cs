using FarmExchange.Gameplay;

namespace FarmExchange.Farming;

/**
 * <summary>一次换季实际清理的逐作物轮数；空田和已收获轮次不计入。</summary>
 */
internal sealed class CropClearResult
{
    private readonly int[] _counts = new int[CropCatalog.Crops.Count];
    internal void Record(CropKind crop) => _counts[(int)crop]++;
    internal int Count(CropKind crop) => _counts[(int)crop];
}
