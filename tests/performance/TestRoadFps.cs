using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestRoadFps : Node
{
    private readonly List<double> _framesMs = new();
    private Main _main = null!;
    private WorldMap _map = null!;
    private Camera2D _camera = null!;
    private ulong _started;
    private ulong _sampleStarted;
    private ulong _previous;
    private int _startFrames;
    private int _startRedraws;
    private int _buildPreviewFrames;
    private int _removePreviewFrames;
    private string _directory = null!;
    private Label _money = null!;
    private Label _calendar = null!;
    private Label _calendarStatus = null!;
    private Label _farms = null!;
    private Label _processors = null!;
    private Label _activity = null!;
    private Button _pause = null!;
    private string _expectedDate = null!;
    private string _uiAtSampleStart = null!;

    public override async void _Ready()
    {
        try { await Prepare(); }
        catch (Exception error) { Fail(error.ToString()); }
    }

    private async Task Prepare()
    {
        if (DisplayServer.GetName() == "headless") { Fail("道路FPS场景需要真实图形窗口"); return; }
        GetWindow().Size = new Vector2I(1920, 1080);
        int? previousSeed = Main.InitialSeed;
        Main.InitialSeed = 12345;
        try
        {
            _main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            _main.InitialTerrainRules = TerrainTestFixtures.AllLandRules;
            AddChild(_main);
        }
        finally { Main.InitialSeed = previousSeed; }
        await _main.ReadyTask;
        // 先冻结宿主推进，再真实提交道路并等待正常同步，不把准备帧算入性能采样。
        _main.SetProcess(false);
        foreach (var space in _main.Game.GetBuildingSpaces()) _main.Game.RemoveBuilding(space.AnchorCell);
        var cross = new List<Vector2I>();
        for (int x = 175; x <= 189; x++) cross.Add(new Vector2I(x, 183));
        if (!_main.Game.TryPlaceRoads(cross, RoadMaterial.Dirt).Success) { Fail("道路FPS横路夹具失败"); return; }
        for (int material = 0; material < 5; material++)
        {
            var column = new List<Vector2I>();
            for (int y = 180; y <= 186; y++)
                if (y != 183) column.Add(new Vector2I(176 + material * 3, y));
            if (!_main.Game.TryPlaceRoads(column, (RoadMaterial)material).Success)
            { Fail("道路FPS五材质纵路夹具失败"); return; }
        }
        await _main.Game.WaitForNavigationAsync(GetTree());
        if (!_main.Game.IsNavigationReady) { Fail("道路FPS导航准备未完成"); return; }
        _pause = Find<Button>("PauseButton");
        _pause.EmitSignal(Button.SignalName.Pressed);
        _money = Find<Control>("ResourcesPanel").FindChildren("*", "Label", true, false)
            .OfType<Label>().Single(label => label.TooltipText.StartsWith("总余额 ", StringComparison.Ordinal));
        _calendar = Find<Label>("CalendarLabel");
        _calendarStatus = _calendar.GetParent().GetChildren().OfType<Label>().Single(label => label != _calendar);
        _farms = Find<Label>("FarmCountLabel");
        _processors = Find<Label>("ProcessorCountLabel");
        _activity = Find<Label>("ActivityLabel");
        var date = _main.Game.Calendar;
        _expectedDate = $"春 · 第 {date.Year} 年 · {date.Month} 月 · {date.Day} 日";
        // 真实Main/UI仍显示；停止其鼠标候选刷新，由本场景提供连续道路操作快照。
        _map = _main.GetNode<WorldMap>("WorldMap");
        _map.SyncFromGame();
        _camera = _main.GetNode<Camera2D>("Camera2D");
        _camera.GlobalPosition = _map.GetCellWorldCenter(new Vector2I(183, 183));
        _camera.ForceUpdateScroll();
        ProcessPriority = 3;
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        _directory = ProjectSettings.GlobalizePath("res://build/issue143-validation/road-performance");
        Directory.CreateDirectory(_directory);
        _started = Time.GetTicksUsec();
        GD.Print("道路图形性能：1080P真实Main、45格五材质跨块交叉路，移动新建/拆除预览；预热2秒，采样8秒");
    }

    public override void _Process(double delta)
    {
        if (_started == 0) return;
        ulong now = Time.GetTicksUsec();
        double elapsed = (now - _started) / 1_000_000.0;
        bool removing = ((int)elapsed & 1) != 0;
        int movingEnd = 179 + (int)(Math.Sin(elapsed * 2) * 6);
        var cells = new List<Vector2I>();
        if (removing)
            for (int x = 173; x <= movingEnd + 11; x++) cells.Add(new Vector2I(x, 183));
        else
        {
            for (int x = 173; x <= movingEnd + 6; x++) cells.Add(new Vector2I(x, 179));
            for (int y = 180; y <= 186; y++) cells.Add(new Vector2I(movingEnd + 6, y));
        }
        _map.UpdateRoadPreview(cells, RoadMaterial.Brick, removing);
        _camera.GlobalPosition = _map.GetCellWorldCenter(new Vector2I(183, 183)) +
            new Vector2((float)Math.Sin(elapsed * .8) * 100, 0);
        if (now - _started < 2_000_000) return;
        if (_sampleStarted == 0)
        {
            if (!CheckUi()) { Fail("道路FPS采样前真实界面与夹具状态不一致"); return; }
            _uiAtSampleStart = UiSnapshot();
            Save("before.png");
            _sampleStarted = _previous = Time.GetTicksUsec();
            _startFrames = Engine.GetFramesDrawn();
            _startRedraws = _map.ChunkRedrawCount;
            return;
        }
        if (removing) _removePreviewFrames++; else _buildPreviewFrames++;
        _framesMs.Add((now - _previous) / 1000.0);
        _previous = now;
        if (now - _sampleStarted < 8_000_000) return;
        double duration = (now - _sampleStarted) / 1_000_000.0;
        int frames = Engine.GetFramesDrawn() - _startFrames;
        _framesMs.Sort();
        double fps = frames / duration;
        double p95 = _framesMs[(int)Math.Ceiling(_framesMs.Count * .95) - 1];
        bool uiConsistent = CheckUi();
        bool passed = fps >= 60 && p95 <= 1000.0 / 60 && _buildPreviewFrames > 0 && _removePreviewFrames > 0 &&
            _main.Game.GetBuildingSpaces().Count == 45 && _main.Game.MoneyCents == 500 && _main.Game.IsPaused && uiConsistent;
        Save("after.png");
        File.WriteAllText(Path.Combine(_directory, "performance.json"), JsonSerializer.Serialize(new
        {
            Scenario = "1080P暂停道路绘制专项：真实Main控件/地图/镜头继续渲染，关闭Main帧刷新；45格五材质跨块道路，移动新建L形/拆除直线预览并平移镜头",
            MainProcessEnabled = _main.IsProcessing(),
            IsPaused = _main.Game.IsPaused,
            RoadCount = _main.Game.GetBuildingSpaces().Count,
            Materials = 5,
            WarmupSeconds = 2,
            SampleSeconds = duration,
            RenderedFrames = frames,
            BuildPreviewFrames = _buildPreviewFrames,
            RemovePreviewFrames = _removePreviewFrames,
            ChunkRedraws = _map.ChunkRedrawCount - _startRedraws,
            AverageFps = fps,
            P95FrameMs = p95,
            WindowSize = GetWindow().Size.ToString(),
            CameraZoom = _camera.Zoom.ToString(),
            Renderer = RenderingServer.GetVideoAdapterName(),
            Cpu = OS.GetProcessorName(),
            Godot = Engine.GetVersionInfo()["string"].ToString(),
            UiAtSampleStart = _uiAtSampleStart,
            UiAtSampleEnd = UiSnapshot(),
            UiConsistent = uiConsistent,
            Passed = passed,
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print(string.Format(CultureInfo.InvariantCulture, "道路FPS：平均{0:F1}，P95 {1:F2}ms，报告{2}", fps, p95, _directory));
        if (!passed) GD.PushError("道路性能/预览负载未达到60FPS与P95≤16.67ms、正式状态不变或真实UI一致要求");
        GetTree().Quit(passed ? 0 : 1);
        _started = 0;
    }

    private void Save(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_directory, name));
    private T Find<T>(string name) where T : Node => _main.FindChild(name, true, false) as T ?? throw new InvalidOperationException(name);

    private bool CheckUi()
    {
        bool passed = !_main.IsProcessing() && _main.Game.IsPaused &&
            _money.IsVisibleInTree() && _activity.IsVisibleInTree() && _calendar.IsVisibleInTree() &&
            _calendarStatus.IsVisibleInTree() && _pause.IsVisibleInTree() && _farms.IsVisibleInTree() && _processors.IsVisibleInTree() &&
            _money.Text == "5.00" && _money.TooltipText == "总余额 5.00 · 冻结 0.00" &&
            _farms.Text == "0 块农田" && _processors.Text == "0 处加工场地" &&
            _activity.Text == "道路 45 格\n经营已暂停，仍可主动交易。" && _calendar.Text == _expectedDate &&
            _calendarStatus.Text == "经营已暂停 · 仍可交易" && _pause.TooltipText == "继续经营";
        if (!passed) GD.PushError("道路FPS界面不一致：" + UiSnapshot());
        return passed;
    }

    private string UiSnapshot() => $"金币={_money.Text}（{_money.TooltipText}）；{_farms.Text}；{_processors.Text}；" +
        $"近况={_activity.Text}；日期={_calendar.Text}；日期状态={_calendarStatus.Text}；暂停按钮={_pause.TooltipText}";

    private void Fail(string message) { GD.PushError(message); GetTree().Quit(1); }
}
