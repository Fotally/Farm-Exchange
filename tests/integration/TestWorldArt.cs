using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Characters;
using FarmExchange.Gameplay;
using FarmExchange.World;
using FarmExchange.Time;

public partial class TestWorldArt : Node
{
    private static readonly Vector2I Farm = new(183, 183);
    private static readonly Vector2I Processor = new(190, 183);
    private static readonly string[] CropNames =
        { "wheat", "corn", "rice", "potato", "sunflower", "sugarcane", "radish" };

    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        bool passed = await RunChecksAsync(this);
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        bool graphical = DisplayServer.GetName() != "headless";
        for (int crop = 0; crop < CropNames.Length; crop++)
        {
            var game = new FarmGame(12345);
            foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
            if (crop == (int)CropKind.Sugarcane) game.AdvanceTicks(4320); // 夏初，真实适季播种。
            if (!game.TryPlace(Farm, BuildingKind.Farm, (CropKind)crop).Success ||
                !game.TryPlace(Processor, BuildingKind.Processor, (CropKind)crop).Success)
                return Fail("世界美术夹具建造失败");
            var scope = new Node2D();
            parent.AddChild(scope);
            var map = new WorldMap { Position = new Vector2(113, -67) };
            scope.AddChild(map);
            var driver = new SimulationDriver();
            map.SetGame(game, driver);
            var camera = new Camera2D { GlobalPosition = map.GetCellWorldCenter(Farm + new Vector2I(3, 1)) };
            scope.AddChild(camera);
            camera.MakeCurrent();
            try
            {
                await Frames(parent);
                uint before = game.Calendar.ElapsedSeconds;
                int redraws = map.ChunkRedrawCount;
                map.SyncFromGame();
                await Frames(parent);
                if (game.Calendar.ElapsedSeconds != before || map.ChunkRedrawCount != redraws)
                    return Fail("重复外观同步推进经营或重绘不变地面");
                if (graphical && (!MatchesTexture(Capture(parent), map, Farm + Vector2I.One,
                        "soil/field_dry_q0.png", new Vector2(112, 128)) ||
                    !MatchesTexture(Capture(parent), map, Processor + Vector2I.One,
                        crop == 0 ? "buildings/motion/windmill_body.png" :
                        crop == 5 ? "buildings/motion/sugarworkshop_body.png" :
                        $"buildings/{CropNames[crop]}_workshop_q0.png", new Vector2(128, 176), minimumY: crop is 0 or 5 ? 170 : 0)))
                    return Fail($"{CropNames[crop]} 的干土或加工设施原图不符");

                int waited = 0;
                while (game.GetPlot(Farm).Crop != CropStage.Seeded && waited++ < 60) game.AdvanceTick();
                if (game.GetPlot(Farm).Crop != CropStage.Seeded) return Fail("真实工人没有完成播种");
                map.SyncFromGame();
                await Frames(parent);
                if (graphical) Save(Capture(parent), $"world-art-{CropNames[crop]}-seeded.png");
                if (graphical && !MatchesTexture(Capture(parent), map, Farm + Vector2I.One,
                    "crops/field_seeded_q0.png", new Vector2(112, 128)))
                    return Fail("播种层未对应真实待水状态");
                game.AdvanceTick(isRaining: true);
                for (int stage = 0; stage < 3; stage++)
                {
                    while (game.GetPlot(Farm).Crop == CropStage.Growing &&
                        game.GetPlot(Farm).GrowthProgress < stage / 3d) game.AdvanceTick();
                    PlotSnapshot plot = game.GetPlot(Farm);
                    if (plot.Crop != CropStage.Growing || !plot.HasWater ||
                        plot.GrowthProgress >= (stage + 1) / 3d)
                        return Fail("实际生长三档夹具未落在指定区间");
                    map.SyncFromGame();
                    await Frames(parent);
                    if (crop == 0 && stage == 0 && !CheckVisualRates(map, game, driver)) return false;
                    if (graphical)
                    {
                        Image shot = Capture(parent);
                        // 三个根点动效作物的九档真实像素由TestFacilityMotion验证；其余保持整图对照。
                        if (crop is not (0 or 5 or 6) && !MatchesTexture(shot, map, Farm + Vector2I.One,
                            $"crops/{CropNames[crop]}_growing_{stage + 1:D2}_q0.png", new Vector2(112, 128)))
                            return Fail($"{CropNames[crop]} 第{stage + 1}档没有使用实际进度原图");
                        Save(shot, $"world-art-{CropNames[crop]}-{stage + 1}.png");
                    }
                }
                // 同品种重启必须立刻撤掉旧生长层，保留当前水分。
                game.SetFarmCrop(Farm, (CropKind)crop);
                map.SyncFromGame();
                await Frames(parent);
                if (graphical && !MatchesTexture(Capture(parent), map, Farm + Vector2I.One,
                    "soil/field_wet_q0.png", new Vector2(112, 128)))
                    return Fail("重启后残留旧作物或丢失湿土");
                if (crop == 0 && !await CheckDepth(parent, game, map, camera)) return false;
                game.RemoveBuilding(Farm + new Vector2I(2, 2));
                map.SyncFromGame();
                await Frames(parent);
                if (game.GetBuildingSpace(Farm) != null || game.GetBuildingSpace(Processor) == null)
                    return Fail("跨块拆除影响相邻设施");
                if (!game.TryPlace(Farm, BuildingKind.Processor, CropKind.Radish).Success)
                    return Fail("同锚点重建失败");
                map.SyncFromGame();
                await Frames(parent);
                if (graphical && !MatchesTexture(Capture(parent), map, Farm + Vector2I.One,
                    "buildings/radish_workshop_q0.png", new Vector2(128, 176)))
                    return Fail("同锚点重建残留农田或显示错误设施");
            }
            finally { scope.Free(); }
        }
        GD.Print("世界美术：七作物三档、播种/干湿土、七建筑、重启重建及前后遮挡通过");
        return true;
    }

    private static async Task<bool> CheckDepth(Node parent, FarmGame game, WorldMap map, Camera2D camera)
    {
        var actors = new Node2D();
        map.AttachDepthSorted(actors);
        var character = GD.Load<PackedScene>("res://scenes/npc_character.tscn").Instantiate<NpcCharacter>();
        character.SetCharacter(1);
        actors.AddChild(character);
        Vector2 root = MapCoordinates.CellToLocalCenter(Processor + Vector2I.One);
        var sprite = character.GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        character.ShowAt(root, Vector2.Down, false, false);
        character.ShowAt(root, Vector2.Zero, false, true);
        Image actor = sprite.SpriteFrames.GetFrameTexture(sprite.Animation, sprite.Frame).GetImage();
        Image building = GD.Load<Texture2D>("res://assets/gameplay/buildings/motion/windmill_body.png").GetImage();
        bool graphical = DisplayServer.GetName() != "headless";
        foreach (int depth in new[] { -12, 12 })
        {
            Vector2 foot = root + new Vector2(0, depth);
            character.ShowAt(foot, Vector2.Zero, false, true);
            character.Visible = false;
            await Frames(parent);
            Image? background = graphical ? Capture(parent) : null;
            character.Visible = true;
            await Frames(parent);
            if (!graphical) continue;
            Image shot = Capture(parent);
            int overlaps = 0;
            for (int y = 4; y < 60; y += 2)
                for (int x = 4; x < 60; x += 2)
                {
                    Color person = actor.GetPixel(x, y);
                    Vector2 local = foot + new Vector2(x - 32 + 0.5f, y - 60 + 0.5f);
                    Vector2 buildingPixel = local - root + new Vector2(128, 176);
                    Color wall = building.GetPixel((int)buildingPixel.X, (int)buildingPixel.Y);
                    if (person.A < 0.99f || wall.A < 0.99f) continue;
                    overlaps++;
                    if (!Near(PixelAt(shot, map, local), depth < 0 ? PixelAt(background!, map, local) : person))
                        return Fail(depth < 0 ? "建筑后方人物穿过墙面" : "建筑前方人物被建筑整图压住");
                }
            if (overlaps < 10) return Fail("人物/建筑遮挡夹具缺少足够不透明交叠");
            Save(shot, depth < 0 ? "world-depth-behind.png" : "world-depth-front.png");
        }
        actors.Free();
        await Frames(parent);
        Image? fullBuilding = graphical ? Capture(parent) : null;
        Transform2D fullTransform = map.GetViewport().GetStretchTransform() * map.GetGlobalTransformWithCanvas();
        // 屋顶仍可见而工作中心在屏幕下方；不能按中心或锚点所在块隐藏整图。
        Vector2 saved = camera.Position;
        camera.GlobalPosition = map.ToGlobal(root + new Vector2(0, -parent.GetViewport().GetVisibleRect().Size.Y / 2 - 40));
        camera.ForceUpdateScroll();
        await Frames(parent);
        if (graphical)
        {
            Image edge = Capture(parent);
            int compared = 0;
            for (int y = 3; y < 170; y += 2)
                for (int x = 3; x < building.GetWidth() - 3; x += 2)
                {
                    if (building.GetPixel(x, y).A < .99f) continue;
                    Vector2 local = root + new Vector2(x + .5f - 128, y + .5f - 176);
                    Vector2 current = ToPixel(map, local);
                    if (current.X < 0 || current.Y < 0 || current.X >= edge.GetWidth() || current.Y >= edge.GetHeight()) continue;
                    Vector2 previous = fullTransform * local;
                    if (!Near(edge.GetPixel((int)current.X, (int)current.Y), fullBuilding!.GetPixel((int)previous.X, (int)previous.Y)))
                        return Fail("视野边缘在锚点不可见时改变或裁掉跨块屋顶");
                    compared++;
                }
            if (compared < 10) return Fail("屋顶边缘取样不足");
            Save(Capture(parent), "world-roof-edge.png");
        }
        camera.Position = saved;
        camera.ForceUpdateScroll();
        await Frames(parent);
        return true;
    }

    private static bool CheckVisualRates(WorldMap map, FarmGame game, SimulationDriver driver)
    {
        ShaderMaterial? FindMaterial(Node node)
        {
            if (node is Sprite2D sprite && sprite.Material is ShaderMaterial material) return material;
            foreach (Node child in node.GetChildren())
            {
                ShaderMaterial? found = FindMaterial(child);
                if (found != null) return found;
            }
            return null;
        }
        ShaderMaterial? wind = FindMaterial(map);
        if (wind == null) return Fail("真实生长农田没有根点风摆材质");
        map.SetProcess(false);
        try
        {
            foreach (double rate in new[] { .5, 1, 2, 16 })
            {
                driver.SetDevelopmentRate(rate, SimulationRateSource.Scenario);
                double before = wind.GetShaderParameter("visual_time").AsDouble();
                map._Process(.25);
                double advanced = wind.GetShaderParameter("visual_time").AsDouble() - before;
                double expected = .25 * (rate > 2 ? 1 : rate);
                if (Math.Abs(advanced - expected) > .001) return Fail("公共倍率或高开发倍率1×视觉约定不一致");
            }
            game.SetPaused(true);
            double paused = wind.GetShaderParameter("visual_time").AsDouble();
            map._Process(.5);
            if (wind.GetShaderParameter("visual_time").AsDouble() != paused) return Fail("暂停仍推进世界动效");
            return true;
        }
        finally
        {
            driver.SetRate(1);
            game.SetPaused(false);
            map.SetProcess(true);
        }
    }

    public static bool MatchesTexture(Image shot, WorldMap map, Vector2I center, string path, Vector2 pivot, bool leftHalf = false, int minimumY = 0)
    {
        Image texture = GD.Load<Texture2D>($"res://assets/gameplay/{path}").GetImage();
        Vector2 root = MapCoordinates.CellToLocalCenter(center);
        int samples = 0;
        for (int y = Math.Max(3, minimumY); y < texture.GetHeight() - 3; y += 2)
            for (int x = 3; x < texture.GetWidth() - 3; x += 2)
            {
                Color expected = texture.GetPixel(x, y);
                if (expected.A < 0.99f) continue;
                Vector2 relative = new Vector2(x + 0.5f, y + 0.5f) - pivot;
                if (path.StartsWith("soil/") && Math.Abs(Math.Abs(relative.X) / 96 + Math.Abs(relative.Y) / 48 - 1) < 0.1)
                    continue; // 选框位于逻辑占地边缘，不属于土层纹理。
                if (leftHalf && relative.X >= -4) continue;
                Vector2 pixel = ToPixel(map, root + relative);
                if (pixel.X < 0 || pixel.Y < 0 || pixel.X >= shot.GetWidth() || pixel.Y >= shot.GetHeight()) continue;
                samples++;
                if (!Near(shot.GetPixel((int)pixel.X, (int)pixel.Y), expected))
                {
                    Color actual = shot.GetPixel((int)pixel.X, (int)pixel.Y);
                    Save(shot, "world-art-failure.png");
                    GD.PushError($"原图取样不符 {path}: texture=({x},{y}), screen={pixel}, expected={expected}, actual={actual}");
                    return false;
                }
            }
        if (samples < 10) GD.PushError($"原图有效不透明取样不足 {path}: {samples}");
        return samples >= 10;
    }

    private static Vector2 ToPixel(WorldMap map, Vector2 local) =>
        map.GetViewport().GetStretchTransform() * (map.GetGlobalTransformWithCanvas() * local);
    private static Color PixelAt(Image shot, WorldMap map, Vector2 local)
    {
        Vector2 pixel = ToPixel(map, local);
        return shot.GetPixel((int)pixel.X, (int)pixel.Y);
    }
    private static bool Near(Color a, Color b) =>
        Math.Abs(a.R - b.R) < 0.035f && Math.Abs(a.G - b.G) < 0.035f && Math.Abs(a.B - b.B) < 0.035f;
    private static Image Capture(Node parent) => parent.GetViewport().GetTexture().GetImage();
    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static void Save(Image image, string name)
    {
        string path = ProjectSettings.GlobalizePath("res://coverage");
        Directory.CreateDirectory(path);
        image.SavePng(Path.Combine(path, name));
    }
    private static bool Fail(string message) { GD.PushError(message); return false; }
}
