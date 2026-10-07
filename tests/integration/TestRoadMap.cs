using System.IO;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.World;

public partial class TestRoadMap : Node
{
    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        if (passed) GD.Print("集成测试：道路跨块同步、地图选择与移除外观通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        var game = new FarmGame(12345);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        var scope = new Node2D();
        parent.AddChild(scope);
        var map = new WorldMap { Position = new Vector2(113, -67) };
        scope.AddChild(map);
        map.SetGame(game);
        var camera = new Camera2D
        {
            Position = map.GetCellWorldCenter(new Vector2I(183, 183)),
            Zoom = new Vector2(1.25f, 1.25f),
        };
        scope.AddChild(camera);
        camera.MakeCurrent();
        try
        {
            await LayoutFrames(parent);
            int initialRedraws = map.ChunkRedrawCount;
            Vector2I firstRoad = new(183, 186);
            Vector2I secondRoad = new(184, 186); // 分处相邻两个 8×8 块。
            if (!game.TryPlace(firstRoad, BuildingKind.Road, default).Success ||
                !game.TryPlace(secondRoad, BuildingKind.Road, default).Success)
                return Fail("道路地图夹具无法放置相邻道路");
            map.SyncFromGame();
            await LayoutFrames(parent);
            if (map.ChunkRedrawCount < initialRedraws + 2)
                return Fail("跨8×8边界铺路未同步两个可见地图块");

            int roadRedraws = map.ChunkRedrawCount;
            Vector2I selected = new(-1, -1);
            map.SelectionChanged += cell => selected = cell;
            map.SelectAtScreenPosition(ScreenCenter(map, firstRoad));
            await LayoutFrames(parent);
            if (selected != firstRoad || map.ChunkRedrawCount != roadRedraws ||
                game.Calendar.ElapsedSeconds != 0)
                return Fail("平移地图后的道路选择错误，或选框重建了地块/推进了经营");

            bool graphical = DisplayServer.GetName() != "headless";
            if (graphical)
            {
                Image before = parent.GetViewport().GetTexture().GetImage();
                if (!IsGray(PixelAt(before, map, firstRoad)) || !IsGray(PixelAt(before, map, secondRoad)))
                    return Fail("道路中心没有实际绘制为灰色路面，或被当作加工场地绘制");
                if (!HasGoldenOutline(before, map, firstRoad, 1, 1))
                    return Fail("道路选中后没有实际绘制金色选框");
                SaveScreenshot(before, "road-map-before.png");
            }

            if (game.RemoveBuilding(firstRoad) != null)
                return Fail("道路地图夹具无法移除道路");
            map.SyncFromGame();
            await LayoutFrames(parent);
            if (map.ChunkRedrawCount < roadRedraws + 1 ||
                game.GetPlot(firstRoad).Building != BuildingKind.None ||
                game.GetPlot(secondRoad).Building != BuildingKind.Road)
                return Fail("移除道路未更新对应地图块，或覆盖了相邻道路");
            if (graphical)
            {
                Image after = parent.GetViewport().GetTexture().GetImage();
                Color cleared = PixelAt(after, map, firstRoad);
                if (cleared.G <= cleared.R + 0.05f || !IsGray(PixelAt(after, map, secondRoad)))
                    return Fail("拆除后没有恢复绿色空地，或相邻道路外观被清除");
                SaveScreenshot(after, "road-map-after.png");
            }
            map.SelectAtScreenPosition(ScreenCenter(map, secondRoad));
            if (selected != secondRoad)
                return Fail("道路移除后，相邻道路无法继续选择");
            return await CheckProductionFootprints(parent, game, map);

        }
        finally
        {
            parent.RemoveChild(scope);
            scope.Free();
        }
    }

