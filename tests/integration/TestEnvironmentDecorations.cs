using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Characters;
using FarmExchange.Gameplay;
using FarmExchange.World;

public partial class TestEnvironmentDecorations : Node
{
    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        bool passed = await RunChecksAsync(this);
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        var game = new FarmGame(12345);
        var scope = new Node2D();
        parent.AddChild(scope);
        var map = new WorldMap();
        scope.AddChild(map);
        map.SetGame(game);
        var camera = new Camera2D { Position = map.GetCellWorldCenter(new Vector2I(192, 192)) };
        scope.AddChild(camera);
        camera.MakeCurrent();
        bool graphical = DisplayServer.GetName() != "headless";
        try
        {
            await Frames(parent);
            var initial = VisibleDecorations(map);
            if (initial.Count == 0 || initial.Keys.Any(cell => game.GetBuildingSpace(cell) != null))
                return Fail("初始环境未出现或覆盖真实开局占地");
            string[] manual = { "hay_bale", "timber_store", "fence", "farm_sign", "bush_hedge" };
            if (manual.Any(name => !initial.Values.Any(sprite => sprite.Texture.ResourcePath.EndsWith($"/{name}_q0.png"))))
                return Fail("开局外围没有实际采用全部五类人工环境陈设");
            if (graphical) Save(parent, "environment-center.png");
            foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
            map.SyncFromGame();
            await Frames(parent);
            if (initial.Count != VisibleDecorations(map).Count)
                return Fail("拆除开局设施复生被剔除环境");

            Vector2I anchor = new(159, 7); // 根点横纵跨160/8处缓存边界。
            camera.Position = map.GetCellWorldCenter(anchor + Vector2I.One);
            camera.ForceUpdateScroll();
            await Frames(parent);
            var before = VisibleDecorations(map);
            Vector2I[] inside = { new(159, 8), new(160, 8), new(159, 9) };
            if (inside.Any(cell => !before.ContainsKey(cell))) return Fail("跨块场景缺少真实树木/灌木夹具");
            int balance = game.MoneyCents;
            var workers = game.GetWorkers().ToArray();
            map.UpdatePlacementPreview(BuildingKind.Farm, CropKind.Wheat, anchor);
            game.TryPlace(new Vector2I(383, 383), BuildingKind.Farm, CropKind.Wheat);
            map.SyncFromGame();
            await Frames(parent);
            if (!SameRoots(before, VisibleDecorations(map)) || game.MoneyCents != balance)
                return Fail("候选或越界失败清除了环境或扣费");
            map.ClearPlacementPreview();
            if (graphical) Save(parent, "environment-before-build.png");
            if (!game.TryPlace(anchor, BuildingKind.Farm, CropKind.Wheat).Success)
                return Fail("纯环境根点阻止正式建造");
            map.SyncFromGame();
            await Frames(parent);
            var after = VisibleDecorations(map);
            if (inside.Any(after.ContainsKey) || before.Count - after.Count != inside.Length ||
                before.Keys.Except(inside).Any(cell => !after.ContainsKey(cell)) ||
                game.MoneyCents != balance - FarmGame.BuildingCostCents ||
                !workers.SequenceEqual(game.GetWorkers()) || game.Calendar.ElapsedSeconds != 0)
                return Fail("跨块完整占地未只清内部根点，或环境触发额外经营变化");
            if (graphical) Save(parent, "environment-after-build.png");
            game.RemoveBuilding(anchor + new Vector2I(2, 2));
            map.SyncFromGame();
            camera.Position += new Vector2(3000, 0);
            camera.ForceUpdateScroll();
            await Frames(parent);
            camera.Position = map.GetCellWorldCenter(anchor + Vector2I.One);
            camera.ForceUpdateScroll();
            await Frames(parent);
            if (!SameRoots(after, VisibleDecorations(map))) return Fail("拆除或镜头往返恢复已清环境");
            Vector2I road = after.Keys.First();
            if (!game.TryPlace(road, BuildingKind.Road, default).Success) return Fail("环境根点上的道路建造失败");
            map.SyncFromGame();
            await Frames(parent);
            var roadAfter = VisibleDecorations(map);
            if (roadAfter.ContainsKey(road) || roadAfter.Count != after.Count - 1)
                return Fail("道路没有仅清除一格环境");
            if (!await CheckTreeDepth(parent, map, camera)) return false;
            GD.Print("环境：开局人工五类、完整占地永久清除、跨块/道路与树前后遮挡通过");
            return true;
        }
        finally { scope.Free(); }
    }

    private static async Task<bool> CheckTreeDepth(Node parent, WorldMap map, Camera2D camera)
    {
        Vector2I treeCell = new(89, 94); // 固定自然布局中周围四格无其他根点的橡树。
        camera.Position = map.GetCellWorldCenter(treeCell);
        camera.ForceUpdateScroll();
        await Frames(parent);
        if (!VisibleDecorations(map).TryGetValue(treeCell, out Sprite2D? tree))
            return Fail("完整自然布局的树木夹具缺失");
        Vector2 root = map.ToLocal(tree.GlobalPosition);
        Image treeImage = tree.Texture.GetImage();
        var actors = new Node2D();
        map.AttachDepthSorted(actors);
        var character = GD.Load<PackedScene>("res://scenes/npc_character.tscn").Instantiate<NpcCharacter>();
        character.SetCharacter(1);
        actors.AddChild(character);
        character.ShowAt(root, Vector2.Down, false, false);
        character.ShowAt(root, Vector2.Zero, false, true);
        var sprite = character.GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        Image actor = sprite.SpriteFrames.GetFrameTexture(sprite.Animation, sprite.Frame).GetImage();
        bool graphical = DisplayServer.GetName() != "headless";
        foreach (int depth in new[] { -12, 12 })
        {
            Vector2 foot = root + new Vector2(0, depth);
            character.ShowAt(foot, Vector2.Zero, false, true);
            await Frames(parent);
            if (!graphical) continue;
            Image shot = parent.GetViewport().GetTexture().GetImage();
            int overlaps = 0;
            for (int y = 4; y < 60; y += 2)
                for (int x = 4; x < 60; x += 2)
                {
                    Color person = actor.GetPixel(x, y);
                    Vector2 local = foot + new Vector2(x - 32 + 0.5f, y - 60 + 0.5f);
                    Vector2 sample = local - root + new Vector2(48, 112);
                    Color trunk = treeImage.GetPixel((int)sample.X, (int)sample.Y);
                    if (person.A < 0.99f || trunk.A < 0.99f) continue;
                    overlaps++;
                    Vector2 screen = map.GetViewport().GetStretchTransform() * (map.GetGlobalTransformWithCanvas() * local);
                    Color actual = shot.GetPixel((int)screen.X, (int)screen.Y);
                    Color expected = depth < 0 ? trunk : person;
                    if (Math.Abs(actual.R - expected.R) > 0.035f || Math.Abs(actual.G - expected.G) > 0.035f ||
                        Math.Abs(actual.B - expected.B) > 0.035f)
                        return Fail("树冠/人物没有按独立脚根前后排序");
                }
            if (overlaps < 10) return Fail("树木遮挡夹具不透明交叠不足");
            Save(parent, depth < 0 ? "environment-tree-behind.png" : "environment-tree-front.png");
        }
        actors.Free();
        camera.Position = root + new Vector2(0, -parent.GetViewport().GetVisibleRect().Size.Y / 2 - 20);
        camera.ForceUpdateScroll();
        await Frames(parent);
        if (!VisibleDecorations(map).ContainsKey(treeCell)) return Fail("根点离屏时仍可见树冠被裁掉");
        if (graphical)
        {
            if (!TestWorldArt.MatchesTexture(parent.GetViewport().GetTexture().GetImage(), map, treeCell,
                "decor/tree_oak_00_q0.png", new Vector2(48, 112)))
                return Fail("树冠跨越视口时没有显示完整原像素");
            Save(parent, "environment-tree-edge.png");
        }
        return true;
    }

    private static Dictionary<Vector2I, Sprite2D> VisibleDecorations(WorldMap map)
    {
        var result = new Dictionary<Vector2I, Sprite2D>();
        void Visit(Node node)
        {
            if (node is Sprite2D sprite && sprite.Texture?.ResourcePath.StartsWith("res://assets/gameplay/decor/") == true)
                result.Add(MapCoordinates.LocalPositionToCell(map.ToLocal(sprite.GlobalPosition)), sprite);
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(map);
        return result;
    }
    private static bool SameRoots(Dictionary<Vector2I, Sprite2D> a, Dictionary<Vector2I, Sprite2D> b) =>
        a.Count == b.Count && a.Keys.All(b.ContainsKey);
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
