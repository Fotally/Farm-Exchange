using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestFullWorldFps : Node
{
    private const ulong WarmupUsec = 2_000_000;
    private const ulong SampleUsec = 8_000_000;

    private Camera2D _camera = null!;
    private WorldMap _map = null!;
    private readonly List<double> _frameTimesMs = new();
    private ulong _readyAtUsec;
    private ulong _sampleStartUsec;
    private ulong _previousFrameUsec;
    private int _startFrames;
    private int _ticks;
    private int _maxVisiblePlotCandidates;
    private int _startChunkRedraws;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PushError("帧率性能测试需要图形窗口，不能使用 --headless");
            GetTree().Quit(1);
            return;
        }

        Main main = GetNode<Main>("Main");
        main.Game.FillWorldForBenchmark();
        _map = main.GetNode<WorldMap>("WorldMap");
        _map.SyncFromGame();
        _camera = main.GetNode<Camera2D>("Camera2D");
        main.GetNode<Timer>("TickTimer").Timeout += () => _ticks++;
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectSettings.GlobalizePath("res://coverage/performance.json"))!);
        _readyAtUsec = Time.GetTicksUsec();
        GD.Print("满地图渲染性能测试：16,384 格实体，预热 2 秒，采样 8 秒");
    }

    public override void _Process(double delta)
    {
        if (_readyAtUsec == 0)
            return;

        ulong now = Time.GetTicksUsec();
        double seconds = (now - _readyAtUsec) / 1_000_000.0;
        _camera.Position = _map.ClampCameraCenter(new Vector2(
            (float)(Math.Sin(seconds * 0.8) * 500.0), 2032f));
        _maxVisiblePlotCandidates = Math.Max(_maxVisiblePlotCandidates, _map.LastVisiblePlotCount);

        if (now - _readyAtUsec < WarmupUsec)
            return;
        if (_sampleStartUsec == 0)
        {
            string screenshotPath = ProjectSettings.GlobalizePath("res://coverage/performance.png");
            GetViewport().GetTexture().GetImage().SavePng(screenshotPath);
            _sampleStartUsec = now;
            _previousFrameUsec = now;
            _startFrames = Engine.GetFramesDrawn();
            _startChunkRedraws = _map.ChunkRedrawCount;
            return;
        }

        _frameTimesMs.Add((now - _previousFrameUsec) / 1000.0);
        _previousFrameUsec = now;
        if (now - _sampleStartUsec < SampleUsec)
            return;

        int renderedFrames = Engine.GetFramesDrawn() - _startFrames;
        if (renderedFrames == 0 || _frameTimesMs.Count < 2 || _maxVisiblePlotCandidates == 0 || _ticks == 0)
        {
            GD.PushError("帧率性能测试没有取得有效的渲染帧、可见地块或经营 tick");
            GetTree().Quit(1);
            return;
        }

        double duration = (now - _sampleStartUsec) / 1_000_000.0;
        _frameTimesMs.Sort();
        double fps = renderedFrames / duration;
        double p95 = Percentile(_frameTimesMs, 0.95);
        string reportPath = ProjectSettings.GlobalizePath("res://coverage/performance.json");
        var report = new
        {
            Scenario = "128x128 满地图，8,192 农田与 8,192 加工场地，六种作物，镜头水平往返移动",
            EntityCount = FarmGame.MapSize * FarmGame.MapSize,
            FarmCount = 8192,
            ProcessorCount = 8192,
            TickCount = _ticks,
            MaxVisiblePlotCandidates = _maxVisiblePlotCandidates,
            ChunkRedraws = _map.ChunkRedrawCount - _startChunkRedraws,
            WarmupSeconds = WarmupUsec / 1_000_000,
            SampleSeconds = duration,
            RenderedFrames = renderedFrames,
            AverageFps = fps,
            MedianFrameMs = Percentile(_frameTimesMs, 0.5),
            P95FrameMs = p95,
            P99FrameMs = Percentile(_frameTimesMs, 0.99),
            WindowSize = GetWindow().Size.ToString(),
            CameraZoom = _camera.Zoom.ToString(),
            VsyncMode = DisplayServer.WindowGetVsyncMode().ToString(),
            FpsLimit = Engine.MaxFps,
            Renderer = RenderingServer.GetVideoAdapterName(),
            Cpu = OS.GetProcessorName(),
            Os = OS.GetName() + " " + OS.GetVersion(),
            Godot = Engine.GetVersionInfo()["string"].ToString(),
            Build = "Godot 编辑器 Debug",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://coverage/performance-end.png"));
        GD.Print(string.Format(CultureInfo.InvariantCulture,
            "满地图性能：平均 {0:F1} FPS，P95 帧间隔 {1:F2} ms，最多可见候选格 {2}，报告 {3}",
            fps, p95, _maxVisiblePlotCandidates, reportPath));
        if (fps < 60.0 || p95 > 1000.0 / 60.0)
        {
            GD.PushError("满地图渲染未达到稳定 60 FPS：平均 FPS 需至少 60，P95 帧间隔需不超过 16.67 ms");
            GetTree().Quit(1);
            return;
        }
        GetTree().Quit(0);
    }

    private static double Percentile(List<double> sorted, double fraction)
    {
        int index = (int)Math.Ceiling(sorted.Count * fraction) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
