using System;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Characters;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Time;
using FarmExchange.Terrain;
using FarmExchange.Workers;
using FarmExchange.World;

public partial class TestWaterPresentation : Node
{
    public override async void _Ready() => GetTree().Quit(await RunChecksAsync(this) ? 0 : 1);

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        using var game = await FarmGame.CreateAsync(parent.GetTree(), 12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit);
        var driver = new SimulationDriver();
        var map = new WorldMap();
        parent.AddChild(map);
        map.SetGame(game, driver);
        map.SetProcess(false);
        var workers = new WorkerPresentation();
        map.AttachDepthSorted(workers);
        workers.SetGame(game, map, driver);
        workers.SetProcess(false);
        try
        {
            Vector2I water = new(-1, -1);
            for (int y = 200; y < 220 && water.X < 0; y++)
                for (int x = 182; x < 202; x++)
                    if (!game.Terrain.IsFullLand(new(x, y))) { water = new(x, y); break; }
            if (water.X < 0) return Fail("默认近景范围没有含水格");
            bool shoreDry = false;
            for (int y = 205; y <= 214 && !shoreDry; y++)
                for (int x = 185; x <= 199 && !shoreDry; x++)
                {
                    if (game.Terrain.IsFullLand(new(x, y))) continue;
                    for (int dy = -3; dy <= 3 && !shoreDry; dy++)
                        for (int dx = -3; dx <= 3 && !shoreDry; dx++)
                        {
                            LandPosition point = LandPosition.FromCoordinates(x + dx / 8.0, y + dy / 8.0);
                            shoreDry = game.Terrain.FindLandPath(point, point).Status == LandPathStatus.Success;
                        }
                }
            if (!shoreDry) return Fail("岸格干地没有合法圆脚底位置");
            var preview = PlacementPreviewGeometry.GetCells(water, BuildingFootprint.Get(BuildingKind.Road).Offsets, game);
            if (!preview[0].Blocked) return Fail("普通候选未标记含水格冲突");
            map.UpdateRoadPreview(new[] { water }, RoadMaterial.Dirt);
            if (map.GetDisplayedRoadAppearance(water) != null) return Fail("道路预览在含水格绘制可建道路");
            map.ClearRoadPreview();
            int money = game.MoneyCents;
            if (game.TryPlaceRoads(new[] { water }, RoadMaterial.Dirt).Success || game.MoneyCents != money)
                return Fail("水域预览与真实道路拒绝不一致");

            foreach (var building in game.GetBuildingSpaces()) game.RemoveBuilding(building.AnchorCell);
            foreach (Vector2I cell in new[] { new Vector2I(187, 220), new Vector2I(192, 220), new Vector2I(197, 220) })
                if (game.BuildFarm(cell) != null) return Fail("绕近景池塘目标农田夹具无效");
            int cornerIntervals = 0;
            int routedIntervals = 0;
            for (int second = 0; second < 35; second++)
            {
                game.AdvanceTick();
                var snapshots = game.GetWorkers();
                for (int frame = 1; frame <= 4; frame++)
                {
                    workers._Process(.25);
                    foreach (WorkerSnapshot worker in snapshots)
                    {
                        if (worker.Movement is not WorkerMovementSnapshot movement || !game.IsWorkerMovementCurrent(movement)) continue;
                        double distance = movement.FromDistance + (movement.ToDistance - movement.FromDistance) * frame / 4;
                        Vector2 expected = movement.Path.GetPositionAtDistance(distance).ToVector2();
                        NpcCharacter character = workers.GetNode<NpcCharacter>($"Worker{worker.WorkerNumber}");
                        Vector2 actual = character.GlobalPosition;
                        if (!actual.IsEqualApprox(map.GetGridWorldPosition(expected))) return Fail("人物没有沿真实路线采样");
                        if (frame < 4)
                        {
                            Vector2 direction = MapCoordinates.GridPositionToLocal(movement.Path.GetDirectionAtDistance(distance));
                            string facing = Math.Abs(direction.X) > Math.Abs(direction.Y)
                                ? direction.X > 0 ? "right" : "left"
                                : direction.Y < 0 ? "up" : "down";
                            if (character.GetNode<AnimatedSprite2D>("AnimatedSprite2D").Animation != $"run_{facing}")
                                return Fail("人物朝向未跟随原生路线当前段，或共线节点造成错误转向");
                        }
                        if (frame == 2 && movement.Path.Points.Count > 2)
                        {
                            routedIntervals++;
                            Vector2 chord = movement.Path.GetPositionAtDistance(movement.FromDistance).ToVector2()
                                .Lerp(movement.Path.GetPositionAtDistance(movement.ToDistance).ToVector2(), .5f);
                            if (!chord.IsEqualApprox(expected)) cornerIntervals++;
                        }
                    }
                }
            }
            if (routedIntervals == 0 || cornerIntervals == 0) return Fail("真实路线未覆盖一秒内转角，不能证明消除直线切水");
            foreach (double rate in new[] { .5, 1, 2, 16 })
            {
                driver.SetDevelopmentRate(rate);
                double before = map.WaterAnimationSeconds;
                map._Process(.25);
                if (Math.Abs(map.WaterAnimationSeconds - before - .25 * rate) > 1e-9)
                    return Fail("水纹未跟随唯一经营倍率");
            }
            game.SetPaused(true);
            double paused = map.WaterAnimationSeconds;
            map._Process(2);
            workers._Process(2);
            if (map.WaterAnimationSeconds != paused) return Fail("暂停时水纹继续推进");
            foreach (WorkerSnapshot worker in game.GetWorkers())
                if (!workers.GetNode<NpcCharacter>($"Worker{worker.WorkerNumber}").GlobalPosition
                    .IsEqualApprox(map.GetGridWorldPosition(worker.GridPosition))) return Fail("暂停未对齐真实位置");
            game.SetPaused(false);
            game.AdvanceTicks(5);
            workers._Process(.01);
            foreach (WorkerSnapshot worker in game.GetWorkers())
                if (!workers.GetNode<NpcCharacter>($"Worker{worker.WorkerNumber}").GlobalPosition
                    .IsEqualApprox(map.GetGridWorldPosition(worker.GridPosition))) return Fail("批量跳秒仍回播旧路线");
            return true;
        }
        finally
        {
            map.QueueFree();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
