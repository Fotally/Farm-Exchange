using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Terrain;

public partial class TestRoadNavigation : Node
{
    public override async void _Ready()
    {
        try { GetTree().Quit(await RunChecksAsync(GetTree()) ? 0 : 1); }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    public static async Task<bool> RunChecksAsync(SceneTree tree)
    {
        using var warnings = new NavigationWarningCapture();
        OS.AddLogger(warnings);
        try
        {
            bool passed = await RunNavigationChecksAsync(tree);
            if (!passed) return false;
            if (warnings.EdgeErrors != 0) return Fail($"原生地图发布出现{warnings.EdgeErrors}次完整边冲突警告。");
            GD.Print("[通过] 道路原生成本、纯陆地首次准备、断路拆路、材质、保留行程、连续取消及原生零边冲突");
            return true;
        }
        finally { OS.RemoveLogger(warnings); }
    }

    private static async Task<bool> RunNavigationChecksAsync(SceneTree tree)
    {
        using var game = new FarmGame(110, terrainRules: FarmGame.ParseTerrainRules(TestTerrainMap.AllLandJson(), "道路偏好纯陆地配置"));
        TerrainReadView map = game.Terrain;
        LandPosition start = LandPosition.FromCell(new Vector2I(2, 6));
        LandPosition target = LandPosition.FromCell(new Vector2I(19, 6));
        LandPath original = map.FindLandPath(start, target).Path!;
        LandPosition[] originalPoints = original.Points.ToArray();
        LandPosition originalSample = original.GetPositionAtDistance(3);
        Vector2I[] road = Enumerable.Range(2, 18).Select(x => new Vector2I(x, 3))
            .Concat(Enumerable.Range(4, 3).Select(y => new Vector2I(2, y)))
            .Concat(Enumerable.Range(4, 3).Select(y => new Vector2I(19, y))).ToArray();
        if (!game.TryPlaceRoads(road, RoadMaterial.Dirt).Success) return Fail("实际建路命令失败。");
        var timer = Stopwatch.StartNew();
        Task preparing = game.WaitForNavigationAsync(tree);
        if (game.IsNavigationReady) return Fail("纯陆地首次建路没有进入原生准备。");
        try { map.FindLandPath(start, target); return Fail("准备期间仍交付旧道路快照路线。"); }
        catch (InvalidOperationException) { }
        await preparing;
        GD.Print($"[道路导航] 纯陆地首次准备{timer.Elapsed.TotalMilliseconds:F1}ms");
        LandPathResult alongRoad = map.FindLandPath(start, target);
        if (!Complete(alongRoad, start, target) || alongRoad.Path!.Points.Min(point => point.Y) > 4)
            return Fail("原生区域成本未对已连接U形道路产生偏好。");
        if (!original.Points.SequenceEqual(originalPoints) || original.GetPositionAtDistance(3) != originalSample)
            return Fail("道路更新修改已领取的不可变行程。");
        LandPosition continuous = alongRoad.Path.GetPositionAtDistance(3);
        if (!Complete(map.FindLandPath(continuous, start), continuous, start)) return Fail("道路路线连续取消改变真实端点。");
        if (!game.TryRemoveRoads(new[] { new Vector2I(10, 3) }).Success) return Fail("断开道路命令失败。");
        timer.Restart();
        await game.WaitForNavigationAsync(tree);
        GD.Print($"[道路导航] 单格重分组并同步{timer.Elapsed.TotalMilliseconds:F1}ms");
        if (!Complete(map.FindLandPath(start, target), start, target)) return Fail("断路偏好导致原陆地不可达。");
        if (!game.TryRemoveRoads(road).Success) return Fail("拆除余下道路失败。");
        await game.WaitForNavigationAsync(tree);
        LandPathResult removed = map.FindLandPath(start, target);
        if (!Complete(removed, start, target) || removed.Path!.Points.Any(point => Math.Abs(point.Y - 6) > 1d / 4096))
            return Fail("最后道路拆除后仍强迫绕行。");
        if (!game.TryPlaceRoads(road, RoadMaterial.Cobble).Success) return Fail("其他材质道路实际建造失败。");
        await game.WaitForNavigationAsync(tree);
        if (!map.FindLandPath(start, target).Path!.Equals(alongRoad.Path)) return Fail("道路材质改变相同成本的静态几何路线。");
        await game.WaitForNavigationAsync(tree);
        if (!game.IsNavigationReady || !ReferenceEquals(map, game.Terrain)) return Fail("相同土地快照重复准备或替换了读取视图。");
        var allRoads = game.GetBuildingSpaces();
        if (!game.TryRemoveRoads(new[] { new Vector2I(10, 3) }).Success) return Fail("准备生命周期测试断路失败。");
        var gapRoads = game.GetBuildingSpaces();
        Task pendingDispose = game.WaitForNavigationAsync(tree);
        game.Dispose();
        game.Dispose();
        try { await pendingDispose; return Fail("等待原生更新时释放局未取消准备。"); }
        catch (OperationCanceledException) { }
        try { map.FindLandPath(start, target); return Fail("局释放后读取视图仍提供路线。"); }
        catch (ObjectDisposedException) { }
        TerrainRules allLand = FarmGame.ParseTerrainRules(TestTerrainMap.AllLandJson(), "道路准备取消配置");
        using (TerrainMap cancelled = TerrainMap.Create(111, allLand))
        {
            await cancelled.UpdateRoadsAsync(allRoads, tree);
            using var cancellation = new CancellationTokenSource();
            Task pendingCancel = cancelled.UpdateRoadsAsync(gapRoads, tree, cancellation.Token);
            cancellation.Cancel();
            if (!await CheckCanceled(pendingCancel, cancelled, start, target)) return Fail("等待原生更新时外部取消未结束该地图。");
        }
        using (TerrainMap initial = TerrainMap.Create(112, allLand))
        {
            Task pendingInitial = initial.UpdateRoadsAsync(allRoads, tree);
            initial.Dispose();
            if (!await CheckCanceled(pendingInitial, initial, start, target)) return Fail("纯陆地首次分帧准备中Dispose后仍继续生成。");
        }
        return true;
    }

    private static bool Complete(LandPathResult result, LandPosition start, LandPosition target) =>
        result.Status == LandPathStatus.Success && result.Path is LandPath path && path.Points[0] == start && path.Points[^1] == target;
    private static async Task<bool> CheckCanceled(Task pending, TerrainMap map, LandPosition start, LandPosition target)
    {
        try { await pending; return false; }
        catch (OperationCanceledException) { }
        if (map.IsNavigationReady) return false;
        try { map.FindLandPath(start, target); return false; }
        catch (ObjectDisposedException) { return true; }
    }
    private sealed partial class NavigationWarningCapture : Logger
    {
        private int _edgeErrors;
        internal int EdgeErrors => Volatile.Read(ref _edgeErrors);
        public override void _LogError(string function, string file, int line, string code, string rationale,
            bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBackTraces)
        {
            if (code.Contains("Navigation map synchronization had", StringComparison.Ordinal)
                || rationale.Contains("Navigation map synchronization had", StringComparison.Ordinal)) Interlocked.Increment(ref _edgeErrors);
        }
    }
    private static bool Fail(string reason) { GD.PushError($"[失败] 道路导航：{reason}"); return false; }
}
