using System;
using Godot;

public partial class WorldMap : Node2D
{
    [Signal]
    public delegate void SelectionChangedEventHandler(Vector2I cell);

    public const int MapSize = FarmGame.MapSize;
    public const float TileWidth = 64f;
    public const float TileHeight = 32f;
    private const float HalfWidth = TileWidth / 2f;
    private const float HalfHeight = TileHeight / 2f;

    private static readonly Color LockedLight = new(0.35f, 0.38f, 0.34f);
    private static readonly Color LockedDark = new(0.32f, 0.35f, 0.31f);
    private static readonly Color TileLight = new(0.55f, 0.66f, 0.40f);
    private static readonly Color TileDark = new(0.51f, 0.62f, 0.36f);
    private static readonly Color FarmColor = new(0.53f, 0.36f, 0.22f);
    private static readonly Color MillColor = new(0.36f, 0.47f, 0.61f);
    private static readonly Color MillMarkerColor = new(0.86f, 0.90f, 0.93f);
    private static readonly Color SeedColor = new(0.98f, 0.84f, 0.46f);
    private static readonly Color GrowingColor = new(0.39f, 0.84f, 0.39f);
    private static readonly Color GridColor = new(0.32f, 0.42f, 0.26f);
    private static readonly Color SelectedColor = new(0.95f, 0.76f, 0.31f);
    private static readonly Color EdgeColor = new(0.91f, 0.86f, 0.66f);

    private FarmGame _game = null!;
    private Vector2I _selectedCell = new(-1, -1);
    private Transform2D _lastCanvasTransform;
    private Vector2 _lastViewportSize;

    public void SetGame(FarmGame game)
    {
        _game = game;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        Transform2D canvasTransform = GetGlobalTransformWithCanvas();
        Vector2 viewportSize = GetViewportRect().Size;
        if (canvasTransform != _lastCanvasTransform || viewportSize != _lastViewportSize)
        {
            _lastCanvasTransform = canvasTransform;
            _lastViewportSize = viewportSize;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Transform2D inverse = GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 viewportSize = GetViewportRect().Size;
        Vector2[] corners =
        {
            inverse * Vector2.Zero,
            inverse * new Vector2(viewportSize.X, 0f),
            inverse * viewportSize,
            inverse * new Vector2(0f, viewportSize.Y),
        };

        int firstCol = MapSize;
        int lastCol = -1;
        int firstRow = MapSize;
        int lastRow = -1;
        foreach (Vector2 corner in corners)
        {
            Vector2 grid = WorldToGrid(corner);
            firstCol = Math.Min(firstCol, Mathf.FloorToInt(grid.X) - 2);
            lastCol = Math.Max(lastCol, Mathf.CeilToInt(grid.X) + 2);
            firstRow = Math.Min(firstRow, Mathf.FloorToInt(grid.Y) - 2);
            lastRow = Math.Max(lastRow, Mathf.CeilToInt(grid.Y) + 2);
        }

        firstCol = Math.Clamp(firstCol, 0, MapSize - 1);
        lastCol = Math.Clamp(lastCol, 0, MapSize - 1);
        firstRow = Math.Clamp(firstRow, 0, MapSize - 1);
        lastRow = Math.Clamp(lastRow, 0, MapSize - 1);
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = firstCol; col <= lastCol; col++)
            {
                Vector2 center = new((col - row) * HalfWidth, (col + row) * HalfHeight);
                Vector2[] outline =
                {
                    center + new Vector2(0f, -HalfHeight),
                    center + new Vector2(HalfWidth, 0f),
                    center + new Vector2(0f, HalfHeight),
                    center + new Vector2(-HalfWidth, 0f),
                };
                PlotSnapshot plot = _game.GetPlot(new Vector2I(col, row));
                Color tileColor = !plot.IsUnlocked
                    ? ((col + row) % 2 == 0 ? LockedLight : LockedDark)
                    : plot.Building switch
                    {
                        BuildingKind.Farm => FarmColor,
                        BuildingKind.Mill => MillColor,
                        _ => (col + row) % 2 == 0 ? TileLight : TileDark,
                    };
                DrawColoredPolygon(outline, tileColor);
                bool selected = _selectedCell == new Vector2I(col, row);
                DrawPolyline(new[] { outline[0], outline[1], outline[2], outline[3], outline[0] },
                    selected ? SelectedColor : GridColor, selected ? 3f : 1f);
                if (plot.Crop == CropStage.Seeded)
                    DrawCircle(center, 3f, SeedColor);
                else if (plot.Crop == CropStage.Growing)
                    DrawCircle(center, 6f, GrowingColor);
                if (plot.Building == BuildingKind.Mill)
                    DrawRect(new Rect2(center - new Vector2(5f, 5f), new Vector2(10f, 10f)), MillMarkerColor);
            }
        }
        DrawPolyline(new[]
        {
            new Vector2(0f, -HalfHeight),
            new Vector2(MapSize * HalfWidth, (MapSize - 1) * HalfHeight),
            new Vector2(0f, (MapSize * 2 - 1) * HalfHeight),
            new Vector2(-MapSize * HalfWidth, (MapSize - 1) * HalfHeight),
            new Vector2(0f, -HalfHeight),
        }, EdgeColor, 3f);
    }

    public void SelectAtScreenPosition(Vector2 screenPosition)
    {
        Vector2 localPosition = GetGlobalTransformWithCanvas().AffineInverse() * screenPosition;
        Vector2I cell = CellAtWorld(localPosition);
        if (cell.X < 0 || cell.X >= MapSize || cell.Y < 0 || cell.Y >= MapSize)
            return;

        _selectedCell = cell;
        EmitSignal(SignalName.SelectionChanged, cell);
        QueueRedraw();
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
}
