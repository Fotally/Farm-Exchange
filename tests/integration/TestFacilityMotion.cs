using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.World;

public partial class TestFacilityMotion : Node
{
    private static readonly Color Backdrop = new(.07f, .09f, .11f);

    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        GetTree().Quit(await RunChecksAsync(this) ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        bool graphical = DisplayServer.GetName() != "headless";
        var scope = new Node2D { YSortEnabled = true };
        parent.AddChild(scope);
        scope.AddChild(new ColorRect
        {
            Color = Backdrop,
            Size = new Vector2(1920, 1080),
            ZIndex = -100,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var camera = new Camera2D { Position = new Vector2(960, 540) };
        scope.AddChild(camera);
        camera.MakeCurrent();
        var mill = new FacilityMotion { Position = new Vector2(330, 400) };
        var sugar = new FacilityMotion { Position = new Vector2(640, 400) };
        scope.AddChild(mill);
        scope.AddChild(sugar);
        try
        {
            mill.Configure(new PlotSnapshot(BuildingKind.Processor, CropKind.Wheat, CropStage.None, 0), 0);
            sugar.Configure(new PlotSnapshot(BuildingKind.Processor, CropKind.Sugarcane, CropStage.None, 10), 0);
            var art = mill.GetNode<Node2D>("Art");
            var sails = art.GetNode<Sprite2D>("Sails");
            var hub = art.GetNode<Sprite2D>("Hub");
            if (mill.YSortEnabled || art.YSortEnabled || art.GetChild(0).Name != "Body" ||
                art.GetChild(1) != sails || art.GetChild(2) != hub ||
                sails.Position != new Vector2(-3, -72) || hub.Position != sails.Position ||
                art.GetNode<Sprite2D>("Body").Offset != new Vector2(-128, -176) ||
                !art.GetNode<Sprite2D>("Body").Texture.ResourcePath.EndsWith("motion/windmill_body.png"))
                return Fail("磨坊必须以去叶片塔身→活动叶片→固定轮毂组成局部遮挡");
            mill.Advance(1, false);
            if (sails.Frame != 0) return Fail("待料磨坊仍在转动");
            mill.Advance(1, true);
            int frame = sails.Frame;
            if (frame == 0) return Fail("真实加工没有驱动叶片");
            mill.Advance(0, true);
            mill.Advance(1, false);
            if (sails.Frame != frame || hub.Position != new Vector2(-3, -72))
                return Fail("暂停/等待未冻结叶片或轮毂漂移");
            await Frames(parent);
            if (graphical && !CheckBuildingPixels(Capture(parent), mill, sugar, frame)) return false;
            sugar.Advance(.5, true);
            Vector2[,] expectedRoots =
            {
                { new(5, 12), new(10, 27), new(12, 41) },
                { new(6, 15), new(13, 32), new(17, 52) },
                { new(6, 9), new(9, 19), new(14, 29) },
            };
            CropKind[] crops = { CropKind.Wheat, CropKind.Sugarcane, CropKind.Radish };
            string[] cropFiles = { "wheat", "sugarcane", "radish" };
            for (int crop = 0; crop < crops.Length; crop++)
                for (int stage = 0; stage < 3; stage++)
                {
                    var plot = new PlotSnapshot(BuildingKind.Farm, crops[crop], CropStage.Growing, 1, true, (stage + .5) / 3);
                    var motion = new FacilityMotion { Position = new Vector2(970 + stage * 250, 300 + crop * 220) };
                    scope.AddChild(motion);
                    motion.Configure(plot, stage);
                    Node2D plants = motion.GetNode<Node2D>("Art");
                    if (!FacilityMotion.ReplacesSurface(plot) || !motion.YSortEnabled || !plants.YSortEnabled || plants.GetChildCount() != 15)
                        return Fail("三作物每档必须有15个参与共同YSort的独立根点");
                    float previousY = float.MinValue;
                    foreach (Node node in plants.GetChildren())
                    {
                        var plant = (Sprite2D)node;
                        if (plant.Position.Y < previousY || plant.ZIndex != 0 || plant.Centered ||
                            plant.Offset != -expectedRoots[crop, stage] - new Vector2(4, 0))
                            return Fail("逐株根点、稳定顺序或padding偏移错误");
                        previousY = plant.Position.Y;
                    }
                    var first = plants.GetChild<Sprite2D>(0);
                    var material = (ShaderMaterial)first.Material;
                    int pin = crop == 2 ? new[] { 1, 7, 12 }[stage] : 1;
                    if (material.GetShaderParameter("fixed_base_px").AsInt32() != pin)
                        return Fail("萝卜根部或植株根固定高度未采用清单约定");
                    Image source = GD.Load<Texture2D>($"res://assets/gameplay/crops/motion/{cropFiles[crop]}_growing_{stage + 1:D2}.png").GetImage();
                    await Frames(parent);
                    if (graphical && !CheckFixedRootPixels(Capture(parent), motion,
                        source, expectedRoots[crop, stage], pin, crops[crop], stage)) return false;
                    Vector2 oldRoot = first.Position;
                    motion.Advance(1, false);
                    await Frames(parent);
                    if (graphical && !CheckFixedRootPixels(Capture(parent), motion,
                        source, expectedRoots[crop, stage], pin, crops[crop], stage)) return false;
                    double time = material.GetShaderParameter("visual_time").AsDouble();
                    motion.Advance(0, false);
                    if (time == 0 || material.GetShaderParameter("visual_time").AsDouble() != time || first.Position != oldRoot)
                        return Fail("作物风摆未冻结暂停或移动了实体根点");
                    motion.Configure(plot, stage);
                    if (plants.GetChild(0) != first) return Fail("重复快照重建不变植株");
                }
            await Frames(parent);
            if (graphical) Save(Capture(parent), "facility-motion-stages.png");

            var resultNode = new FacilityMotion { Position = new Vector2(330, 650) };
            scope.AddChild(resultNode);
            var result = new ProductionResult(42, new Vector2I(1, 1), CropKind.Wheat, ProductionResultKind.Product, 137);
            resultNode.ShowResult(result);
            resultNode.Advance(.2, false);
            await Frames(parent);
            Image? once = graphical ? Capture(parent) : null;
            resultNode.ShowResult(result);
            resultNode.Advance(0, true);
            await Frames(parent);
            if (graphical && !EqualRegion(once!, Capture(parent), new Rect2I(280, 585, 100, 100)))
                return Fail("相同结果重复同步或暂停使产出数量重新播放");
            if (graphical) Save(Capture(parent), "facility-motion-output.png");
            resultNode.ClearResult();
            await Frames(parent);
            Image? cleared = graphical ? Capture(parent) : null;
            resultNode.ShowResult(result);
            await Frames(parent);
            if (graphical && !EqualRegion(cleared!, Capture(parent), new Rect2I(280, 585, 100, 100)))
                return Fail("清除过期结果后相同结果复活");
            resultNode.ShowResult(result with { ElapsedSeconds = 43 });
            resultNode.Advance(10000, true);
            sugar.Advance(10000, true);
            await Frames(parent);
            if (graphical && !EqualRegion(cleared!, Capture(parent), new Rect2I(280, 585, 100, 100)))
                return Fail("高倍率没有淘汰过期数量反馈");
            var harvest = result with { Kind = ProductionResultKind.Harvest, Quantity = 24, ElapsedSeconds = 44 };
            resultNode.ShowResult(harvest);
            resultNode.Advance(.3, false);
            await Frames(parent);
            if (graphical) Save(Capture(parent), "facility-motion-harvest.png");
            sugar.Advance(2, false);
            mill.Configure(new PlotSnapshot(BuildingKind.Farm, CropKind.Corn, CropStage.Growing, 1), 0);
            if (mill.GetNode<Node2D>("Art").GetChildCount() != 0 ||
                FacilityMotion.ReplacesSurface(new PlotSnapshot(BuildingKind.Farm, CropKind.Wheat, CropStage.Seeded, 0)))
                return Fail("未覆盖静态作物或播种层被动效错误替换");
            GD.Print("生产动效：磨坊局部分层、等待暂停、三作物九档根点、重复结果与高倍率淘汰通过");
            return true;
        }
        finally { scope.Free(); }
    }

    // 只取来源层中明确的像素，不模拟渲染器：三个真实重叠关系及旧固定叶片残留。
    private static bool CheckBuildingPixels(Image shot, FacilityMotion mill, FacilityMotion sugar, int frame)
    {
        Image body = GD.Load<Texture2D>("res://assets/gameplay/buildings/motion/windmill_body.png").GetImage();
        Image atlas = GD.Load<Texture2D>("res://assets/gameplay/buildings/motion/windmill_sails_256.png").GetImage();
        Image hub = GD.Load<Texture2D>("res://assets/gameplay/buildings/motion/windmill_hub.png").GetImage();
        Image old = GD.Load<Texture2D>("res://assets/gameplay/buildings/wheat_workshop_q0.png").GetImage();
        Image sugarBody = GD.Load<Texture2D>("res://assets/gameplay/buildings/motion/sugarworkshop_body.png").GetImage();
        int bodySamples = 0, sailOverlapSamples = 0, hubSamples = 0, removedSailSamples = 0, sugarSamples = 0;
        for (int y = 0; y < 240; y++)
            for (int x = 0; x < 256; x++)
            {
                Color wall = body.GetPixel(x, y);
                Color blade = x >= 61 && x < 189 && y >= 40 && y < 168 ?
                    atlas.GetPixel(frame % 16 * 128 + x - 61, frame / 16 * 128 + y - 40) : Colors.Transparent;
                Color center = x >= 117 && x < 133 && y >= 96 && y < 112 ?
                    hub.GetPixel(x - 117, y - 96) : Colors.Transparent;
                Vector2 local = new(x - 128 + .5f, y - 176 + .5f);
                Color actual = PixelAt(shot, mill, local);
                if (center.A > .99f && blade.A > .99f)
                {
                    hubSamples++;
                    if (!Near(actual, center)) return Fail("真实画面轮毂未覆盖活动叶片");
                }
                else if (center.A == 0 && blade.A > .99f && wall.A > .99f)
                {
                    sailOverlapSamples++;
                    if (!Near(actual, blade)) return Fail("真实画面活动叶片未覆盖塔身");
                }
                else if (center.A == 0 && blade.A == 0 && wall.A > .99f)
                {
                    bodySamples++;
                    if (!Near(actual, wall)) return Fail("真实画面磨坊塔身与去叶片底图不符");
                }
                else if (center.A == 0 && blade.A == 0 && wall.A == 0 && old.GetPixel(x, y).A > .99f)
                {
                    removedSailSamples++;
                    if (!Near(actual, Backdrop)) return Fail("活动叶片之外仍残留旧静态叶片");
                }
                Color sugarPixel = sugarBody.GetPixel(x, y);
                if (sugarPixel.A > .99f)
                {
                    sugarSamples++;
                    if (!Near(PixelAt(shot, sugar, local), sugarPixel)) return Fail("真实画面制糖坊底图像素不符");
                }
            }
        if (bodySamples < 100 || sailOverlapSamples < 20 || hubSamples < 5 || removedSailSamples < 10 || sugarSamples < 100)
            return Fail($"建筑图形夹具样本不足：塔身{bodySamples}、交叠叶片{sailOverlapSamples}、轮毂{hubSamples}、去旧叶片{removedSailSamples}、糖坊{sugarSamples}");
        GD.Print($"动效像素：塔身{bodySamples}、叶片覆盖{sailOverlapSamples}、轮毂覆盖{hubSamples}、去旧叶片{removedSailSamples}、糖坊{sugarSamples}");
        return true;
    }

    private static bool CheckFixedRootPixels(Image shot, FacilityMotion motion, Image texture,
        Vector2 sourceRoot, int pin, CropKind crop, int stage)
    {
        // q0 最前方株的根为(0,32)，根及固定区不会被其他株覆盖；直接对照原PNG颜色。
        int samples = 0;
        for (int y = (int)sourceRoot.Y - pin; y <= (int)sourceRoot.Y; y++)
            for (int x = 0; x < texture.GetWidth(); x++)
            {
                Color expected = texture.GetPixel(x, y);
                if (expected.A < .99f) continue;
                Vector2 local = new Vector2(0, 32) - sourceRoot - new Vector2(4, 0) + new Vector2(x + .5f, y + .5f);
                samples++;
                if (!Near(PixelAt(shot, motion, local), expected))
                    return Fail($"{crop}第{stage + 1}档固定根区真实像素漂移：源({x},{y})、固定{pin}px");
            }
        if (samples < 2) return Fail($"{crop}第{stage + 1}档根区图形夹具没有足够不透明样本");
        return true;
    }

    private static Color PixelAt(Image shot, Node2D node, Vector2 local)
    {
        Vector2 pixel = node.GetViewport().GetStretchTransform() * (node.GetGlobalTransformWithCanvas() * local);
        return shot.GetPixel((int)pixel.X, (int)pixel.Y);
    }

    private static bool Near(Color a, Color b) =>
        Math.Abs(a.R - b.R) < .035f && Math.Abs(a.G - b.G) < .035f && Math.Abs(a.B - b.B) < .035f;

    private static bool EqualRegion(Image first, Image second, Rect2I region)
    {
        for (int y = region.Position.Y; y < region.End.Y; y++)
            for (int x = region.Position.X; x < region.End.X; x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) return false;
        return true;
    }

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static Image Capture(Node parent) => parent.GetViewport().GetTexture().GetImage();
    private static void Save(Image image, string name)
    {
        string directory = ProjectSettings.GlobalizePath("res://coverage");
        Directory.CreateDirectory(directory);
        image.SavePng(Path.Combine(directory, name));
    }
    private static bool Fail(string message) { GD.PushError(message); return false; }
}
