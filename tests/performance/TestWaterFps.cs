using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Terrain;
using FarmExchange.UI;
using FarmExchange.World;
using FarmExchange.Workers;

public partial class TestWaterFps : Node
{
    private Main _main = null!;
    private WorldMap _map = null!;
    private string _directory = null!;
    private string _stage = "地图准备";
    private readonly List<object> _samples = new();
    private readonly List<object> _fpsFailures = new();
    private readonly List<object> _roadChecks = new();
    private string? _firstFailureStage;
    private string? _firstFailureImage;

    public override async void _Ready()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("含水 FPS 场景需要真实图形窗口");
            GetWindow().Size = new(1920, 1080);
            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            _directory = ProjectSettings.GlobalizePath("res://build/issue110-validation/water-performance");
            Directory.CreateDirectory(_directory);
            int? previousSeed = Main.InitialSeed;
            var timer = Stopwatch.StartNew();
            try
            {
                Main.InitialSeed = 12345;
                _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
                AddChild(_main);
                if (_main.ReadyTask.IsCompleted || _main.AdvanceSimulation(10) != 0 ||
                    _main.GetNode<Camera2D>("Camera2D").ProcessMode != ProcessModeEnum.Disabled ||
                    !_main.GetNode<Label>("CanvasLayer/UiRoot/MapPreparationLabel").IsVisibleInTree())
                    throw new InvalidOperationException("含水局没有先等待导航准备，或准备期间允许经营/镜头交互");
                await _main.ReadyTask;
                if (_main.Game.Calendar.ElapsedSeconds != 0)
                    throw new InvalidOperationException("导航准备期间已推进经营秒");
            }
            finally { Main.InitialSeed = previousSeed; }
            double creationMs = timer.Elapsed.TotalMilliseconds;
            _stage = "近景与暂停";
            _map = _main.GetNode<WorldMap>("WorldMap");
            _main.Game.SetPaused(true);
            await Frames(5);
            CheckNearPond();
            Save("before.png");
            double waterBefore = _map.WaterAnimationSeconds;
            uint secondBefore = _main.Game.Calendar.ElapsedSeconds;
            await Frames(20);
            if (_map.WaterAnimationSeconds != waterBefore || _main.Game.Calendar.ElapsedSeconds != secondBefore)
                throw new InvalidOperationException("暂停仍推进水纹或经营");
            Save("paused.png");
            _main.SetProcess(false);
            _stage = "水纹真实像素";
            try { await TestWaterRippleRendering.RunChecksAsync(this, _main.Game, _main.Driver, _directory); }
            finally { _main.SetProcess(true); }
            _stage = "真实道路偏好与建拆同步";
            await CheckRoadPreference();
            _stage = "长途路线准备";
            foreach (var building in _main.Game.GetBuildingSpaces()) _main.Game.RemoveBuilding(building.AnchorCell);
            foreach (int x in new[] { 2, 190, 378 })
            {
                Vector2I target = new(x, 380);
                if (_main.Game.BuildFarm(target) != null) throw new InvalidOperationException($"含水长途负载目标无效：{target}");
            }
            _map.SyncFromGame();
            _main.Game.SetPaused(false);
            await Frames(2);
            bool passed = true;
            foreach (double rate in new[] { 1.0, 2.0, .5, 16.0 })
            {
                _stage = $"{rate}× FPS";
                _main.Driver.SetDevelopmentRate(rate);
                passed &= await Measure(rate);
            }
            Save("after.png");
            File.WriteAllText(Path.Combine(_directory, "performance.json"), JsonSerializer.Serialize(new
            {
                Scenario = "正式48池塘与默认近景、真实建拆道路偏好及工人沿路、三工人跨地图完整绕水路线、真实Main/UI、水纹、暂停及四倍率",
                CreationMilliseconds = creationMs,
                PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                Window = GetWindow().Size.ToString(),
                Renderer = RenderingServer.GetCurrentRenderingMethod(),
                Samples = _samples,
                RoadPreference = _roadChecks,
                Passed = passed,
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!passed) SaveFailureReport();
            GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception error)
        {
            if (_directory != null)
            {
                Save(_fpsFailures.Count == 0 ? "failure.png" : "exception.png");
                SaveFailureReport(error);
            }
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task CheckRoadPreference()
    {
        FarmGame game = _main.Game;
        LandPosition start = LandPosition.FromCell(new Vector2I(190, 190));
        LandPosition target = LandPosition.FromCell(new Vector2I(202, 190));
        Vector2I farm = new(201, 189);
        Vector2I[] roads = Enumerable.Range(190, 13).Select(x => new Vector2I(x, 187))
            .Concat(Enumerable.Range(188, 3).Select(y => new Vector2I(190, y)))
            .Append(new Vector2I(202, 188)).ToArray();
        _main.SetProcess(false);
        try
        {
            foreach (var building in game.GetBuildingSpaces()) game.RemoveBuilding(building.AnchorCell);
            if (game.BuildFarm(farm) != null || game.SetFarmCrop(farm, CropKind.Wheat) != null)
                throw new InvalidOperationException("道路图形夹具无法真实建造并选择当季小麦");
            LandPath original = CompletePath(game.Terrain.FindLandPath(start, target), start, target);
            RecordRoadCheck(new { Stage = "建路前", Points = original.Points.Select(point => new { point.X, point.Y }) });
            _map.SyncFromGame();
            await Frames(5);
            Save("roads-without-before.png");
            long moneyBefore = game.AvailableMoneyCents;
            RoadPlacementResult placed = game.TryPlaceRoads(roads, RoadMaterial.Dirt);
            if (!placed.Success || placed.CellCount != 17 || placed.ChargedCents != 1700 || game.IsNavigationReady)
                throw new InvalidOperationException("真实17格道路建造、扣费或导航准备状态不符");
            uint beforePreparation = game.Calendar.ElapsedSeconds;
            await game.WaitForNavigationAsync(GetTree());
            if (!game.IsNavigationReady || game.Calendar.ElapsedSeconds != beforePreparation)
                throw new InvalidOperationException("道路正常同步未完成，或等待时推进经营");
            LandPath alongRoad = CompletePath(game.Terrain.FindLandPath(start, target), start, target);
            if (alongRoad.Equals(original) || alongRoad.Points.Min(point => point.Y) > 188.5)
                throw new InvalidOperationException("原生成本未在固定中心U形道路上形成可见绕路偏好");
            RecordRoadCheck(new { Stage = "建路同步后", Points = alongRoad.Points.Select(point => new { point.X, point.Y }) });
            _map.SyncFromGame();
            await Frames(5);
            Save("roads-with-before-moving.png");
            game.SetPaused(false);
            _main.Driver.SetDevelopmentRate(1);
            var movement = new List<object>();
            Node2D character = _main.FindChild("Worker1", true, false) as Node2D ??
                throw new InvalidOperationException("道路图形夹具缺少真实WorkerPresentation人物");
            Vector2 visualBefore = character.GlobalPosition;
            bool observedRoadMovement = false;
            uint lastSecond = game.Calendar.ElapsedSeconds;
            ulong began = Time.GetTicksUsec(), previous = began;
            while (Time.GetTicksUsec() - began < 4_000_000)
            {
                await Frames(1);
                ulong now = Time.GetTicksUsec();
                _main.AdvanceSimulation((now - previous) / 1_000_000.0);
                previous = now;
                WorkerSnapshot worker = game.GetWorkers()[0];
                if (game.Calendar.ElapsedSeconds == lastSecond) continue;
                lastSecond = game.Calendar.ElapsedSeconds;
                movement.Add(new { Second = lastSecond, worker.Activity, worker.ExactPosition.X, worker.ExactPosition.Y });
                if (worker.Activity == WorkerActivity.Moving && worker.Path?.Equals(alongRoad) == true &&
                    roads.Any(cell => Math.Abs(worker.ExactPosition.X - cell.X) <= .5001 &&
                        Math.Abs(worker.ExactPosition.Y - cell.Y) <= .5001))
                    observedRoadMovement = true;
            }
            Save("roads-with-moving.png");
            RecordRoadCheck(new
            {
                Stage = "真实人物移动后",
                ActualWorkerMovement = movement,
                ObservedRoadMovement = observedRoadMovement,
                VisualDisplacementPixels = character.GlobalPosition.DistanceTo(visualBefore)
            });
            if (!observedRoadMovement || character.GlobalPosition.DistanceTo(visualBefore) < 1)
                throw new InvalidOperationException("未观察到真实工人沿已确认道路行程移动及人物节点位移");
            game.SetPaused(true);
            RoadRemovalResult removed = game.TryRemoveRoads(roads);
            if (!removed.Success || removed.RemovedCount != 17 || game.IsNavigationReady)
                throw new InvalidOperationException("道路拆除或导航准备状态不符");
            await game.WaitForNavigationAsync(GetTree());
            LandPath afterRemoval = CompletePath(game.Terrain.FindLandPath(start, target), start, target);
            if (afterRemoval.Points.Any(point => Math.Abs(point.Y - 190) > 1d / 4096))
                throw new InvalidOperationException("拆路同步后同端点仍保留道路绕行");
            RecordRoadCheck(new
            {
                Stage = "拆路同步后",
                Points = afterRemoval.Points.Select(point => new { point.X, point.Y }),
                ActualWorkerMovement = movement,
                ObservedRoadMovement = observedRoadMovement
            });
            _map.SyncFromGame();
            await Frames(5);
            Save("roads-without-after.png");
            game.SetPaused(false);
            // 用实际播种、供水、成熟和出售支付后续三座远途田，不写入测试金币。
            SimulationAdvanceResult grown = game.AdvanceTicks(1000);
            SaleResult sale = game.SellRaw(CropKind.Wheat);
            if (grown.AdvancedTicks != 1000 || !sale.Success || sale.Quantity == 0 || game.AvailableMoneyCents < 3000)
                throw new InvalidOperationException($"道路夹具真实生产出售不足以建造三座远途田：数量={sale.Quantity}，可用分={game.AvailableMoneyCents}");
            game.SetPaused(true);
            RecordRoadCheck(new
            {
                Stage = "真实经营与出售完成",
                RoadMaterial = "Dirt",
                RoadCost = 1,
                PlainCost = 2,
                SpeedCellsPerSecond = 3,
                Roads = roads.Select(cell => new { cell.X, cell.Y }),
                placed.ChargedCents,
                MoneyBefore = moneyBefore,
                Original = original.Points.Select(point => new { point.X, point.Y }),
                AlongRoad = alongRoad.Points.Select(point => new { point.X, point.Y }),
                AfterRemoval = afterRemoval.Points.Select(point => new { point.X, point.Y }),
                ActualWorkerMovement = movement,
                ObservedRoadMovement = observedRoadMovement,
                VisualDisplacementPixels = character.GlobalPosition.DistanceTo(visualBefore),
                ProductionTicks = grown.AdvancedTicks,
                sale.Quantity,
                sale.RevenueCents,
                MoneyAfterProductionCents = game.AvailableMoneyCents,
                Passed = true,
            });
        }
        finally { _main.SetProcess(true); }
    }

    private void RecordRoadCheck(object fact)
    {
        _roadChecks.Add(fact);
        File.WriteAllText(Path.Combine(_directory, "road-preference.json"), JsonSerializer.Serialize(_roadChecks,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static LandPath CompletePath(LandPathResult result, LandPosition start, LandPosition target)
    {
        if (result.Status != LandPathStatus.Success || result.Path is not LandPath path ||
            path.Points[0] != start || path.Points[^1] != target)
            throw new InvalidOperationException("道路图形夹具没有返回完整真实端点路线");
        return path;
    }

    private async Task<bool> Measure(double rate)
    {
        ulong warmup = Time.GetTicksUsec();
        ulong warmupUsec = rate == 16 ? 500_000UL : 2_000_000UL;
        ulong sampleUsec = rate == 16 ? 2_000_000UL : 3_000_000UL;
        while (Time.GetTicksUsec() - warmup < warmupUsec) await Frames(1);
        var frames = new List<double>();
        ulong start = Time.GetTicksUsec(), previous = start;
        uint startSecond = _main.Game.Calendar.ElapsedSeconds;
        int drawn = Engine.GetFramesDrawn();
        double water = _map.WaterAnimationSeconds;
        int pathPoints = _main.Game.GetWorkers().Sum(worker => worker.Path?.Points.Count ?? 0);
        int movingRoutedWorkers = _main.Game.GetWorkers().Count(worker =>
            worker.Activity == FarmExchange.Workers.WorkerActivity.Moving && HasVisibleTurn(worker.Path));
        int minimumMovingRoutedWorkers = movingRoutedWorkers;
        while (Time.GetTicksUsec() - start < sampleUsec)
        {
            await Frames(1);
            ulong now = Time.GetTicksUsec();
            frames.Add((now - previous) / 1000.0);
            minimumMovingRoutedWorkers = Math.Min(minimumMovingRoutedWorkers, _main.Game.GetWorkers().Count(worker =>
                worker.Activity == FarmExchange.Workers.WorkerActivity.Moving && HasVisibleTurn(worker.Path)));
            previous = now;
        }
        double seconds = (previous - start) / 1_000_000.0;
        frames.Sort();
        double fps = (Engine.GetFramesDrawn() - drawn) / seconds;
        double p95 = frames[(int)Math.Ceiling(frames.Count * .95) - 1];
        bool progressed = _main.Game.Calendar.ElapsedSeconds > startSecond && _map.WaterAnimationSeconds > water;
        int movingRoutedWorkersAtEnd = _main.Game.GetWorkers().Count(worker =>
            worker.Activity == FarmExchange.Workers.WorkerActivity.Moving && HasVisibleTurn(worker.Path));
        bool passed = fps >= 60 && p95 <= 1000.0 / 60 && progressed && minimumMovingRoutedWorkers == 3 && movingRoutedWorkersAtEnd == 3;
        var sample = new
        {
            Rate = rate,
            AverageFps = fps,
            P95FrameMs = p95,
            Seconds = seconds,
            WarmupSeconds = warmupUsec / 1_000_000.0,
            PathPoints = pathPoints,
            MovingRoutedWorkers = movingRoutedWorkers,
            MovingRoutedWorkersAtEnd = movingRoutedWorkersAtEnd,
            MinimumMovingRoutedWorkers = minimumMovingRoutedWorkers,
            SimulationTicks = _main.Game.Calendar.ElapsedSeconds - startSecond,
            WaterSeconds = _map.WaterAnimationSeconds - water,
            Passed = passed
        };
        _samples.Add(sample);
        string image = $"rate-{rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}.png";
        Save(image);
        if (!passed)
        {
            _fpsFailures.Add(new { Rate = rate, Stage = _stage, Image = image, Measurement = sample });
            if (_fpsFailures.Count == 1)
            {
                _firstFailureStage = _stage;
                _firstFailureImage = image;
                // 保留首个失败倍率的同一真实帧，不以矩阵后来阶段的图替代。
                File.Copy(Path.Combine(_directory, image), Path.Combine(_directory, "failure.png"), overwrite: true);
            }
        }
        GD.Print($"含水 {rate}×：FPS={fps:F1}，P95={p95:F2}ms，路径节点={pathPoints}，通过={passed}");
        return passed;
    }

    private void SaveFailureReport(Exception? error = null)
    {
        File.WriteAllText(Path.Combine(_directory, "failure.json"), JsonSerializer.Serialize(new
        {
            Scenario = "正式含水1080P场景",
            Stage = _firstFailureStage ?? _stage,
            Passed = false,
            Error = error?.ToString(),
            ExceptionStage = error == null ? null : _stage,
            FailureImage = "failure.png",
            FailureImageSource = _firstFailureImage,
            FailureImageRecordedAt = _firstFailureImage == null ? "场景异常时当前帧" : "首个失败倍率的测量末帧",
            ExceptionImage = error == null ? null : _fpsFailures.Count == 0 ? "failure.png" : "exception.png",
            FailedFpsSamples = _fpsFailures,
            CompletedFpsSamples = _samples,
            CompletedRoadChecks = _roadChecks,
            Window = GetWindow().Size.ToString(),
            Renderer = RenderingServer.GetCurrentRenderingMethod(),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void CheckNearPond()
    {
        var centers = new List<Vector2>();
        for (int y = 201; y < 218; y++)
            for (int x = 182; x < 202; x++)
                if (!_main.Game.Terrain.IsFullLand(new(x, y)))
                    centers.Add(_map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(new(x, y)));
        if (centers.Count == 0 || centers.Any(p => p.X < 0 || p.Y < 0 || p.X >= 1920 || p.Y >= 1080))
            throw new InvalidOperationException("默认1080P近景池塘不完整可见");
        foreach (string name in new[] { "ActivityLayout", "BottomBar", "CalendarPanel", "ResourcesPanel" })
        {
            var control = (Control)_main.FindChild(name, true, false);
            if (control.IsVisibleInTree() && centers.Any(p => control.GetGlobalRect().HasPoint(p)))
                throw new InvalidOperationException($"近景池塘被真实UI遮挡：{name}");
        }
    }

    private async Task Frames(int count)
    {
        for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static bool HasVisibleTurn(LandPath? path)
    {
        if (path == null) return false;
        for (int index = 2; index < path.Points.Count; index++)
        {
            Vector2 incoming = path.Points[index - 1].ToVector2() - path.Points[index - 2].ToVector2();
            Vector2 outgoing = path.Points[index].ToVector2() - path.Points[index - 1].ToVector2();
            if (incoming != Vector2.Zero && outgoing != Vector2.Zero && incoming.Normalized().Dot(outgoing.Normalized()) < .999f)
                return true;
        }
        return false;
    }

    private void Save(string filename) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_directory, filename));
}
