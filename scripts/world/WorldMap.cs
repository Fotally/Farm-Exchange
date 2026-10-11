using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Time;

namespace FarmExchange.World;

public partial class WorldMap : Node2D
{
    [Signal]
    public delegate void SelectionChangedEventHandler(Vector2I cell);

    private const int MapSize = MapCoordinates.MapSize;
    private const int ChunkSize = 8;
    private const int ChunksPerSide = MapSize / ChunkSize;
    private const float SeamOverlap = 0.75f;

    private static readonly Vector2 FarmPivot = new(112, 128);
    private static readonly Vector2 BuildingPivot = new(128, 176);
    private static readonly string[] CropNames =
        { "wheat", "corn", "rice", "potato", "sunflower", "sugarcane", "radish" };
    private static readonly Color SelectedColor = new(0.95f, 0.76f, 0.31f);
    private static readonly Color EdgeColor = new(0.91f, 0.86f, 0.66f);

    private readonly RoadAppearance _roads = new();
    private readonly Texture2D[,,] _roadTextures = new Texture2D[5, 16, 4];
    private RoadPreviewOverlay _roadPreviewOverlay = null!;
    private readonly Dictionary<int, FacilityVisual> _facilities = new();
    private readonly Texture2D[,] _buildings = new Texture2D[7, 4];
    private readonly Texture2D[,,] _crops = new Texture2D[7, 3, 4];
    private Texture2D _grass = null!;
    private readonly Texture2D[] _drySoil = new Texture2D[4];
    private readonly Texture2D[] _wetSoil = new Texture2D[4];
    private readonly Texture2D[] _seeds = new Texture2D[4];
    private Node2D _soilLayer = null!;
    private Node2D _depthLayer = null!;
    private Rect2 _viewBounds;
    private EnvironmentDecorations _environment = null!;
    private WeatherPresentation _weather = null!;
    private readonly List<FacilityVisual> _visibleFacilities = new();
    private SimulationDriver? _driver;
    private uint _resultSecond;
    private readonly MapChunk[] _chunks = new MapChunk[ChunksPerSide * ChunksPerSide];
    private FarmGame _game = null!;
    private IReadOnlyList<BuildingSpaceSnapshot>? _lastSpaces;
    private SelectionOverlay _overlay = null!;
    private PlacementOverlay _placementOverlay = null!;
    private PlacementVisual? _placement;
    private Vector2I _selectedCell = new(-1, -1);
    private Transform2D _lastCanvasTransform;
    private Vector2 _lastViewportSize;

    internal int LastVisiblePlotCount { get; private set; }
    internal int ChunkRedrawCount { get; private set; }

    /**
     * <summary>查询当前实际显示的候选锚点，隐藏预览时为空。</summary>
     */
    public Vector2I? PlacementPreviewAnchor => _placement?.AnchorCell;

    /**
     * <summary>初始化本局世界表现及只读经营绑定。</summary>
     * <param name="game">本局经营状态。</param>
     * <param name="driver">同局倍率来源；独立场景省略时按1×表现。</param>
     * <remarks>地图挂树后只调用一次；初始化不回播既有成功结果，环境以实际占地剔除。</remarks>
     */
    public void SetGame(FarmGame game, SimulationDriver? driver = null)
    {
        _game = game;
        _driver = driver;
        _resultSecond = game.Calendar.ElapsedSeconds;
        ProcessPriority = 1;
        TextureFilter = TextureFilterEnum.Nearest;
        LoadTextures();
        _soilLayer = new Node2D { Name = "Soil", ZIndex = -1 };
        _depthLayer = new Node2D { Name = "DepthSorted", YSortEnabled = true };
        AddChild(_soilLayer);
        AddChild(_depthLayer);
        _environment = new EnvironmentDecorations(_depthLayer);
        for (int row = 0; row < ChunksPerSide; row++)
        {
            for (int col = 0; col < ChunksPerSide; col++)
            {
                var chunk = new MapChunk(this, col * ChunkSize, row * ChunkSize);
                _chunks[row * ChunksPerSide + col] = chunk;
                AddChild(chunk);
            }
        }
        _overlay = new SelectionOverlay(this) { ZIndex = 10 };
        AddChild(_overlay);
        _placementOverlay = new PlacementOverlay(this) { ZIndex = 11 };
        AddChild(_placementOverlay);
        _roadPreviewOverlay = new RoadPreviewOverlay(this) { ZIndex = 11 };
        AddChild(_roadPreviewOverlay);
        _weather = new WeatherPresentation { Name = "Weather", ZIndex = 9 };
        AddChild(_weather);
        SyncFromGame();
        UpdateVisibleChunks();
    }

