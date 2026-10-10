using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Time;
using FarmExchange.UI;
using FarmExchange.World;
using Godot;

public partial class TestLoggingPerformance : Node
{
    private const int Seed = 130;
    private const double SampleSeconds = 65;
    private const int BoundedEventLimit = 500;
    private readonly List<double> _frames = new();
    private RuntimeLog _logging = null!;
    private Main _main = null!;
    private WorldMap _map = null!;
    private Camera2D _camera = null!;
    private FarmGame? _second;
    private SimulationDriver? _secondDriver;
    private string _directory = "";
    private string _profile = "";
    private string _workload = "";
    private ulong _ready;
    private ulong _start;
    private ulong _previous;
    private long _allocated;
    private int[] _collections = Array.Empty<int>();
    private uint _ticks;
    private uint _secondTicks;
    private int _drawn;
    private int _tradePairs;
    private int _diagnostics;
    private bool _finished;
    private double _requestedRate;

    public override void _EnterTree()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _profile = Argument(args, "--profile");
        _workload = Argument(args, "--workload");
        _directory = Path.GetFullPath(Argument(args, "--output"));
        if (!new[] { "off", "runtime", "development", "failure" }.Contains(_profile) ||
            !new[] { "full-1", "full-16", "trades-multigame", "bounded" }.Contains(_workload))
            throw new ArgumentException("未知日志性能档位或负载");
        Directory.CreateDirectory(_directory);
        Main.InitialSeed = Seed;
        Main.LoggingFactory = () =>
        {
            _logging = _profile == "off" ? RuntimeLog.Disabled() : RuntimeLog.OpenFile(
                Path.Combine(_directory, "logs"), _profile == "development", new(BuildKind: "Debug", WindowWidth: 1920, WindowHeight: 1080),
                _profile == "failure" ? new LogFileRetention(128, 10) : null, _ => _diagnostics++);
            if (_profile == "failure") TestLoggingFaults.BlockNextRoll(Path.Combine(_directory, "logs", "runtime"));
            return _logging;
        };
    }

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("专项 FPS 必须有图形窗口");
        GetWindow().Size = new(1920, 1080);
        ProcessPriority = 3;
        _main = GetNode<Main>("Main");
        _map = _main.GetNode<WorldMap>("WorldMap");
        _camera = _main.GetNode<Camera2D>("Camera2D");
        _main.Game.FillWorldForPresentationBenchmark();
        _map.SyncFromGame();
        _requestedRate = _workload is "full-16" or "bounded" ? 16 : 1;
        _main.Driver.SetDevelopmentRate(_requestedRate);
        if (_workload == "trades-multigame")
        {
            _second = new FarmGame(Seed, _logging, GamePurpose.ScenarioIndependent);
            _secondDriver = new(_second.Log?.Time);
        }
        if (_workload == "bounded")
        {
            var anchors = _main.Game.GetBuildingSpaces().Take(32).Select(space => space.AnchorCell).ToArray();
            TestLogging.Require(_main.Game.Log!.Diagnostics.Start(new(
                new[] { "ProductionStateChanged", "WorkerTaskChanged", "WorkerMoved", "EventTickTiming" },
                anchors, WorkerNumbers: new[] { 1, 2, 3 }, DurationLimitMs: 120000, EventLimit: BoundedEventLimit, IncludeGameEvents: true)),
                "有界诊断未开始");
        }
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        _ready = Time.GetTicksUsec();
    }

    public override void _Process(double delta)
    {
        if (_ready == 0 || _finished) return;
        _secondDriver?.Advance(delta, _second!);
        ulong now = Time.GetTicksUsec();
        double elapsed = (now - _ready) / 1_000_000.0;
        _camera.GlobalPosition = _map.GetGridWorldPosition(new Vector2(191.5f, 191.5f)) + new Vector2((float)(Math.Sin(elapsed * .8) * 200), 0);
        if (_workload == "trades-multigame")
        {
            int target = (int)(elapsed * 10);
            while (_tradePairs < target)
            {
                // 加工品立即等价买回卖出，真实成功交易且不与加工领取竞争。
                var commodity = new CommodityId(CropKind.Wheat, CommodityKind.Product);
                TestLogging.Require(_main.Game.Buy(commodity, 1).Success && _main.Game.Sell(commodity, 1).Success,
                    "持续交易准备失效");
                TestLogging.Require(_second!.Buy(commodity, 1).Success && _second.Sell(commodity, 1).Success,
                    "第二局持续交易失效");
                _tradePairs++;
            }
        }
        if (elapsed < 2) return;
        if (_start == 0)
        {
            SaveScreenshot("before.png");
            _frames.Clear();
            _start = _previous = Time.GetTicksUsec();
            _drawn = Engine.GetFramesDrawn();
            _ticks = _main.Game.Calendar.ElapsedSeconds;
            _secondTicks = _second?.Calendar.ElapsedSeconds ?? 0;
            _collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            _allocated = GC.GetTotalAllocatedBytes(true);
            return;
        }
        _frames.Add((now - _previous) / 1000.0);
        _previous = now;
        if ((now - _start) / 1_000_000.0 >= SampleSeconds) Finish(now);
    }

    private void Finish(ulong now)
    {
        _finished = true;
        double seconds = (now - _start) / 1_000_000.0;
        long allocated = GC.GetTotalAllocatedBytes(true) - _allocated;
        int[] collections = Enumerable.Range(0, 3).Select(index => GC.CollectionCount(index) - _collections[index]).ToArray();
        double fps = (Engine.GetFramesDrawn() - _drawn) / seconds;
        uint ticks = _main.Game.Calendar.ElapsedSeconds - _ticks;
        uint secondTicks = (_second?.Calendar.ElapsedSeconds ?? 0) - _secondTicks;
        _frames.Sort();
        double p95 = _frames[(int)Math.Ceiling(_frames.Count * .95) - 1];
        bool captureActiveBeforeShutdown = _main.Game.Log?.Diagnostics.IsActive ?? false;
        _second?.Dispose();
        _main.Game.Dispose();
        _logging.Dispose();
        string[] lines = Directory.Exists(Path.Combine(_directory, "logs"))
            ? Directory.GetFiles(Path.Combine(_directory, "logs"), "*.log", SearchOption.AllDirectories)
                .SelectMany(path => TestLogging.Lines(File.ReadAllText(path))).ToArray() : Array.Empty<string>();
        // runtime/debug 是同一事件的两份输出，按会话与序号去重后计数。
        string[] unique = lines.GroupBy(line => TestLogging.Field(line, "SessionId") + ":" + TestLogging.Field(line, "Sequence"))
            .Select(group => group.First()).ToArray();
        var counts = unique.GroupBy(line => TestLogging.Field(line, "EventName")).ToDictionary(group => group.Key, group => group.Count());
        string[] captureEnds = unique.Where(line => TestLogging.HasEvent(line, "DiagnosticCaptureEnded")).ToArray();
        long quiet = SummaryTicks(unique, "QuietTicks");
        long events = SummaryTicks(unique, "EventTicks");
        bool valid = _frames.Count >= 2 && ticks > 0 && _main.Driver.Rate == _requestedRate &&
            _main.Game.GetBuildingSpaces().Count == 16384 && GetWindow().Size.X >= 1920 && GetWindow().Size.Y >= 1080 &&
            (_second == null || secondTicks > 0) &&
            (_profile != "failure" || _logging.Health.FailureCount > 0) &&
            (_workload != "bounded" || !captureActiveBeforeShutdown && captureEnds.Any(line => line.Contains("StopReason: \"EventLimit\"") &&
                TestLogging.Field(line, "CapturedCount") == BoundedEventLimit.ToString())) &&
            (_profile is "off" or "failure" || unique.Any(line => TestLogging.HasEvent(line, "PerformanceSummary") && line.Contains("IsPartialWindow: false")));
        var report = new
        {
            Profile = _profile,
            Workload = _workload,
            Seed,
            RequestedRate = _requestedRate,
            EffectiveSelectedRate = _main.Driver.Rate,
            SampleSeconds = seconds,
            AverageFps = fps,
            P95FrameMs = p95,
            FrameSamples = _frames.Count,
            AdvancedTicks = ticks,
            SecondaryAdvancedTicks = secondTicks,
            ActualSimulationSecondsPerRealSecond = ticks / seconds,
            AdvanceBudget = "使用正式 Main 驱动及现有完整检查点预算；请求 16× 不表示实测吞吐保持 16×",
            ManagedAllocatedBytes = allocated,
            ManagedAllocatedBytesPerSecond = allocated / seconds,
            GcCollections = collections,
            AllocationScope = "GC.GetTotalAllocatedBytes(true) 进程托管分配；不含 Godot/GPU 原生内存；首截图后至末截图前",
            EntityCount = _main.Game.GetBuildingSpaces().Count,
            OccupiedCells = FarmGame.MapSize * FarmGame.MapSize,
            GameCount = _second == null ? 1 : 2,
            TradePairsPerGame = _tradePairs,
            EventCounts = counts,
            LoggedQuietTicks = quiet,
            LoggedEventTicks = events,
            LoggedTickScope = "全部已保留窗口含预热和尾段；关闭/失败无日志不是零推进",
            CaptureActiveBeforeShutdown = captureActiveBeforeShutdown,
            RequestedCaptureEventLimit = _workload == "bounded" ? BoundedEventLimit : (int?)null,
            CaptureEndEvidence = captureEnds,
            Health = _logging.Health,
            IndependentDiagnosticCount = _diagnostics,
            WindowWidth = GetWindow().Size.X,
            WindowHeight = GetWindow().Size.Y,
            CameraZoom = _camera.Zoom.ToString(),
            CameraPath = "固定地图中心 + sin(现实秒×0.8)×200 水平像素；不随经营速率变轨迹",
            Renderer = RenderingServer.GetVideoAdapterName(),
            Cpu = OS.GetProcessorName(),
            Godot = Engine.GetVersionInfo()["string"].ToString(),
            Valid = valid,
            Passed = valid && fps >= 60 && p95 <= 1000.0 / 60,
        };
        File.WriteAllText(Path.Combine(_directory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        SaveScreenshot("after.png");
        GD.Print($"日志矩阵 {_workload}/{_profile}：{fps:F1} FPS，P95 {p95:F2} ms，托管分配 {allocated}，实际 {ticks} tick");
        GetTree().Quit(report.Passed ? 0 : 1);
    }

    private static long SummaryTicks(string[] lines, string field) => lines.Where(line =>
        TestLogging.HasEvent(line, "PerformanceSummary") && line.Contains("SummaryScope: \"GameAdvance\""))
        .Sum(line => long.Parse(TestLogging.Field(line, field)));

    private void SaveScreenshot(string name)
    {
        Error error = GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_directory, name));
        if (error != Error.Ok) throw new IOException("无法保存性能截图：" + error);
    }

    private static string Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("缺少 " + name);
        return args[index + 1];
    }

    public override void _ExitTree()
    {
        _second?.Dispose();
        _logging?.Dispose();
        Main.LoggingFactory = null;
        Main.InitialSeed = null;
    }
}
