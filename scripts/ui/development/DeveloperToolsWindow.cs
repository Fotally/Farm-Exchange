using System;
using System.Globalization;
using System.IO;
using Godot;
using FarmExchange.Development;
using FarmExchange.Gameplay;
using FarmExchange.Time;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>选择持久配置、执行固定流程并展示进度与真实报告路径。</summary>
 * <remarks>当前局借用 Main 唯一驱动；独立局只推进数据，不更换地图。</remarks>
 */
public partial class DeveloperToolsWindow : DraggableWindow
{
    private readonly FarmGame _currentGame;
    private readonly SimulationDriver _currentDriver;
    private readonly Action _refreshCurrent;
    private readonly LineEdit _configurationPath;
    private readonly Label _progress;
    private readonly Label _feedback;
    private readonly Label _reportPath;
    private readonly Button _start;
    private readonly Button _abort;
    private readonly Button _choose;
    private readonly FileDialog _files;
    private BuyProcessSellScenario? _scenario;
    private SimulationDriver? _independentDriver;
    private string? _runDirectory;
    private bool _isCurrent;
    private bool _reportAttempted;
    private bool _observedPaused;

    /**
     * <summary>组装绑定当前局及唯一驱动的开发窗口。</summary>
     * <param name="currentGame">当前地图使用的经营对象。</param>
     * <param name="currentDriver">当前局唯一时间驱动。</param>
     * <param name="refreshCurrent">正常经营变化后的统一刷新。</param>
     */
    public DeveloperToolsWindow(FarmGame currentGame, SimulationDriver currentDriver, Action refreshCurrent)
        : base("DeveloperToolsWindow", "参数化经营测试", new Vector2(540, 160), new Vector2(840, 690))
    {
        _currentGame = currentGame;
        _currentDriver = currentDriver;
        _refreshCurrent = refreshCurrent;
        Body.AddChild(MakeLabel("选择 JSON 配置，执行买入 → 加工 → 一次委托卖出。独立局只运行数据。", 17, Ink));
        var fileRow = new HBoxContainer();
        Body.AddChild(fileRow);
        _configurationPath = new LineEdit
        {
            Name = "ScenarioConfigurationPath",
            PlaceholderText = "持久配置文件的完整路径",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        fileRow.AddChild(_configurationPath);
        _files = new FileDialog
        {
            Name = "ScenarioFileDialog",
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Filters = new[] { "*.json ; JSON 配置" },
        };
        _files.FileSelected += path => _configurationPath.Text = path;
        AddChild(_files);
        _choose = MakeSecondaryButton("选择文件", 116, 42);
        _choose.Name = "ScenarioChooseFileButton";
        _choose.Pressed += () => _files.PopupCentered(new Vector2I(1000, 680));
        fileRow.AddChild(_choose);

        var actions = new HBoxContainer();
        Body.AddChild(actions);
        _start = MakeButton("启动流程", new Color("657342"), 142, 44);
        _start.Name = "ScenarioStartButton";
        _start.Pressed += Start;
        actions.AddChild(_start);
        _abort = MakeQuietButton("中止流程", 142, 44);
        _abort.Name = "ScenarioAbortButton";
        _abort.Disabled = true;
        _abort.Pressed += () => Abort("用户中止");
        actions.AddChild(_abort);
        _progress = MakeLabel("尚未运行", 17, Ink);
        _progress.Name = "ScenarioProgressLabel";
        Body.AddChild(_progress);
        _feedback = MakeLabel("", 15, Ink);
        _feedback.Name = "ScenarioFeedbackLabel";
        Body.AddChild(_feedback);
        _reportPath = MakeLabel("报告尚未生成", 14, Muted);
        _reportPath.Name = "ScenarioReportPathLabel";
        Body.AddChild(_reportPath);
        Body.AddChild(MakeLabel("现场暂停时流程等待继续；主动改速会中断现场流程，已经发生的交易与生产保留。", 15, Muted));

        Body.AddChild(MakeLabel("当前局经营倍率", 17, Ink));
        var rates = new HBoxContainer();
        Body.AddChild(rates);
        foreach (double rate in new[] { 0.5, 1, 2, 5, 10, 20 })
        {
            Button button = MakeQuietButton($"{rate.ToString(CultureInfo.InvariantCulture)}×", 78, 40);
            button.Name = "DevelopmentRate" + rate.ToString(CultureInfo.InvariantCulture).Replace(".", "_") + "Button";
            button.Pressed += () => _currentDriver.SetDevelopmentRate(rate);
            rates.AddChild(button);
        }
        var custom = new HBoxContainer();
        Body.AddChild(custom);
        var value = new LineEdit
        {
            Name = "DevelopmentRateInput",
            PlaceholderText = "正整数倍率",
            CustomMinimumSize = new Vector2(180, 40),
        };
        custom.AddChild(value);
        Button apply = MakeQuietButton("应用倍率", 140, 40);
        apply.Name = "DevelopmentRateApplyButton";
        apply.Pressed += () =>
        {
            if (!double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) ||
                !SimulationDriver.IsDevelopmentRateAllowed(rate))
            {
                _feedback.Text = "倍率只接受 0.5、1、2 或有限正整数。";
                return;
            }
            _currentDriver.SetDevelopmentRate(rate);
        };
        custom.AddChild(apply);
        _currentDriver.RateChanged += OnCurrentRateChanged;
    }

    public override void _ExitTree()
    {
        _currentDriver.RateChanged -= OnCurrentRateChanged;
        if (_scenario?.IsRunning == true) Abort("开发窗口宿主退出");
        base._ExitTree();
    }

