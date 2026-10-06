using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using FarmExchange.Development;
using FarmExchange.Gameplay;
using FarmExchange.Time;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>协调分类流程目录、配置草稿及真实流程的三个独立步骤。</summary>
 * <remarks>当前局借用Main唯一驱动；配置持久化由配置库拥有，独立局不更换地图。</remarks>
 */
public partial class DeveloperToolsWindow : DraggableWindow
{
    private readonly FarmGame _currentGame;
    private readonly SimulationDriver _currentDriver;
    private readonly Action _refreshCurrent;
    private readonly ScenarioCatalogStep _catalog;
    private readonly ScenarioEditorStep _editor;
    private readonly ScenarioResultStep _result;
    private readonly ScenarioConfirmationPanel _confirmation;
    private readonly Label _feedback;
    private readonly Button[] _steps = new Button[3];
    private ScenarioConfigurationLibrary? _library;
    private IReadOnlyList<ScenarioConfigurationEntry> _entries = Array.Empty<ScenarioConfigurationEntry>();
    private string? _flowId;
    private ScenarioConfigurationDraft? _draft;
    private int _step;
    private BuyProcessSellScenario? _scenario;
    private SimulationDriver? _independentDriver;
    private string? _runDirectory;
    private bool _isCurrent;
    private bool _reportAttempted;
    private bool _observedPaused;

