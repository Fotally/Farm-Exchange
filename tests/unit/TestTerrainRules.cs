using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Terrain;

public partial class TestTerrainRules : Node
{
    private const string Source = "terrain-rules-test";

    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
        using var game = new FarmGame(110, terrainRules: TerrainTestFixtures.AllLandRules);
        Vector2I[] cells = game.GetBuildingSpaces().SelectMany(space =>
            space.Footprint.Offsets.Select(offset => space.AnchorCell + offset)).ToArray();
        Vector2[] starts = game.GetWorkers().Select(worker => worker.GridPosition).ToArray();
        string json = FileAccess.GetFileAsString(TerrainRules.ResourcePath);
        TerrainRules Parse(string value) => TerrainRules.Parse(value, Source, FarmGame.MapSize, cells, starts);
        TerrainRules standard = TerrainRules.Load(TerrainRules.ResourcePath, FarmGame.MapSize, cells, starts);
        if (standard.SchemaVersion != 1 || standard.GenerationVersion != 1 || standard.TotalPondCount != 48 ||
            standard.Small.Count != 16 || standard.Medium.Count != 24 || standard.Large.Count != 8 ||
            standard.Small.LongAxis != new TerrainRules.NumberRange(8, 14) ||
            standard.Small.ShortAxis != new TerrainRules.NumberRange(6, 10) ||
            standard.Medium.LongAxis != new TerrainRules.NumberRange(18, 26) ||
            standard.Medium.ShortAxis != new TerrainRules.NumberRange(12, 18) ||
            standard.Large.LongAxis != new TerrainRules.NumberRange(28, 36) ||
            standard.Large.ShortAxis != new TerrainRules.NumberRange(18, 24) ||
            standard.WaterCoverage != new TerrainRules.Coverage(.08m, .06m, .10m) ||
            standard.MinimumPondSeparation != 8 || standard.MapEdgeLandWidth != 8 ||
            standard.MaximumOutlinePerturbation != .15m || standard.WorkerFootRadius != .125m ||
            standard.CenterProtection != new TerrainRules.Protection(24, 24, 2) ||
            standard.NearPond != new TerrainRules.NearPondRules(10, 7, new(14, 18)) ||
            standard.ProtectionCells != new Rect2I(180, 180, 24, 24) || standard.IsAllLand ||
            standard.MapSize != FarmGame.MapSize || standard.Source != TerrainRules.ResourcePath)
            return Fail("正式参数或派生中心保护范围错误。");

        string allLand = Edit(json, root =>
        {
            foreach (string band in new[] { "Small", "Medium", "Large" })
                root[band]!["Count"] = 0;
            foreach (string field in new[] { "Min", "Target", "Max" })
                root["WaterCoverage"]![field] = 0;
        });
        TerrainRules landRules = Parse(allLand);
        if (!landRules.IsAllLand || landRules.TotalPondCount != 0 || landRules.WorkerFootRadius != .125m ||
            standard.TotalPondCount != 48 || standard.WaterCoverage.Target != .08m)
            return Fail("纯陆地配置或独立对象隔离错误。");
        TerrainRules varied = Parse(Edit(json, root =>
        {
            root["Small"]!["Count"] = 18;
            root["WorkerFootRadius"] = .2;
            root["CenterProtection"]!["Width"] = 25;
            root["CenterProtection"]!["Height"] = 23;
            root["MaximumOutlinePerturbation"] = 0;
        }));
        if (varied.TotalPondCount != 50 || varied.WorkerFootRadius != .2m ||
            varied.ProtectionCells != new Rect2I(179, 180, 25, 23) || standard.WorkerFootRadius != .125m)
            return Fail("合法变体或奇数保护尺寸向小坐标取整错误。");