    private static async Task<bool> CheckProductionFootprints(Node parent, FarmGame game, WorldMap map)
    {
        Vector2I farm = new(183, 183); // 九格横纵均跨越184处的8×8块边界。
        Vector2I processor = new(187, 183);
        int beforeRedraws = map.ChunkRedrawCount;
        if (!game.TryPlace(farm, BuildingKind.Farm, CropKind.Wheat).Success ||
            !game.TryPlace(processor, BuildingKind.Processor, CropKind.Corn).Success)
            return Fail("多格地图夹具无法放置生产设施");
        map.SyncFromGame();
        await LayoutFrames(parent);
        if (map.ChunkRedrawCount != beforeRedraws)
            return Fail("设施纹理变化不应重建草地/道路地面块");
        int builtRedraws = map.ChunkRedrawCount;
        Vector2I selected = new(-1, -1);
        map.SelectionChanged += cell => selected = cell;
        foreach (var space in new[] { game.GetBuildingSpace(farm)!, game.GetBuildingSpace(processor)! })
            foreach (Vector2I offset in space.Footprint.Offsets)
            {
                Vector2I child = space.AnchorCell + offset;
                map.SelectAtScreenPosition(ScreenCenter(map, child));
                if (selected != child || game.GetBuildingSpace(selected) != space)
                    return Fail("平移后的任一生产子格未关联同一整座设施");
            }
        map.SelectAtScreenPosition(ScreenCenter(map, farm + new Vector2I(2, 2)));
        await LayoutFrames(parent);
        if (map.ChunkRedrawCount != builtRedraws)
            return Fail("多格设施选框重建了地图块");
        bool graphical = DisplayServer.GetName() != "headless";
        if (graphical)
        {
            map.ClearSelection();
            await LayoutFrames(parent);
            Image image = parent.GetViewport().GetTexture().GetImage();
            if (!TestWorldArt.MatchesTexture(image, map, farm + Vector2I.One,
                    "soil/field_dry_q0.png", new Vector2(112, 128), leftHalf: true) ||
                !TestWorldArt.MatchesTexture(image, map, processor + Vector2I.One,
                    "buildings/corn_workshop_q0.png", new Vector2(128, 176)))
                return Fail("跨块农田土层或加工设施没有按工作中心绘制完整原图");
            map.SelectAtScreenPosition(ScreenCenter(map, farm + new Vector2I(2, 2)));
            await LayoutFrames(parent);
            image = parent.GetViewport().GetTexture().GetImage();
            if (!HasGoldenOutline(image, map, farm, 3, 3))
                return Fail("点击末端子格未绘制完整3×3金色选框");
            SaveScreenshot(image, "production-footprint-before.png");
        }
        if (game.SetFarmCrop(farm + new Vector2I(2, 1), CropKind.Radish) != null)
            return Fail("末端子格改种失败");
        map.SyncFromGame();
        await LayoutFrames(parent);
        if (graphical)
        {
            if (!TestWorldArt.MatchesTexture(parent.GetViewport().GetTexture().GetImage(), map,
                    farm + Vector2I.One, "soil/field_dry_q0.png", new Vector2(112, 128), leftHalf: true))
                return Fail("未播种改种后没有保留土层，或出现虚假作物");
        }
        if (game.RemoveBuilding(farm + new Vector2I(2, 2)) != null)
            return Fail("末端子格整座拆除失败");
        map.SyncFromGame();
        await LayoutFrames(parent);
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                Vector2I child = farm + new Vector2I(col, row);
                if (game.GetPlot(child).Building != BuildingKind.None)
                    return Fail("跨块整座拆除残留占用");
                if (graphical && col < row)
                {
                    Color pixel = PixelAt(parent.GetViewport().GetTexture().GetImage(), map, child);
                    if (pixel.G <= pixel.R + 0.05f)
                        return Fail("跨块整座拆除残留棕色路面或标记");
                }
            }
        if (graphical)
        {
            Image after = parent.GetViewport().GetTexture().GetImage();
            if (!TestWorldArt.MatchesTexture(after, map, processor + Vector2I.One,
                "buildings/corn_workshop_q0.png", new Vector2(128, 176)))
                return Fail("拆除农田清除了相邻加工设施原图");
            SaveScreenshot(after, "production-footprint-after.png");
        }
        return true;
    }

    private static async Task LayoutFrames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static Vector2 ScreenCenter(WorldMap map, Vector2I cell) =>
        map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);

    private static Color PixelAt(Image image, WorldMap map, Vector2I cell)
    {
        Vector2 center = ScreenCenter(map, cell);
        return image.GetPixel(Mathf.RoundToInt(center.X), Mathf.RoundToInt(center.Y));
    }

    private static bool HasGoldenOutline(Image image, WorldMap map, Vector2I anchorCell, int columns, int rows)
    {
        Vector2[] outline = MapCoordinates.GridRectangleOutline(anchorCell, columns, rows);
        // 像素中心可能落在线段端点的线帽外；检查四条完整几何边的内部位置。
        // 1/4、1/2、3/4也避开3×3选框内部各小格边段的连接点。
        for (int side = 0; side < 4; side++)
            for (int sample = 1; sample <= 3; sample++)
            {
                Vector2 local = outline[side].Lerp(outline[(side + 1) % 4], sample / 4f);
                Vector2 screen = map.GetGlobalTransformWithCanvas() * local;
                Color pixel = image.GetPixel(Mathf.RoundToInt(screen.X), Mathf.RoundToInt(screen.Y));
                if (pixel.R <= pixel.G + 0.04f || pixel.G <= pixel.B + 0.1f)
                    return false;
            }
        return true;
    }

    private static bool IsGray(Color color) =>
        Mathf.Abs(color.R - color.G) < 0.03f && Mathf.Abs(color.G - color.B) < 0.03f &&
        color.R > 0.3f && color.R < 0.8f;

    private static void SaveScreenshot(Image image, string name)
    {
        string directory = ProjectSettings.GlobalizePath("res://coverage");
        Directory.CreateDirectory(directory);
        image.SavePng(Path.Combine(directory, name));
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
