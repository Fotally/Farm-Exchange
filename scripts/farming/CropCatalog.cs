using System;
using System.Collections.Generic;
using FarmExchange.Gameplay;

namespace FarmExchange.Farming;

internal static class CropCatalog
{
    private static readonly CropDefinition[] Definitions =
    {
        new(CropKind.Wheat, "小麦", "磨坊", "面粉", 16, 2, 4, 100, 50),
        new(CropKind.Corn, "玉米", "玉米加工坊", "玉米粉", 20, 4, 6, 100, 50),
        new(CropKind.Rice, "水稻", "碾米坊", "大米", 12, 3, 4, 120, 50),
        new(CropKind.Potato, "马铃薯", "淀粉坊", "淀粉", 14, 8, 2, 80, 50),
        new(CropKind.Sunflower, "向日葵", "榨油坊", "葵花籽油", 17, 1, 12, 160, 50),
        new(CropKind.Sugarcane, "甘蔗", "制糖坊", "蔗糖", 40, 12, 4, 200, 50),
        new(CropKind.Radish, "萝卜", "腌制坊", "腌萝卜", 4, 6, 1, 10, 50),
    };

    internal static IReadOnlyList<CropDefinition> Crops { get; } = Array.AsReadOnly(Definitions);

    internal static bool IsDefined(CropKind crop) => (uint)crop < Definitions.Length;

    internal static CropDefinition Get(CropKind crop)
    {
        if (!IsDefined(crop))
            throw new ArgumentOutOfRangeException(nameof(crop));
        return Definitions[(int)crop];
    }
}
