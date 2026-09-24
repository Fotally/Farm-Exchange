using System;
using System.Collections.Generic;
using Godot;

public partial class WorldMap : Node2D
{
    [Signal]
    public delegate void SelectionChangedEventHandler(Vector2I cell);

    public const int MapSize = FarmGame.MapSize;
    public const float TileWidth = 64f;
    public const float TileHeight = 32f;
    private const int ChunkSize = 8;
    private const int ChunksPerSide = MapSize / ChunkSize;
    private const float HalfWidth = TileWidth / 2f;
    private const float HalfHeight = TileHeight / 2f;
    private const float SeamOverlap = 0.75f;

    private static readonly Color LockedColor = new(0.34f, 0.37f, 0.33f);
    private static readonly Color TileColor = new(0.53f, 0.64f, 0.38f);
    private static readonly Color FarmColor = new(0.53f, 0.36f, 0.22f);
    private static readonly Color ProcessorColor = new(0.36f, 0.47f, 0.61f);
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
    };
    private static readonly Color SelectedColor = new(0.95f, 0.76f, 0.31f);
    private static readonly Color EdgeColor = new(0.91f, 0.86f, 0.66f);

    private readonly MapChunk[] _chunks = new MapChunk[ChunksPerSide * ChunksPerSide];
    private FarmGame _game = null!;
    private SelectionOverlay _overlay = null!;
    private Vector2I _selectedCell = new(-1, -1);
    private Transform2D _lastCanvasTransform;
    private Vector2 _lastViewportSize;

    internal int LastVisiblePlotCount { get; private set; }
    internal int ChunkRedrawCount { get; private set; }

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
        SyncFromGame();
        UpdateVisibleChunks();
    }

    public void SyncFromGame()
    {
        for (int row = 0; row < MapSize; row++)
        {
            for (int col = 0; col < MapSize; col++)
            {
                PlotSnapshot plot = _game.GetPlot(new Vector2I(col, row));
                MapChunk chunk = _chunks[(row / ChunkSize) * ChunksPerSide + col / ChunkSize];
                chunk.SetVisual(col % ChunkSize, row % ChunkSize, PlotVisual.FromSnapshot(plot));
            }
        }
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
        Vector2 localPosition = GetGlobalTransformWithCanvas().AffineInverse() * screenPosition;
        Vector2I cell = CellAtWorld(localPosition);
        if (cell.X < 0 || cell.X >= MapSize || cell.Y < 0 || cell.Y >= MapSize)
            return;

        _selectedCell = cell;
        EmitSignal(SignalName.SelectionChanged, cell);
        _overlay.QueueRedraw();
    }

    public Vector2I CellAtWorld(Vector2 worldPosition)
    {
        Vector2 grid = WorldToGrid(worldPosition);
        return new Vector2I(Mathf.FloorToInt(grid.X + 0.5f), Mathf.FloorToInt(grid.Y + 0.5f));
    }

    public Rect2 WorldBounds() => new(
        new Vector2(-MapSize * HalfWidth, -HalfHeight),
        new Vector2(MapSize * TileWidth, MapSize * TileHeight));

    public Vector2 ClampCameraCenter(Vector2 worldCenter)
    {
        Vector2 grid = WorldToGrid(worldCenter);
        float col = Math.Clamp(grid.X, 0f, MapSize - 1f);
        float row = Math.Clamp(grid.Y, 0f, MapSize - 1f);
        return new Vector2((col - row) * HalfWidth, (col + row) * HalfHeight);
    }

    private static Vector2 WorldToGrid(Vector2 worldPosition)
    {
        float x = worldPosition.X / HalfWidth;
        float y = worldPosition.Y / HalfHeight;
        return new Vector2((x + y) / 2f, (y - x) / 2f);
    }

    private static Vector2[] Outline(Vector2 center) => new[]
    {
        center + new Vector2(0f, -HalfHeight - SeamOverlap),
        center + new Vector2(HalfWidth + SeamOverlap, 0f),
        center + new Vector2(0f, HalfHeight + SeamOverlap),
        center + new Vector2(-HalfWidth - SeamOverlap, 0f),
    };

    private readonly record struct PlotVisual(bool IsUnlocked, BuildingKind Building, CropKind CropKind, CropStage Crop)
    {
        public static PlotVisual FromSnapshot(PlotSnapshot plot) => new(
            plot.IsUnlocked,
            plot.Building,
            plot.Building == BuildingKind.Farm ? plot.CropKind : CropKind.Wheat,
            plot.Building == BuildingKind.Farm ? plot.Crop : CropStage.None);
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
            Bounds = new Rect2(
                new Vector2((firstCol - (firstRow + ChunkSize - 1)) * HalfWidth - HalfWidth - SeamOverlap,
                    (firstCol + firstRow) * HalfHeight - HalfHeight - SeamOverlap),
                new Vector2(ChunkSize * TileWidth + SeamOverlap * 2f,
                    ChunkSize * TileHeight + SeamOverlap * 2f));
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
                    Vector2 center = new((mapCol - mapRow) * HalfWidth, (mapCol + mapRow) * HalfHeight);
                    PlotVisual plot = _plots[row * ChunkSize + col];
                    Color tileColor = !plot.IsUnlocked
                        ? LockedColor
                        : plot.Building switch
                        {
                            BuildingKind.Farm => FarmColor,
                            BuildingKind.Processor => ProcessorColor,
                            _ => TileColor,
                        };
                    AddQuad(vertices, colors, indices, Outline(center), tileColor);
                }
            }
            for (int row = 0; row < ChunkSize; row++)
            {
                for (int col = 0; col < ChunkSize; col++)
                {
                    int mapCol = _firstCol + col;
                    int mapRow = _firstRow + row;
                    Vector2 center = new((mapCol - mapRow) * HalfWidth, (mapCol + mapRow) * HalfHeight);
                    PlotVisual plot = _plots[row * ChunkSize + col];
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
            DrawPolyline(new[]
            {
                new Vector2(0f, -HalfHeight),
                new Vector2(MapSize * HalfWidth, (MapSize - 1) * HalfHeight),
                new Vector2(0f, (MapSize * 2 - 1) * HalfHeight),
                new Vector2(-MapSize * HalfWidth, (MapSize - 1) * HalfHeight),
                new Vector2(0f, -HalfHeight),
            }, EdgeColor, 3f);
            if (_map._selectedCell.X < 0)
                return;
            Vector2I cell = _map._selectedCell;
            Vector2 center = new((cell.X - cell.Y) * HalfWidth, (cell.X + cell.Y) * HalfHeight);
            Vector2[] outline = Outline(center);
            DrawPolyline(new[] { outline[0], outline[1], outline[2], outline[3], outline[0] },
                SelectedColor, 3f);
        }
    }
}
