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
        Vector2I[] initial = { new(63, 63), new(64, 63), new(65, 63), new(63, 64), new(64, 64) };
        foreach (Vector2I cell in initial) game.RemoveBuilding(cell);
        var scope = new Node2D();
        parent.AddChild(scope);
        var map = new WorldMap { Position = new Vector2(113, -67) };
        scope.AddChild(map);
        map.SetGame(game);
        var camera = new Camera2D
        {
            Position = map.GetCellWorldCenter(new Vector2I(63, 63)),
            Zoom = new Vector2(1.25f, 1.25f),
        };
        scope.AddChild(camera);
        camera.MakeCurrent();
        try
        {
            await LayoutFrames(parent);
            int initialRedraws = map.ChunkRedrawCount;
            Vector2I firstRoad = new(63, 63);
            Vector2I secondRoad = new(64, 63); // 分处相邻两个 8×8 块。
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
                Vector2 selectedEdge = map.GetGlobalTransformWithCanvas() *
                    (MapCoordinates.CellToLocalCenter(firstRoad) + new Vector2(MapCoordinates.TileWidth / 2, 0));
                Color outline = before.GetPixel(Mathf.RoundToInt(selectedEdge.X), Mathf.RoundToInt(selectedEdge.Y));
                if (outline.R <= outline.G + 0.04f || outline.G <= outline.B + 0.1f)
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
            return true;
        }
        finally
        {
            parent.RemoveChild(scope);
            scope.Free();
        }
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
