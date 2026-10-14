using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.World;
using FarmExchange.Weather;

public static class TestFacilityOrientations
{
    private static readonly Vector2I Farm = new(183, 183);
    private static readonly Vector2I Processor = new(190, 183);
    private static readonly Vector2 FarmPivot = new(112, 128);
    private static readonly Vector2 BuildingPivot = new(128, 176);
    private static readonly string[] CropNames = { "wheat", "corn", "rice", "potato", "sunflower", "sugarcane", "radish" };

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        bool graphical = DisplayServer.GetName() != "headless";
        for (int direction = 1; direction < 4; direction++)
            for (int crop = 0; crop < CropNames.Length; crop++)
            {
                var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
                foreach (BuildingSpaceSnapshot space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
                if (crop == (int)CropKind.Sugarcane) game.AdvanceTicks(4320);
                var orientation = (BuildingOrientation)direction;
                if (!game.TryPlace(Farm, BuildingKind.Farm, (CropKind)crop, orientation: orientation).Success ||
                    !game.TryPlace(Processor, BuildingKind.Processor, (CropKind)crop, orientation: orientation).Success)
                    return Fail("四向真实建造夹具失败");
                var scope = new Node2D();
                parent.AddChild(scope);
                var map = new WorldMap { Position = new Vector2(113, -67) };
                scope.AddChild(map);
                map.SetGame(game);
                var camera = new Camera2D { GlobalPosition = map.GetCellWorldCenter(Farm + new Vector2I(3, 1)) };
                scope.AddChild(camera);
                camera.MakeCurrent();
                try
                {
                    await Frames(parent);
                    if (!CheckLayer(parent, map, Farm + Vector2I.One, $"soil/field_dry_q{direction}.png", FarmPivot, graphical) ||
                        !CheckLayer(parent, map, Processor + Vector2I.One,
                            $"buildings/{CropNames[crop]}_workshop_q{direction}.png", BuildingPivot, graphical)) return false;
                    if (!CheckNoQ0Art(map)) return false;
                    int waited = 0;
                    while (game.GetPlot(Farm).Crop != CropStage.Seeded && waited++ < 60) game.AdvanceTick();
                    if (game.GetPlot(Farm).Crop != CropStage.Seeded) return Fail("四向农田真实播种失败");
                    map.SyncFromGame();
                    await Frames(parent);
                    if (!CheckLayer(parent, map, Farm + Vector2I.One, $"crops/field_seeded_q{direction}.png", FarmPivot, graphical))
                        return false;
                    WeatherTestInput.AdvanceTick(game, true);
                    for (int stage = 0; stage < 3; stage++)
                    {
                        while (game.GetPlot(Farm).Crop == CropStage.Growing && game.GetPlot(Farm).GrowthProgress < stage / 3d)
                            game.AdvanceTick();
                        if (game.GetPlot(Farm).Crop != CropStage.Growing || game.GetPlot(Farm).GrowthProgress >= (stage + 1) / 3d)
                            return Fail("四向农田未处于实际三档生长区间");
                        map.SyncFromGame();
                        await Frames(parent);
                        if (!CheckLayer(parent, map, Farm + Vector2I.One,
                            $"crops/{CropNames[crop]}_growing_{stage + 1:D2}_q{direction}.png", FarmPivot, graphical) ||
                            !HasLayer(map, Farm + Vector2I.One, $"soil/field_wet_q{direction}.png", FarmPivot) ||
                            !CheckNoQ0Art(map)) return false;
                    }
                    game.SetPaused(true);
                    uint second = game.Calendar.ElapsedSeconds;
                    game.AdvanceTicks(100);
                    map.SyncFromGame();
                    await Frames(parent);
                    if (game.Calendar.ElapsedSeconds != second || game.GetBuildingSpace(Farm)!.Orientation != orientation ||
                        !CheckNoQ0Art(map)) return Fail("暂停同步改变朝向或出现q0动效");
                    if (graphical)
                    {
                        string path = ProjectSettings.GlobalizePath("res://build/issue111-validation");
                        Directory.CreateDirectory(path);
                        parent.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(path, $"{CropNames[crop]}-q{direction}.png"));
                    }
                }
                finally { scope.Free(); }
            }
        GD.Print("设施四向：q1～q3七建筑、七作物三档、同向干湿土/播种层、pivot与无q0动效通过");
        return true;
    }

    private static bool CheckLayer(Node parent, WorldMap map, Vector2I center, string path, Vector2 pivot, bool graphical) =>
        HasLayer(map, center, path, pivot) && (!graphical ||
            TestWorldArt.MatchesTexture(parent.GetViewport().GetTexture().GetImage(), map, center, path, pivot));

    private static bool HasLayer(WorldMap map, Vector2I center, string path, Vector2 pivot)
    {
        bool found = false;
        void Visit(Node node)
        {
            if (node is Sprite2D sprite && sprite.IsVisibleInTree() && sprite.Texture?.ResourcePath == "res://assets/gameplay/" + path &&
                !sprite.Centered && sprite.Offset == -pivot && map.ToLocal(sprite.GlobalPosition) == MapCoordinates.CellToLocalCenter(center))
                found = true;
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(map);
        return found || Fail("缺少同向原图、pivot或工作中心：" + path);
    }

    private static bool CheckNoQ0Art(WorldMap map)
    {
        bool valid = true;
        void Visit(Node node)
        {
            if (node is Sprite2D sprite && sprite.Texture?.ResourcePath.Contains("/motion/") == true) valid = false;
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(map);
        return valid || Fail("q1～q3残留q0建筑或植株动效层");
    }

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static bool Fail(string message) { GD.PushError("设施四向：" + message); return false; }
}