        foreach ((string path, JsonNode? value) in InvalidValues())
        {
            string invalid = Edit(json, root => Set(root, path, value));
            if (!Rejects(() => Parse(invalid)))
                return Fail($"非法字段未明确拒绝：{path}={value}。");
        }
        // 每一个对象的字段均不可省略、扩展或重复；遍历输入结构，不依赖解析器私有代码。
        JsonObject original = JsonNode.Parse(json)!.AsObject();
        foreach (string objectPath in ObjectPaths(original, ""))
        {
            JsonObject target = (objectPath.Length == 0 ? original : Get(original, objectPath))!.AsObject();
            string member = target.First().Key;
            string memberPath = objectPath.Length == 0 ? member : $"{objectPath}.{member}";
            if (!Rejects(() => Parse(Edit(json, root =>
                (objectPath.Length == 0 ? root : Get(root, objectPath))!.AsObject().Remove(member))), memberPath) ||
                !Rejects(() => Parse(Edit(json, root =>
                (objectPath.Length == 0 ? root : Get(root, objectPath))!["Unexpected"] = 1)), "Unexpected"))
                return Fail($"对象缺失/未知字段未拒绝：{objectPath}。");
            string duplicate = target.ToJsonString().Insert(1, $"\"{member}\":{target[member]!.ToJsonString()},");
            string invalidDuplicate = objectPath.Length == 0 ? duplicate :
                original.ToJsonString().Replace(target.ToJsonString(), duplicate, StringComparison.Ordinal);
            if (!Rejects(() => Parse(invalidDuplicate), memberPath))
                return Fail($"重复字段未拒绝：{memberPath}。");
        }
        foreach (string invalid in new[] { "null", "[]", "{", json.Replace("0.125", "1e999", StringComparison.Ordinal),
            json.Replace("0.125", "0.125000000000000000000000000001", StringComparison.Ordinal),
            json.Replace("0.125", "1e-999", StringComparison.Ordinal) })
            if (!Rejects(() => Parse(invalid)))
                return Fail("空根、语法或非有限数值未拒绝。");
        if (!Rejects(() => Parse(null!)) ||
            !Rejects(() => TerrainRules.Parse(json, "", FarmGame.MapSize, cells, starts), "Source", "<未指定来源>") ||
            !Rejects(() => TerrainRules.Parse(json, Source, 0, cells, starts), "MapSize") ||
            !Rejects(() => TerrainRules.Parse(json, Source, FarmGame.MapSize, null!, starts), "OpeningCells") ||
            !Rejects(() => TerrainRules.Parse(json, Source, FarmGame.MapSize, Array.Empty<Vector2I>(), starts), "OpeningCells") ||
            !Rejects(() => TerrainRules.Parse(json, Source, FarmGame.MapSize, new[] { Vector2I.Zero }, starts), "OpeningCells[0]") ||
            !Rejects(() => TerrainRules.Parse(json, Source, FarmGame.MapSize, cells, null!), "WorkerStarts") ||
            !Rejects(() => TerrainRules.Parse(json, Source, FarmGame.MapSize, cells, Array.Empty<Vector2>()), "WorkerStarts") ||
            !Rejects(() => TerrainRules.Parse(json, Source, FarmGame.MapSize, cells, new[] { new Vector2(float.NaN, 190) }), "WorkerStarts[0]") ||
            !Rejects(() => TerrainRules.Parse(json, Source, FarmGame.MapSize, cells, new[] { new Vector2(179.5f, 190) }), "WorkerStarts[0]"))
            return Fail("来源、地图、真实占地或圆脚底输入未完整检查。");
        standard.ValidateContext(FarmGame.MapSize, cells, starts);
        if (!Rejects(() => standard.ValidateContext(383, cells, starts), "MapSize", TerrainRules.ResourcePath) ||
            !Rejects(() => standard.ValidateContext(FarmGame.MapSize, new[] { Vector2I.Zero }, starts), "OpeningCells", TerrainRules.ResourcePath))
            return Fail("显式配置未重验实际建局上下文。");
        if (Parse(json.Replace("0.125", "125e-3", StringComparison.Ordinal)).WorkerFootRadius != .125m)
            return Fail("可无损表示的指数数值未接受。");
        foreach (string invalid in new[]
        {
            Edit(json, root => { root["Small"]!["LongAxis"]!["Min"] = 1; root["Small"]!["LongAxis"]!["Max"] = 2; }),
            Edit(json, root => { root["NearPond"]!["CenterDistance"]!["Min"] = 1; root["NearPond"]!["CenterDistance"]!["Max"] = 2; }),
            Edit(json, root => root["MapEdgeLandWidth"] = 150),
        })
            if (!Rejects(() => Parse(invalid)))
                return Fail("跨字段范围、退让或可用面积冲突未拒绝。");
        // 输入集合不被保存，调用方后续修改不改变已发布配置。
        cells[0] = Vector2I.Zero;
        starts[0] = Vector2.Zero;
        if (standard.ProtectionCells != new Rect2I(180, 180, 24, 24))
            return Fail("配置保存了可变开局输入。");
        cells = game.GetBuildingSpaces().SelectMany(space => space.Footprint.Offsets.Select(offset => space.AnchorCell + offset)).ToArray();
        starts = game.GetWorkers().Select(worker => worker.GridPosition).ToArray();
        foreach (string invalid in new[]
        {
            Edit(allLand, root => root["WorkerFootRadius"] = 0),
            Edit(allLand, root => root["WorkerFootRadius"] = 20),
            Edit(allLand, root => root["CenterProtection"]!["Width"] = 2),
            Edit(allLand, root => root["NearPond"]!["ShortAxis"] = 100),
            Edit(allLand, root => root["MapEdgeLandWidth"] = 192),
            Edit(allLand, root => root["WaterCoverage"]!["Max"] = .1),
        })
            if (!Rejects(() => Parse(invalid)))
                return Fail("纯陆地跳过了字段或开局校验。");

