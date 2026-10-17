using System.IO;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.World;

public partial class TestPlacementPreview : Node
{
    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        var game = new FarmGame(12345, terrainRules: TerrainTestFixtures.AllLandRules);
        foreach (BuildingSpaceSnapshot space in game.GetBuildingSpaces())
            game.RemoveBuilding(space.AnchorCell);
        var scope = new Node2D();
        parent.AddChild(scope);
        var map = new WorldMap { Name = "WorldMap", Position = new Vector2(113, -67) };
        scope.AddChild(map);
        map.SetGame(game);
        var camera = new CameraController
        {
            Name = "Camera2D",
            Position = map.GetCellWorldCenter(new Vector2I(183, 183)),
            Zoom = new Vector2(1.25f, 1.25f),
        };
        scope.AddChild(camera);
        camera.MakeCurrent();
        try
        {
            await Frames(parent);
            int initialRedraws = map.ChunkRedrawCount;
            int initialCoins = game.MoneyCents;
            int initialBuildings = game.GetBuildingSpaces().Count;
            Vector2I anchor = new(183, 183); // 非三格对齐，横纵跨8×8块边界。
            bool graphical = DisplayServer.GetName() != "headless";
            Image? previewBackground = graphical ? parent.GetViewport().GetTexture().GetImage() : null;
            foreach (BuildingOrientation orientation in new[] { BuildingOrientation.Q0, BuildingOrientation.Q1,
                BuildingOrientation.Q2, BuildingOrientation.Q3, BuildingOrientation.Q0 })
            {
                map.UpdatePlacementPreview(BuildingKind.Processor, CropKind.Potato, anchor, orientation);
                await Frames(parent);
                if (map.PlacementPreviewAnchor != anchor || map.ChunkRedrawCount != initialRedraws ||
                    game.MoneyCents != initialCoins || game.GetBuildingSpaces().Count != initialBuildings)
                    return Fail("候选四向切换改变锚点、地面块或经营资源");
                if (previewBackground != null &&
                    !MatchesPreview(parent.GetViewport().GetTexture().GetImage(), previewBackground, map, anchor, orientation))
                    return false;
            }
            foreach (BuildingKind building in new[] { BuildingKind.Farm, BuildingKind.Processor, BuildingKind.Road })
            {
                map.UpdatePlacementPreview(building, CropKind.Wheat, anchor);
                await Frames(parent);
                if (map.PlacementPreviewAnchor != anchor || map.ChunkRedrawCount != initialRedraws ||
                    game.MoneyCents != initialCoins || game.GetBuildingSpaces().Count != initialBuildings ||
                    game.Calendar.ElapsedSeconds != 0)
                    return Fail("悬停预览改变经营资源、占地或重绘地图块");
                if (graphical && !CheckOuterLines(parent.GetViewport().GetTexture().GetImage(), map, anchor,
                        building == BuildingKind.Road ? 1 : 3, game))
                    return false;
            }
            map.UpdatePlacementPreview(BuildingKind.Farm, CropKind.Wheat, anchor);
            await Frames(parent);
            if (graphical)
            {
                Image image = parent.GetViewport().GetTexture().GetImage();
                foreach (Vector2I offset in BuildingFootprint.Get(BuildingKind.Farm).Offsets)
                    if (!IsGreen(PixelAt(image, map, anchor + offset)))
                        return Fail("有效多格预览没有实际覆盖全部占地");
                Save(image, "placement-valid.png");
            }

            Vector2I conflict = anchor + new Vector2I(2, 0);
            if (!game.TryPlace(conflict, BuildingKind.Road, default).Success)
                return Fail("冲突预览夹具未能建造道路");
            map.SyncFromGame();
            await Frames(parent);
            int conflictRedraws = map.ChunkRedrawCount;
            map.UpdatePlacementPreview(BuildingKind.Farm, CropKind.Wheat, anchor);
            await Frames(parent);
            if (game.CheckPlacement(anchor, BuildingKind.Farm, CropKind.Wheat).Allowed ||
                map.ChunkRedrawCount != conflictRedraws)
                return Fail("局部冲突预览未保持整座拒绝或重绘了地图块");
            if (graphical)
            {
                Image image = parent.GetViewport().GetTexture().GetImage();
                foreach (Vector2I offset in BuildingFootprint.Get(BuildingKind.Farm).Offsets)
                {
                    Vector2I cell = anchor + offset;
                    Color pixel = PixelAt(image, map, cell);
                    if (cell == conflict ? !IsRed(pixel) : !IsGreen(pixel))
                        return Fail("局部冲突没有仅标红实际冲突格");
                }
                if (!CheckOuterLines(image, map, anchor, 3, game)) return false;
                Save(image, "placement-conflict.png");
            }
            map.ClearPlacementPreview();
            await Frames(parent);
            if (map.PlacementPreviewAnchor != null || map.ChunkRedrawCount != conflictRedraws)
                return Fail("隐藏预览未清除候选或重绘了地图块");

            Vector2I edgeAnchor = new(382, 382);
            camera.GlobalPosition = map.GetCellWorldCenter(edgeAnchor);
            camera.ForceUpdateScroll();
            await Frames(parent);
            map.UpdatePlacementPreview(BuildingKind.Processor, CropKind.Wheat, edgeAnchor);
            await Frames(parent);
            if (map.PlacementPreviewAnchor != edgeAnchor ||
                game.CheckPlacement(edgeAnchor, BuildingKind.Processor, CropKind.Wheat).Allowed)
                return Fail("越界候选被自动移位或没有整体拒绝");
            if (graphical)
            {
                Image image = parent.GetViewport().GetTexture().GetImage();
                foreach (Vector2I offset in BuildingFootprint.Get(BuildingKind.Processor).Offsets)
                {
                    Vector2I cell = edgeAnchor + offset;
                    // 工作中心是类型标记，采样稍偏离中心以检查格覆盖。
                    Color pixel = PixelAt(image, map, cell);
                    if (MapCoordinates.ContainsCell(cell) ? !IsGreen(pixel) : !IsRed(pixel))
                        return Fail("边缘预览未显示完整占地，或越界格配色错误");
                }
                if (!CheckOuterLines(image, map, edgeAnchor, 3, game)) return false;
                Save(image, "placement-edge.png");
            }
            camera.GlobalPosition = map.GetCellWorldCenter(anchor);
            camera.ForceUpdateScroll();
            await Frames(parent);
            Vector2 point = map.GetGlobalTransformWithCanvas() * MapCoordinates.GridPositionToLocal((Vector2)anchor);
            if (map.ScreenToCell(point) != anchor)
                return Fail("地图平移后的候选与屏幕鼠标位置不一致");
            camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
            camera.ForceUpdateScroll();
            await Frames(parent);
            Vector2I zoomed = map.ScreenToCell(point);
            map.UpdatePlacementPreview(BuildingKind.Road, default, zoomed);
            if (map.PlacementPreviewAnchor != zoomed)
                return Fail("缩放后的候选未按鼠标当前位置重算");
            camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
            camera._UnhandledInput(new InputEventMouseMotion { Position = point + new Vector2(30, 0), Relative = new Vector2(30, 0) });
            if (!camera.IsDragging)
                return Fail("达到左键拖动阈值后未对协调入口报告拖动状态");
            map.ClearPlacementPreview();
            int selections = 0;
            camera.MapShortClicked += _ => selections++;
            camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point + new Vector2(30, 0) });
            if (camera.IsDragging || selections != 0 || map.PlacementPreviewAnchor != null)
                return Fail("拖动松开后仍拖动或误发建造确认");
            camera.ForceUpdateScroll();
            await Frames(parent);
            Vector2I panned = map.ScreenToCell(point);
            map.UpdatePlacementPreview(BuildingKind.Road, default, panned);
            await Frames(parent);
            if (graphical)
                Save(parent.GetViewport().GetTexture().GetImage(), "placement-camera.png");
            camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
            if (!camera.IsDragging) return Fail("中键拖动未报告预览隐藏状态");
            camera._Process(0);
            if (camera.IsDragging) return Fail("被界面截获的中键释放没有校正拖动状态");
            camera.GetWindow().EmitSignal(Window.SignalName.MouseExited);
            if (camera.IsMouseInsideWindow || camera.IsDragging)
                return Fail("鼠标离窗后未清除窗口或拖动状态");
            camera.GetWindow().EmitSignal(Window.SignalName.MouseEntered);
            if (!camera.IsMouseInsideWindow) return Fail("鼠标回窗没有恢复候选可见条件");
            GD.Print("放置预览：全类型、跨块、逐格冲突、越界、取消、镜头和只读资源检查通过");
            return true;
        }
        finally
        {
            scope.Free();
        }
    }

    private static Color PixelAt(Image image, WorldMap map, Vector2I cell)
    {
        Vector2 screen = map.GetGlobalTransformWithCanvas() *
            (MapCoordinates.GridPositionToLocal((Vector2)cell) + new Vector2(9f, 3f));
        screen = map.GetViewport().GetStretchTransform() * screen;
        return image.GetPixel(Mathf.RoundToInt(screen.X), Mathf.RoundToInt(screen.Y));
    }

    private static bool MatchesPreview(Image image, Image background, WorldMap map, Vector2I anchor, BuildingOrientation orientation)
    {
        Image source = GD.Load<Texture2D>($"res://assets/gameplay/buildings/potato_workshop_q{(int)orientation}.png").GetImage();
        Vector2 center = MapCoordinates.CellToLocalCenter(anchor + Vector2I.One);
        int samples = 0;
        for (int y = 5; y < 120; y += 3)
            for (int x = 5; x < source.GetWidth() - 5; x += 3)
            {
                Color color = source.GetPixel(x, y);
                if (color.A < .99f) continue;
                Vector2 pixel = map.GetViewport().GetStretchTransform() *
                    (map.GetGlobalTransformWithCanvas() * (center + new Vector2(x + .5f - 128, y + .5f - 176)));
                if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= image.GetWidth() || pixel.Y >= image.GetHeight()) continue;
                Color actual = image.GetPixel((int)pixel.X, (int)pixel.Y);
                // 上部屋顶避开地面冲突覆盖，按候选65%透明与实际底图核对。
                Color expected = background.GetPixel((int)pixel.X, (int)pixel.Y).Lerp(color, .65f);
                if (System.Math.Abs(actual.R - expected.R) > .04f ||
                    System.Math.Abs(actual.G - expected.G) > .04f || System.Math.Abs(actual.B - expected.B) > .04f)
                    return Fail($"候选Q{(int)orientation}没有显示对应屋顶颜色或pivot错位");
                samples++;
            }
        return samples >= 10 || Fail("候选四向屋顶图形取样不足");
    }

    private static bool IsGreen(Color color) => color.G > color.R + 0.07f && color.G > color.B + 0.07f;
    private static bool IsRed(Color color) => color.R > color.G + 0.12f && color.R > color.B + 0.12f;

    private static bool CheckOuterLines(Image image, WorldMap map, Vector2I anchor, int size, FarmGame game)
    {
        Vector2[] outline = MapCoordinates.GridRectangleOutline(anchor, size, size);
        for (int side = 0; side < outline.Length; side++)
            for (int index = 0; index < size; index++)
            {
                Vector2I offset = side switch
                {
                    0 => new Vector2I(index, 0),
                    1 => new Vector2I(size - 1, index),
                    2 => new Vector2I(size - index - 1, size - 1),
                    _ => new Vector2I(0, size - index - 1),
                };
                Vector2I cell = anchor + offset;
                bool blocked = !MapCoordinates.ContainsCell(cell) || game.GetBuildingSpace(cell) != null;
                Vector2 local = outline[side].Lerp(outline[(side + 1) % outline.Length], (index + .5f) / size);
                Vector2 screen = map.GetViewport().GetStretchTransform() * (map.GetGlobalTransformWithCanvas() * local);
                Color pixel = image.GetPixel(Mathf.RoundToInt(screen.X), Mathf.RoundToInt(screen.Y));
                if (blocked ? !IsRed(pixel) : !IsGreen(pixel))
                    return Fail($"候选格{cell}的外围线配色错误：边{side}，实际{pixel}");
            }
        return true;
    }

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Save(Image image, string name)
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
