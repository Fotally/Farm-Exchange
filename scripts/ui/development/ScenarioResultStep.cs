using System;
using System.Globalization;
using Godot;
using FarmExchange.Time;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

internal partial class ScenarioResultStep : VBoxContainer
{
    internal readonly Label Progress;
    internal readonly Label ReportPath;
    internal readonly Label Summary;
    internal readonly Button StartButton;
    internal readonly Button AbortButton;
    private readonly Button _back;
    private readonly Label _flowTitle;
    internal event Action? StartRequested;
    internal event Action? AbortRequested;
    internal event Action? BackRequested;

    internal ScenarioResultStep(SimulationDriver currentDriver, Action<string> feedback)
    {
        Name = "ScenarioResultStep";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 14);
        var intro = new VBoxContainer();
        intro.AddThemeConstantOverride("separation", 0);
        AddChild(intro);
        _flowTitle = MakeLabel("运行与结果", 24, Ink);
        _flowTitle.Name = "ScenarioResultFlowTitle";
        intro.AddChild(_flowTitle);
        Summary = MakeLabel("选择已保存配置后运行。", 14, Muted);
        Summary.Name = "ScenarioRunConfigurationLabel";
        intro.AddChild(Summary);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(content);
        var panel = new PanelContainer { Name = "ScenarioResultPanel" };
        StyleBoxFlat panelStyle = Style(new Color("e4dfbc"), 0);
        panelStyle.BorderColor = new Color("b7ad75");
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        content.AddChild(panel);
        var resultBody = new VBoxContainer();
        WrapMargin(panel, 15, 15).AddChild(resultBody);
        Progress = MakeLabel("尚未运行", 15, Ink);
        Progress.Name = "ScenarioProgressLabel";
        resultBody.AddChild(Progress);
        ReportPath = MakeLabel("报告尚未生成", 12, Muted);
        ReportPath.Name = "ScenarioReportPathLabel";
        resultBody.AddChild(ReportPath);
        content.AddChild(MakeLabel("现场暂停时等待继续；主动改速中断现场流程，已经发生的交易与生产保留。", 13, Muted));
        content.AddChild(MakeLabel("当前局经营倍率", 14, Ink));
        var rates = new HBoxContainer();
        content.AddChild(rates);
        foreach (double rate in new[] { 0.5, 1, 2, 5, 10, 20 })
        {
            Button button = MakeQuietButton(rate.ToString(CultureInfo.InvariantCulture) + "×", 70, 38);
            button.Name = "DevelopmentRate" + rate.ToString(CultureInfo.InvariantCulture).Replace(".", "_") + "Button";
            button.Pressed += () => currentDriver.SetDevelopmentRate(rate);
            rates.AddChild(button);
        }
        var custom = new HBoxContainer();
        content.AddChild(custom);
        var value = new LineEdit { Name = "DevelopmentRateInput", PlaceholderText = "正整数倍率", CustomMinimumSize = new Vector2(180, 38) };
        custom.AddChild(value);
        Button apply = MakeQuietButton("应用倍率", 120, 38);
        apply.Name = "DevelopmentRateApplyButton";
        apply.Pressed += () =>
        {
            if (!double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) ||
                !SimulationDriver.IsDevelopmentRateAllowed(rate))
            {
                feedback("倍率只接受0.5、1、2或有限正整数。");
                return;
            }
            currentDriver.SetDevelopmentRate(rate);
        };
        custom.AddChild(apply);
        var separator = new HSeparator { Name = "ScenarioResultFooterSeparator" };
        separator.AddThemeConstantOverride("separation", 1);
        separator.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color("bba573"), Thickness = 1 });
        AddChild(separator);
        var actions = new HBoxContainer();
        AddChild(actions);
        _back = MakeSecondaryButton("← 返回配置", 140, 43);
        _back.Name = "ScenarioResultBackButton";
        _back.Pressed += () => BackRequested?.Invoke();
        actions.AddChild(_back);
        actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        StartButton = MakeButton("启动流程", Mid, 140, 43);
        StartButton.Name = "ScenarioStartButton";
        StartButton.Pressed += () => StartRequested?.Invoke();
        actions.AddChild(StartButton);
        AbortButton = MakeQuietButton("中止流程", 140, 43);
        AbortButton.Name = "ScenarioAbortButton";
        AbortButton.Pressed += () => AbortRequested?.Invoke();
        actions.AddChild(AbortButton);
        foreach (Button button in new[] { _back, StartButton, AbortButton }) button.AddThemeFontSizeOverride("font_size", 16);
        SetRunning(false, false);
    }

    internal void ShowFlow(string name) => _flowTitle.Text = name.Length == 0 ? "运行与结果" : name;

    internal void SetRunning(bool running, bool runnable)
    {
        _back.Disabled = running;
        StartButton.Disabled = running || !runnable;
        AbortButton.Disabled = !running;
    }
}