    private void Start()
    {
        if (_scenario?.IsRunning == true) return;
        try
        {
            ScenarioConfiguration configuration = ScenarioConfiguration.Load(_configurationPath.Text);
            string root = ScenarioReport.ResolveRoot(ProjectSettings.GlobalizePath("res://"),
                OS.GetExecutablePath(), !OS.HasFeature("editor"));
            _runDirectory = ScenarioReport.CreateRunDirectory(root);
            _reportAttempted = false;
            _isCurrent = configuration.Target == "current";
            _scenario = BuyProcessSellScenario.Start(configuration, _isCurrent ? _currentGame : null);
            _independentDriver = _isCurrent ? null : new SimulationDriver();
            _observedPaused = _scenario.Game.IsPaused;
            if (_scenario.IsRunning) ApplyScenarioRate();
            if (_isCurrent) _refreshCurrent();
            _feedback.Text = "";
            _reportPath.Text = "本次报告目录：" + _runDirectory;
            RefreshProgress();
        }
        catch (Exception error) when (error is ScenarioConfigurationException or IOException or UnauthorizedAccessException)
        {
            _feedback.Text = (error is ScenarioConfigurationException ? "启动失败（配置错误）：" : "启动失败（输出错误）：") + error.Message;
        }
    }

    private void OnCurrentRateChanged(double rate, SimulationRateSource source)
    {
        if (_isCurrent && _scenario?.IsRunning == true)
        {
            _scenario.ObserveTime(rate, source.ToString(), _scenario.Game.IsPaused);
            if (source == SimulationRateSource.Player) Abort("玩家主动改速，中断自动流程");
        }
        _refreshCurrent();
    }

    private void ApplyScenarioRate()
    {
        SimulationDriver driver = _isCurrent ? _currentDriver : _independentDriver!;
        bool changed = driver.Rate != _scenario!.CurrentRateIntent;
        if (changed)
            driver.SetDevelopmentRate(_scenario.CurrentRateIntent, SimulationRateSource.Scenario);
        if (!_isCurrent || !changed)
            _scenario.ObserveTime(driver.Rate, SimulationRateSource.Scenario.ToString(), _scenario.Game.IsPaused);
    }

    internal uint GetCurrentMaxTicks() => _isCurrent && _scenario?.IsRunning == true
        ? _scenario.GetMaxAdvanceTicks() : uint.MaxValue;

    internal bool ObserveCurrentCheckpoint(SimulationCheckpoint point)
    {
        if (!_isCurrent || _scenario?.IsRunning != true) return true;
        bool keepGoing = _scenario.ObserveCheckpoint(point);
        if (_scenario.IsRunning) ApplyScenarioRate();
        RefreshProgress();
        return keepGoing;
    }

    internal void AdvanceIndependent(double delta)
    {
        if (_scenario?.IsRunning != true) return;
        if (_observedPaused != _scenario.Game.IsPaused)
        {
            _observedPaused = _scenario.Game.IsPaused;
            _scenario.ObserveTime((_isCurrent ? _currentDriver : _independentDriver!).Rate,
                SimulationRateSource.Scenario.ToString(), _scenario.Game.IsPaused);
        }
        if (!_isCurrent)
        {
            try
            {
                _independentDriver!.Advance(delta, _scenario.Game, point =>
                {
                    bool keepGoing = _scenario.ObserveCheckpoint(point);
                    if (_scenario.IsRunning) ApplyScenarioRate();
                    return keepGoing;
                }, () => _scenario.IsRunning ? _scenario.GetMaxAdvanceTicks() : 0);
            }
            catch (InvalidOperationException error)
            {
                Abort("经营推进被拒绝：" + error.Message);
            }
        }
        RefreshProgress();
    }

    internal void Abort(string reason)
    {
        if (_scenario?.IsRunning != true) return;
        _scenario.Abort(reason);
        RefreshProgress();
    }

    private void RefreshProgress()
    {
        if (_scenario == null) return;
        _start.Disabled = _scenario.IsRunning;
        _configurationPath.Editable = !_scenario.IsRunning;
        _choose.Disabled = _scenario.IsRunning;
        _abort.Disabled = !_scenario.IsRunning;
        _progress.Text = $"{(_isCurrent ? "当前局" : "独立局")} · {_scenario.Progress}\n" +
            $"实际倍率 {(_isCurrent ? _currentDriver : _independentDriver!)?.Rate}× · " +
            (_scenario.Game.IsPaused && _scenario.IsRunning ? "暂停，等待继续" : OutcomeText(_scenario.Outcome));
        if (_scenario.IsRunning || _reportAttempted) return;
        _reportAttempted = true;
        try
        {
            _reportPath.Text = "报告：" + _scenario.WriteReport(_runDirectory!);
            _feedback.Text = _scenario.Report.Reason ?? "流程结束，检查结果见 JSON 报告。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _feedback.Text = "输出错误（报告尚未保存）：" + error.Message + "；执行结果保留，未切换输出位置。";
        }
    }

    private static string OutcomeText(ScenarioOutcome outcome) => outcome switch
    {
        ScenarioOutcome.Running => "运行中",
        ScenarioOutcome.Passed => "严格检查通过",
        ScenarioOutcome.CompletedWithInsufficientEvidence => "完成，部分证据不足",
        ScenarioOutcome.PreconditionsRejected => "前置条件不满足",
        ScenarioOutcome.OperationRejected => "经营命令拒绝",
        ScenarioOutcome.WaitLimitExceeded => "等待预算耗尽",
        ScenarioOutcome.TimeRangeExhausted => "日期范围耗尽",
        ScenarioOutcome.CheckFailed => "检查失败",
        ScenarioOutcome.Aborted => "已中止",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}
