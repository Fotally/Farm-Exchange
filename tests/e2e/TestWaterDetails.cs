using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Terrain;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestWaterDetails : Node
{
    public override async void _Ready()
    {
        try
        {
            GetWindow().Size = new Vector2I(1920, 1080);
            bool passed = await RunChecksAsync(this);
            if (passed) GD.Print("水域、岸边与完整陆地详情真实主场景回归通过");
            GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        int? previousSeed = Main.InitialSeed;
        Main.InitialSeed = 12345;
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        try
        {
            await main.ReadyTask;
            main.SetProcess(false);
            main.GetNode<CameraController>("Camera2D").SetProcess(false);
            (Vector2I water, Vector2I shore, Vector2I land) = FindTerrainCells(main.Game);
            Select(main, water);
            bool initialWaterCorrect = CheckUnoccupied(main, water, fullLand: false);
            await Capture(parent, initialWaterCorrect ? "after-water.png" : "before-water.png");
            if (!initialWaterCorrect) return false;

            foreach (Vector2I cell in new[] { land, shore, water, shore, land, water })
            {
                bool fullLand = main.Game.Terrain.IsFullLand(cell);
                Select(main, cell);
                if (!CheckUnoccupied(main, cell, fullLand)) return false;
                uint before = main.Game.Calendar.ElapsedSeconds;
                if (main.AdvanceSimulation(1) != 1 || main.Game.Calendar.ElapsedSeconds != before + 1 ||
                    !CheckUnoccupied(main, cell, fullLand))
                    return Fail("经营刷新后详情与当前所选地形不一致");

                Find<Button>(main, "PauseButton").EmitSignal(Button.SignalName.Pressed);
                if (!main.Game.IsPaused || !CheckUnoccupied(main, cell, fullLand))
                    return Fail("暂停按钮刷新后详情与当前所选地形不一致");
                if (main.AdvanceSimulation(2) != 0 || main.Game.Calendar.ElapsedSeconds != before + 1 ||
                    !CheckUnoccupied(main, cell, fullLand))
                    return Fail("暂停期间详情或经营时间发生错误变化");
                Find<Button>(main, "PauseButton").EmitSignal(Button.SignalName.Pressed);
                if (main.Game.IsPaused || main.AdvanceSimulation(1) != 1 ||
                    !CheckUnoccupied(main, cell, fullLand))
                    return Fail("恢复经营后详情与当前所选地形不一致");
            }

            Select(main, shore);
            await Capture(parent, "after-shore.png");
            Select(main, land);
            await Capture(parent, "after-land.png");
            if (!await CheckBuildings(parent, main, land)) return false;
            Select(main, water);
            return CheckUnoccupied(main, water, fullLand: false);
        }
        finally
        {
            main.QueueFree();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            Main.InitialSeed = previousSeed;
        }
    }

    private static (Vector2I Water, Vector2I Shore, Vector2I Land) FindTerrainCells(FarmGame game)
    {
        Vector2I? water = null;
        Vector2I? shore = null;
        Vector2I? land = null;
        Vector2I center = new(FarmGame.MapSize / 2, FarmGame.MapSize / 2);
        for (int radius = 0; radius < FarmGame.MapSize / 2; radius++)
        {
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
                for (int x = center.X - radius; x <= center.X + radius; x++)
                {
                    if (Math.Max(Math.Abs(x - center.X), Math.Abs(y - center.Y)) != radius) continue;
                    Vector2I cell = new(x, y);
                    if (game.GetPlot(cell).Building != BuildingKind.None) continue;
                    TerrainCellSnapshot terrain = game.Terrain.GetCell(cell);
                    if (terrain.IsFullLand) land ??= cell;
                    else if (terrain.Top == TerrainKind.Water && terrain.Right == TerrainKind.Water &&
                        terrain.Bottom == TerrainKind.Water && terrain.Left == TerrainKind.Water) water ??= cell;
                    else shore ??= cell;
                }
            if (water is Vector2I w && shore is Vector2I s && land is Vector2I l) return (w, s, l);
        }
        throw new InvalidOperationException("含水主场景未提供水面、岸边含水及完整陆地夹具");
    }

    private static void Select(Main main, Vector2I cell)
    {
        WorldMap map = main.GetNode<WorldMap>("WorldMap");
        CameraController camera = main.GetNode<CameraController>("Camera2D");
        camera.GlobalPosition = map.GetGridWorldPosition(cell);
        camera.ForceUpdateScroll();
        Vector2 screen = map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);
        TestMapInput.ShortClick(camera, screen);
    }

    private static bool CheckUnoccupied(Main main, Vector2I cell, bool fullLand)
    {
        Control detail = Find<Control>(main, "DetailWindow");
        Label[] labels = detail.FindChildren("*", "Label", true, false).OfType<Label>()
            .Where(label => label.IsVisibleInTree()).ToArray();
        string text = string.Join("\n", labels.Select(label => label.Text));
        if (!detail.IsVisibleInTree() || !text.Contains($"地块 ({cell.X}, {cell.Y})") ||
            Find<FarmDetailsPanel>(main, "FarmDetailsPanel").IsVisibleInTree() ||
            Find<ProcessorDetailsPanel>(main, "ProcessorDetailsPanel").IsVisibleInTree() ||
            Find<RoadDetailsPanel>(main, "RoadDetailsPanel").IsVisibleInTree())
            return Fail($"未占用地块 {cell} 未显示独立地块详情");
        if (fullLand)
            return text.Contains("空地") && text.Contains("点击底部“建造”选择建筑") && !text.Contains("不可建造") ||
                Fail($"完整陆地 {cell} 丢失原空地建造提示：{text}");
        return text.Contains("水域") && text.Contains("不可建造") && !text.Contains("空地") &&
            !text.Contains("点击底部“建造”选择建筑") ||
            Fail($"含水地块 {cell} 误显示空地或可建造提示：{text}");
    }

    private static async Task<bool> CheckBuildings(Node parent, Main main, Vector2I roadCell)
    {
        var spaces = main.Game.GetBuildingSpaces();
        Vector2I farm = spaces.First(space => space.Building == BuildingKind.Farm).AnchorCell;
        Vector2I processor = spaces.First(space => space.Building == BuildingKind.Processor).AnchorCell;
        Select(main, farm);
        if (!Find<FarmDetailsPanel>(main, "FarmDetailsPanel").IsVisibleInTree() ||
            !Find<Button>(main, "ChangeCropButton").IsVisibleInTree())
            return Fail("含水地图农田详情或改种入口回归");
        Select(main, processor);
        if (!Find<ProcessorDetailsPanel>(main, "ProcessorDetailsPanel").IsVisibleInTree() ||
            !Find<Label>(main, "ProcessorRelation").IsVisibleInTree())
            return Fail("含水地图加工详情或投入产出说明回归");
        if (!main.Game.TryPlaceRoads(new[] { roadCell }, FarmExchange.Land.RoadMaterial.Dirt).Success)
            return Fail("完整陆地道路详情夹具建造失败");
        await main.Game.WaitForNavigationAsync(parent.GetTree());
        Select(main, roadCell);
        if (!Find<RoadDetailsPanel>(main, "RoadDetailsPanel").IsVisibleInTree() ||
            !Find<Button>(main, "RemoveRoadButton").IsVisibleInTree())
            return Fail("含水地图道路详情或拆除入口回归");
        Find<Button>(main, "RemoveRoadButton").EmitSignal(Button.SignalName.Pressed);
        await main.Game.WaitForNavigationAsync(parent.GetTree());
        return main.Game.GetPlot(roadCell).Building == BuildingKind.None &&
            CheckUnoccupied(main, roadCell, fullLand: true) || Fail("拆路后未恢复完整陆地空地详情");
    }

    private static async Task Capture(Node parent, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        for (int frame = 0; frame < 2; frame++)
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (parent.GetViewport().GetVisibleRect().Size.X < 1920 || parent.GetViewport().GetVisibleRect().Size.Y < 1080)
            throw new InvalidOperationException("水域详情截图验收必须至少为1920×1080");
        string directory = ProjectSettings.GlobalizePath("res://build/issue173");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        Error saved = parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + name);
        if (saved != Error.Ok) throw new InvalidOperationException($"水域详情截图保存失败：{saved}");
    }

    private static T Find<T>(Node node, string name) where T : Node =>
        node.FindChild(name, true, false) as T ?? throw new InvalidOperationException(name);

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
