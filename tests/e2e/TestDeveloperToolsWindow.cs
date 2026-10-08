using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Development;
using FarmExchange.Gameplay;
using FarmExchange.Logging;
using FarmExchange.Time;
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
        string directory = ProjectSettings.GlobalizePath("res://build/developer-window-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
            var defaultWindow = Find<DeveloperToolsWindow>(ui, "DeveloperToolsWindow");
            await ClickNative(parent, Find<Button>(ui, "DeveloperToolsButton"));
            if (!defaultWindow.IsVisibleInTree()) return Fail("开发入口原生点击被近况面板或地图遮挡，未打开窗口");
            Rect2 bounds = defaultWindow.GetGlobalRect();
            Rect2 viewport = parent.GetViewport().GetVisibleRect();
            if (bounds.Position.Y < 143 || bounds.End.Y > viewport.End.Y - 180 ||
                bounds.Position.X < 31 || bounds.End.X > viewport.End.X - 31)
                return Fail("开发窗口超出1080P可用区域");
            defaultWindow.Hide();
            uint beforeTicks = main.Game.Calendar.ElapsedSeconds;
            int beforeBalance = main.Game.MoneyCents;
            string file = Path.Combine(directory, "independent.json");
            string independentJson = """
                {"schemaVersion":1,"caseId":"window-independent","revision":1,"flow":"buy-process-sell",
                 "run":{"target":"independent","seed":12345},
                 "execution":{"timePlan":[{"endDate":"01-04-01","rate":20}]},
                 "parameters":{"rawCommodity":"Radish.Raw","quantity":1,"processorAnchor":{"x":0,"y":0},
                 "processingWaitLimitTicks":1000,"orderWaitLimitTicks":10}}
                """;
            File.WriteAllText(file, independentJson);
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
            var library = new ScenarioConfigurationLibrary(new Dictionary<string, byte[]>(), directory);
            var window = new DeveloperToolsWindow(main.Game, main.Driver, () => { }, library);
            window.SetInitialPlacement(false);
            ui.AddChild(window);
            window.ShowRaised();
            Find<Button>(window, "ScenarioSelectFlow_buy-process-sell").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioCatalogContinueButton").EmitSignal(Button.SignalName.Pressed);
            SelectProfile(window, "window-independent");
            Find<Button>(window, "ScenarioEditorContinueButton").EmitSignal(Button.SignalName.Pressed);
            var start = Find<Button>(window, "ScenarioStartButton");
            var abort = Find<Button>(window, "ScenarioAbortButton");
            File.Delete(file);
            start.EmitSignal(Button.SignalName.Pressed);
            if (!Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("启动失败") ||
                main.Game.MoneyCents != beforeBalance || main.Game.Calendar.ElapsedSeconds != beforeTicks)
                return Fail("运行前配置文件缺失未明确反馈或修改了经营状态");
            File.WriteAllText(file, independentJson);
            start.EmitSignal(Button.SignalName.Pressed);
            if (!start.Disabled || abort.Disabled) return Fail("真实启动没有进入运行状态");
            byte[] runningBytes = File.ReadAllBytes(file);
            string changedJson = independentJson.Replace("\"quantity\":1", "\"quantity\":4");
            File.WriteAllText(file, changedJson);
            window.AdvanceIndependent(100);
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

            if (!CheckReport(reportText[3..], runningBytes, 1)) return false;
            start.EmitSignal(Button.SignalName.Pressed);
            if (!Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("配置文件已改变") || start.Disabled)
                return Fail("外部变更未拒绝旧凭据启动");
            Find<Button>(window, "ScenarioResultBackButton").EmitSignal(Button.SignalName.Pressed);
            SelectProfile(window, "window-independent");
            if (Find<LineEdit>(window, "ScenarioField_parameters_quantity").Text != "4")
                return Fail("外部quantity=1→4后显式重选仍使用旧目录凭据");
            Find<Button>(window, "ScenarioEditorContinueButton").EmitSignal(Button.SignalName.Pressed);
            byte[] reloadedBytes = File.ReadAllBytes(file);
            start.EmitSignal(Button.SignalName.Pressed);
            window.AdvanceIndependent(100);
            reportText = Find<Label>(window, "ScenarioReportPathLabel").Text;
            if (!reportText.StartsWith("报告：") || !CheckReport(reportText[3..], reloadedBytes, 4)) return false;
            GD.Print("#118 真实窗口外部重选、固定参数与原字节报告摘要检查通过");
            Find<Button>(window, "ScenarioResultBackButton").EmitSignal(Button.SignalName.Pressed);
            SelectProfile(window, "window-current");
            Find<Button>(window, "ScenarioEditorContinueButton").EmitSignal(Button.SignalName.Pressed);
            main.Game.SetPaused(true);
            start.EmitSignal(Button.SignalName.Pressed);
            main.AdvanceSimulation(100);
            window.AdvanceIndependent(100);
            if (!start.Disabled || abort.Disabled || main.Game.Calendar.ElapsedSeconds != beforeTicks ||
                !Find<Label>(window, "ScenarioProgressLabel").Text.Contains("暂停"))
                return Fail("现场流程绕过玩家暂停或未报告等待");
            await ClickNative(parent, Find<Button>(ui, "SimulationRateButton"));
            reportText = Find<Label>(window, "ScenarioReportPathLabel").Text;
            if (main.Driver.Rate != 1 || start.Disabled || !abort.Disabled ||
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
            Find<Button>(window, "ScenarioResultBackButton").EmitSignal(Button.SignalName.Pressed);
            SelectProfile(window, "window-independent");
            var quantity = Find<LineEdit>(window, "ScenarioField_parameters_quantity");
            quantity.Text = "2";
            quantity.EmitSignal(LineEdit.SignalName.TextChanged, "2");
            string newestJson = independentJson.Replace("\"quantity\":1", "\"quantity\":5");
            File.WriteAllText(file, newestJson);
            byte[] newestBytes = File.ReadAllBytes(file);
            Find<Button>(window, "ScenarioOverwriteButton").EmitSignal(Button.SignalName.Pressed);
            if (!Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("配置文件已改变") ||
                quantity.Text != "2" || !File.ReadAllBytes(file).SequenceEqual(newestBytes))
                return Fail("旧脏草稿覆盖外部文件，或冲突后丢失草稿");
            var profiles = Find<OptionButton>(window, "ScenarioProfileChoice");
            if (!profiles.AllowReselect) return Fail("配置下拉不允许真实用户重选当前项");
            int originalSelection = profiles.Selected;
            SelectProfile(window, "window-current");
            if (!Find<Control>(window, "ScenarioConfirmationPanel").Visible || profiles.Selected != originalSelection ||
                Find<Button>(window, "ScenarioConfirmationConfirmButton").Text != "确认重新加载")
                return Fail("脏草稿重选缺少明确放弃确认，或确认前改变原选择");
            await ClickNative(parent, Find<Button>(window, "ScenarioConfirmationCancelButton"));
            if (!ReferenceEquals(quantity, Find<LineEdit>(window, "ScenarioField_parameters_quantity")) ||
                quantity.Text != "2" || profiles.Selected != originalSelection)
                return Fail("取消重新加载丢失草稿、控件或原选择");
            SelectProfile(window, "window-independent");
            await ClickNative(parent, Find<Button>(window, "ScenarioConfirmationConfirmButton"));
            if (Find<LineEdit>(window, "ScenarioField_parameters_quantity").Text != "5" ||
                !File.ReadAllBytes(file).SequenceEqual(newestBytes))
                return Fail("确认重新加载未显示最新值或修改外部文件");
            File.WriteAllText(file, "{}");
            SelectProfile(window, "window-independent");
            if (!CheckInvalidSelection(window, 1) || !Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("independent.json"))
                return Fail("外部非法配置没有同步目录、清空选择与错误");
            SelectProfile(window, "window-current");
            File.Delete(current);
            SelectProfile(window, "window-current");
            if (!CheckInvalidSelection(window, 0)) return Fail("外部删除仍保留可运行的过期引用");
            GD.Print("#118 脏草稿冲突保护、确认取消与重载、非法及删除目录同步检查通过");
            return await CheckShutdownAsync(parent, directory);
        }
        finally
        {
            main.QueueFree();
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static async Task<bool> CheckShutdownAsync(Node parent, string directory)
    {
        foreach (bool independent in new[] { true, false })
        {
            using var text = new StringWriter();
            using var log = RuntimeLog.Capture(text);
            using var game = new FarmGame(12345, log);
            foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
            if (game.BuildProcessor(Vector2I.Zero, CropKind.Radish) != null) return Fail("退出夹具建造失败");
            game.SetPaused(true);
            var driver = new SimulationDriver(game.Log!.Time);
            string configDirectory = Path.Combine(directory, independent ? "shutdown-independent" : "shutdown-current");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, "case.json"),
                "{\"schemaVersion\":1,\"caseId\":\"shutdown\",\"revision\":1,\"flow\":\"buy-process-sell\"," +
                "\"run\":{\"target\":\"" + (independent ? "independent\",\"seed\":12345" : "current\"") + "}," +
                "\"execution\":{\"timePlan\":[{\"endDate\":\"01-04-01\",\"rate\":1}]}," +
                "\"parameters\":{\"rawCommodity\":\"Radish.Raw\",\"quantity\":1,\"processorAnchor\":{\"x\":0,\"y\":0}," +
                "\"processingWaitLimitTicks\":1000,\"orderWaitLimitTicks\":10}}");
            var library = new ScenarioConfigurationLibrary(new Dictionary<string, byte[]>(), configDirectory);
            var window = new DeveloperToolsWindow(game, driver, () => { }, library, log);
            parent.AddChild(window);
            Find<Button>(window, "ScenarioSelectFlow_buy-process-sell").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioCatalogContinueButton").EmitSignal(Button.SignalName.Pressed);
            SelectProfile(window, "shutdown");
            Find<Button>(window, "ScenarioEditorContinueButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioStartButton").EmitSignal(Button.SignalName.Pressed);
            string output = Find<Label>(window, "ScenarioReportPathLabel").Text[7..];
            if (!independent)
            {
                // 与Main退出时主动通知的同一入口；随后真实ExitTree再调用也不重复。
                window.ShutdownScenario();
                window.ShutdownScenario();
            }
            window.QueueFree();
            await Frames(parent);
            string[] records = text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string[] finished = records.Where(line => line.Contains("EventName=ScenarioFinished ")).ToArray();
            string[] saved = records.Where(line => line.Contains("EventName=ScenarioReportSaved ")).ToArray();
            string[] ended = records.Where(line => line.Contains("EventName=GameEnded ")).ToArray();
            if (!File.Exists(Path.Combine(output, "report.json")) || finished.Length != 1 || saved.Length != 1 ||
                !finished[0].Contains("ScenarioOutcome: \"Aborted\"") || ended.Length != (independent ? 1 : 0) ||
                independent && Array.IndexOf(records, saved[0]) >= Array.IndexOf(records, ended[0]))
                return Fail("宿主退出未先终结/保存再释放独立局，或原地主局被错误释放");
            game.SetPaused(false);
            game.AdvanceTick();
            if (game.Calendar.ElapsedSeconds != 1) return Fail("退出开发窗口影响主局继续推进");
        }
        return true;
    }

    private static bool CheckInvalidSelection(Node window, int count) =>
        Find<OptionButton>(window, "ScenarioProfileChoice").ItemCount == count &&
        Find<OptionButton>(window, "ScenarioProfileChoice").Selected == -1 &&
        !Find<Control>(window, "ScenarioConfigurationForm").Visible &&
        Find<Button>(window, "ScenarioEditorContinueButton").Disabled &&
        Find<Button>(window, "ScenarioStartButton").Disabled &&
        Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("不再合法");

    private static bool CheckReport(string path, byte[] bytes, int quantity)
    {
        using JsonDocument report = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = report.RootElement;
        if (root.GetProperty("outcome").GetString() != "Passed" ||
            root.GetProperty("configuration").GetProperty("sha256").GetString() != Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() ||
            root.GetProperty("produced").GetInt64() != quantity ||
            root.GetProperty("operations").EnumerateArray().First(operation => operation.GetProperty("name").GetString() == "Buy")
                .GetProperty("request").GetProperty("quantity").GetInt32() != quantity)
            return Fail("窗口报告未保持启动参数、实际产量或原文件SHA-256");
        return true;
    }

    private static void SelectProfile(Node window, string caseId)
    {
        OptionButton profiles = Find<OptionButton>(window, "ScenarioProfileChoice");
        for (int index = 0; index < profiles.ItemCount; index++)
        {
            if (!profiles.GetItemText(index).Contains(caseId)) continue;
            profiles.Select(index);
            profiles.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
            return;
        }
        throw new InvalidOperationException("配置目录缺失：" + caseId);
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
