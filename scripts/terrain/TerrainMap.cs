using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;

namespace FarmExchange.Terrain;

public enum TerrainKind { Water = 0, Soil = 1, Grass = 2 }
public readonly record struct TerrainCellSnapshot(TerrainKind Top, TerrainKind Right, TerrainKind Bottom, TerrainKind Left)
{
    public int TernaryCode => (int)Top + 3 * (int)Right + 9 * (int)Bottom + 27 * (int)Left;
    public bool IsFullLand => Top != TerrainKind.Water && Right != TerrainKind.Water && Bottom != TerrainKind.Water && Left != TerrainKind.Water;
}

public sealed class TerrainCreationException : Exception
{
    public string Stage { get; }
    internal TerrainCreationException(string stage, string reason) : base($"地形创建失败（{stage}）：{reason}") { Stage = stage; }
}

/**
 * <summary>本局唯一静态地形与完整路线入口，封装原生导航准备及资源释放。</summary>
 * <remarks>静态岸线保留有理几何；运行路线使用有限精度坐标。</remarks>
 */
public sealed class TerrainMap : IDisposable
{
    private const float NavigationScale = 1024;
    private const float NavigationCellSize = NavigationScale / 4096;
    private const double SafetyMargin = 1d / 256;
    private const double ShoreSimplificationTolerance = 1d / 32;
    private const double ExtraShoreRetreat = 1d / 8;
    private const int NavigationChunkCells = 16;
    private const float PlainTravelCost = 2;
    private const float RoadTravelCost = 1;
    private Vector2[][] _navigationObstacles = Array.Empty<Vector2[]>();
    private readonly List<NavigationChunk> _navigationChunks = new();
    private HashSet<Vector2I> _roadCells = new();
    private bool _navigationUpdating;
    private readonly System.Threading.CancellationTokenSource _navigationLifetime = new();
    private readonly bool[] _waterVertices;
    private readonly TerrainPond[] _ponds;
    private readonly Vector2[][] _shores;
    private readonly List<(Vector2 Start, Vector2 End)> _shoreSegments = new();
    private readonly Dictionary<Vector2I, List<int>> _shoreBuckets = new();
    private readonly double _radius;
    private Rid _navigationMap;
    private readonly List<Rid> _navigationRegions = new();
    private readonly List<NavigationPolygon> _polygons = new();
    private bool _disposed;
    /**
     * <summary>本地形稳定的只读视图，供非拥有者共享查询。</summary>
     * <remarks>没有维护或释放入口，不复制地形和原生导航状态。</remarks>
     */
    public TerrainReadView ReadOnly { get; }
    public TerrainRules Rules { get; }
    public int Seed { get; }
    public int MapSize => Rules.MapSize;
    public bool IsAllLand => _ponds.Length == 0;
    public int PondCount => _ponds.Length;
    public double WaterArea { get; }
    public int CandidateCount { get; }

