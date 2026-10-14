using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Terrain;

public partial class TestTerrainMap : Node
{
    public override async void _Ready()
    {
        try
        {
            string? argument = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--terrain-seed=", StringComparison.Ordinal));
            if (argument is null) GetTree().Quit(await RunChecks(GetTree()) ? 0 : 1);
            else
            {
                int seed = int.Parse(argument["--terrain-seed=".Length..], System.Globalization.CultureInfo.InvariantCulture);
                TerrainRules rules = FarmGame.ParseTerrainRules(FileAccess.GetFileAsString(TerrainRules.ResourcePath), "定向种子正式地形配置");
                var timer = Stopwatch.StartNew();
                using TerrainMap map = await TerrainMap.CreateAsync(seed, rules, GetTree());
                GD.Print($"[地形定向验收] 种子{seed}完整创建{timer.Elapsed.TotalSeconds:F3}秒");
                GetTree().Quit(CheckMap(map) ? 0 : 1);
            }
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    public static string AllLandJson()
    {
        JsonObject root = JsonNode.Parse(FileAccess.GetFileAsString(TerrainRules.ResourcePath))!.AsObject();
        foreach (string band in new[] { "Small", "Medium", "Large" }) root[band]!["Count"] = 0;
        foreach (string field in new[] { "Min", "Target", "Max" }) root["WaterCoverage"]![field] = 0;
        return root.ToJsonString();
    }
    public static async System.Threading.Tasks.Task<bool> RunChecks(SceneTree tree)
    {
        TerrainRules rules = FarmGame.ParseTerrainRules(FileAccess.GetFileAsString(TerrainRules.ResourcePath), "正式默认测试配置");
        using (var cancelled = new System.Threading.CancellationTokenSource())
        {
            var cancellationTimer = Stopwatch.StartNew();
            var pending = TerrainMap.CreateAsync(110, rules, tree, cancelled.Token);
            for (int frame = 0; frame < 3; frame++) await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            cancelled.Cancel();
            try { using TerrainMap unexpected = await pending; return Fail("取消完整创建仍发布了地图。"); }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
            GD.Print($"[通过] 创建期间正常推进3物理帧并取消生成，{cancellationTimer.Elapsed.TotalSeconds:F3}秒");
        }
        int[]? firstCells = null;
        double firstArea = 0;
        LandPath? firstRoute = null;
        foreach (int seed in new[] { 0, 1, 110, 12345, 0 })
        {
            var creationTimer = Stopwatch.StartNew();
            using TerrainMap generated = await TerrainMap.CreateAsync(seed, rules, tree);
            GD.Print($"地形种子{seed}：{generated.PondCount}座，水面{generated.WaterArea / FarmGame.MapSize / FarmGame.MapSize:P6}，候选{generated.CandidateCount}，完整创建{creationTimer.Elapsed.TotalSeconds:F3}秒");
            if (!CheckMap(generated)) return false;
            if (seed != 0) continue;
            int[] cells = Enumerable.Range(0, 384 * 384).Select(index => generated.GetCell(new Vector2I(index % 384, index / 384)).TernaryCode).ToArray();
            LandPath route = generated.FindLandPath(LandPosition.FromCoordinates(182, 209.5), LandPosition.FromCoordinates(202, 209.5)).Path!;
            if (firstCells is null) { firstCells = cells; firstArea = generated.WaterArea; firstRoute = route; }
            else if (!firstCells.SequenceEqual(cells) || firstArea != generated.WaterArea || !route.Equals(firstRoute)) return Fail("重复种子0的地形、面积或原生完整路线不一致。");
        }
        using TerrainMap allLand = TerrainMap.Create(0, FarmGame.ParseTerrainRules(AllLandJson(), "显式全零配置"));
        if (!allLand.IsAllLand || allLand.WaterArea != 0 || allLand.GetCell(Vector2I.Zero).TernaryCode != 80 || allLand.GetCell(Vector2I.Zero).Top != TerrainKind.Grass) return Fail("纯陆地完整入口或素材三元编码错误。");
        GD.Print("[通过] 多种子地形完整生成、重复确定性、中心边缘、真实绕行、连续位置及岸格干地"); return true;
    }
    private static bool CheckMap(TerrainMap map)
    {
        var timer = Stopwatch.StartNew();
        if (map.PondCount != 48 || map.IsAllLand || map.WaterArea < .06 * 384 * 384 || map.WaterArea > .10 * 384 * 384) return Fail("默认数量或最终岸线面积错误。");
        if (map.IsFullLand(new Vector2I(-1, 0))) return Fail("地图外不应是完整陆地。");
        try { map.GetCell(new Vector2I(384, 0)); return Fail("地图外查询未明确拒绝。"); } catch (ArgumentOutOfRangeException) { }
        int waterCells = 0;
        for (int y = 0; y < 384; y++)
            for (int x = 0; x < 384; x++)
            {
                bool full = map.IsFullLand(new Vector2I(x, y));
                if (!full) waterCells++;
                if ((x < 8 || y < 8 || x >= 376 || y >= 376 || x >= 180 && x <= 203 && y >= 180 && y <= 203) && !full) return Fail("边缘或中心完整陆地被水覆盖。");
            }
        if (waterCells <= map.WaterArea) return Fail("禁建面积必须单独计入含水岸格。");
        if (map.Seed == 0)
        {
            // 真路线取消点：旧原生射线同时计数同一共享顶点，曾将距岸2.28格的陆地误判为水。
            foreach (LandPosition point in new[]
            {
                LandPosition.FromCoordinates(96924.0859375 / 1024, 185884.609375 / 1024),
                LandPosition.FromCoordinates(96920.2109375 / 1024, 185885.421875 / 1024)
            })
                if (map.FindLandPath(point, point).Status != LandPathStatus.Success)
                    return Fail("共享岸点射线回归：实际陆地点仍被误判为无效起点。");
            LandPosition capturedContinuous = LandPosition.FromCoordinates(185, 210.95731608072916);
            LandPosition capturedTarget = LandPosition.FromCoordinates(182, 209.5);
            LandPathResult captured = map.FindLandPath(capturedContinuous, capturedTarget);
            if (captured.Status != LandPathStatus.Success || captured.Path is null
                || captured.Path.Points[0] != capturedContinuous || captured.Path.Points[^1] != capturedTarget)
                return Fail("长细三角形数值回归：真实陆地连续起点仍被原生投影偏移而拒绝。");
            // 实验捕获的旧近岸路线点：脚底可放置，但已落入新确认的离岸退让带。
            LandPosition oldNearShore = LandPosition.FromCoordinates(189.5, 213.12894);
            if (map.FindLandPath(oldNearShore, oldNearShore).Status != LandPathStatus.Success
                || map.FindLandPath(oldNearShore, capturedTarget).Status != LandPathStatus.Unreachable)
                return Fail("保守简化退让带不应改变真实水域分类，也不能投影瞬移回导航区域。");
        }
        LandPosition center = LandPosition.FromCell(new Vector2I(191, 191));
        foreach (Vector2I anchor in new[] { new Vector2I(0, 0), new Vector2I(381, 0), new Vector2I(0, 381), new Vector2I(381, 381), new Vector2I(191, 0), new Vector2I(0, 191), new Vector2I(381, 191), new Vector2I(191, 381) })
        {
            LandPosition workCenter = LandPosition.FromCell(anchor + Vector2I.One);
            LandPathResult remote = map.FindLandPath(center, workCenter);
            if (remote.Status != LandPathStatus.Success || remote.Path!.Points[0] != center || remote.Path.Points[^1] != workCenter) return Fail($"种子{map.Seed}未抵达远端合法农田工作中心{anchor}。");
        }
        LandPosition left = LandPosition.FromCoordinates(182, 209.5), right = LandPosition.FromCoordinates(202, 209.5);
        LandPathResult around = map.FindLandPath(left, right);
        if (around.Status != LandPathStatus.Success || around.Path is not LandPath route || route.Points.Count < 3) return Fail("近景两侧未返回真实绕水路线。");
        if (map.FindLandPath(LandPosition.FromCoordinates(192, 209.5), right).Status != LandPathStatus.InvalidStart) return Fail("水中起点未拒绝。");
        LandPosition continuous = route.GetPositionAtDistance(3);
        LandPathResult continued = map.FindLandPath(continuous, left);
        if (continued.Status != LandPathStatus.Success || continued.Path is not LandPath continuation)
            return Fail($"连续起点接续失败：{continued.Status}，真实起点({continuous.X:R},{continuous.Y:R})，目标({left.X:R},{left.Y:R})。");
        if (continuation.Points[0] != continuous) return Fail("取消任务没有保留实际连续位置。");
        var queryTimer = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            LandPosition goal = i % 2 == 0 ? left : right;
            LandPathResult leg = map.FindLandPath(continuous, goal);
            if (leg.Status != LandPathStatus.Success || leg.Path is null || leg.Path.Points[0] != continuous)
                return Fail($"第{i + 1}次原生取消改派失败或改变起点：{leg.Status}，真实起点({continuous.X:R},{continuous.Y:R})，目标({goal.X:R},{goal.Y:R})。");
            continuous = leg.Path.GetPositionAtDistance(3);
        }
        GD.Print($"种子{map.Seed}原生100次连续改派：{queryTimer.Elapsed.TotalMilliseconds:F1}ms");
        bool foundShorePoint = false;
        for (int y = 205; y <= 214 && !foundShorePoint; y++)
            for (int x = 185; x <= 199 && !foundShorePoint; x++)
            {
                if (map.IsFullLand(new Vector2I(x, y))) continue;
                for (int iy = -3; iy <= 3 && !foundShorePoint; iy++)
                    for (int ix = -3; ix <= 3 && !foundShorePoint; ix++)
                    {
                        LandPosition p = LandPosition.FromCoordinates(x + ix / 8d, y + iy / 8d);
                        LandPathResult candidate = map.FindLandPath(p, left);
                        foundShorePoint = candidate.Status == LandPathStatus.Success;
                    }
            }
        if (!foundShorePoint) return Fail("含水岸格的合法干地未被导航识别。");
        GD.Print($"种子{map.Seed}完整路线与岸格检查：{timer.Elapsed.TotalSeconds:F3}秒"); return true;
    }
    private static bool Fail(string reason) { GD.PushError($"[失败] 水域地形：{reason}"); return false; }
}