    /**
     * <summary>将人物表现容器挂入地图共同深度排序层。</summary>
     * <param name="presentation">尚无父节点的表现容器，子节点原点必须位于各自脚根。</param>
     * <remarks>在 SetGame 后调用；容器设为零位置并启用嵌套 YSort，由地图释放。不得给人物另设 ZIndex。</remarks>
     */
    public void AttachDepthSorted(Node2D presentation)
    {
        presentation.Position = Vector2.Zero;
        presentation.YSortEnabled = true;
        _depthLayer.AddChild(presentation);
    }

    public void SyncFromGame()
    {
        var spaces = _game.GetBuildingSpaces();
        bool spaceChanged = !ReferenceEquals(spaces, _lastSpaces);
        HashSet<int>? remaining = null;
        if (spaceChanged)
        {
            remaining = new HashSet<int>(_facilities.Keys);
            _environment.Synchronize(spaces);
        }
        foreach (BuildingSpaceSnapshot space in spaces)
        {
            int index = space.AnchorCell.Y * MapSize + space.AnchorCell.X;
            if (space.Building == BuildingKind.Road)
            {
                continue;
            }
            PlotSnapshot plot = _game.GetPlot(space.AnchorCell);
            var appearance = new FacilityAppearance(plot.Building, plot.CropKind, plot.Crop,
                plot.HasWater, plot.Crop == CropStage.Growing ? Math.Min(2, (int)(plot.GrowthProgress * 3)) : 0,
                space.Orientation);
            remaining?.Remove(index);
            if (!_facilities.TryGetValue(index, out FacilityVisual? visual))
            {
                Vector2 position = MapCoordinates.CellToLocalCenter(space.WorkCell);
                visual = new FacilityVisual(position);
                _facilities.Add(index, visual);
                _soilLayer.AddChild(visual.Soil);
                _depthLayer.AddChild(visual.Surface);
            }
            visual.Snapshot = plot;
            visual.Processing = plot.Building == BuildingKind.Processor &&
                plot.RemainingSeconds > 0;
            if (visual.Appearance != appearance)
            {
                visual.Appearance = appearance;
                int orientation = (int)space.Orientation;
                visual.Soil.Texture = plot.Building == BuildingKind.Farm ? (plot.HasWater ? _wetSoil[orientation] : _drySoil[orientation]) : null;
                visual.Surface.Offset = -(plot.Building == BuildingKind.Farm ? FarmPivot : BuildingPivot);
                visual.Surface.Texture = plot.Building == BuildingKind.Processor ? _buildings[(int)plot.CropKind, orientation] :
                    plot.Crop switch
                    {
                        CropStage.Seeded => _seeds[orientation],
                        CropStage.Growing => _crops[(int)plot.CropKind, appearance.GrowthStage, orientation],
                        _ => null,
                    };
            }
        }
        if (remaining != null)
        {
            foreach (int index in remaining)
            {
                FacilityVisual removed = _facilities[index];
                removed.Soil.Free();
                removed.Surface.Free();
                removed.Motion?.Free();
                _facilities.Remove(index);
            }
        }
        UpdateFacilityVisibility();
        if (spaceChanged)
        {
            RefreshRoadCells(_roads.Synchronize(spaces));
            _lastSpaces = spaces;
        }
        _overlay.QueueRedraw();
    }

