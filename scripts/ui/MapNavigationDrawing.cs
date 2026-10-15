using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Terrain;
using FarmExchange.World;

namespace FarmExchange.UI;

// 本局派生的只读绘图数据；空间快照仍由土地模块唯一拥有。
internal sealed class MapNavigationDrawing : IDisposable
{
    internal static readonly Color Land = new("a8b675");
    internal static readonly Color Water = new("70a9bb");
    internal static readonly Color Farm = new("d6aa4e");
    internal static readonly Color Processor = new("b66743");
    internal static readonly Color Road = new("6c5540");
    internal static readonly (string, Color)[] Legend =
    {
        ("陆地", Land), ("水域", Water), ("农田", Farm),
        ("加工场地", Processor), ("道路", Road), ("当前视野", UiElements.Cream),
    };
    internal static readonly Vector2 Center = MapCoordinates.GridPositionToLocal(new Vector2((FarmGame.MapSize - 1) / 2f, (FarmGame.MapSize - 1) / 2f));
    internal static readonly Rect2 Bounds = MapCoordinates.GridRectangleBounds(Vector2I.Zero, FarmGame.MapSize, FarmGame.MapSize);
    internal ImageTexture? TerrainTexture { get; private set; }
    private IReadOnlyList<BuildingSpaceSnapshot>? _spaces;

    internal void Initialize(TerrainMap terrain)
    {
        TerrainTexture?.Dispose();
        _spaces = null;
        // 每格 2×2 采样复用世界的权威水 mask，避免把含水岸格全部画成水面。
        const int samples = 2;
        int size = FarmGame.MapSize * samples;
        byte[] pixels = new byte[size * size * 4];
        using Image waterMask = GD.Load<Texture2D>("res://assets/gameplay/terrain/water_81.png").GetImage();
        for (int row = 0; row < FarmGame.MapSize; row++)
            for (int col = 0; col < FarmGame.MapSize; col++)
            {
                int code = terrain.GetCell(new Vector2I(col, row)).TernaryCode;
                for (int y = 0; y < samples; y++)
                    for (int x = 0; x < samples; x++)
                    {
                        float gx = (x + .5f) / samples, gy = (y + .5f) / samples;
                        int maskX = code % 9 * 68 + 2 + (int)((gx - gy) * 32 + 32);
                        int maskY = code / 9 * 36 + 2 + (int)((gx + gy) * 16);
                        Color color = Land.Lerp(Water, waterMask.GetPixel(maskX, maskY).A);
                        int offset = ((row * samples + y) * size + col * samples + x) * 4;
                        pixels[offset] = (byte)color.R8;
                        pixels[offset + 1] = (byte)color.G8;
                        pixels[offset + 2] = (byte)color.B8;
                        pixels[offset + 3] = 255;
                    }
            }
        using Image image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, pixels);
        TerrainTexture = ImageTexture.CreateFromImage(image);
    }

    internal bool Refresh(IReadOnlyList<BuildingSpaceSnapshot> spaces)
    {
        if (ReferenceEquals(spaces, _spaces)) return false;
        _spaces = spaces;
        return true;
    }

    internal ArrayMesh BuildFacilities(float mapScale)
    {
        var mesh = new ArrayMesh();
        if (_spaces == null || _spaces.Count == 0) return mesh;
        var vertices = new Vector2[_spaces.Count * 4];
        var colors = new Color[vertices.Length];
        var indices = new int[_spaces.Count * 6];
        for (int index = 0; index < _spaces.Count; index++)
        {
            BuildingSpaceSnapshot space = _spaces[index];
            Vector2 center = MapCoordinates.CellToLocalCenter(space.WorkCell);
            bool road = space.Building == BuildingKind.Road;
            // 最小标记只放大表现，不改变真实坐标或占地。
            float halfWidth = Math.Max(road ? 32 : 96, (road ? 1f : 2f) / mapScale);
            float halfHeight = Math.Max(road ? 16 : 48, (road ? .8f : 1.5f) / mapScale);
            int start = index * 4;
            vertices[start] = center + new Vector2(0, -halfHeight);
            vertices[start + 1] = center + new Vector2(halfWidth, 0);
            vertices[start + 2] = center + new Vector2(0, halfHeight);
            vertices[start + 3] = center + new Vector2(-halfWidth, 0);
            Color color = road ? Road : space.Building == BuildingKind.Farm ? Farm : Processor;
            for (int vertex = 0; vertex < 4; vertex++) colors[start + vertex] = color;
            int first = index * 6;
            indices[first] = start; indices[first + 1] = start + 1; indices[first + 2] = start + 2;
            indices[first + 3] = start; indices[first + 4] = start + 2; indices[first + 5] = start + 3;
        }
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public void Dispose() => TerrainTexture?.Dispose();
}
