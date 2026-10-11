using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.World;

public partial class TestRoadAppearance : Node
{
    public override async void _Ready()
    {
        bool passed = await TestRoadMap.RunChecksAsync(this);
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool CheckAllMasks()
    {
        Vector2I center = new(183, 186);
        Vector2I[] offsets = { Vector2I.Up, Vector2I.Right, Vector2I.Down, Vector2I.Left };
        var appearance = new RoadAppearance();
        var variants = new HashSet<int>();
        for (int material = 0; material < 5; material++)
            for (int mask = 0; mask < 16; mask++)
            {
                var spaces = new List<BuildingSpaceSnapshot> { Road(center, (RoadMaterial)material) };
                for (int side = 0; side < offsets.Length; side++)
                    if ((mask & (1 << side)) != 0)
                        spaces.Add(Road(center + offsets[side], (RoadMaterial)((material + side + 1) % 5)));
                appearance.Synchronize(spaces);
                var tile = appearance.GetTile(center)!.Value;
                if (tile.Mask != mask || tile.Material != (RoadMaterial)material ||
                    tile.Variant != RoadAppearance.GetVariant(center) || tile.Added || tile.Removed)
                    return Fail($"道路五材质/16种四邻拓扑错误：材质{material}，mask{mask}");
                appearance.Synchronize(Array.Empty<BuildingSpaceSnapshot>());
                if (appearance.GetTile(center) != null) return Fail("空地被当作mask=0孤立道路绘制");
            }
        for (int x = 0; x < 32; x++) variants.Add(RoadAppearance.GetVariant(new Vector2I(x, 10)));
        if (variants.Count != 4) return Fail("稳定道路变体未覆盖v0～v3");
        return true;
    }

    public static async Task<bool> CheckPreview(Node parent, FarmGame game, WorldMap map, Vector2I first, Vector2I second)
    {
        map.ClearSelection();
        int coins = game.MoneyCents;
        var spaces = game.GetBuildingSpaces();
        Vector2I firstNew = first + Vector2I.Up;
        Vector2I secondNew = second + Vector2I.Up;
        Vector2I[] candidates = { firstNew, secondNew, first, new(-1, 186) };
        var before = map.GetDisplayedRoadAppearance(first)!.Value;
        map.UpdateRoadPreview(candidates, RoadMaterial.Brick);
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(first) is not { Mask: 3, Material: RoadMaterial.Dirt, Added: false } ||
            map.GetDisplayedRoadAppearance(firstNew) is not { Mask: 6, Material: RoadMaterial.Brick, Added: true } ||
            map.GetDisplayedRoadAppearance(secondNew) is not { Mask: 12, Material: RoadMaterial.Brick, Added: true } ||
            map.GetDisplayedRoadAppearance(new Vector2I(-1, 186)) != null)
            return Fail("跨块候选、混材质旧路连接或冲突格排除不正确");
        int redraws = map.ChunkRedrawCount;
        map.UpdateRoadPreview(candidates, RoadMaterial.Brick);
        await Frames(parent);
        if (map.ChunkRedrawCount != redraws || game.MoneyCents != coins ||
            !ReferenceEquals(spaces, game.GetBuildingSpaces()))
            return Fail("相同候选重绘地面或预览修改了正式土地/金币");
        if (DisplayServer.GetName() != "headless") Save(parent, "road-connections-build-preview.png");
        map.UpdateRoadPreview(new[] { first + Vector2I.Down }, RoadMaterial.Slab);
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(firstNew) != null ||
            map.GetDisplayedRoadAppearance(first) is not { Mask: 6 })
            return Fail("移动候选后残留旧候选或旧连接");
        map.ClearRoadPreview();
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(first) != before || map.GetDisplayedRoadAppearance(first + Vector2I.Down) != null)
            return Fail("隐藏/取消后未恢复真实道路形状");
        map.UpdateRoadPreview(new[] { firstNew, secondNew }, RoadMaterial.Brick);
        var expectedFirst = map.GetDisplayedRoadAppearance(first)!.Value;
        var expectedNew = map.GetDisplayedRoadAppearance(firstNew)!.Value;
        if (!game.TryPlaceRoads(new[] { firstNew, secondNew }, RoadMaterial.Brick).Success)
            return Fail("正式混材质道路夹具建造失败");
        map.ClearRoadPreview();
        map.SyncFromGame();
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(first) != expectedFirst ||
            map.GetDisplayedRoadAppearance(firstNew) != expectedNew with { Added = false })
            return Fail("新建提交后的mask/材质/稳定变体与有效预览不一致");
        if (DisplayServer.GetName() != "headless")
        {
            if (!MatchesRoad(parent.GetViewport().GetTexture().GetImage(), map, firstNew))
                return Fail("正式红砖路未实际按manifest区域与pivot绘制");
            Save(parent, "road-connections-build-after.png");
        }
        map.UpdateRoadPreview(new[] { firstNew, secondNew, new Vector2I(-1, 186) }, RoadMaterial.Dirt, removing: true);
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(first) != before ||
            map.GetDisplayedRoadAppearance(firstNew) is not { Removed: true, Material: RoadMaterial.Brick })
            return Fail("拆除预览未保留待拆材质或周围旧路未断开");
        if (DisplayServer.GetName() != "headless") Save(parent, "road-connections-remove-preview.png");
        map.ClearRoadPreview();
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(first) != expectedFirst) return Fail("取消拆除未恢复旧路连接");
        map.UpdateRoadPreview(new[] { firstNew, secondNew }, RoadMaterial.Dirt, removing: true);
        if (game.RemoveBuilding(firstNew) != null || game.RemoveBuilding(secondNew) != null)
            return Fail("正式拆除道路夹具失败");
        map.ClearRoadPreview();
        map.SyncFromGame();
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(first) != before || map.GetDisplayedRoadAppearance(firstNew) != null)
            return Fail("拆除提交与预览连接不一致或残留待拆图");
        if (DisplayServer.GetName() != "headless") Save(parent, "road-connections-remove-after.png");
        return true;
    }

    public static bool MatchesRoad(Image shot, WorldMap map, Vector2I cell)
    {
        var appearance = map.GetDisplayedRoadAppearance(cell);
        if (appearance is not { } road) return false;
        string material = road.Material.ToString().ToLowerInvariant();
        Image atlas = GD.Load<Texture2D>($"res://assets/gameplay/terrain/roads/road_{material}_atlas.png").GetImage();
        // 图集位置从运行清单的权威区域读取，测试不重算排序坐标。
        using var regions = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString(
            "res://assets/gameplay/terrain/roads/atlas_regions.json"));
        Vector2I origin = default;
        foreach (var entry in regions.RootElement.EnumerateArray())
            if (entry.GetProperty("material").GetString() == material && entry.GetProperty("mask").GetInt32() == road.Mask &&
                entry.GetProperty("variant").GetInt32() == road.Variant)
            {
                origin = new Vector2I(entry.GetProperty("region")[0].GetInt32(), entry.GetProperty("region")[1].GetInt32());
                break;
            }
        int samples = 0;
        for (int y = 5; y < 28; y += 3)
            for (int x = 5; x < 60; x += 3)
            {
                Color expected = atlas.GetPixel(origin.X + x, origin.Y + y);
                if (expected.A < .99f) continue;
                Vector2 screen = map.GetGlobalTransformWithCanvas() *
                    (MapCoordinates.CellToLocalCenter(cell) + new Vector2(x + .5f - 32, y + .5f - 16));
                Color actual = shot.GetPixel((int)screen.X, (int)screen.Y);
                if (Math.Abs(actual.R - expected.R) > .04f || Math.Abs(actual.G - expected.G) > .04f ||
                    Math.Abs(actual.B - expected.B) > .04f)
                {
                    GD.PushError($"道路纹理取样不符：cell={cell} material={material} mask={road.Mask} variant={road.Variant} source=({origin.X + x},{origin.Y + y}) sample=({x},{y}) screen={screen} expected={expected} actual={actual}");
                    shot.SavePng(ProjectSettings.GlobalizePath("res://coverage/road-texture-failure.png"));
                    return false;
                }
                samples++;
            }
        return samples >= 10;
    }

    private static BuildingSpaceSnapshot Road(Vector2I cell, RoadMaterial material) =>
        new(cell.Y * FarmGame.MapSize + cell.X, BuildingKind.Road, cell, BuildingOrientation.Q0, material);

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Save(Node parent, string name)
    {
        string directory = ProjectSettings.GlobalizePath("res://coverage");
        Directory.CreateDirectory(directory);
        parent.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name));
    }

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