    /**
     * <summary>组装绑定当前局、唯一驱动和配置库的开发窗口。</summary>
     * <param name="currentGame">当前地图使用的经营对象。</param>
     * <param name="currentDriver">当前局唯一时间驱动。</param>
     * <param name="refreshCurrent">经营变化后的统一刷新。</param>
     * <param name="library">已明确目录的配置库；省略时使用仓库或开发包配置目录。</param>
     */
    public DeveloperToolsWindow(FarmGame currentGame, SimulationDriver currentDriver, Action refreshCurrent,
        ScenarioConfigurationLibrary? library = null)
        : base("DeveloperToolsWindow", "参数化经营测试", new Vector2(280, 155), new Vector2(1360, 740))
    {
        _currentGame = currentGame;
        _currentDriver = currentDriver;
        _refreshCurrent = refreshCurrent;
        SetInitialPlacement(false);
        // 各步骤正文独立滚动，保存和运行入口留在固定底部。
        ((ScrollContainer)FindChild("WindowScroll", true, false)).VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var navigation = new HBoxContainer();
        Body.AddChild(navigation);
        string[] titles = { "1 选择流程", "2 编辑配置", "3 运行与结果" };
        for (int index = 0; index < 3; index++)
        {
            int destination = index;
            _steps[index] = MakeSecondaryButton(titles[index], 220, 42);
            _steps[index].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _steps[index].Name = "ScenarioStep" + (index + 1) + "Button";
            _steps[index].Pressed += () => Navigate(destination);
            navigation.AddChild(_steps[index]);
        }
        _feedback = MakeLabel("", 12, Ink);
        _feedback.Name = "ScenarioFeedbackLabel";
        var feedbackScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 40),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _feedback.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        feedbackScroll.AddChild(_feedback);
        Body.AddChild(feedbackScroll);
        _catalog = new ScenarioCatalogStep();
        _catalog.Selected += SelectFlow;
        _catalog.DeleteRequested += ConfirmDeleteFlow;
        _catalog.ContinueRequested += () => Navigate(1);
        Body.AddChild(_catalog);
        _editor = new ScenarioEditorStep();
        _editor.ProfileSelected += SelectProfile;
        _editor.SaveAsRequested += ConfirmSaveAs;
        _editor.OverwriteRequested += () => ExecuteEdit(() => ReplaceDraft(_library!.Overwrite(_draft!)));
        _editor.DeleteRequested += ConfirmDeleteConfiguration;
        _editor.BackRequested += () => Navigate(0);
        _editor.ContinueRequested += () => Navigate(2);
        _editor.Form.Edited += () => _feedback.Text = string.Join("\n", _draft!.Errors.Values);
        Body.AddChild(_editor);
        _result = new ScenarioResultStep(currentDriver, message => _feedback.Text = message);
        _result.StartRequested += Start;
        _result.AbortRequested += () => Abort("用户中止");
        _result.BackRequested += () => Navigate(1);
        Body.AddChild(_result);
        _confirmation = new ScenarioConfirmationPanel();
        _confirmation.Closed += () =>
        {
            UpdateStep();
            if (_step == 1) _editor.Form.RestoreFocus();
        };
        AddChild(_confirmation);
        try
        {
            _library = library ?? CreateLibrary();
            RefreshCatalog();
            _feedback.Text = string.Join("\n", _library.DiscoveryErrors);
        }
        catch (Exception error) when (IsConfigurationError(error))
        {
            _feedback.Text = "配置目录读取失败：" + error.Message;
        }
        UpdateStep();
        _currentDriver.RateChanged += OnCurrentRateChanged;
    }

    private static ScenarioConfigurationLibrary CreateLibrary()
    {
        var builtin = new Dictionary<string, byte[]>();
        foreach (string name in new[] { "wheat-basic-r1", "radish-three-r1", "current-radish-r1" })
        {
            string resource = "res://assets/development/scenario_configs/" + name.Replace('-', '_') + ".json";
            if (!Godot.FileAccess.FileExists(resource))
                throw new ScenarioConfigurationException("内置示例资源不存在：" + name);
            builtin.Add(name + ".json", Godot.FileAccess.GetFileAsBytes(resource));
        }
        string directory = ScenarioConfigurationLibrary.ResolveUserDirectory(ProjectSettings.GlobalizePath("res://"),
            OS.GetExecutablePath(), !OS.HasFeature("editor"));
        return new ScenarioConfigurationLibrary(builtin, directory);
    }

    public override void _ExitTree()
    {
        _currentDriver.RateChanged -= OnCurrentRateChanged;
        if (_scenario?.IsRunning == true) Abort("开发窗口宿主退出");
        base._ExitTree();
    }

    private bool IsRunning => _scenario?.IsRunning == true;
    private void RefreshCatalog() => _catalog.ShowFlows(_library?.Flows ?? Array.Empty<ScenarioFlowDefinition>(), _flowId);

    private void SelectFlow(string id)
    {
        if (IsRunning || _library == null) return;
        ExecuteEdit(() =>
        {
            if (_flowId != id)
            {
                ResetResults();
                _flowId = id;
                _draft = null;
                RefreshProfiles();
            }
            RefreshCatalog();
            UpdateStep();
        });
    }

    private void RefreshProfiles()
    {
        _entries = _flowId == null ? Array.Empty<ScenarioConfigurationEntry>() : _library!.ListConfigurations(_flowId);
        if (_draft == null || !_entries.Any(entry => entry.Id == _draft.Entry!.Id))
        {
            ResetResults();
            _draft = _entries.Count == 0 ? null : _library!.Open(_entries[0]);
        }
        _editor.ShowProfiles(_entries, _draft);
        _feedback.Text = string.Join("\n", _library!.DiscoveryErrors);
        RefreshSummary();
    }

    private void SelectProfile(int index)
    {
        if (IsRunning || index < 0 || index >= _entries.Count) return;
        ExecuteEdit(() =>
        {
            ScenarioConfigurationDraft next = _library!.Open(_entries[index]);
            if (_draft?.Entry != next.Entry) ResetResults();
            _draft = next;
            _editor.ShowProfiles(_entries, _draft);
            RefreshSummary();
            UpdateStep();
        });
    }

    private void ReplaceDraft(ScenarioConfigurationDraft draft)
    {
        ResetResults();
        _draft = draft;
        RefreshProfiles();
        _feedback.Text = "保存成功。";
        UpdateStep();
    }

    private void ConfirmSaveAs()
    {
        if (IsRunning || _draft == null) return;
        _confirmation.Open("为这份配置输入新名称。另存为从修订1开始，原配置保留。", name =>
            ExecuteEdit(() => ReplaceDraft(_library!.SaveAs(_draft, name))), enterName: true);
        UpdateStep();
    }

    private void ConfirmDeleteFlow(string id)
    {
        if (IsRunning || _library == null) return;
        ScenarioFlowDefinition flow = _library.Flows.Single(item => item.Id == id);
        _confirmation.Open("删除流程「" + flow.Name + "」？\n从目录移除，并删除此流程的所有可写配置；内置示例、历史报告和流程代码保留。", _ =>
            ExecuteEdit(() =>
            {
                _library.DeleteFlow(id);
                if (_flowId == id)
                {
                    _flowId = null;
                    _draft = null;
                    _entries = Array.Empty<ScenarioConfigurationEntry>();
                    _editor.ShowProfiles(_entries, null);
                    ResetResults();
                    _step = 0;
                }
                RefreshCatalog();
                _feedback.Text = "流程已从目录移除，其可写配置已删除。";
                UpdateStep();
            }, synchronizeDeletionFailure: true));
        UpdateStep();
    }

    private void ConfirmDeleteConfiguration()
    {
        if (IsRunning || _draft == null || _draft.Entry!.IsReadOnly) return;
        ScenarioConfigurationEntry entry = _draft.Entry;
        _confirmation.Open("删除配置「" + entry.CaseId + "」？\n删除此配置文件，已有报告保留。", _ =>
            ExecuteEdit(() =>
            {
                _library!.DeleteConfiguration(entry);
                _draft = null;
                RefreshProfiles();
                _feedback.Text = "配置文件已删除。";
                UpdateStep();
            }, synchronizeDeletionFailure: true));
        UpdateStep();
    }

    private void ExecuteEdit(Action action, bool synchronizeDeletionFailure = false)
    {
        if (IsRunning) return;
        try { action(); }
        catch (Exception error) when (IsConfigurationError(error))
        {
            string message = "配置操作失败：" + error.Message;
            if (synchronizeDeletionFailure)
            {
                try
                {
                    RefreshProfiles();
                    RefreshCatalog();
                    UpdateStep();
                    if (_step == 1) _editor.Form.RestoreFocus();
                }
                catch (Exception refreshError) when (IsConfigurationError(refreshError))
                {
                    message += "\n实际目录刷新失败：" + refreshError.Message;
                }
            }
            _feedback.Text = message;
        }
    }

    private void ResetResults()
    {
        _scenario = null;
        _independentDriver = null;
        _runDirectory = null;
        _reportAttempted = false;
        _result.Summary.Text = "选择已保存配置后运行。";
        _result.Progress.Text = "尚未运行";
        _result.ReportPath.Text = "报告尚未生成";
    }

    private static bool IsConfigurationError(Exception error) => error is ScenarioConfigurationException or IOException or UnauthorizedAccessException;

    private void Navigate(int destination)
    {
        if (IsRunning || _confirmation.Visible || destination > 0 && _flowId == null || destination == 2 && _draft == null) return;
        _step = destination;
        UpdateStep();
        if (destination == 1) _editor.Form.RestoreFocus();
    }

    private void RefreshSummary()
    {
        _result.Summary.Text = _draft == null ? "没有可运行配置。" : _draft.Entry!.CaseId + " · 修订" + _draft.Entry.Revision +
            (_draft.Entry.IsReadOnly ? " · 内置只读，请先另存为" : "");
    }

    private void UpdateStep()
    {
        bool locked = IsRunning || _confirmation.Visible;
        _catalog.Visible = _step == 0;
        _editor.Visible = _step == 1;
        _result.Visible = _step == 2;
        for (int index = 0; index < _steps.Length; index++)
        {
            _steps[index].Disabled = locked || index == 1 && _flowId == null || index == 2 && _draft == null;
            _steps[index].Text = new[] { "1 选择流程", "2 编辑配置", "3 运行与结果" }[index] + (index == _step ? " ●" : "");
        }
        _catalog.SetLocked(locked);
        _editor.UpdateState(locked);
        _result.SetRunning(locked, _draft != null && !_draft.Entry!.IsReadOnly);
        if (_confirmation.Visible) _result.AbortButton.Disabled = true;
    }

    private void Start()
    {
        if (IsRunning || _draft == null || _library == null) return;
        try
        {
            ScenarioConfiguration configuration = _library.LoadForRun(_draft);
            string root = ScenarioReport.ResolveRoot(ProjectSettings.GlobalizePath("res://"), OS.GetExecutablePath(), !OS.HasFeature("editor"));
            _runDirectory = ScenarioReport.CreateRunDirectory(root);
            _reportAttempted = false;
            _isCurrent = configuration.Target == "current";
            _scenario = BuyProcessSellScenario.Start(configuration, _isCurrent ? _currentGame : null);
            _independentDriver = _isCurrent ? null : new SimulationDriver();
            _observedPaused = _scenario.Game.IsPaused;
            if (_scenario.IsRunning) ApplyScenarioRate();
            if (_isCurrent) _refreshCurrent();
            _feedback.Text = "";
            _result.ReportPath.Text = "本次报告目录：" + _runDirectory;
            _result.Summary.Text = configuration.CaseId + " · 修订" + configuration.Revision;
            _step = 2;
            RefreshProgress();
        }
        catch (Exception error) when (IsConfigurationError(error))
        {
            _feedback.Text = (error is ScenarioConfigurationException ? "启动失败（配置错误）：" : "启动失败（输出错误）：") + error.Message;
        }
    }

    private void OnCurrentRateChanged(double rate, SimulationRateSource source)
    {
        if (_isCurrent && IsRunning)
        {
            _scenario!.ObserveTime(rate, source.ToString(), _scenario.Game.IsPaused);
            if (source == SimulationRateSource.Player) Abort("玩家主动改速，中断自动流程");
        }
        _refreshCurrent();
    }

    private void ApplyScenarioRate()
    {
        SimulationDriver driver = _isCurrent ? _currentDriver : _independentDriver!;
        bool changed = driver.Rate != _scenario!.CurrentRateIntent;
        if (changed) driver.SetDevelopmentRate(_scenario.CurrentRateIntent, SimulationRateSource.Scenario);
        if (!_isCurrent || !changed) _scenario.ObserveTime(driver.Rate, SimulationRateSource.Scenario.ToString(), _scenario.Game.IsPaused);
    }

    internal uint GetCurrentMaxTicks() => _isCurrent && IsRunning ? _scenario!.GetMaxAdvanceTicks() : uint.MaxValue;

    internal bool ObserveCurrentCheckpoint(SimulationCheckpoint point)
    {
        if (!_isCurrent || !IsRunning) return true;
        bool keepGoing = _scenario!.ObserveCheckpoint(point);
        if (IsRunning) ApplyScenarioRate();
        RefreshProgress();
        return keepGoing;
    }

    internal void AdvanceIndependent(double delta)
    {
        if (!IsRunning) return;
        if (_observedPaused != _scenario!.Game.IsPaused)
        {
            _observedPaused = _scenario.Game.IsPaused;
            _scenario.ObserveTime((_isCurrent ? _currentDriver : _independentDriver!).Rate, SimulationRateSource.Scenario.ToString(), _scenario.Game.IsPaused);
        }
        if (!_isCurrent)
        {
            try
            {
                _independentDriver!.Advance(delta, _scenario.Game, point =>
                {
                    bool keepGoing = _scenario.ObserveCheckpoint(point);
                    if (IsRunning) ApplyScenarioRate();
                    return keepGoing;
                }, () => IsRunning ? _scenario.GetMaxAdvanceTicks() : 0);
            }
            catch (InvalidOperationException error) { Abort("经营推进被拒绝：" + error.Message); }
        }
        RefreshProgress();
    }

    internal void Abort(string reason)
    {
        if (!IsRunning) return;
        _scenario!.Abort(reason);
        RefreshProgress();
    }

    private void RefreshProgress()
    {
        if (_scenario == null) return;
        UpdateStep();
        _result.Progress.Text = $"{(_isCurrent ? "当前局" : "独立局")} · {_scenario.Progress}\n" +
            $"实际倍率 {(_isCurrent ? _currentDriver : _independentDriver!)?.Rate}× · " +
            (_scenario.Game.IsPaused && IsRunning ? "暂停，等待继续" : OutcomeText(_scenario.Outcome));
        if (IsRunning || _reportAttempted) return;
        _reportAttempted = true;
        try
        {
            _result.ReportPath.Text = "报告：" + _scenario.WriteReport(_runDirectory!);
            _feedback.Text = _scenario.Report.Reason ?? "流程结束，检查结果见JSON报告。";
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