        string temporary = $"user://terrain_rules_test_{Guid.NewGuid():N}.json";
        try
        {
            Write(temporary, json);
            TerrainRules before = TerrainRules.Load(temporary, FarmGame.MapSize, cells, starts);
            Write(temporary, allLand);
            TerrainRules after = TerrainRules.Load(temporary, FarmGame.MapSize, cells, starts);
            if (before.IsAllLand || !after.IsAllLand || before.TotalPondCount != 48)
                return Fail("再次读取未生效或改写了之前的配置。");
            Write(temporary, "{}");
            if (!Rejects(() => TerrainRules.Load(temporary, FarmGame.MapSize, cells, starts), "SchemaVersion", temporary))
                return Fail("非法新文件未拒绝。");
        }
        finally
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(temporary));
        }
        if (!Rejects(() => TerrainRules.Load(temporary, FarmGame.MapSize, cells, starts), "$", temporary))
            return Fail("缺失配置未报告来源。");
        return true;
    }

    private static IEnumerable<(string, JsonNode?)> InvalidValues()
    {
        yield return ("SchemaVersion", JsonValue.Create(2));
        yield return ("GenerationVersion", JsonValue.Create(0));
        yield return ("Small.Count", JsonValue.Create(-1));
        yield return ("Small.Count", JsonValue.Create(1.5));
        yield return ("Small.Count", JsonValue.Create(2147483648L));
        yield return ("Small.Count", JsonValue.Create(0));
        yield return ("Small", null);
        yield return ("Small.LongAxis.Min", JsonValue.Create(0));
        yield return ("Small.LongAxis.Max", JsonValue.Create(2));
        yield return ("Small.ShortAxis.Max", JsonValue.Create("10"));
        yield return ("Large.ShortAxis.Min", JsonValue.Create(40));
        yield return ("WaterCoverage.Target", JsonValue.Create(.5));
        yield return ("WaterCoverage.Min", JsonValue.Create(-.1));
        yield return ("WaterCoverage.Min", JsonValue.Create(0));
        yield return ("WaterCoverage.Max", JsonValue.Create(1));
        yield return ("MinimumPondSeparation", JsonValue.Create(-1));
        yield return ("MinimumPondSeparation", JsonValue.Create(1000));
        yield return ("MapEdgeLandWidth", JsonValue.Create(192));
        yield return ("MapEdgeLandWidth", JsonValue.Create(184));
        yield return ("WaterCoverage.Min", JsonValue.Create(.5));
        yield return ("MaximumOutlinePerturbation", JsonValue.Create(-.1));
        yield return ("MaximumOutlinePerturbation", JsonValue.Create(1));
        yield return ("CenterProtection.Width", JsonValue.Create(385));
        yield return ("CenterProtection.ShoreClearance", JsonValue.Create(-1));
        yield return ("CenterProtection.ShoreClearance", JsonValue.Create(100));
        yield return ("NearPond.LongAxis", JsonValue.Create(7));
        yield return ("NearPond.ShortAxis", JsonValue.Create(11));
        yield return ("NearPond.CenterDistance.Max", JsonValue.Create(13));
        yield return ("WorkerFootRadius", JsonValue.Create(0));
        yield return ("WorkerFootRadius", JsonValue.Create(192));
        yield return ("WorkerFootRadius", JsonValue.Create(true));
    }

    private static IEnumerable<string> ObjectPaths(JsonObject value, string path)
    {
        yield return path;
        foreach ((string name, JsonNode? node) in value)
            if (node is JsonObject child)
                foreach (string childPath in ObjectPaths(child, path.Length == 0 ? name : $"{path}.{name}"))
                    yield return childPath;
    }

    private static JsonNode? Get(JsonObject root, string path)
    {
        JsonNode? node = root;
        foreach (string name in path.Split('.'))
            node = node![name];
        return node;
    }

    private static void Set(JsonObject root, string path, JsonNode? value)
    {
        int separator = path.LastIndexOf('.');
        JsonNode parent = separator < 0 ? root : Get(root, path[..separator])!;
        parent[separator < 0 ? path : path[(separator + 1)..]] = value?.DeepClone();
    }

    private static string Edit(string json, Action<JsonObject> edit)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        edit(root);
        return root.ToJsonString();
    }

    private static void Write(string path, string value)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(value);
    }

    private static bool Rejects(Action action, string field = "", string source = Source)
    {
        try { action(); }
        catch (JsonException exception)
        {
            return exception.Message.Contains(source, StringComparison.Ordinal) &&
                exception.Message.Contains(field, StringComparison.Ordinal);
        }
        return false;
    }

    private static bool Fail(string message)
    {
        GD.PrintErr($"TestTerrainRules: {message}");
        return false;
    }
}
