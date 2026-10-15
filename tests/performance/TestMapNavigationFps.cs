using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;
using FarmExchange.Workers;
using static TestMapInput;

public partial class TestMapNavigationFps : Node
{
    private Main _main = null!;
    private MapNavigation _maps = null!;
    private MapNavigationCanvas _mini = null!;
    private MapNavigationCanvas _atlas = null!;
    private readonly List<double> _frames = new();
    private ulong _started;
    private ulong _sampleStarted;
    private ulong _lastFrame;
    private ulong _lastInput;
    private int _drawnAtStart;
    private int _miniNavigations;
    private int _atlasNavigations;
    private int _zoomEvents;
    private int _panEvents;
    private uint _initialTicks;
    private string _directory = null!;
    private int _operation;

    public override async void _Ready()
    {
        try { await Prepare(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Prepare()
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("大小地图FPS必须使用真实1080P窗口");
        GetWindow().Size = new Vector2I(1920, 1080);
        _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        _main.InitialTerrainRules = TerrainTestFixtures.AllLandRules;
        AddChild(_main);
        await _main.ReadyTask;
        _main.Game.FillWorldForPresentationBenchmark();
        _main.GetNode<WorldMap>("WorldMap").SyncFromGame();
        _maps = Find<MapNavigation>(_main, "MapNavigation");
        _maps.RefreshMap();
        _mini = Find<MapNavigationCanvas>(_maps, "MiniMap");
        _atlas = Find<MapNavigationCanvas>(_maps, "AtlasMap");
        _maps.NavigateRequested += _ =>
        {
            if (_sampleStarted == 0) return;
            if (_maps.IsExpanded) _atlasNavigations++; else _miniNavigations++;
        };
        GetWindow().GrabFocus();
        await Frames(this);
        if (!GetWindow().HasFocus()) throw new InvalidOperationException("大小地图性能输入夹具未取得真实窗口焦点");
        ProcessPriority = 3;
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        _directory = ProjectSettings.GlobalizePath("res://build/issue153-validation/navigation-performance");
        Directory.CreateDirectory(_directory);
        _started = Time.GetTicksUsec();
        GD.Print("大小地图FPS：1080P真实Main、16,384生产实例、经营继续；2秒预热，8秒连续小图导航/大图平移缩放定位");
    }

    public override void _Process(double delta)
    {
        if (_started == 0) return;
        try { Advance(); }
        catch (Exception error) { _started = 0; GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Advance()
    {
        ulong now = Time.GetTicksUsec();
        double elapsed = (now - _started) / 1_000_000.0;
        bool expanded = elapsed >= 6;
        if (_maps.IsExpanded != expanded) Key(_main, Godot.Key.M);
        if (now - _lastInput >= 120_000)
        {
            _lastInput = now;
            _operation++;
            if (!expanded)
            {
                Vector2 local = MapCoordinates.GridPositionToLocal(new Vector2(192 + (float)Math.Sin(elapsed) * 24, 192));
                Click(_main, TestMapNavigation.Point(_mini, local));
            }
            else
            {
                Vector2 center = _atlas.GetGlobalRect().GetCenter();
                // 实际滚轮每四步一档，其间真实短按与左右键拖动，持续重绘真实视野框。
                switch (_operation % 4)
                {
                    case 0:
                        float previousZoom = _atlas.ViewZoom;
                        Wheel(_main, center + new Vector2(70, 20), _atlas.ViewZoom < 2.4f);
                        if (_atlas.ViewZoom != previousZoom) _zoomEvents++;
                        break;
                    case 1:
                        Click(_main, center + new Vector2((float)Math.Sin(elapsed) * 80, 10));
                        break;
                    default:
                        MouseButton button = _operation % 4 == 2 ? MouseButton.Left : MouseButton.Right;
                        Vector2 previousCenter = _atlas.ViewCenter;
                        MouseButtonEvent(_main, center, button, true);
                        Move(_main, center + new Vector2((float)Math.Sin(elapsed) * 28, 10),
                            button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right);
                        MouseButtonEvent(_main, center + new Vector2((float)Math.Sin(elapsed) * 28, 10), button, false);
                        if (_atlas.ViewCenter != previousCenter) _panEvents++;
                        break;
                }
            }
        }
        if (now - _started < 2_000_000) return;
        if (_sampleStarted == 0)
        {
            Save("before-minimap.png");
            _initialTicks = _main.Game.Calendar.ElapsedSeconds;
            _sampleStarted = _lastFrame = Time.GetTicksUsec();
            _drawnAtStart = Engine.GetFramesDrawn();
            return;
        }
        _frames.Add((now - _lastFrame) / 1000.0);
        _lastFrame = now;
        if (now - _sampleStarted < 8_000_000) return;
        double seconds = (now - _sampleStarted) / 1_000_000.0;
        int rendered = Engine.GetFramesDrawn() - _drawnAtStart;
        _frames.Sort();
        double fps = rendered / seconds;
        double p95 = _frames[(int)Math.Ceiling(_frames.Count * .95) - 1];
        uint ticks = _main.Game.Calendar.ElapsedSeconds - _initialTicks;
        int entities = _main.Game.GetBuildingSpaces().Count;
        int moving = _main.Game.GetWorkers().Count(worker => worker.Activity == WorkerActivity.Moving);
        bool passed = fps >= 60 && p95 <= 1000.0 / 60 && entities == 16384 && ticks > 0 &&
            _miniNavigations >= 10 && _atlasNavigations >= 4 && _zoomEvents >= 4 && _panEvents >= 8 &&
            _maps.IsExpanded && !_main.Game.IsPaused && _main.IsProcessing() && rendered > 0;
        Save("after-atlas.png");
        File.WriteAllText(Path.Combine(_directory, "performance.json"), JsonSerializer.Serialize(new
        {
            Scenario = "1080P真实主场景、满地图16,384生产实例；采样前半段小地图连续定位，后半段大地图连续短按定位、左右拖动和鼠标锚点滚轮缩放；经营及世界表现保持运行",
            EntityCount = entities,
            OccupiedCellCount = FarmGame.MapSize * FarmGame.MapSize,
            WorkerCount = _main.Game.GetWorkers().Count,
            MovingWorkersAtEnd = moving,
            TickCount = ticks,
            MiniNavigations = _miniNavigations,
            AtlasNavigations = _atlasNavigations,
            WheelEvents = _zoomEvents,
            PanEvents = _panEvents,
            WarmupSeconds = 2,
            SampleSeconds = seconds,
            RenderedFrames = rendered,
            AverageFps = fps,
            P95FrameMs = p95,
            WindowSize = GetWindow().Size.ToString(),
            Renderer = RenderingServer.GetVideoAdapterName(),
            Cpu = OS.GetProcessorName(),
            Godot = Engine.GetVersionInfo()["string"].ToString(),
            Passed = passed,
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print(FormattableString.Invariant($"大小地图FPS：平均{fps:F1}，P95 {p95:F2}ms，报告{_directory}"));
        if (!passed) GD.PushError("大小地图密集导航/缩放没有达到帧率、实际操作次数或经营继续条件");
        _started = 0;
        GetTree().Quit(passed ? 0 : 1);
    }

    private void Save(string name)
    {
        if (GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_directory, name)) != Error.Ok)
            throw new IOException("大小地图性能截图保存失败：" + name);
    }
}
