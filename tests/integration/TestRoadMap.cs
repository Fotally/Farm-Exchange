using System.IO;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
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
        if (!TestRoadAppearance.CheckAllMasks()) return false;
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

            if (!await TestRoadAppearance.CheckPreview(parent, game, map, firstRoad, secondRoad)) return false;

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
                if (!HasGoldenOutline(before, map, firstRoad, 1, 1))
                    return Fail("道路选中后没有实际绘制金色选框");
                SaveScreenshot(before, "road-map-before.png");
                // 金框正确覆盖连接端部；原图验证使用清框后的真实渲染，不跳过端部样点。
                map.ClearSelection();
                await LayoutFrames(parent);
                Image roadTexture = parent.GetViewport().GetTexture().GetImage();
                if (!TestRoadAppearance.MatchesRoad(roadTexture, map, firstRoad) ||
                    !TestRoadAppearance.MatchesRoad(roadTexture, map, secondRoad))
                    return Fail("道路未实际按四邻mask、材质与稳定变体绘制原图");
                SaveScreenshot(roadTexture, "road-map-texture-before.png");
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
                if (cleared.G <= cleared.R + 0.05f || !TestRoadAppearance.MatchesRoad(after, map, secondRoad))
                    return Fail("拆除后没有恢复绿色空地，或相邻道路外观被清除");
                SaveScreenshot(after, "road-map-after.png");
            }
            map.SelectAtScreenPosition(ScreenCenter(map, secondRoad));
            if (selected != secondRoad)
                return Fail("道路移除后，相邻道路无法继续选择");
            return await CheckSameCountRoadReplacement(parent, game, map, secondRoad) &&
                await CheckProductionFootprints(parent, game, map);

        }
        finally
        {
            parent.RemoveChild(scope);
            scope.Free();
        }
    }

    private static async Task<bool> CheckSameCountRoadReplacement(Node parent, FarmGame game, WorldMap map,
        Vector2I oldRoad)
    {
        var previousSpaces = game.GetBuildingSpaces();
        int redraws = map.ChunkRedrawCount;
        map.SyncFromGame();
        await LayoutFrames(parent);
        if (!ReferenceEquals(previousSpaces, game.GetBuildingSpaces()) || map.ChunkRedrawCount != redraws)
            return Fail("占地不变的同步改变了空间快照或重绘地面");
        Vector2I newRoad = oldRoad + Vector2I.Right;
        if (game.RemoveBuilding(oldRoad) != null || !game.TryPlace(newRoad, BuildingKind.Road, default).Success)
            return Fail("同数量道路替换夹具失败");
        if (previousSpaces.Count != game.GetBuildingSpaces().Count ||
            ReferenceEquals(previousSpaces, game.GetBuildingSpaces()))
            return Fail("同数量道路替换没有产生新的真实空间快照");
        map.ClearSelection();
        map.SyncFromGame();
        await LayoutFrames(parent);
        if (map.ChunkRedrawCount <= redraws) return Fail("同数量道路替换没有更新地面");
        if (DisplayServer.GetName() != "headless")
        {
            Image image = parent.GetViewport().GetTexture().GetImage();
            Color cleared = PixelAt(image, map, oldRoad);
            if (cleared.G <= cleared.R + .05f || !TestRoadAppearance.MatchesRoad(image, map, newRoad))
                return Fail("同数量道路替换沿用了旧路面缓存");
        }
        return true;
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
        // 一次同步前拆除并重建，同数量/同锚点也必须撤掉旧建筑层并使用新朝向。
        var oldSpaces = game.GetBuildingSpaces();
        if (game.RemoveBuilding(processor) != null ||
            !game.TryPlace(processor, BuildingKind.Farm, CropKind.Radish, orientation: BuildingOrientation.Q1).Success)
            return Fail("同锚点同数量生产设施替换夹具失败");
        if (oldSpaces.Count != game.GetBuildingSpaces().Count || ReferenceEquals(oldSpaces, game.GetBuildingSpaces()))
            return Fail("同锚点替换没有产生新的空间快照");
        map.ClearSelection();
        map.SyncFromGame();
        await LayoutFrames(parent);
        Vector2 expectedCenter = MapCoordinates.CellToLocalCenter(processor + Vector2I.One);
        bool HasOldSurface(Node node)
        {
            if (node is Sprite2D sprite && map.ToLocal(sprite.GlobalPosition) == expectedCenter &&
                sprite.Texture?.ResourcePath == "res://assets/gameplay/buildings/corn_workshop_q0.png") return true;
            foreach (Node child in node.GetChildren()) if (HasOldSurface(child)) return true;
            return false;
        }
        if (HasOldSurface(map)) return Fail("同锚点替换仍保留旧加工建筑层");
        if (graphical && !TestWorldArt.MatchesTexture(parent.GetViewport().GetTexture().GetImage(), map,
                processor + Vector2I.One, "soil/field_dry_q1.png", new Vector2(112, 128)))
            return Fail("同锚点替换未显示真实新类别及朝向");
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
