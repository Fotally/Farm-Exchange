using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;

namespace FarmExchange.World;

public partial class WorldMap : Node2D
{
    [Signal]
    public delegate void SelectionChangedEventHandler(Vector2I cell);

    private const int MapSize = MapCoordinates.MapSize;
    private const int ChunkSize = 8;
    private const int ChunksPerSide = MapSize / ChunkSize;
    private const float SeamOverlap = 0.75f;

    private static readonly Color TileColor = new(0.53f, 0.64f, 0.38f);
    private static readonly Color FarmColor = new(0.53f, 0.36f, 0.22f);
    private static readonly Color ProcessorColor = new(0.36f, 0.47f, 0.61f);
    private static readonly Color RoadColor = new(0.52f, 0.52f, 0.52f);
    private static readonly Color ProcessorMarkerColor = new(0.86f, 0.90f, 0.93f);
    private static readonly Color SeedColor = new(0.98f, 0.84f, 0.46f);
    private static readonly Color[] GrowingColors =
    {
        new(0.39f, 0.84f, 0.39f),
        new(0.93f, 0.76f, 0.24f),
        new(0.48f, 0.82f, 0.43f),
        new(0.70f, 0.57f, 0.40f),
        new(0.95f, 0.78f, 0.28f),
        new(0.54f, 0.88f, 0.57f),
        new(0.94f, 0.43f, 0.28f),
    };
    private static readonly Color SelectedColor = new(0.95f, 0.76f, 0.31f);
    private static readonly Color EdgeColor = new(0.91f, 0.86f, 0.66f);

    private readonly PlotVisual[] _visuals = new PlotVisual[MapSize * MapSize];
    private readonly MapChunk[] _chunks = new MapChunk[ChunksPerSide * ChunksPerSide];
    private FarmGame _game = null!;
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

    public void SetGame(FarmGame game)
    {
        _game = game;
        ProcessPriority = 1;
        for (int row = 0; row < ChunksPerSide; row++)
        {
            for (int col = 0; col < ChunksPerSide; col++)
            {
                var chunk = new MapChunk(this, col * ChunkSize, row * ChunkSize);
                _chunks[row * ChunksPerSide + col] = chunk;
                AddChild(chunk);
            }
        }
        _overlay = new SelectionOverlay(this);
        AddChild(_overlay);
        _placementOverlay = new PlacementOverlay(this);
        AddChild(_placementOverlay);
        SyncFromGame();
        UpdateVisibleChunks();
    }

    public void SyncFromGame()
    {
        Array.Clear(_visuals);
        foreach (BuildingSpaceSnapshot space in _game.GetBuildingSpaces())
        {
            PlotSnapshot plot = _game.GetPlot(space.AnchorCell);
            foreach (Vector2I offset in space.Footprint.Offsets)
            {
                Vector2I cell = space.AnchorCell + offset;
                _visuals[cell.Y * MapSize + cell.X] = PlotVisual.FromSnapshot(plot, cell == space.WorkCell);
            }
        }
        for (int row = 0; row < MapSize; row++)
        {
            for (int col = 0; col < MapSize; col++)
            {
                MapChunk chunk = _chunks[(row / ChunkSize) * ChunksPerSide + col / ChunkSize];
                chunk.SetVisual(col % ChunkSize, row % ChunkSize, _visuals[row * MapSize + col]);
            }
        }
        _overlay.QueueRedraw();
        foreach (MapChunk chunk in _chunks)
            chunk.RedrawIfVisible();
    }

    public override void _Process(double delta)
    {
        if (_game == null)
            return;
        Transform2D canvasTransform = GetGlobalTransformWithCanvas();
        Vector2 viewportSize = GetViewportRect().Size;
        if (canvasTransform != _lastCanvasTransform || viewportSize != _lastViewportSize)
            UpdateVisibleChunks();
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
     * <remarks>只检查当前占地的越界与实际占用，不登记建筑、不改金币，不同步地图块。</remarks>
     */
    public void UpdatePlacementPreview(BuildingKind building, CropKind crop, Vector2I anchorCell)
    {
        BuildingFootprint footprint = BuildingFootprint.Get(building);
        PlacementPreviewGeometry.PreviewCell[] cells = PlacementPreviewGeometry.GetCells(anchorCell, footprint.Offsets, _game);
        bool changed = _placement == null || _placement.Building != building ||
            _placement.Crop != crop || _placement.AnchorCell != anchorCell;
        for (int index = 0; index < cells.Length; index++)
        {
            if (!changed && _placement!.Cells[index] != cells[index])
                changed = true;
        }
        if (!changed)
            return;
        _placement = new PlacementVisual(building, crop, anchorCell, cells);
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

    private readonly record struct PlotVisual(BuildingKind Building, CropKind CropKind, CropStage Crop, bool Marker)
    {
        public static PlotVisual FromSnapshot(PlotSnapshot plot, bool marker) => new(
            plot.Building,
            plot.Building == BuildingKind.Farm ? plot.CropKind : CropKind.Wheat,
            plot.Building == BuildingKind.Farm ? plot.Crop : CropStage.None,
            marker);
    }

    private sealed record PlacementVisual(BuildingKind Building, CropKind Crop, Vector2I AnchorCell,
        PlacementPreviewGeometry.PreviewCell[] Cells);

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
            Vector2 center = MapCoordinates.GridPositionToLocal((Vector2)(preview.AnchorCell + footprint.WorkOffset));
            Color marker = preview.Building switch
            {
                BuildingKind.Farm => new Color(GrowingColors[(int)preview.Crop], 0.65f),
                BuildingKind.Processor => new Color(ProcessorMarkerColor, 0.65f),
                _ => new Color(RoadColor, 0.65f),
            };
            if (preview.Building == BuildingKind.Farm)
                DrawCircle(center, 6f, marker);
            else if (preview.Building == BuildingKind.Processor)
                DrawRect(new Rect2(center - new Vector2(6f, 6f), new Vector2(12f, 12f)), marker);
            else
                DrawLine(center - new Vector2(9f, 0f), center + new Vector2(9f, 0f), marker, 4f);
            Vector2 anchor = MapCoordinates.GridPositionToLocal((Vector2)preview.AnchorCell);
            DrawArc(anchor, 4f, 0f, MathF.Tau, 16, new Color(1f, 1f, 0.87f, 0.9f), 1.5f);
        }
    }

