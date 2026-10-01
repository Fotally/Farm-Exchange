using System;
using Godot;
using FarmExchange.Gameplay;

namespace FarmExchange.World;

public static class MapCoordinates
{
    public const int MapSize = FarmGame.MapSize;
    public const float TileWidth = 64f;
    public const float TileHeight = 32f;
    private const float HalfWidth = TileWidth / 2f;
    private const float HalfHeight = TileHeight / 2f;

    public static bool ContainsCell(Vector2I cell) =>
        cell.X >= 0 && cell.X < MapSize && cell.Y >= 0 && cell.Y < MapSize;

    public static Vector2 CellToLocalCenter(Vector2I cell)
    {
        if (!ContainsCell(cell))
            throw new ArgumentOutOfRangeException(nameof(cell));
        return GridPositionToLocal((Vector2)cell);
    }

    public static Vector2 GridPositionToLocal(Vector2 gridPosition) => new(
        (gridPosition.X - gridPosition.Y) * HalfWidth,
        (gridPosition.X + gridPosition.Y) * HalfHeight);

    public static Vector2I LocalPositionToCell(Vector2 localPosition)
    {
        Vector2 grid = LocalPositionToGrid(localPosition);
        return new Vector2I(Mathf.FloorToInt(grid.X + 0.5f), Mathf.FloorToInt(grid.Y + 0.5f));
    }

    public static Vector2 ClampLocalCenter(Vector2 localCenter)
    {
        Vector2 grid = LocalPositionToGrid(localCenter);
        float col = Math.Clamp(grid.X, 0f, MapSize - 1f);
        float row = Math.Clamp(grid.Y, 0f, MapSize - 1f);
        return GridPositionToLocal(new Vector2(col, row));
    }

    public static Vector2[] GridRectangleOutline(Vector2I firstCell, int columns, int rows)
    {
        Vector2 firstCorner = (Vector2)firstCell - new Vector2(0.5f, 0.5f);
        return new[]
        {
            GridPositionToLocal(firstCorner),
            GridPositionToLocal(firstCorner + new Vector2(columns, 0)),
            GridPositionToLocal(firstCorner + new Vector2(columns, rows)),
            GridPositionToLocal(firstCorner + new Vector2(0, rows)),
        };
    }

    public static Rect2 GridRectangleBounds(Vector2I firstCell, int columns, int rows)
    {
        Vector2[] outline = GridRectangleOutline(firstCell, columns, rows);
        return new Rect2(new Vector2(outline[3].X, outline[0].Y),
            new Vector2(outline[1].X - outline[3].X, outline[2].Y - outline[0].Y));
    }

    private static Vector2 LocalPositionToGrid(Vector2 localPosition)
    {
        float x = localPosition.X / HalfWidth;
        float y = localPosition.Y / HalfHeight;
        return new Vector2((x + y) / 2f, (y - x) / 2f);
    }
}
