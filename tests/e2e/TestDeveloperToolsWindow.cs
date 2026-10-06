using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Development;
using FarmExchange.UI;

public partial class TestDeveloperToolsWindow : Node
{
    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool passed = await RunChecksAsync(this);
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        main.SetProcess(false);
        string directory = ProjectSettings.GlobalizePath("res://build/developer-window-tests");
        Directory.CreateDirectory(directory);
        try
        {
            var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
            var window = Find<DeveloperToolsWindow>(ui, "DeveloperToolsWindow");
            await ClickNative(parent, Find<Button>(ui, "DeveloperToolsButton"));
            if (!window.IsVisibleInTree()) return Fail("开发入口原生点击被近况面板或地图遮挡，未打开窗口");
            Rect2 bounds = window.GetGlobalRect();
            Rect2 viewport = parent.GetViewport().GetVisibleRect();
            if (bounds.Position.Y < 143 || bounds.End.Y > viewport.End.Y - 180 ||
                bounds.Position.X < 31 || bounds.End.X > viewport.End.X - 31)
                return Fail("开发窗口超出1080P可用区域");
            var input = Find<LineEdit>(window, "ScenarioConfigurationPath");
            var start = Find<Button>(window, "ScenarioStartButton");
            var abort = Find<Button>(window, "ScenarioAbortButton");
            uint beforeTicks = main.Game.Calendar.ElapsedSeconds;
            int beforeBalance = main.Game.MoneyCents;
            input.Text = Path.Combine(directory, "missing.json");
            start.EmitSignal(Button.SignalName.Pressed);
            if (!Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("启动失败") ||
                main.Game.MoneyCents != beforeBalance || main.Game.Calendar.ElapsedSeconds != beforeTicks)
                return Fail("文件选择错误未明确反馈或修改了经营状态");
            string file = Path.Combine(directory, "independent.json");
            File.WriteAllText(file, """
                {"schemaVersion":1,"caseId":"window-independent","revision":1,"flow":"buy-process-sell",
                 "run":{"target":"independent","seed":12345},
                 "execution":{"timePlan":[{"endDate":"01-04-01","rate":20}]},
                 "parameters":{"rawCommodity":"Radish.Raw","quantity":3,"processorAnchor":{"x":0,"y":0},
                 "processingWaitLimitTicks":1000,"orderWaitLimitTicks":10}}
                """);
            Find<Button>(window, "ScenarioChooseFileButton").EmitSignal(Button.SignalName.Pressed);
            var dialog = window.GetNode<FileDialog>("ScenarioFileDialog");
            if (!dialog.Visible || dialog.FileMode != FileDialog.FileModeEnum.OpenFile ||
                dialog.Access != FileDialog.AccessEnum.Filesystem)
                return Fail("文件选择按钮没有打开真实文件选择窗口");
            dialog.EmitSignal(FileDialog.SignalName.FileSelected, file);
            dialog.Hide();
            if (input.Text != file) return Fail("文件选择回调没有使用真实选中文件");
            start.EmitSignal(Button.SignalName.Pressed);
            if (!start.Disabled || abort.Disabled) return Fail("真实启动没有进入运行状态");
            window.AdvanceIndependent(5);
            if (DisplayServer.GetName() != "headless")
            {
                await Frames(parent);
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                parent.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "independent-report.png"));
            }
            string reportText = Find<Label>(window, "ScenarioReportPathLabel").Text;
            if (!reportText.StartsWith("报告：") || !File.Exists(reportText[3..]) || start.Disabled || !abort.Disabled)
                return Fail("独立局未通过真实控件完成流程并输出报告：" + reportText);
            if (main.Game.MoneyCents != beforeBalance || main.Game.Calendar.ElapsedSeconds != beforeTicks)
                return Fail("独立运行更换或推进了当前局");

            var processor = System.Linq.Enumerable.First(main.Game.GetBuildingSpaces(),
                space => space.Building == FarmExchange.Gameplay.BuildingKind.Processor);
            var plot = main.Game.GetPlot(processor.AnchorCell);
            string current = Path.Combine(directory, "current.json");
            File.WriteAllText(current, $$$"""
                {"schemaVersion":1,"caseId":"window-current","revision":1,"flow":"buy-process-sell",
                 "run":{"target":"current"},
                 "execution":{"timePlan":[{"endDate":"01-04-01","rate":10}]},
                 "parameters":{"rawCommodity":"{{{plot.CropKind}}}.Raw","quantity":1,
                  "processorAnchor":{"x":{{{processor.AnchorCell.X}}},"y":{{{processor.AnchorCell.Y}}}},
                  "processingWaitLimitTicks":1000,"orderWaitLimitTicks":10}}
                """);
            input.Text = current;
            main.Game.SetPaused(true);
            start.EmitSignal(Button.SignalName.Pressed);
            main.AdvanceSimulation(100);
            window.AdvanceIndependent(100);
            if (!start.Disabled || abort.Disabled || main.Game.Calendar.ElapsedSeconds != beforeTicks ||
                !Find<Label>(window, "ScenarioProgressLabel").Text.Contains("暂停"))
                return Fail("现场流程绕过玩家暂停或未报告等待");
            Find<OptionButton>(ui, "SimulationRateChoice").EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            reportText = Find<Label>(window, "ScenarioReportPathLabel").Text;
            if (main.Driver.Rate != 2 || start.Disabled || !abort.Disabled ||
                !reportText.StartsWith("报告：") || !File.ReadAllText(reportText[3..]).Contains("Aborted"))
                return Fail("手动改速没有中断整个现场流程并保留玩家倍率");
            start.EmitSignal(Button.SignalName.Pressed);
            if (!start.Disabled) return Fail("中止后不能重新运行持久配置");
            string pendingDirectory = Find<Label>(window, "ScenarioReportPathLabel").Text[7..];
            File.WriteAllText(Path.Combine(pendingDirectory, "report.json"), "existing-report");
            abort.EmitSignal(Button.SignalName.Pressed);
            if (!Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("输出错误") ||
                Find<Label>(window, "ScenarioReportPathLabel").Text.StartsWith("报告：") || start.Disabled || !abort.Disabled)
                return Fail("报告落盘失败未明确反馈、伪报保存或丢失结束状态");
            return true;
        }
        finally
        {
            main.QueueFree();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static async Task ClickNative(Node parent, Button button)
    {
        await Frames(parent);
        Viewport viewport = parent.GetViewport();
        bool graphical = DisplayServer.GetName() != "headless";
        Vector2I originalMouse = graphical ? DisplayServer.MouseGetPosition() : default;
        Vector2 position = button.GetGlobalRect().GetCenter();
        void Send(InputEventMouse input)
        {
            var actual = (InputEventMouse)input.XformedBy(viewport.GetFinalTransform());
            actual.GlobalPosition = viewport.GetFinalTransform() * input.GlobalPosition;
            if (graphical && input is InputEventMouseMotion) Input.WarpMouse(actual.Position);
            Input.ParseInputEvent(actual);
            Input.FlushBufferedEvents();
        }
        try
        {
            Send(new InputEventMouseMotion { Position = position, GlobalPosition = position });
            await Frames(parent);
            Send(new InputEventMouseButton
            {
                Position = position,
                GlobalPosition = position,
                ButtonIndex = MouseButton.Left,
                ButtonMask = MouseButtonMask.Left,
                Pressed = true,
            });
            await Frames(parent);
            Send(new InputEventMouseButton
            {
                Position = position,
                GlobalPosition = position,
                ButtonIndex = MouseButton.Left,
                Pressed = false,
            });
            await Frames(parent);
        }
        finally
        {
            if (graphical) Input.WarpMouse(originalMouse - DisplayServer.WindowGetPosition());
        }
    }

    private static async Task Frames(Node parent)
    {
        for (int i = 0; i < 3; i++)
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static T Find<T>(Node root, string name) where T : Node =>
        (T)(root.FindChild(name, true, false) ?? throw new InvalidOperationException("控件缺失：" + name));
    private static bool Fail(string message) { GD.PushError(message); return false; }
}