    private TerrainMap(int seed, TerrainRules rules, TerrainPond[] ponds, int candidates)
    {
        ReadOnly = new TerrainReadView(this);
        Seed = seed; Rules = rules; _ponds = ponds; CandidateCount = candidates;
        _radius = (double)rules.WorkerFootRadius;
        _waterVertices = new bool[checked((MapSize + 1) * (MapSize + 1))];
        TerrainNumber area = 0;
        foreach (TerrainPond pond in ponds)
        {
            area += pond.Area;
            foreach (Vector2I point in pond.WaterVertices) _waterVertices[point.Y * (MapSize + 1) + point.X] = true;
        }
        WaterArea = area.ToDouble();
        _shores = ponds.Select(p => p.Shore.Select(v => new Vector2((float)(v.X * NavigationScale), (float)(v.Y * NavigationScale))).ToArray()).ToArray();
        foreach (Vector2[] shore in _shores)
            for (int i = 0; i < shore.Length; i++)
            {
                Vector2 a = shore[i], b = shore[(i + 1) % shore.Length];
                int index = _shoreSegments.Count;
                _shoreSegments.Add((a, b));
                foreach (Vector2I bucket in Buckets(a, b, 0))
                {
                    if (!_shoreBuckets.TryGetValue(bucket, out var entries)) _shoreBuckets[bucket] = entries = new();
                    entries.Add(index);
                }
            }
        ValidateWorkCenters();
    }
    /**
     * <summary>同步创建显式纯陆地；含水配置须使用异步完整创建入口。</summary>
     * <param name="seed">本局独立地形种子。</param>
     * <param name="rules">已验证的不可变配置。</param>
     * <returns>同一完整入口直接完成的纯陆地。</returns>
     */
    public static TerrainMap Create(int seed, TerrainRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!rules.IsAllLand) throw new InvalidOperationException("含水地形须通过 CreateAsync 等待原生导航准备。");
        return CreateAsync(seed, rules, null).GetAwaiter().GetResult();
    }
    /**
     * <summary>完整生成地形并等待原生导航正常同步，失败不发布半成品。</summary>
     * <param name="seed">本局独立地形种子。</param>
     * <param name="rules">已验证的不可变配置。</param>
     * <param name="tree">含水创建所需的活动场景树；纯陆地可为空。</param>
     * <param name="cancellationToken">退出准备场景时取消并释放本局资源。</param>
     * <returns>可立即完整求路的地形；失败抛明确阶段异常。</returns>
     */
    public static async System.Threading.Tasks.Task<TerrainMap> CreateAsync(int seed, TerrainRules rules, SceneTree? tree, System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        cancellationToken.ThrowIfCancellationRequested();
        if (rules.IsAllLand) return new TerrainMap(seed, rules, Array.Empty<TerrainPond>(), 0);
        ArgumentNullException.ThrowIfNull(tree);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        TraceCreation(seed, "生成开始", timer);
        TerrainGenerator.PrepareTemplates();
        var generated = await System.Threading.Tasks.Task.Run(() =>
        {
            TerrainPond[] result = TerrainGenerator.Generate(seed, rules, out int count, cancellationToken);
            return (Ponds: result, Candidates: count);
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        TerrainPond[] ponds = generated.Ponds;
        int candidates = generated.Candidates;
        TraceCreation(seed, $"生成完成，{ponds.Length}塘、{ponds.Sum(pond => pond.Shore.Length)}岸点", timer);
        var map = new TerrainMap(seed, rules, ponds, candidates);
        TraceCreation(seed, "地形数据完成", timer);
        try
        {
            await map.PrepareNavigation(tree, cancellationToken);
            Vector2 center = new((rules.MapSize - 1) * NavigationScale / 2, (rules.MapSize - 1) * NavigationScale / 2);
            if (NavigationServer2D.MapGetClosestPoint(map._navigationMap, center).DistanceTo(center) > NavigationCellSize)
                throw new TerrainCreationException("导航同步", "正常物理帧后中心保护区仍没有已登记的导航区域。");
            TraceCreation(seed, "正常同步完成，开始全量工作中心连通校验", timer);
            await map.ValidateNavigationWorkCenters(tree, cancellationToken);
            TraceCreation(seed, "全部工作中心连通校验通过", timer);
            return map;
        }
        catch { map.Dispose(); throw; }
    }
    [System.Diagnostics.Conditional("DEBUG")]
    private static void TraceCreation(int seed, string stage, System.Diagnostics.Stopwatch timer) =>
        GD.Print($"[地形准备] 种子{seed} {stage}，累计{timer.Elapsed.TotalSeconds:F3}s");
    [System.Diagnostics.Conditional("DEBUG")]
    private void TraceNavigationMemory()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        GD.Print($"[导航资源] 种子{Seed}，{_navigationRegions.Count}区域，进程当前托管{GC.GetTotalMemory(false) / 1048576d:F2}MiB、私有内存{process.PrivateMemorySize64 / 1048576d:F2}MiB、工作集{process.WorkingSet64 / 1048576d:F2}MiB");
    }
    private static async System.Threading.Tasks.Task WaitPhysicsFrame(SceneTree tree)
    {
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }
    private readonly record struct CellTriangles(Vector2I Cell, Vector2[] Vertices);
    private sealed record NavigationChunk(Vector2I Cell, CellTriangles[] Faces);

    /**
     * <summary>当前道路偏好是否已完成原生正常同步。</summary>
     * <remarks>准备期间宿主让出物理帧，并暂停经营推进；既有 LandPath 保持不变。</remarks>
     */
    public bool IsNavigationReady => !_disposed && !_navigationUpdating;

    /**
     * <summary>根据土地已提交的只读快照更新道路偏好，并等待原生导航完整同步。</summary>
     * <param name="spaces">土地模块拥有的最新完整建筑空间快照。</param>
     * <param name="tree">供原生导航正常同步的活动场景树。</param>
     * <param name="cancellationToken">退出本局时取消准备并释放本局导航资源。</param>
     * <returns>新领取任务可读取本次道路偏好的完成任务。</returns>
     * <remarks>主线程串行调用；失败或取消后结束本局，不回退旧地图或直线路径。</remarks>
     */
    public async System.Threading.Tasks.Task UpdateRoadsAsync(IReadOnlyList<BuildingSpaceSnapshot> spaces, SceneTree tree,
        System.Threading.CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spaces);
        ArgumentNullException.ThrowIfNull(tree);
        if (_navigationUpdating) throw new InvalidOperationException("道路导航正在准备，调用方必须串行等待。");
        var roads = spaces.Where(space => space.Building == BuildingKind.Road).Select(space => space.AnchorCell).ToHashSet();
        if (roads.SetEquals(_roadCells)) return;
        using var lifetime = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _navigationLifetime.Token);
        cancellationToken = lifetime.Token;
        _navigationUpdating = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_navigationMap.IsValid)
            {
                // 显式纯陆地首次建路也建立同一原生连续网格，不忽略道路或强制刷新。
                _roadCells = roads;
                await PrepareNavigation(tree, cancellationToken);
                await ValidateNavigationWorkCenters(tree, cancellationToken);
                return;
            }
            var changedCells = new HashSet<Vector2I>(_roadCells);
            changedCells.SymmetricExceptWith(roads);
            var changedChunks = changedCells.Select(cell => new Vector2I(cell.X / NavigationChunkCells, cell.Y / NavigationChunkCells)).ToHashSet();
            _roadCells = roads;
            var indices = new List<int>();
            var retired = new List<NavigationPolygon>();
            try
            {
                for (int chunk = 0; chunk < _navigationChunks.Count; chunk++)
                {
                    if (!changedChunks.Contains(_navigationChunks[chunk].Cell)) continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    NavigationPolygon[] replacements = CreateChunkPolygons(_navigationChunks[chunk]);
                    for (int group = 0; group < 2; group++)
                    {
                        int index = chunk * 2 + group;
                        retired.Add(_polygons[index]);
                        _polygons[index] = replacements[group];
                        indices.Add(index);
                    }
                }
                await PublishNavigationPolygons(indices, cancellationToken);
            }
            finally { foreach (NavigationPolygon polygon in retired) polygon.Dispose(); }
        }
        catch { Dispose(); throw; }
        finally { _navigationUpdating = false; }
    }

    private async System.Threading.Tasks.Task PrepareNavigation(SceneTree tree, System.Threading.CancellationToken cancellationToken)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        _navigationObstacles = new Vector2[_shores.Length][];
        for (int pond = 0; pond < _shores.Length; pond++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _navigationObstacles[pond] = CreateNavigationObstacle(_shores[pond], pond);
            await WaitPhysicsFrame(tree).WaitAsync(cancellationToken);
        }
        TraceCreation(Seed, $"保守障碍完成，{_shores.Sum(shore => shore.Length)}岸点→{_navigationObstacles.Sum(outline => outline.Length)}障碍点", timer);
        var obstacleBounds = _navigationObstacles.Select(outline => Bounds(outline)).ToArray();
        float minimum = (float)((-.5 + _radius + SafetyMargin) * NavigationScale);
        float maximum = (float)((MapSize - .5 - _radius - SafetyMargin) * NavigationScale);
        var partitionCpu = System.Diagnostics.Stopwatch.StartNew();
        for (int chunkY = 0; chunkY < MapSize; chunkY += NavigationChunkCells)
            for (int chunkX = 0; chunkX < MapSize; chunkX += NavigationChunkCells)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bounds = new Rect2((chunkX - .5f) * NavigationScale, (chunkY - .5f) * NavigationScale,
                    Math.Min(NavigationChunkCells, MapSize - chunkX) * NavigationScale,
                    Math.Min(NavigationChunkCells, MapSize - chunkY) * NavigationScale);
                int[] ponds = Enumerable.Range(0, obstacleBounds.Length).Where(pond => obstacleBounds[pond].Intersects(bounds, true)).ToArray();
                var cells = new List<CellTriangles>();
                for (int y = chunkY; y < Math.Min(chunkY + NavigationChunkCells, MapSize); y++)
                    for (int x = chunkX; x < Math.Min(chunkX + NavigationChunkCells, MapSize); x++)
                    {
                        float left = Math.Max((x - .5f) * NavigationScale, minimum);
                        float top = Math.Max((y - .5f) * NavigationScale, minimum);
                        float right = Math.Min((x + .5f) * NavigationScale, maximum);
                        float bottom = Math.Min((y + .5f) * NavigationScale, maximum);
                        Vector2[] rectangle = { new(left, top), new(right, top), new(right, bottom), new(left, bottom) };
                        var outlines = new List<Vector2[]> { rectangle };
                        var cellBounds = new Rect2(left, top, right - left, bottom - top);
                        foreach (int pond in ponds)
                        {
                            if (!obstacleBounds[pond].Intersects(cellBounds, true)) continue;
                            var remaining = new List<Vector2[]>();
                            foreach (Vector2[] outline in outlines)
                                foreach (Vector2[] cut in Geometry2D.ClipPolygons(outline, _navigationObstacles[pond]))
                                {
                                    if (Geometry2D.IsPolygonClockwise(cut))
                                        throw new TerrainCreationException("道路导航分区", $"地块({x},{y})裁剪返回洞环，不能独立三角化。");
                                    remaining.Add(cut);
                                }
                            outlines = remaining;
                        }
                        var vertices = new List<Vector2>();
                        foreach (Vector2[] outline in outlines)
                        {
                            Vector2[] prepared = PrepareNavigationOutline(new Vector2I(x, y), outline);
                            int[] indices = Geometry2D.TriangulatePolygon(prepared);
                            if (indices.Length == 0) throw new TerrainCreationException("道路导航分区", $"地块({x},{y})原生三角化失败。");
                            foreach (int index in indices) vertices.Add(prepared[index]);
                        }
                        if (vertices.Count != 0) cells.Add(new CellTriangles(new Vector2I(x, y), vertices.ToArray()));
                    }
                var chunk = new NavigationChunk(new Vector2I(chunkX / NavigationChunkCells, chunkY / NavigationChunkCells), cells.ToArray());
                _navigationChunks.Add(chunk);
                _polygons.AddRange(CreateChunkPolygons(chunk));
                if (_navigationChunks.Count % 8 == 0)
                {
                    partitionCpu.Stop();
                    await WaitPhysicsFrame(tree).WaitAsync(cancellationToken);
                    partitionCpu.Start();
                }
            }
        partitionCpu.Stop();
        if (_polygons.All(polygon => polygon.GetPolygonCount() == 0))
            throw new TerrainCreationException("道路导航分区", "原生连续网格没有可行走多边形。");
        TraceCreation(Seed, $"原生道路成本分区完成，{_navigationChunks.Count}块、{_polygons.Sum(polygon => polygon.GetPolygonCount())}面，分区CPU{partitionCpu.Elapsed.TotalMilliseconds:F1}ms", timer);
        _navigationMap = NavigationServer2D.MapCreate();
        NavigationServer2D.MapSetUseAsyncIterations(_navigationMap, false);
        NavigationServer2D.MapSetCellSize(_navigationMap, NavigationCellSize);
        NavigationServer2D.MapSetUseEdgeConnections(_navigationMap, false);
        NavigationServer2D.MapSetActive(_navigationMap, true);
        for (int index = 0; index < _polygons.Count; index++)
        {
            Rid region = NavigationServer2D.RegionCreate();
            _navigationRegions.Add(region);
            NavigationServer2D.RegionSetUseEdgeConnections(region, false);
            // 同轮发布成本分组，避免异步区域的新旧面在地图中暂时重叠。
            NavigationServer2D.RegionSetUseAsyncIterations(region, false);
            NavigationServer2D.RegionSetTravelCost(region, index % 2 == 0 ? PlainTravelCost : RoadTravelCost);
            NavigationServer2D.RegionSetMap(region, _navigationMap);
        }
        await PublishNavigationPolygons(Enumerable.Range(0, _polygons.Count).ToArray(), cancellationToken);
        TraceNavigationMemory();
    }

    private static Vector2[] PrepareNavigationOutline(Vector2I cell, Vector2[] outline)
    {
        var points = new List<Vector2>(outline.Length);
        foreach (Vector2 point in outline)
        {
            Vector2 snapped = point.Snapped(Vector2.One * (NavigationCellSize * .1f));
            if (points.Count == 0 || points[^1] != snapped) points.Add(snapped);
        }
        if (points.Count > 1 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        if (points.Count < 3) throw new TerrainCreationException("道路导航分区", $"地块{cell}的对齐轮廓不足三个顶点。");
        Vector2[] prepared = points.ToArray();
        if (prepared.Length != outline.Length)
        {
            Vector2[] original = outline.Select(point => point.Snapped(Vector2.One * (NavigationCellSize * .1f))).ToArray();
            if (Geometry2D.ClipPolygons(original, prepared).Count != 0 || Geometry2D.ClipPolygons(prepared, original).Count != 0)
                throw new TerrainCreationException("道路导航分区", $"地块{cell}移除连续相等对齐顶点后几何发生变化。");
        }
        return prepared;
    }

    private static Rect2 Bounds(Vector2[] outline)
    {
        float left = outline.Min(point => point.X), top = outline.Min(point => point.Y);
        return new Rect2(left, top, outline.Max(point => point.X) - left, outline.Max(point => point.Y) - top);
    }

    private NavigationPolygon[] CreateChunkPolygons(NavigationChunk chunk)
    {
        var result = new NavigationPolygon[2];
        try
        {
            for (int group = 0; group < 2; group++)
            {
                var polygon = new NavigationPolygon { AgentRadius = 0, CellSize = NavigationCellSize };
                result[group] = polygon;
                var vertices = new List<Vector2>();
                var indices = new Dictionary<Vector2, int>();
                foreach (CellTriangles cell in chunk.Faces)
                {
                    if (_roadCells.Contains(cell.Cell) != (group == 1)) continue;
                    for (int face = 0; face < cell.Vertices.Length; face += 3)
                    {
                        Vector2 a = cell.Vertices[face], b = cell.Vertices[face + 1], c = cell.Vertices[face + 2];
                        if ((b - a).Cross(c - a) == 0)
                            throw new TerrainCreationException("道路导航分区", $"地块{cell.Cell}的原生三角形在既有 snap 后退化。");
                        var triangle = new int[3];
                        for (int vertex = 0; vertex < 3; vertex++)
                        {
                            Vector2 point = cell.Vertices[face + vertex];
                            if (!indices.TryGetValue(point, out int index))
                            {
                                index = vertices.Count; indices.Add(point, index); vertices.Add(point);
                            }
                            triangle[vertex] = index;
                        }
                        polygon.AddPolygon(triangle);
                    }
                }
                Vector2[] coordinates = vertices.ToArray();
                polygon.SetVertices(coordinates);
                if (polygon.GetPolygonCount() > 0) NormalizeTriangleStarts(polygon, coordinates);
            }
            return result;
        }
        catch
        {
            foreach (NavigationPolygon? polygon in result) polygon?.Dispose();
            throw;
        }
    }

    private async System.Threading.Tasks.Task PublishNavigationPolygons(IReadOnlyList<int> changed,
        System.Threading.CancellationToken cancellationToken)
    {
        var previousMap = NavigationServer2D.MapGetIterationId(_navigationMap);
        var previousRegions = changed.ToDictionary(index => index, index => NavigationServer2D.RegionGetIterationId(_navigationRegions[index]));
        var ready = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        bool registrationComplete = false;
        void OnMapChanged(Rid changedMap)
        {
            if (_disposed || !registrationComplete || changedMap != _navigationMap || NavigationServer2D.MapGetIterationId(_navigationMap) == previousMap) return;
            foreach (int index in changed)
            {
                Rid region = _navigationRegions[index];
                if (NavigationServer2D.RegionGetMap(region) != _navigationMap
                    || NavigationServer2D.RegionGetIterationId(region) == previousRegions[index]) return;
            }
            ready.TrySetResult();
        }
        NavigationServer2D.MapChanged += OnMapChanged;
        try
        {
            foreach (int index in changed)
                NavigationServer2D.RegionSetNavigationPolygon(_navigationRegions[index], _polygons[index]);
            registrationComplete = true;
            await ready.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally { NavigationServer2D.MapChanged -= OnMapChanged; }
    }

    private Vector2[] CreateNavigationObstacle(Vector2[] shore, int pond)
    {
        Vector2[] closed = shore.Append(shore[0]).ToArray();
        Vector2[] reduced = NavigationServer2D.SimplifyPath(closed, (float)(ShoreSimplificationTolerance * NavigationScale));
        if (reduced.Length < 4 || reduced[0] != shore[0] || reduced[^1] != shore[0])
            throw new TerrainCreationException("导航简化", $"第{pond}塘的原生 RDP 未保留有效封闭轮廓。");
        double retreat = _radius + SafetyMargin + ExtraShoreRetreat;
        double compensation = ShoreSimplificationTolerance + NavigationCellSize / NavigationScale;
        // 合并补偿与离岸距离，仅 Round 一次，避免两次圆角引入重叠短边。
        var expanded = Geometry2D.OffsetPolygon(reduced[..^1], (float)((retreat + compensation) * NavigationScale), Geometry2D.PolyJoinType.Round);
        if (expanded.Count != 1) throw new TerrainCreationException("导航简化", $"第{pond}塘的原生 Round 产生{expanded.Count}个闭环。");
        Vector2[] original = expanded[0];
        var points = new List<Vector2>();
        foreach (Vector2 point in original)
            if (points.Count == 0 || point != points[^1]) points.Add(point);
        if (points.Count > 1 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        Vector2[] obstacle = points.ToArray();
        if (obstacle.Length < 3 || obstacle.Distinct().Count() != obstacle.Length)
            throw new TerrainCreationException("导航简化", $"第{pond}塘的原生障碍含退化轮廓或非连续重复顶点。");
        if (obstacle.Length != original.Length && (Geometry2D.ClipPolygons(original, obstacle).Count != 0
            || Geometry2D.ClipPolygons(obstacle, original).Count != 0))
            throw new TerrainCreationException("导航简化", $"第{pond}塘仅去除连续相等顶点后几何发生变化。");
        // 一次性认证包含未简化原岸的完整脚底与额外离岸区域；不凭 RDP 容差推断保守性。
        var required = Geometry2D.OffsetPolygon(shore, (float)(retreat * NavigationScale), Geometry2D.PolyJoinType.Round);
        if (required.Count == 0 || required.Any(outline => Geometry2D.ClipPolygons(outline, obstacle).Count != 0)
            || Geometry2D.ClipPolygons(shore, obstacle).Count != 0)
            throw new TerrainCreationException("导航简化", $"第{pond}塘的保守障碍未完整覆盖原水域及已确认的离岸区域。");
        return obstacle;
    }
    private static void NormalizeTriangleStarts(NavigationPolygon polygon, Vector2[] vertices)
    {
        int[][] faces = Enumerable.Range(0, polygon.GetPolygonCount()).Select(polygon.GetPolygon).ToArray();
        bool hasArea = false;
        for (int i = 0; i < faces.Length; i++)
        {
            int[] face = faces[i];
            Vector2 a = vertices[face[0]], b = vertices[face[1]], c = vertices[face[2]];
            hasArea |= (double)(b.X - a.X) * (c.Y - a.Y) - (double)(b.Y - a.Y) * (c.X - a.X) != 0;
            int shortest = 0;
            float shortestLength = float.PositiveInfinity;
            for (int edge = 0; edge < 3; edge++)
            {
                float length = vertices[face[edge]].DistanceSquaredTo(vertices[face[(edge + 1) % 3]]);
                if (length < shortestLength) { shortest = edge; shortestLength = length; }
            }
            // 仅循环轮换原三角形索引，保持坐标、朝向、三边和面顺序完全相同。
            if (shortest != 0) faces[i] = new[] { face[shortest], face[(shortest + 1) % 3], face[(shortest + 2) % 3] };
        }
        if (faces.Length > 0 && !hasArea) throw new TerrainCreationException("导航三角化", "非空原生区域没有正面积多边形。");
        polygon.ClearPolygons();
        foreach (int[] face in faces) polygon.AddPolygon(face);
    }
    private async System.Threading.Tasks.Task ValidateNavigationWorkCenters(SceneTree tree, System.Threading.CancellationToken cancellationToken)
    {
        var validationTimer = System.Diagnostics.Stopwatch.StartNew();
        // 只验证原生网格的完整共享边，不生成或修补任何路线。
        float mergeCell = NavigationCellSize * NavigationServer2D.MapGetMergeRasterizerCellScale(_navigationMap);
        var faces = new List<Vector2[]>();
        var parents = new List<int>();
        var buckets = new Dictionary<Vector2I, List<int>>();
        var mapEdges = new Dictionary<(ulong, ulong), (int Face, int Count)>();
        int Root(int index)
        {
            while (parents[index] != index) { parents[index] = parents[parents[index]]; index = parents[index]; }
            return index;
        }
        void Join(int a, int b) { a = Root(a); b = Root(b); if (a != b) parents[b] = a; }
        ulong PointKey(Vector2 point)
        {
            int x = (int)MathF.Floor(point.X / mergeCell), y = (int)MathF.Floor(point.Y / mergeCell);
            return (uint)x | ((ulong)(uint)y << 32);
        }
        void Edge(Dictionary<(ulong, ulong), (int Face, int Count)> edges, (ulong, ulong) key, int face)
        {
            if (!edges.TryGetValue(key, out var previous)) { edges.Add(key, (face, 1)); return; }
            if (previous.Count != 1) throw new TerrainCreationException("导航连通", "同一原生完整边出现超过两张面，不能认证连通性。");
            Join(previous.Face, face);
            edges[key] = (previous.Face, 2);
        }
        int chunkIndex = 0;
        foreach (NavigationPolygon polygon in _polygons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regionEdges = new Dictionary<(ulong, ulong), (int Face, int Count)>();
            Vector2[] vertices = polygon.GetVertices();
            for (int i = 0; i < polygon.GetPolygonCount(); i++)
            {
                int index = faces.Count;
                Vector2[] face = polygon.GetPolygon(i).Select(vertex => vertices[vertex]).ToArray();
                faces.Add(face); parents.Add(index);
                for (int edge = 0; edge < face.Length; edge++)
                {
                    ulong a = PointKey(face[edge]), b = PointKey(face[(edge + 1) % face.Length]);
                    Edge(regionEdges, a <= b ? (a, b) : (b, a), index);
                }
                int left = (int)MathF.Floor(face.Min(v => v.X) / NavigationScale);
                int right = (int)MathF.Floor(face.Max(v => v.X) / NavigationScale);
                int top = (int)MathF.Floor(face.Min(v => v.Y) / NavigationScale);
                int bottom = (int)MathF.Floor(face.Max(v => v.Y) / NavigationScale);
                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++)
                    {
                        var key = new Vector2I(x, y);
                        if (!buckets.TryGetValue(key, out var entries)) buckets[key] = entries = new();
                        entries.Add(index);
                    }
            }
            // 引擎先在区域内部配对，仅剩余自由边参与地图跨区域配对。
            foreach (var pair in regionEdges)
                if (pair.Value.Count == 1) Edge(mapEdges, pair.Key, pair.Value.Face);
            if (++chunkIndex % 16 == 0) await WaitPhysicsFrame(tree).WaitAsync(cancellationToken);
        }
        var boundary = new List<(Vector2 A, Vector2 B)>();
        var boundaryBuckets = new Dictionary<Vector2I, List<int>>();
        void AddBoundary(Vector2 a, Vector2 b)
        {
            int index = boundary.Count;
            boundary.Add((a, b));
            foreach (Vector2I bucket in Buckets(a, b, NavigationCellSize / NavigationScale))
            {
                if (!boundaryBuckets.TryGetValue(bucket, out var entries)) boundaryBuckets[bucket] = entries = new();
                entries.Add(index);
            }
        }
        foreach (Vector2[] outline in _navigationObstacles)
            for (int edge = 0; edge < outline.Length; edge++) AddBoundary(outline[edge], outline[(edge + 1) % outline.Length]);
        float minimum = (float)((-.5 + _radius + SafetyMargin) * NavigationScale);
        float maximum = (float)((MapSize - .5 - _radius - SafetyMargin) * NavigationScale);
        Vector2[] outer = { new(minimum, minimum), new(maximum, minimum), new(maximum, maximum), new(minimum, maximum) };
        for (int edge = 0; edge < outer.Length; edge++) AddBoundary(outer[edge], outer[(edge + 1) % outer.Length]);
        bool OnBoundary(Vector2 a, Vector2 b)
        {
            var seen = new HashSet<int>();
            foreach (Vector2I bucket in Buckets(a, b, NavigationCellSize / NavigationScale))
                if (boundaryBuckets.TryGetValue(bucket, out var entries))
                    foreach (int index in entries)
                    {
                        if (!seen.Add(index)) continue;
                        var edge = boundary[index];
                        if (a.DistanceTo(Geometry2D.GetClosestPointToSegment(a, edge.A, edge.B)) <= NavigationCellSize
                            && b.DistanceTo(Geometry2D.GetClosestPointToSegment(b, edge.A, edge.B)) <= NavigationCellSize) return true;
                    }
            return false;
        }
        int boundaryEdges = 0;
        foreach (var pair in mapEdges)
        {
            if (pair.Value.Count != 1) continue;
            cancellationToken.ThrowIfCancellationRequested();
            Vector2[] face = faces[pair.Value.Face];
            for (int edge = 0; edge < face.Length; edge++)
            {
                Vector2 a = face[edge], b = face[(edge + 1) % face.Length];
                ulong first = PointKey(a), second = PointKey(b);
                if ((first <= second ? (first, second) : (second, first)) != pair.Key) continue;
                if (!OnBoundary(a, b)) throw new TerrainCreationException("导航共边", $"原生网格内部出现未配对完整边{a / NavigationScale}→{b / NavigationScale}。");
                break;
            }
            if (++boundaryEdges % 1024 == 0) await WaitPhysicsFrame(tree).WaitAsync(cancellationToken);
        }
        TraceCreation(Seed, $"原生共边审计通过，{faces.Count}面、{boundaryEdges}条外边界", validationTimer);
        bool Contains(Vector2 point, int component, out int containing)
        {
            containing = -1;
            var bucket = new Vector2I((int)MathF.Floor(point.X / NavigationScale), (int)MathF.Floor(point.Y / NavigationScale));
            if (!buckets.TryGetValue(bucket, out var entries)) return false;
            foreach (int index in entries)
            {
                if (component >= 0 && Root(index) != component) continue;
                Vector2[] face = faces[index];
                if ((face[1] - face[0]).Cross(face[2] - face[0]) == 0) continue;
                // 原生三角形谓词的半开边界随朝向改变；两种朝向覆盖同一闭三角形。
                if (Geometry2D.PointIsInsideTriangle(point, face[0], face[1], face[2])
                    || Geometry2D.PointIsInsideTriangle(point, face[2], face[1], face[0])) { containing = index; return true; }
            }
            return false;
        }
        Vector2 center = Vector2.One * ((MapSize - 1) * NavigationScale / 2);
        if (!Contains(center, -1, out int centerFace)) throw new TerrainCreationException("导航连通", "保护区中心没有原生可行走面。");
        int component = Root(centerFace);
        var checkedCenters = new HashSet<Vector2I>();
        foreach (BuildingKind kind in new[] { BuildingKind.Farm, BuildingKind.Processor })
        {
            BuildingFootprint footprint = BuildingFootprint.Get(kind);
            for (int y = 0; y < MapSize; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < MapSize; x++)
                {
                    var anchor = new Vector2I(x, y);
                    if (!footprint.Offsets.All(offset => IsFullLand(anchor + offset))) continue;
                    Vector2I work = BuildingFootprint.WorkCell(anchor, kind);
                    if (!checkedCenters.Add(work)) continue;
                    if (!Contains(new Vector2(work.X * NavigationScale, work.Y * NavigationScale), component, out _))
                        throw new TerrainCreationException("工作中心连通", $"合法{kind}({x},{y})的工作中心不在保护区同一原生连通分量中。");
                }
                if (y % 8 == 7) await WaitPhysicsFrame(tree).WaitAsync(cancellationToken);
            }
        }
    }
    public TerrainCellSnapshot GetCell(Vector2I cell)
    {
        if (!InMap(cell)) throw new ArgumentOutOfRangeException(nameof(cell), "地块越出地图。");
        int offset = cell.Y * (MapSize + 1) + cell.X;
        TerrainKind Kind(int index) => _waterVertices[index] ? TerrainKind.Water : TerrainKind.Grass;
        return new(Kind(offset), Kind(offset + 1), Kind(offset + MapSize + 2), Kind(offset + MapSize + 1));
    }
    public bool IsFullLand(Vector2I cell) => InMap(cell) && GetCell(cell).IsFullLand;
    private bool InMap(Vector2I cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < MapSize && cell.Y < MapSize;

    /**
     * <summary>通过原生 A* 与 Funnel 查询完整圆脚底路线，保持真实连续起终点。</summary>
     * <param name="start">基础格平面的真实连续起点。</param>
     * <param name="target">真实目标，不吸附或移动目标。</param>
     * <returns>完整不可变路线或明确拒绝，不返回部分路线。</returns>
     */
    public LandPathResult FindLandPath(LandPosition start, LandPosition target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_navigationUpdating) throw new InvalidOperationException("道路导航尚未正常同步，宿主须先等待准备完成。");
        if (!ValidPosition(start)) return new(LandPathStatus.InvalidStart, null);
        if (!ValidPosition(target)) return new(LandPathStatus.InvalidTarget, null);
        if (start == target) return new(LandPathStatus.Success, new LandPath(new[] { start }));
        if (!_navigationMap.IsValid) return new(LandPathStatus.Success, new LandPath(new[] { start, target }));
        using var parameters = new NavigationPathQueryParameters2D
        {
            Map = _navigationMap,
            StartPosition = ToNavigation(start),
            TargetPosition = ToNavigation(target),
            PathfindingAlgorithm = NavigationPathQueryParameters2D.PathfindingAlgorithmEnum.Astar,
            PathPostprocessing = NavigationPathQueryParameters2D.PathPostProcessing.Corridorfunnel,
            PathSearchMaxPolygons = 0,
            SimplifyPath = false
        };
        using var result = new NavigationPathQueryResult2D();
        NavigationServer2D.QueryPath(parameters, result);
        Vector2[] native = result.Path;
        // 仅容许引擎坐标舍入量级的端点修正，并用真实端点连接后逐段核验。
        if (native.Length == 0 || native[0].DistanceTo(parameters.StartPosition) > NavigationCellSize
            || native[^1].DistanceTo(parameters.TargetPosition) > NavigationCellSize)
        {
            return new(LandPathStatus.Unreachable, null);
        }
        var points = new List<LandPosition> { start };
        foreach (Vector2 point in native) points.Add(LandPosition.FromCoordinates(point.X / (double)NavigationScale, point.Y / (double)NavigationScale));
        points.Add(target);
        for (int i = 1; i < points.Count; i++)
            if (!SafeSegment(points[i - 1], points[i])) throw new TerrainCreationException("路线净空", $"原生完整路线第{i}段{points[i - 1].ToVector2()}→{points[i].ToVector2()}未通过真实圆脚底逐段检查。");
        return new(LandPathStatus.Success, new LandPath(points));
    }
    private static Vector2 ToNavigation(LandPosition point) => new((float)(point.X * NavigationScale), (float)(point.Y * NavigationScale));
    private bool ValidPosition(LandPosition point)
    {
        if (!InBounds(point)) return false;
        Vector2 scaled = ToNavigation(point);
        foreach (int candidate in ShoreCandidates(scaled, scaled))
        {
            var edge = _shoreSegments[candidate];
            if (scaled.DistanceTo(Geometry2D.GetClosestPointToSegment(scaled, edge.Start, edge.End)) / NavigationScale < _radius) return false;
        }
        Vector2[]? square = null;
        for (int p = 0; p < _ponds.Length; p++)
        {
            if (!Near(_ponds[p], point, point)) continue;
            float half = (float)(_radius * NavigationScale / 2);
            square ??= new[] { scaled + new Vector2(-half, -half), scaled + new Vector2(half, -half), scaled + new Vector2(half, half), scaled + new Vector2(-half, half) };
            // 已确认距所有岸段至少为圆半径，圆内方框不可能跨岸；仅借原生 Clipper 区分水内外。
            if (Geometry2D.IntersectPolygons(square, _shores[p]).Count != 0) return false;
        }
        return true;
    }
    private bool InBounds(LandPosition point)
    {
        double minimum = _radius - .5, maximum = MapSize - .5 - _radius;
        return point.X >= minimum && point.Y >= minimum && point.X <= maximum && point.Y <= maximum;
    }
    private bool SafeSegment(LandPosition a, LandPosition b)
    {
        // 真实起终点已检查内外；连续折线若从陆地进入水内，必有一段扫圆触及岸线。
        if (!InBounds(a) || !InBounds(b)) return false;
        Vector2 start = ToNavigation(a), end = ToNavigation(b);
        foreach (int candidate in ShoreCandidates(start, end))
        {
            var edge = _shoreSegments[candidate];
            Vector2[] pair = Geometry2D.GetClosestPointsBetweenSegments(start, end, edge.Start, edge.End);
            if (pair[0].DistanceTo(pair[1]) / NavigationScale < _radius) return false;
        }
        return true;
    }
    private IEnumerable<int> ShoreCandidates(Vector2 a, Vector2 b)
    {
        var seen = new HashSet<int>();
        foreach (Vector2I bucket in Buckets(a, b, _radius))
            if (_shoreBuckets.TryGetValue(bucket, out var entries))
                foreach (int index in entries)
                    if (seen.Add(index)) yield return index;
    }
    private static IEnumerable<Vector2I> Buckets(Vector2 a, Vector2 b, double radius)
    {
        double x0 = Math.Min(a.X, b.X) / NavigationScale - radius, y0 = Math.Min(a.Y, b.Y) / NavigationScale - radius;
        double x1 = Math.Max(a.X, b.X) / NavigationScale + radius, y1 = Math.Max(a.Y, b.Y) / NavigationScale + radius;
        int left = (int)Math.Floor(x0), top = (int)Math.Floor(y0);
        // 闭边界同时进入邻桶，不把落在整格线上的岸段或扫圆边界遗漏。
        if (x0 == left) left--;
        if (y0 == top) top--;
        for (int y = top; y <= Math.Floor(y1); y++)
            for (int x = left; x <= Math.Floor(x1); x++) yield return new Vector2I(x, y);
    }
    private static bool Near(TerrainPond pond, LandPosition a, LandPosition b) => Math.Max(a.X, b.X) >= pond.MinX && Math.Min(a.X, b.X) <= pond.MaxX && Math.Max(a.Y, b.Y) >= pond.MinY && Math.Min(a.Y, b.Y) <= pond.MaxY;
    private void ValidateWorkCenters()
    {
        foreach (BuildingKind kind in new[] { BuildingKind.Farm, BuildingKind.Processor })
        {
            BuildingFootprint footprint = BuildingFootprint.Get(kind);
            int minimumX = footprint.Offsets.Min(p => p.X), maximumX = footprint.Offsets.Max(p => p.X);
            int minimumY = footprint.Offsets.Min(p => p.Y), maximumY = footprint.Offsets.Max(p => p.Y);
            bool rectangle = footprint.Offsets.Count == (maximumX - minimumX + 1) * (maximumY - minimumY + 1);
            double inner = Math.Min(Math.Min(footprint.WorkOffset.X - minimumX + .5, maximumX - footprint.WorkOffset.X + .5),
                Math.Min(footprint.WorkOffset.Y - minimumY + .5, maximumY - footprint.WorkOffset.Y + .5));
            if (rectangle && _radius + SafetyMargin <= inner) continue;
            for (int y = -minimumY; y < MapSize - maximumY; y++)
                for (int x = -minimumX; x < MapSize - maximumX; x++)
                {
                    Vector2I anchor = new(x, y);
                    if (footprint.Offsets.All(offset => IsFullLand(anchor + offset)) && !ValidPosition(LandPosition.FromCell(BuildingFootprint.WorkCell(anchor, kind))))
                        throw new TerrainCreationException("工作中心净空", $"合法{kind}占地({x},{y})的工作中心容不下本局圆脚底。");
                }
        }
    }
    /**
     * <summary>释放本局独占的原生地图与区域；可重复调用。</summary>
     */
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _navigationLifetime.Cancel();
        foreach (Rid region in _navigationRegions) NavigationServer2D.FreeRid(region);
        if (_navigationMap.IsValid) NavigationServer2D.FreeRid(_navigationMap);
        foreach (NavigationPolygon polygon in _polygons) polygon.Dispose();
        _navigationLifetime.Dispose();
    }
}