    private sealed partial class MapChunk : Node2D
    {
        private readonly WorldMap _map;
        private readonly int _firstCol;
        private readonly int _firstRow;
        private readonly PlotVisual[] _plots = new PlotVisual[ChunkSize * ChunkSize];
        private readonly ArrayMesh _mesh = new();
        private bool _dirty = true;

        public Rect2 Bounds { get; }

        public MapChunk(WorldMap map, int firstCol, int firstRow)
        {
            _map = map;
            _firstCol = firstCol;
            _firstRow = firstRow;
            Visible = false;
            Bounds = MapCoordinates.GridRectangleBounds(new Vector2I(firstCol, firstRow), ChunkSize, ChunkSize)
                .Grow(SeamOverlap);
        }

        public void SetVisual(int col, int row, PlotVisual visual)
        {
            int index = row * ChunkSize + col;
            if (_plots[index] == visual)
                return;
            _plots[index] = visual;
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
            var vertices = new List<Vector2>(ChunkSize * ChunkSize * 24);
            var colors = new List<Color>(ChunkSize * ChunkSize * 24);
            var indices = new List<int>(ChunkSize * ChunkSize * 54);
            for (int row = 0; row < ChunkSize; row++)
            {
                for (int col = 0; col < ChunkSize; col++)
                {
                    int mapCol = _firstCol + col;
                    int mapRow = _firstRow + row;
                    PlotVisual plot = _plots[row * ChunkSize + col];
                    Color tileColor = plot.Building switch
                    {
                        BuildingKind.Farm => FarmColor,
                        BuildingKind.Processor => ProcessorColor,
                        BuildingKind.Road => RoadColor,
                        _ => TileColor,
                    };
                    AddQuad(vertices, colors, indices, Outline(new Vector2I(mapCol, mapRow)), tileColor);
                }
            }
            for (int row = 0; row < ChunkSize; row++)
            {
                for (int col = 0; col < ChunkSize; col++)
                {
                    int mapCol = _firstCol + col;
                    int mapRow = _firstRow + row;
                    Vector2 center = MapCoordinates.CellToLocalCenter(new Vector2I(mapCol, mapRow));
                    PlotVisual plot = _plots[row * ChunkSize + col];
                    if (!plot.Marker)
                        continue;
                    if (plot.Building == BuildingKind.Farm && plot.Crop == CropStage.None)
                        AddCircle(vertices, colors, indices, center, 4f, GrowingColors[(int)plot.CropKind]);
                    else if (plot.Crop == CropStage.Seeded)
                        AddCircle(vertices, colors, indices, center, 3f, SeedColor);
                    else if (plot.Crop == CropStage.Growing)
                        AddCircle(vertices, colors, indices, center, 6f, GrowingColors[(int)plot.CropKind]);
                    if (plot.Building == BuildingKind.Processor)
                        AddQuad(vertices, colors, indices, new[]
                        {
                            center + new Vector2(-5f, -5f), center + new Vector2(5f, -5f),
                            center + new Vector2(5f, 5f), center + new Vector2(-5f, 5f),
                        }, ProcessorMarkerColor);
                }
            }
            Godot.Collections.Array arrays = new();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            _mesh.ClearSurfaces();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            DrawMesh(_mesh, null);
        }

        private static void AddQuad(List<Vector2> vertices, List<Color> colors, List<int> indices,
            Vector2[] points, Color color)
        {
            int start = vertices.Count;
            vertices.AddRange(points);
            for (int i = 0; i < 4; i++)
                colors.Add(color);
            indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private static void AddCircle(List<Vector2> vertices, List<Color> colors, List<int> indices,
            Vector2 center, float radius, Color color)
        {
            const int segments = 16;
            int start = vertices.Count;
            vertices.Add(center);
            colors.Add(color);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * MathF.Tau / segments;
                vertices.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
                colors.Add(color);
            }
            for (int i = 0; i < segments; i++)
                indices.AddRange(new[] { start, start + i + 1, start + (i + 1) % segments + 1 });
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
