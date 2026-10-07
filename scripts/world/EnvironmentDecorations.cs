using System.Collections.Generic;
using Godot;
using FarmExchange.Land;

namespace FarmExchange.World;

// WorldMap 的内部实现：独立拥有纯环境落点，不向经营提供占用或资源。
internal sealed class EnvironmentDecorations
{
    private const uint VisualSeed = 0x6e9375c1;
    private static readonly Definition[] Definitions =
    {
        new("tree_oak_00", new Vector2(48, 112), new Vector2(96, 128)),
        new("tree_oak_01", new Vector2(48, 112), new Vector2(96, 128)),
        new("tree_oak_02", new Vector2(48, 112), new Vector2(96, 128)),
        new("tree_gold", new Vector2(48, 112), new Vector2(96, 128)),
        new("bush", new Vector2(24, 30), new Vector2(48, 40)),
        new("bush_flowers", new Vector2(24, 30), new Vector2(48, 40)),
        new("rock", new Vector2(24, 22), new Vector2(48, 32)),
        new("grass_tuft_00", new Vector2(24, 24), new Vector2(48, 32)),
        new("grass_tuft_01", new Vector2(24, 24), new Vector2(48, 32)),
        new("grass_tuft_02", new Vector2(24, 24), new Vector2(48, 32)),
        new("wildflowers_00", new Vector2(24, 24), new Vector2(48, 32)),
        new("wildflowers_01", new Vector2(24, 24), new Vector2(48, 32)),
        new("wildflowers_02", new Vector2(24, 24), new Vector2(48, 32)),
        new("hay_bale", new Vector2(25, 35), new Vector2(48, 48)),
        new("timber_store", new Vector2(64, 122), new Vector2(128, 144)),
        new("fence", new Vector2(32, 36), new Vector2(64, 48)),
        new("reeds", new Vector2(24, 30), new Vector2(48, 40)),
        new("farm_sign", new Vector2(32, 70), new Vector2(64, 80)),
        new("tree_pine", new Vector2(48, 112), new Vector2(96, 128)),
        new("tree_birch", new Vector2(48, 112), new Vector2(96, 128)),
        new("tree_dead", new Vector2(48, 112), new Vector2(96, 128)),
        new("tree_sapling", new Vector2(32, 82), new Vector2(64, 96)),
        new("tree_stump", new Vector2(24, 30), new Vector2(48, 40)),
        new("fallen_log", new Vector2(32, 35), new Vector2(64, 48)),
        new("mushroom_cluster", new Vector2(24, 31), new Vector2(48, 40)),
        new("fern", new Vector2(24, 31), new Vector2(48, 40)),
        new("bush_berries", new Vector2(24, 30), new Vector2(48, 40)),
        new("bush_hedge", new Vector2(24, 30), new Vector2(48, 40)),
        new("rock_cluster", new Vector2(32, 33), new Vector2(64, 48)),
        new("rock_tall", new Vector2(32, 52), new Vector2(64, 64)),
        new("flower_lupine", new Vector2(24, 39), new Vector2(48, 48)),
        new("flower_bluebell", new Vector2(24, 39), new Vector2(48, 48)),
    };
    private static readonly int[] NaturalTypes =
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28, 29, 30, 31 };
    private readonly Dictionary<int, Decoration> _remaining = new();
    private readonly Node2D _depthLayer;
    private readonly Texture2D?[] _textures = new Texture2D?[Definitions.Length];

    internal EnvironmentDecorations(Node2D depthLayer)
    {
        _depthLayer = depthLayer;
        for (int row = 0; row < MapCoordinates.MapSize; row++)
            for (int col = 0; col < MapCoordinates.MapSize; col++)
            {
                uint hash = Hash(col, row);
                if (hash % 64 < 2)
                    Add(new Vector2I(col, row), NaturalTypes[(hash >> 8) % NaturalTypes.Length]);
            }
        // 人工陈设只在开局外围的明确位置；短列保留入口，不随机铺满全图。
        Add(new Vector2I(198, 194), 13);
        Add(new Vector2I(199, 194), 13);
        Add(new Vector2I(186, 196), 14);
        Add(new Vector2I(186, 188), 15);
        Add(new Vector2I(186, 189), 15);
        Add(new Vector2I(186, 190), 15);
        Add(new Vector2I(198, 196), 17);
        Add(new Vector2I(190, 196), 27);
        Add(new Vector2I(191, 196), 27);
        Add(new Vector2I(192, 196), 27);
    }

    internal void Synchronize(IReadOnlyList<BuildingSpaceSnapshot> spaces)
    {
        if (_remaining.Count == 0) return;
        foreach (BuildingSpaceSnapshot space in spaces)
            foreach (Vector2I offset in space.Footprint.Offsets)
            {
                Vector2I cell = space.AnchorCell + offset;
                int key = cell.Y * MapCoordinates.MapSize + cell.X;
                if (_remaining.Remove(key, out Decoration? removed)) removed.Sprite?.Free();
            }
    }

    internal void UpdateVisible(Rect2 viewBounds)
    {
        foreach (Decoration decoration in _remaining.Values)
        {
            if (!viewBounds.Intersects(decoration.Bounds))
            {
                if (decoration.Sprite != null)
                {
                    decoration.Sprite.Free();
                    decoration.Sprite = null;
                }
                continue;
            }
            if (decoration.Sprite != null) continue;
            Definition definition = Definitions[decoration.Type];
            Texture2D texture = _textures[decoration.Type] ??= GD.Load<Texture2D>(
                $"res://assets/gameplay/decor/{definition.Name}_q0.png");
            decoration.Sprite = new Sprite2D
            {
                Position = decoration.Position,
                Centered = false,
                Offset = -definition.Pivot,
                Texture = texture,
            };
            _depthLayer.AddChild(decoration.Sprite);
        }
    }

    private void Add(Vector2I cell, int type)
    {
        Vector2 position = MapCoordinates.CellToLocalCenter(cell);
        Definition definition = Definitions[type];
        _remaining[cell.Y * MapCoordinates.MapSize + cell.X] =
            new Decoration(type, position, new Rect2(position - definition.Pivot, definition.Canvas));
    }

    private static uint Hash(int col, int row)
    {
        uint value = unchecked((uint)col * 374761393u + (uint)row * 668265263u + VisualSeed);
        value = unchecked((value ^ (value >> 13)) * 1274126177u);
        return value ^ (value >> 16);
    }

    private readonly record struct Definition(string Name, Vector2 Pivot, Vector2 Canvas);
    private sealed class Decoration
    {
        internal readonly int Type;
        internal readonly Vector2 Position;
        internal readonly Rect2 Bounds;
        internal Sprite2D? Sprite;
        internal Decoration(int type, Vector2 position, Rect2 bounds)
        {
            Type = type;
            Position = position;
            Bounds = bounds;
        }
    }
}
