using System;
using System.Text.Json.Nodes;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Terrain;

/**
 * <summary>既有无水基准显式选择的合法配置；仍走正式 JSON 解析及完整地形创建。</summary>
 */
internal static class TerrainTestFixtures
{
    private static readonly Lazy<TerrainRules> AllLandInput = new(CreateAllLandRules);
    private static readonly Lazy<TerrainMap> AllLandMap = new(() => TerrainMap.Create(12345, AllLandRules));

    internal static TerrainRules AllLandRules => AllLandInput.Value;
    internal static TerrainReadView AllLandTerrain => AllLandMap.Value.ReadOnly;

    private static TerrainRules CreateAllLandRules()
    {
        using FileAccess file = FileAccess.Open(TerrainRules.ResourcePath, FileAccess.ModeFlags.Read);
        JsonObject json = JsonNode.Parse(file.GetAsText())!.AsObject();
        json["Small"]!["Count"] = 0;
        json["Medium"]!["Count"] = 0;
        json["Large"]!["Count"] = 0;
        json["WaterCoverage"]!["Target"] = 0;
        json["WaterCoverage"]!["Min"] = 0;
        json["WaterCoverage"]!["Max"] = 0;
        return FarmGame.ParseTerrainRules(json.ToJsonString(), "既有纯陆地基准 JSON");
    }
}
