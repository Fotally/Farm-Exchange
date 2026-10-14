using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Time;
using FarmExchange.UI;
using FarmExchange.Weather;
using FarmExchange.World;

public partial class TestWeatherFps : Node
{
    private const ulong WarmupUsec = 2_000_000;
    private const ulong SampleUsec = 8_000_000;
    private readonly List<double> _frameTimesMs = new();
    private Main _main = null!;
    private WorldMap _map = null!;
    private Camera2D _camera = null!;
    private string _directory = null!;
    private double _rate = 1;
    private bool _baseline;
    private bool _stageProbe;
    private ulong _stageStart;
    private ulong _previous;
    private int _stage;
    private int _startFrames;
    private uint _startTicks;
    private int _maximumRainSamples;
    private Image _before = null!;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PushError("天气图形性能场景需要窗口，不能使用 --headless");
            GetTree().Quit(1);
            return;
        }
        GetWindow().Size = new Vector2I(1920, 1080);
        string[] args = OS.GetCmdlineUserArgs();
        _baseline = Array.IndexOf(args, "--weather-baseline") >= 0;
        _stageProbe = Array.IndexOf(args, "--weather-stage-probe") >= 0;
        if (_baseline && _stageProbe)
        {
            GD.PushError("阶段测量使用真实云雨三区，不能同时指定 --weather-baseline");
            GetTree().Quit(1);
            return;
        }
        int index = Array.IndexOf(args, "--weather-rate");
        if (index >= 0)
        {
            if (index + 1 >= args.Length || !double.TryParse(args[index + 1], NumberStyles.Float,
                CultureInfo.InvariantCulture, out _rate) || !SimulationDriver.IsDevelopmentRateAllowed(_rate))
            {
                GD.PushError("--weather-rate 需指定有效公共或开发倍率");
                GetTree().Quit(1);
                return;
            }
        }
        if (_stageProbe) _rate = 16;
        WeatherMode previousMode = Main.InitialWeatherMode;
        int? previousSeed = Main.InitialSeed;
        Main.InitialWeatherMode = WeatherMode.Explicit;
        Main.InitialSeed = 34;
        try
        {
            _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            _main.InitialTerrainRules = TerrainTestFixtures.AllLandRules;
            AddChild(_main);
        }
        finally { Main.InitialWeatherMode = previousMode; Main.InitialSeed = previousSeed; }
        _main.Game.FillWorldForPresentationBenchmark();
        _map = _main.GetNode<WorldMap>("WorldMap");
        _map.SyncFromGame();
        _camera = _main.GetNode<Camera2D>("Camera2D");
        _camera.GlobalPosition = _map.GetGridWorldPosition(new Vector2(191.5f, 191.5f));
        _camera.ForceUpdateScroll();
        _main.Game.SetPaused(true);
        _main.Driver.SetDevelopmentRate(_rate);
        _map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(186, 189));
        ProcessPriority = 3;
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        string rateName = _rate.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', '-');
        _directory = ProjectSettings.GlobalizePath($"res://coverage/weather-{rateName}x{(_baseline ? "-clear" : "")}");
        Directory.CreateDirectory(_directory);
        if (_stageProbe)
        {
            SetProcess(false);
            _main.SetProcess(false);
            await RunStageProbe();
            return;
        }
        _stageStart = Time.GetTicksUsec();
    }

    private async Task RunStageProbe()
    {
        if (!_main.Game.TrySetWeatherRegion(new Rect2I(96, 96, 96, 192), WeatherKind.Rain, out _) ||
            !_main.Game.TrySetWeatherRegion(new Rect2I(192, 96, 96, 192), WeatherKind.Cloudy, out _) ||
            !_main.Game.TrySetWeatherRegion(new Rect2I(96, 288, 192, 96), WeatherKind.Rain, out _))
        { Fail("阶段测量天气区域夹具失败"); return; }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _main.Game.SetPaused(false);
        uint initialSeconds = _main.Game.Calendar.ElapsedSeconds;
        Action gameAdvance = () => _main.Game.AdvanceTicks(1);
        Action mapSync = () => _map.SyncFromGame();
        Action mainAdvance = () =>
        {
            if (_main.AdvanceSimulation(1.0 / 16) != 1)
                throw new InvalidOperationException("16倍阶段样本没有恰好推进一个经营秒");
        };
        // JIT、首次区域判定和节点配置预热在样本之外；只额外推进两个经营秒。
        gameAdvance();
        mapSync();
        mainAdvance();
        uint warmedSeconds = _main.Game.Calendar.ElapsedSeconds;
        var stages = new[]
        {
            MeasureStage("Game.AdvanceTicks(1)", gameAdvance),
            MeasureStage("WorldMap.SyncFromGame()", mapSync),
            MeasureStage("Main.AdvanceSimulation(1/16)", mainAdvance),
        };
        uint finalSeconds = _main.Game.Calendar.ElapsedSeconds;
        bool passed = finalSeconds - initialSeconds == 18 && finalSeconds <= 26 &&
            _main.Game.ActiveWeatherRegions.Count == 3 && _main.Game.GetBuildingSpaces().Count == 16384;
        string directory = ProjectSettings.GlobalizePath("res://coverage/weather-stage-probe");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(new
        {
            Scenario = "1080P真实云雨三区、满地图、固定中心镜头，自动Main驱动停止后的公开接口阶段测量",
            Seed = 34,
            Rate = 16,
            SamplesPerStage = 8,
            Order = new[] { "纯经营8次", "地图同步8次（不推进经营）", "完整Main推进及刷新8次" },
            InitialElapsedSeconds = initialSeconds,
            WarmedElapsedSeconds = warmedSeconds,
            FinalElapsedSeconds = finalSeconds,
            Warmup = "布局2帧后，各接口预热1次；预热和各阶段GC均在采样外",
            AllocationScope = "GC.GetAllocatedBytesForCurrentThread，仅当前调用线程，不含原生节点/GPU及后台日志线程",
            TimingScope = "各调用内Stopwatch单调时间，布局/GC/报告序列化均在调用计时外；不测渲染帧率",
            ActiveRegions = _main.Game.ActiveWeatherRegions.Count,
            EntityCount = _main.Game.GetBuildingSpaces().Count,
            Stages = stages,
            Passed = passed,
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var stage in stages)
            GD.Print(FormattableString.Invariant($"阶段 {stage.Name}：平均 {stage.AverageMs:F3} ms，平均分配 {stage.AverageAllocatedBytes:F0} B"));
        if (!passed) GD.PushError("阶段测量负载、经营秒数或区域证据不符合设定");
        GetTree().Quit(passed ? 0 : 1);
    }

    private static StageMeasurement MeasureStage(string name, Action operation)
    {
        var elapsedMs = new double[8];
        var allocatedBytes = new long[8];
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        double totalMs = 0;
        long totalBytes = 0;
        for (int sample = 0; sample < elapsedMs.Length; sample++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            operation();
            long ended = Stopwatch.GetTimestamp();
            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            elapsedMs[sample] = (ended - started) * 1000.0 / Stopwatch.Frequency;
            allocatedBytes[sample] = allocatedAfter - allocatedBefore;
            totalMs += elapsedMs[sample];
            totalBytes += allocatedBytes[sample];
        }
        return new StageMeasurement(name, elapsedMs, allocatedBytes,
            totalMs / elapsedMs.Length, totalBytes / (double)allocatedBytes.Length);
    }

    private sealed record StageMeasurement(string Name, double[] ElapsedMs, long[] AllocatedBytes,
        double AverageMs, double AverageAllocatedBytes);

    public override void _Process(double delta)
    {
        if (_stageStart == 0) return;
        ulong now = Time.GetTicksUsec();
        if (_stage == 0)
        {
            if (now - _stageStart < WarmupUsec) return;
            _before = Capture();
            _before.SavePng(Path.Combine(_directory, "before-sunny.png"));
            if (!_baseline && (!_main.Game.TrySetWeatherRegion(new Rect2I(96, 96, 96, 192), WeatherKind.Rain, out _) ||
                !_main.Game.TrySetWeatherRegion(new Rect2I(192, 96, 96, 192), WeatherKind.Cloudy, out _) ||
                !_main.Game.TrySetWeatherRegion(new Rect2I(96, 288, 192, 96), WeatherKind.Rain, out _)))
            { Fail("图形天气区域夹具失败"); return; }
            _main.Game.SetPaused(false);
            _map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(186, 189));
            _stage = 1;
            _stageStart = Time.GetTicksUsec();
            return;
        }
        if (_stage == 1)
        {
            if (now - _stageStart < WarmupUsec) return;
            Image after = Capture();
            after.SavePng(Path.Combine(_directory, _baseline ? "after-sunny.png" : "after-cloud-rain.png"));
            // 固定纸面内部区域不含日期/余额等动态文字，证明世界云影不染暗UI。
            if (!SamePixels(_before, after, new Rect2I(60, 60, 120, 20)))
            { Fail("天气云影染暗了独立CanvasLayer界面"); return; }
            if (_main.FindChild("FarmCurrentWeather", true, false) is not Label label ||
                label.Text != (_baseline ? "当前天气：晴天" : "当前天气：降雨"))
            { Fail("详情没有显示本场景真实整田天气"); return; }
            _stage = 2;
            _stageStart = Time.GetTicksUsec();
            _previous = _stageStart;
            _startFrames = Engine.GetFramesDrawn();
            _startTicks = _main.Game.Calendar.ElapsedSeconds;
            return;
        }
        WeatherPresentation weather = _map.GetNode<WeatherPresentation>("Weather");
        if (_baseline && (_main.Game.ActiveWeatherRegions.Count != 0 ||
            weather.VisibleRegionCount != 0 || weather.RainSamples.Count != 0))
        { Fail("晴天对照场景存在天气区域、云影或雨点"); return; }
        _maximumRainSamples = Math.Max(_maximumRainSamples, weather.RainSamples.Count);
        foreach (var drop in weather.RainSamples)
            if (_main.Game.GetWeather(MapCoordinates.LocalPositionToCell(drop.Landing)) != WeatherKind.Rain)
            { Fail("图形采样发现越出真实雨区的落点"); return; }
        _frameTimesMs.Add((now - _previous) / 1000.0);
        _previous = now;
        if (now - _stageStart < SampleUsec) return;
        double duration = (now - _stageStart) / 1_000_000.0;
        int rendered = Engine.GetFramesDrawn() - _startFrames;
        _frameTimesMs.Sort();
        double fps = rendered / duration;
        double p95 = Percentile(.95);
        uint ticks = _main.Game.Calendar.ElapsedSeconds - _startTicks;
        bool weatherEvidence = _baseline ? _maximumRainSamples == 0 && _main.Game.ActiveWeatherRegions.Count == 0 :
            _maximumRainSamples is > 0 and <= 180 && _main.Game.ActiveWeatherRegions.Count == 3;
        bool passed = fps >= 60 && p95 <= 1000.0 / 60 && rendered > 0 && ticks > 0 &&
            weatherEvidence && GetWindow().Size == new Vector2I(1920, 1080);
        File.WriteAllText(Path.Combine(_directory, "performance.json"), JsonSerializer.Serialize(new
        {
            Scenario = "1080P 满地图主场景，真实详情与独立UI；固定中心镜头；" +
                (_baseline ? "相同输入的全天晴对照" : "真实显式雨/云/离屏雨三区"),
            WeatherBaseline = _baseline,
            Rate = _rate,
            WindowWidth = GetWindow().Size.X,
            WindowHeight = GetWindow().Size.Y,
            WarmupSeconds = 2,
            SampleSeconds = duration,
            RenderedFrames = rendered,
            AverageFps = fps,
            P95FrameMs = p95,
            P99FrameMs = Percentile(.99),
            TickCount = ticks,
            MaximumRainSamples = _maximumRainSamples,
            ActiveRegions = _main.Game.ActiveWeatherRegions.Count,
            EntityCount = _main.Game.GetBuildingSpaces().Count,
            ScreenshotSampling = "两次截图在采样开始前完成，终图在采样结束后保存",
            UiUnchanged = true,
            RainLandingsInsideActualRegion = true,
            Renderer = RenderingServer.GetVideoAdapterName(),
            Cpu = OS.GetProcessorName(),
            Godot = Engine.GetVersionInfo()["string"].ToString(),
            Passed = passed,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Capture().SavePng(Path.Combine(_directory, _baseline ? "end-sunny.png" : "end-cloud-rain.png"));
        GD.Print(FormattableString.Invariant($"天气 {_rate}×：平均 {fps:F1} FPS，P95 {p95:F2} ms；{_directory}"));
        if (!passed) GD.PushError("天气图形性能或真实状态证据未达门槛：平均>=60 FPS，P95<=16.67 ms");
        GetTree().Quit(passed ? 0 : 1);
        _stageStart = 0;
    }

    private Image Capture() => GetViewport().GetTexture().GetImage();
    private double Percentile(double fraction) =>
        _frameTimesMs[Math.Clamp((int)Math.Ceiling(_frameTimesMs.Count * fraction) - 1, 0, _frameTimesMs.Count - 1)];
    private void Fail(string message) { GD.PushError(message); GetTree().Quit(1); _stageStart = 0; }
    private static bool SamePixels(Image first, Image second, Rect2I rectangle)
    {
        for (int y = rectangle.Position.Y; y < rectangle.End.Y; y++)
            for (int x = rectangle.Position.X; x < rectangle.End.X; x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) return false;
        return true;
    }
}