    private void LoadTextures()
    {
        _grass = GD.Load<Texture2D>("res://assets/gameplay/terrain/terrain_81_mesh.png");
        string[] materials = { "dirt", "gravel", "cobble", "brick", "slab" };
        var roadAtlases = new Texture2D[materials.Length];
        for (int index = 0; index < materials.Length; index++)
            roadAtlases[index] = GD.Load<Texture2D>($"res://assets/gameplay/terrain/roads/road_{materials[index]}_atlas.png");
        using JsonDocument regions = JsonDocument.Parse(FileAccess.GetFileAsString(
            "res://assets/gameplay/terrain/roads/atlas_regions.json"));
        foreach (JsonElement entry in regions.RootElement.EnumerateArray())
        {
            int material = Array.IndexOf(materials, entry.GetProperty("material").GetString());
            JsonElement region = entry.GetProperty("region");
            _roadTextures[material, entry.GetProperty("mask").GetInt32(), entry.GetProperty("variant").GetInt32()] =
                new AtlasTexture
                {
                    Atlas = roadAtlases[material],
                    Region = new Rect2(region[0].GetSingle(), region[1].GetSingle(), region[2].GetSingle(), region[3].GetSingle()),
                };
        }
        for (int orientation = 0; orientation < 4; orientation++)
        {
            _drySoil[orientation] = GD.Load<Texture2D>($"res://assets/gameplay/soil/field_dry_q{orientation}.png");
            _wetSoil[orientation] = GD.Load<Texture2D>($"res://assets/gameplay/soil/field_wet_q{orientation}.png");
            _seeds[orientation] = GD.Load<Texture2D>($"res://assets/gameplay/crops/field_seeded_q{orientation}.png");
            for (int crop = 0; crop < CropNames.Length; crop++)
            {
                _buildings[crop, orientation] = GD.Load<Texture2D>($"res://assets/gameplay/buildings/{CropNames[crop]}_workshop_q{orientation}.png");
                for (int stage = 0; stage < 3; stage++)
                    _crops[crop, stage, orientation] = GD.Load<Texture2D>($"res://assets/gameplay/crops/{CropNames[crop]}_growing_{stage + 1:D2}_q{orientation}.png");
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_game == null)
            return;
        Transform2D canvasTransform = GetGlobalTransformWithCanvas();
        Vector2 viewportSize = GetViewportRect().Size;
        if (canvasTransform != _lastCanvasTransform || viewportSize != _lastViewportSize)
            UpdateVisibleChunks();
        AdvanceMotions(delta);
        _weather.Update(_game.ActiveWeatherRegions, _viewBounds, delta, _game.IsPaused, _driver?.Rate ?? 1);
    }

    private void AdvanceMotions(double delta)
    {
        double rate = _driver?.Rate ?? 1;
        double seconds = _game.IsPaused ? 0 : delta * (SimulationDriver.IsPublicRateAllowed(rate) ? rate : 1);
        foreach (FacilityVisual visual in _visibleFacilities)
        {
            if (visual.ActiveResult is ProductionResult active && !_game.IsPresentationTargetCurrent(active))
            {
                visual.Motion!.ClearResult();
                visual.ActiveResult = null;
            }
            visual.Motion!.Advance(seconds, visual.Processing);
        }
        uint second = _game.Calendar.ElapsedSeconds;
        if (_resultSecond == second) return;
        _resultSecond = second;
        foreach (ProductionResult result in _game.GetPresentationResults())
        {
            if (result.Kind is not (ProductionResultKind.Harvest or ProductionResultKind.Product) ||
                !_game.IsPresentationResultCurrent(result)) continue;
            int index = result.AnchorCell.Y * MapSize + result.AnchorCell.X;
            if (!_facilities.TryGetValue(index, out FacilityVisual? visual) || visual.Motion == null) continue;
            visual.Motion.ShowResult(result);
            visual.ActiveResult = result;
        }
    }

    private void UpdateFacilityVisibility()
    {
        _visibleFacilities.Clear();
        foreach (FacilityVisual visual in _facilities.Values)
        {
            bool visible = _viewBounds.Intersects(visual.Bounds);
            visual.SetVisible(visible);
            if (!visible)
            {
                visual.Motion?.Free();
                visual.Motion = null;
                visual.ActiveResult = null;
                continue;
            }
            if (visual.Motion == null)
            {
                visual.Motion = new FacilityMotion { Position = visual.Position };
                _depthLayer.AddChild(visual.Motion);
            }
            FacilityAppearance appearance = visual.Appearance!.Value;
            visual.Motion.Configure(visual.Snapshot, appearance.GrowthStage, appearance.Orientation);
            visual.SetSurfaceVisible(!FacilityMotion.ReplacesSurface(visual.Snapshot, appearance.Orientation));
            _visibleFacilities.Add(visual);
        }
    }

    private void UpdateVisibleChunks()
    {
        _lastCanvasTransform = GetGlobalTransformWithCanvas();
        _lastViewportSize = GetViewportRect().Size;
        Transform2D inverse = _lastCanvasTransform.AffineInverse();
        Vector2[] corners =
        {
            inverse * Vector2.Zero,
            inverse * new Vector2(_lastViewportSize.X, 0f),
            inverse * _lastViewportSize,
            inverse * new Vector2(0f, _lastViewportSize.Y),
        };
        float minX = corners[0].X;
        float maxX = corners[0].X;
        float minY = corners[0].Y;
        float maxY = corners[0].Y;
        foreach (Vector2 corner in corners)
        {
            minX = Math.Min(minX, corner.X);
            maxX = Math.Max(maxX, corner.X);
            minY = Math.Min(minY, corner.Y);
            maxY = Math.Max(maxY, corner.Y);
        }
        Rect2 viewBounds = new(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
        _viewBounds = viewBounds;
        _environment.UpdateVisible(viewBounds);
        LastVisiblePlotCount = 0;
        foreach (MapChunk chunk in _chunks)
        {
            bool visible = viewBounds.Intersects(chunk.Bounds);
            chunk.Visible = visible;
            if (visible)
            {
                LastVisiblePlotCount += ChunkSize * ChunkSize;
                chunk.RedrawIfVisible();
            }
        }
        UpdateFacilityVisibility();
    }

    public void SelectAtScreenPosition(Vector2 screenPosition)
    {
        Vector2I cell = ScreenToCell(screenPosition);
        if (!MapCoordinates.ContainsCell(cell))
            return;

        _selectedCell = cell;
        EmitSignal(SignalName.SelectionChanged, cell);
        _overlay.QueueRedraw();
    }

    /**
     * <summary>将逻辑视口位置转换为鼠标指向的基础格。</summary>
     * <param name="screenPosition">逻辑视口坐标，单位为像素。</param>
     * <returns>未经地图范围限制的格坐标，供边缘候选展示。</returns>
     */
    public Vector2I ScreenToCell(Vector2 screenPosition) =>
        MapCoordinates.LocalPositionToCell(GetGlobalTransformWithCanvas().AffineInverse() * screenPosition);

    /**
     * <summary>只读刷新一座候选建筑的占地预览。</summary>
     * <param name="building">当前可摆放建筑类型。</param>
     * <param name="crop">当前选定作物，用于农田或加工类型标记。</param>
     * <param name="anchorCell">鼠标指向的候选锚点，可在地图范围外。</param>
     * <param name="orientation">合法世界朝向；省略时为 Q0，朝向不改变占地与工作中心。</param>
     * <remarks>只检查当前占地的越界与实际占用，不登记建筑、不改金币，不同步地图块。</remarks>
     */
    public void UpdatePlacementPreview(BuildingKind building, CropKind crop, Vector2I anchorCell,
        BuildingOrientation orientation = BuildingOrientation.Q0)
    {
        if ((uint)orientation >= 4)
            throw new ArgumentOutOfRangeException(nameof(orientation));
        BuildingFootprint footprint = BuildingFootprint.Get(building);
        PlacementPreviewGeometry.PreviewCell[] cells = PlacementPreviewGeometry.GetCells(anchorCell, footprint.Offsets, _game);
        bool changed = _placement == null || _placement.Building != building ||
            _placement.Crop != crop || _placement.AnchorCell != anchorCell || _placement.Orientation != orientation;
        for (int index = 0; index < cells.Length; index++)
        {
            if (!changed && _placement!.Cells[index] != cells[index])
                changed = true;
        }
        if (!changed)
            return;
        _placement = new PlacementVisual(building, crop, anchorCell, cells, orientation);
        _placementOverlay.QueueRedraw();
    }

    /**
     * <summary>移除候选建筑的地图预览。</summary>
     * <remarks>不改变当前建造类型或经营资源，生命周期由场景协调入口持有。</remarks>
     */
    public void ClearPlacementPreview()
    {
        if (_placement == null)
            return;
        _placement = null;
        _placementOverlay.QueueRedraw();
    }

    /**
     * <summary>只读预览道路新建或拆除范围及受影响旧路的完成后连接。</summary>
     * <param name="cells">完整候选路径；允许包含占用格与地图范围外的格。</param>
     * <param name="material">合法道路材质；拆除时保留每格实际材质。</param>
     * <param name="removing">是否仅预览范围内实际道路的拆除。</param>
     * <remarks>冲突格不覆盖旧路，临时连接只刷新受影响地面块；不修改经营与环境。</remarks>
     */
    public void UpdateRoadPreview(IReadOnlyList<Vector2I> cells, RoadMaterial material, bool removing = false)
    {
        IReadOnlyList<RoadAppearance.RoadPreviewCell> previous = _roads.PreviewCells;
        RefreshRoadCells(_roads.UpdatePreview(cells, material, removing, _game));
        bool changed = previous.Count != _roads.PreviewCells.Count;
        for (int index = 0; !changed && index < previous.Count; index++)
            changed = previous[index] != _roads.PreviewCells[index];
        if (changed) _roadPreviewOverlay.QueueRedraw();
    }

    /**
     * <summary>隐藏道路操作提示并恢复真实道路连接外观。</summary>
     * <remarks>仅清理世界临时表现；道路工具的起点与轨迹由场景入口维护。</remarks>
     */
    public void ClearRoadPreview()
    {
        if (_roads.PreviewCells.Count == 0) return;
        RefreshRoadCells(_roads.ClearPreview());
        _roadPreviewOverlay.QueueRedraw();
    }

    internal RoadAppearance.RoadTile? GetDisplayedRoadAppearance(Vector2I cell) => _roads.GetTile(cell);

    private void RefreshRoadCells(HashSet<Vector2I> cells)
    {
        var chunks = new HashSet<MapChunk>();
        foreach (Vector2I cell in cells)
        {
            MapChunk chunk = _chunks[(cell.Y / ChunkSize) * ChunksPerSide + cell.X / ChunkSize];
            chunk.SetRoad(cell.X % ChunkSize, cell.Y % ChunkSize, _roads.GetTile(cell));
            chunks.Add(chunk);
        }
        foreach (MapChunk chunk in chunks) chunk.RedrawIfVisible();
    }

    public void ClearSelection()
    {
        _selectedCell = new Vector2I(-1, -1);
        _overlay.QueueRedraw();
    }

    public Vector2 GetCellWorldCenter(Vector2I cell) => ToGlobal(MapCoordinates.CellToLocalCenter(cell));

    public Vector2 GetGridWorldPosition(Vector2 gridPosition) =>
        ToGlobal(MapCoordinates.GridPositionToLocal(gridPosition));

    public Rect2 LocalBounds() => MapCoordinates.GridRectangleBounds(Vector2I.Zero, MapSize, MapSize);

    public Vector2 ClampGlobalCameraCenter(Vector2 globalCenter) =>
        ToGlobal(MapCoordinates.ClampLocalCenter(ToLocal(globalCenter)));

    private static Vector2[] Outline(Vector2I cell)
    {
        Vector2[] corners = MapCoordinates.GridRectangleOutline(cell, 1, 1);
        corners[0].Y -= SeamOverlap;
        corners[1].X += SeamOverlap;
        corners[2].Y += SeamOverlap;
        corners[3].X -= SeamOverlap;
        return corners;
    }

    private readonly record struct FacilityAppearance(BuildingKind Building, CropKind Crop,
        CropStage Stage, bool Wet, int GrowthStage, BuildingOrientation Orientation);

    private sealed class FacilityVisual
    {
        internal readonly Sprite2D Soil;
        internal readonly Sprite2D Surface;
        internal readonly Rect2 Bounds;
        internal readonly Vector2 Position;
        internal FacilityAppearance? Appearance;
        internal PlotSnapshot Snapshot;
        internal bool Processing;
        internal FacilityMotion? Motion;
        internal ProductionResult? ActiveResult;
        private bool _visible = true;
        private bool _surfaceVisible = true;

        internal FacilityVisual(Vector2 position)
        {
            Position = position;
            Soil = new Sprite2D { Position = position, Centered = false, Offset = -FarmPivot };
            Surface = new Sprite2D { Position = position, Centered = false };
            // 完整透明画布参与可见性，避免块外的屋顶、作物在视野边缘消失。
            Bounds = new Rect2(position - new Vector2(128, 208), new Vector2(256, 272));
        }

        internal void SetVisible(bool visible)
        {
            if (_visible != visible)
            {
                Soil.Visible = visible;
                _visible = visible;
            }
            if (!visible) SetSurfaceVisible(false);
        }

        internal void SetSurfaceVisible(bool visible)
        {
            if (_surfaceVisible == visible) return;
            Surface.Visible = visible;
            _surfaceVisible = visible;
        }
    }

    private sealed record PlacementVisual(BuildingKind Building, CropKind Crop, Vector2I AnchorCell,
        PlacementPreviewGeometry.PreviewCell[] Cells, BuildingOrientation Orientation);

    private sealed partial class PlacementOverlay : Node2D
    {
        private static readonly Color FreeFill = new(0.35f, 0.93f, 0.57f, 0.34f);
        private static readonly Color BlockedFill = new(1f, 0.22f, 0.20f, 0.48f);
        private static readonly Color FreeLine = new(0.65f, 1f, 0.74f, 0.8f);
        private static readonly Color BlockedLine = new(1f, 0.39f, 0.32f, 0.9f);
        private readonly WorldMap _map;

        public PlacementOverlay(WorldMap map) => _map = map;

        public override void _Draw()
        {
            PlacementVisual? preview = _map._placement;
            if (preview == null)
                return;
            BuildingFootprint footprint = BuildingFootprint.Get(preview.Building);
            Vector2 center = MapCoordinates.GridPositionToLocal((Vector2)(preview.AnchorCell + footprint.WorkOffset));
            Color tint = new(1f, 1f, 1f, 0.65f);
            if (preview.Building == BuildingKind.Farm)
                DrawTexture(_map._drySoil[(int)preview.Orientation], center - FarmPivot, tint);
            else if (preview.Building == BuildingKind.Processor)
                DrawTexture(_map._buildings[(int)preview.Crop, (int)preview.Orientation], center - BuildingPivot, tint);
            else if (!preview.Cells[0].Blocked)
            {
                int variant = RoadAppearance.GetVariant(preview.AnchorCell);
                DrawTexture(_map._roadTextures[0, 0, variant], center - new Vector2(32, 16), tint);
            }
            for (int index = 0; index < footprint.Offsets.Count; index++)
            {
                Vector2I cell = preview.Cells[index].Cell;
                Vector2[] corners = MapCoordinates.GridRectangleOutline(cell, 1, 1);
                Color fill = preview.Cells[index].Blocked ? BlockedFill : FreeFill;
                Color line = preview.Cells[index].Blocked ? BlockedLine : FreeLine;
                DrawColoredPolygon(corners, fill);
                DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] }, line, 1f);
            }
            // 外围边从占地定义生成，各段沿用所属格的颜色，不将整座冲突染红。
            var offsets = new HashSet<Vector2I>(footprint.Offsets);
            for (int index = 0; index < footprint.Offsets.Count; index++)
            {
                Vector2I offset = footprint.Offsets[index];
                Color color = preview.Cells[index].Blocked ? BlockedLine : FreeLine;
                foreach ((Vector2 start, Vector2 end) in PlacementPreviewGeometry.GetCellOuterEdges(preview.AnchorCell, offset, offsets))
                    DrawLine(start, end, color, 2f);
            }
            Vector2 anchor = MapCoordinates.GridPositionToLocal((Vector2)preview.AnchorCell);
            DrawArc(anchor, 4f, 0f, MathF.Tau, 16, new Color(1f, 1f, 0.87f, 0.9f), 1.5f);
        }
    }

    private sealed partial class RoadPreviewOverlay : Node2D
    {
        private readonly WorldMap _map;

        internal RoadPreviewOverlay(WorldMap map) => _map = map;

        public override void _Draw()
        {
            foreach (RoadAppearance.RoadPreviewCell cell in _map._roads.PreviewCells)
            {
                Vector2[] corners = MapCoordinates.GridRectangleOutline(cell.Cell, 1, 1);
                Color color = cell.Blocked ? new Color(1f, .22f, .20f, .36f) : new Color(.35f, .93f, .57f, .12f);
                DrawColoredPolygon(corners, color);
                DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] },
                    cell.Blocked ? new Color(1f, .39f, .32f, .9f) : new Color(.65f, 1f, .74f, .8f), 1f);
            }
        }
    }

    private sealed partial class MapChunk : Node2D
    {
        private readonly WorldMap _map;
        private readonly int _firstCol;
        private readonly int _firstRow;
        private readonly RoadAppearance.RoadTile?[] _roads = new RoadAppearance.RoadTile?[ChunkSize * ChunkSize];
        private readonly ArrayMesh _mesh = new();
        private bool _dirty = true;

        public Rect2 Bounds { get; }

        public MapChunk(WorldMap map, int firstCol, int firstRow)
        {
            _map = map;
            _firstCol = firstCol;
            _firstRow = firstRow;
            ZIndex = -2;
            Visible = false;
            Bounds = MapCoordinates.GridRectangleBounds(new Vector2I(firstCol, firstRow), ChunkSize, ChunkSize)
                .Grow(SeamOverlap);
        }

        public void SetRoad(int col, int row, RoadAppearance.RoadTile? road)
        {
            int index = row * ChunkSize + col;
            if (_roads[index] == road)
                return;
            _roads[index] = road;
            _dirty = true;
        }

        public void RedrawIfVisible()
        {
            if (!Visible || !_dirty)
                return;
            _dirty = false;
            QueueRedraw();
        }

        public override void _Draw()
        {
            _map.ChunkRedrawCount++;
            var vertices = new List<Vector2>(ChunkSize * ChunkSize * 4);
            var uvs = new List<Vector2>(ChunkSize * ChunkSize * 4);
            var indices = new List<int>(ChunkSize * ChunkSize * 6);
            Vector2[] corners = { new(32, 0), new(64, 16), new(32, 32), new(0, 16) };
            for (int row = 0; row < ChunkSize; row++)
                for (int col = 0; col < ChunkSize; col++)
                {
                    int mapCol = _firstCol + col;
                    int mapRow = _firstRow + row;
                    int start = vertices.Count;
                    vertices.AddRange(Outline(new Vector2I(mapCol, mapRow)));
                    int variant = (mapCol * 73 + mapRow * 17) % 23 == 0 ? 1 : 0;
                    int atlasIndex = 80 + 81 * variant; // 四角均为grass，保持全可经营地图。
                    Vector2 uvBase = new((atlasIndex % 9) * 68 + 2, (atlasIndex / 9) * 36 + 2);
                    foreach (Vector2 corner in corners)
                        uvs.Add((uvBase + corner) / _map._grass.GetSize());
                    indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
            Godot.Collections.Array arrays = new();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            _mesh.ClearSurfaces();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            DrawMesh(_mesh, _map._grass);
            for (int row = 0; row < ChunkSize; row++)
                for (int col = 0; col < ChunkSize; col++)
                    if (_roads[row * ChunkSize + col] is RoadAppearance.RoadTile road)
                    {
                        Vector2 center = MapCoordinates.CellToLocalCenter(new Vector2I(_firstCol + col, _firstRow + row));
                        Color tint = road.Removed ? new Color(1f, .45f, .4f, .55f) :
                            new Color(1f, 1f, 1f, road.Added ? .65f : 1f);
                        DrawTexture(_map._roadTextures[(int)road.Material, road.Mask, road.Variant],
                            center - new Vector2(32, 16), tint);
                    }
        }
    }

    private sealed partial class SelectionOverlay : Node2D
    {
        private readonly WorldMap _map;

        public SelectionOverlay(WorldMap map) => _map = map;

        public override void _Draw()
        {
            Vector2[] edge = MapCoordinates.GridRectangleOutline(Vector2I.Zero, MapSize, MapSize);
            DrawPolyline(new[] { edge[0], edge[1], edge[2], edge[3], edge[0] }, EdgeColor, 3f);
            if (_map._selectedCell.X < 0)
                return;
            Vector2I selected = _map._selectedCell;
            BuildingSpaceSnapshot? space = _map._game.GetBuildingSpace(selected);
            if (space == null)
            {
                Vector2[] outline = MapCoordinates.GridRectangleOutline(selected, 1, 1);
                DrawPolyline(new[] { outline[0], outline[1], outline[2], outline[3], outline[0] }, SelectedColor, 3f);
                return;
            }
            // 土地拥有固定占地偏移；只绘制外侧边，任一子格均得到同一完整选框。
            var offsets = new HashSet<Vector2I>(space.Footprint.Offsets);
            Vector2I[] neighbours = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };
            foreach (Vector2I offset in space.Footprint.Offsets)
            {
                Vector2[] outline = MapCoordinates.GridRectangleOutline(space.AnchorCell + offset, 1, 1);
                for (int side = 0; side < 4; side++)
                    if (!offsets.Contains(offset + neighbours[side]))
                        DrawLine(outline[side], outline[(side + 1) % 4], SelectedColor, 3f);
            }
        }
    }
}
