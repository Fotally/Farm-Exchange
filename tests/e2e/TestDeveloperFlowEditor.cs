using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FarmExchange.Development;
using FarmExchange.UI;
using Godot;

public partial class TestDeveloperFlowEditor : Node
{
    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit(await RunChecksAsync(this) ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        string root = ProjectSettings.GlobalizePath("res://build/issue104-validation");
        Directory.CreateDirectory(root);
        string user = Path.Combine(root, "editor-users-" + Guid.NewGuid().ToString("N"));
        var builtin = new Dictionary<string, byte[]>();
        foreach (string name in new[] { "wheat-basic-r1", "radish-three-r1", "current-radish-r1" })
            builtin[name + ".json"] = File.ReadAllBytes(ProjectSettings.GlobalizePath("res://tests/scenario-configs/buy-process-sell/" + name + ".json"));
        var library = new ScenarioConfigurationLibrary(builtin, user);
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        main.SetProcess(false);
        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        Find<DeveloperToolsWindow>(ui, "DeveloperToolsWindow").Hide();
        var window = new DeveloperToolsWindow(main.Game, main.Driver, () => { }, library);
        ui.AddChild(window);
        window.ShowRaised();
        var rectangles = new Dictionary<string, object>();
        try
        {
            await Frames(parent);
            rectangles["main"] = Rectangles(main, "DeveloperToolsButton", "SimulationRateButton", "PauseButton");
            var cards = Find<GridContainer>(window, "ScenarioFlowCards");
            if (cards.Columns != 2 || library.Flows.Count != 1) return Fail("真实目录必须按两列显示唯一已实现流程");
            Button select = Find<Button>(window, "ScenarioSelectFlow_buy-process-sell");
            Label[] cardLabels = select.FindChildren("*", "Label", true, false).Cast<Label>().ToArray();
            if (cardLabels.Length != 2 || cardLabels[0].GetThemeFontSize("font_size") != 15 || cardLabels[1].GetThemeFontSize("font_size") != 12)
                return Fail("流程名称与简介未按15/12小字号排版");
            rectangles["catalog"] = Rectangles(window, "ScenarioSelectFlow_buy-process-sell", "ScenarioDeleteFlow_buy-process-sell", "ScenarioCatalogContinueButton", "ScenarioFlowSearch", "ScenarioFlowCategory");
            await Capture(parent, root, "catalog-1920.png");
            await Click(parent, Find<Button>(window, "ScenarioDeleteFlow_buy-process-sell"));
            if (!Find<Control>(window, "ScenarioConfirmationPanel").Visible || !Find<Button>(window, "ScenarioCatalogContinueButton").Disabled)
                return Fail("垃圾桶同时选择了流程或没有确认弹窗");
            rectangles["deleteDialog"] = Rectangles(window, "ScenarioConfirmationCancelButton", "ScenarioConfirmationConfirmButton");
            await Capture(parent, root, "flow-delete-1920.png");
            await Click(parent, Find<Button>(window, "ScenarioConfirmationCancelButton"));
            if (library.Flows.Count != 1 || Directory.Exists(user)) return Fail("流程取消删除仍修改配置库");
            var search = Find<LineEdit>(window, "ScenarioFlowSearch");
            SetText(search, "找不到的流程");
            if (cards.FindChild("ScenarioSelectFlow_buy-process-sell", true, false) != null) return Fail("搜索无结果仍显示旧卡片");
            SetText(search, "委托");
            await Click(parent, Find<Button>(window, "ScenarioSelectFlow_buy-process-sell"));
            await Click(parent, Find<Button>(window, "ScenarioCatalogContinueButton"));
            var profiles = Find<OptionButton>(window, "ScenarioProfileChoice");
            if (profiles.ItemCount != 3 || !Find<Button>(window, "ScenarioOverwriteButton").Disabled || !Find<Button>(window, "ScenarioDeleteConfigurationButton").Disabled)
                return Fail("内置只读示例仍允许覆盖或删除");
            Label[] headings = Find<ScenarioConfigurationForm>(window, "ScenarioConfigurationForm").FindChildren("*", "Label", true, false).Cast<Label>().ToArray();
            if (headings.Count(label => label.Text == "运行对象") != 1 || headings.Count(label => label.Text == "时间安排") != 1)
                return Fail("自动表单重复显示相同的分组或父子字段标题");
            Button[] steps = Enumerable.Range(1, 3).Select(index => Find<Button>(window, "ScenarioStep" + index + "Button")).ToArray();
            if (steps.Any(button => button.SizeFlagsHorizontal != Control.SizeFlags.ExpandFill) || steps.Max(button => button.Size.X) - steps.Min(button => button.Size.X) > 1)
                return Fail("三个分步导航未平均铺满可用宽度");
            rectangles["profiles"] = Enumerable.Range(0, profiles.ItemCount).Select(index => new { caseId = profiles.GetItemText(index).Split(" · ")[0], index }).ToArray();
            rectangles["editor"] = Rectangles(window, "ScenarioProfileChoice", "ScenarioSaveAsButton", "ScenarioOverwriteButton", "ScenarioDeleteConfigurationButton", "ScenarioEditorContinueButton", "ScenarioEditorBackButton");
            await Capture(parent, root, "editor-1920.png");
            await Click(parent, Find<Button>(window, "ScenarioSaveAsButton"));
            rectangles["saveDialog"] = Rectangles(window, "ScenarioSaveNameInput", "ScenarioSaveConfirmButton", "ScenarioConfirmationCancelButton");
            await Capture(parent, root, "save-as-1920.png");
            await Click(parent, Find<Button>(window, "ScenarioConfirmationCancelButton"));

            Choose(profiles, "current-radish");
            if (Find<LineEdit>(window, "ScenarioField_run_seed").IsVisibleInTree()) return Fail("当前局错误显示独立种子");
            var target = Find<OptionButton>(window, "ScenarioField_run_target");
            target.Select(0);
            target.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            if (!Find<LineEdit>(window, "ScenarioField_run_seed").IsVisibleInTree()) return Fail("条件字段未响应运行对象变化");
            Choose(profiles, "radish-three");
            var quantity = Find<LineEdit>(window, "ScenarioField_parameters_quantity");
            SetText(quantity, "1.5");
            if (!Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("quantity")) return Fail("非法整数未定位到字段");
            SetText(quantity, "3");
            await Click(parent, Find<Button>(window, "ScenarioSaveAsButton"));
            SetText(Find<LineEdit>(window, "ScenarioSaveNameInput"), "界面保存验证");
            await Click(parent, Find<Button>(window, "ScenarioSaveConfirmButton"));
            ScenarioConfigurationEntry saved = library.ListConfigurations("buy-process-sell").Single(entry => entry.CaseId == "界面保存验证");
            if (saved.Revision != 1 || saved.IsReadOnly || !File.Exists(saved.Id) || profiles.ItemCount != 4)
                return Fail("另存为没有生成可写修订1配置");
            quantity = Find<LineEdit>(window, "ScenarioField_parameters_quantity");
            SetText(quantity, "2");
            quantity.GrabFocus();
            quantity.CaretColumn = 1;
            await Click(parent, Find<Button>(window, "ScenarioEditorContinueButton"));
            rectangles["result"] = Rectangles(window, "ScenarioStartButton", "ScenarioAbortButton", "ScenarioResultBackButton");
            await Capture(parent, root, "result-1920.png");
            await Click(parent, Find<Button>(window, "ScenarioResultBackButton"));
            if (!ReferenceEquals(quantity, Find<LineEdit>(window, "ScenarioField_parameters_quantity")) || quantity.Text != "2")
                return Fail("步骤切换重建或丢失草稿输入");
            Find<Button>(window, "ScenarioOverwriteButton").EmitSignal(Button.SignalName.Pressed);
            saved = library.ListConfigurations("buy-process-sell").Single(entry => entry.CaseId == "界面保存验证");
            if (saved.Revision != 2 || ScenarioConfiguration.Load(saved.Id).Quantity != 2 || !builtin["radish-three-r1.json"].SequenceEqual(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://tests/scenario-configs/buy-process-sell/radish-three-r1.json"))))
                return Fail("覆盖没有增加修订或改动了内置示例");
            Find<Button>(window, "ScenarioEditorContinueButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioStartButton").EmitSignal(Button.SignalName.Pressed);
            window.AdvanceIndependent(100);
            string previousReport = Find<Label>(window, "ScenarioReportPathLabel").Text;
            if (!previousReport.StartsWith("报告：", StringComparison.Ordinal) || !File.Exists(previousReport[3..]))
                return Fail("结果归属验证没有真实运行并保存报告");
            Find<Button>(window, "ScenarioResultBackButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioEditorContinueButton").EmitSignal(Button.SignalName.Pressed);
            if (Find<Label>(window, "ScenarioReportPathLabel").Text != previousReport)
                return Fail("单纯步骤切换清空了同一配置结果");
            Find<Button>(window, "ScenarioResultBackButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioOverwriteButton").EmitSignal(Button.SignalName.Pressed);
            if (Find<Label>(window, "ScenarioProgressLabel").Text != "尚未运行" ||
                Find<Label>(window, "ScenarioReportPathLabel").Text != "报告尚未生成" || !File.Exists(previousReport[3..]))
                return Fail("成功保存新修订未清旧界面结果，或删除了历史报告");

            Find<Button>(window, "ScenarioArrayAdd_execution_timePlan").EmitSignal(Button.SignalName.Pressed);
            SetText(Find<LineEdit>(window, "ScenarioField_execution_timePlan_2_endDate_0"), "01");
            SetText(Find<LineEdit>(window, "ScenarioField_execution_timePlan_2_endDate_1"), "05");
            SetText(Find<LineEdit>(window, "ScenarioField_execution_timePlan_2_endDate_2"), "01");
            Find<Button>(window, "ScenarioArrayUp_execution_timePlan_2").EmitSignal(Button.SignalName.Pressed);
            if (!Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("timePlan")) return Fail("数组上移破坏日期顺序未反馈");
            Find<Button>(window, "ScenarioArrayDown_execution_timePlan_1").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioArrayRemove_execution_timePlan_2").EmitSignal(Button.SignalName.Pressed);
            if (Find<Label>(window, "ScenarioFeedbackLabel").Text.Length != 0) return Fail("数组删除后没有清理对应错误");

            if (!await CheckOtherShape(parent, window)) return false;
            await CheckSize(parent, window, new Vector2I(2560, 1440), root, "editor-2560.png");
            await CheckSize(parent, window, new Vector2I(3840, 2160), root, "editor-3840.png");
            GetWindow(parent).Size = new Vector2I(1920, 1080);
            UiScaling.SetFontScale(window, 1.2f);
            await Frames(parent);
            if (!Inside(window, Find<Button>(window, "ScenarioSaveAsButton"))) return Fail("字体倍率挤出固定保存入口");
            await Capture(parent, root, "editor-font-1.2.png");
            UiScaling.SetFontScale(window, 1);

            var pendingQuantity = Find<LineEdit>(window, "ScenarioField_parameters_quantity");
            SetText(pendingQuantity, "非法整数");
            byte[] savedBytes = File.ReadAllBytes(saved.Id);
            File.AppendAllText(saved.Id, "\n");
            await Click(parent, Find<Button>(window, "ScenarioDeleteConfigurationButton"));
            await Click(parent, Find<Button>(window, "ScenarioConfirmationConfirmButton"));
            if (!File.Exists(saved.Id) || !ReferenceEquals(pendingQuantity, Find<LineEdit>(window, "ScenarioField_parameters_quantity")) ||
                pendingQuantity.Text != "非法整数" || !Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("配置操作失败"))
                return Fail("删除失败重扫丢失仍存在文件的非法输入草稿或伪报成功");
            File.WriteAllBytes(saved.Id, savedBytes);
            SetText(pendingQuantity, "4");
            await Click(parent, Find<Button>(window, "ScenarioDeleteConfigurationButton"));
            await Capture(parent, root, "config-delete-1920.png");
            SendEscape();
            await Frames(parent);
            if (!File.Exists(saved.Id) || Find<LineEdit>(window, "ScenarioField_parameters_quantity").Text != "4")
                return Fail("Esc取消配置删除丢失草稿或删除文件");
            await Click(parent, Find<Button>(window, "ScenarioDeleteConfigurationButton"));
            await Click(parent, Find<Button>(window, "ScenarioConfirmationConfirmButton"));
            if (File.Exists(saved.Id) || profiles.ItemCount != 3 || !Find<Button>(window, "ScenarioDeleteConfigurationButton").Disabled)
                return Fail("配置删除没有移除实际文件并清理选择");
            Find<Button>(window, "ScenarioSaveAsButton").EmitSignal(Button.SignalName.Pressed);
            SetText(Find<LineEdit>(window, "ScenarioSaveNameInput"), "流程删除验证");
            Find<Button>(window, "ScenarioSaveConfirmButton").EmitSignal(Button.SignalName.Pressed);
            string writable = library.ListConfigurations("buy-process-sell").Single(entry => !entry.IsReadOnly).Id;
            string history = Path.Combine(root, "history-preserved-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(history, "历史报告");
            Find<Button>(window, "ScenarioEditorBackButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioDeleteFlow_buy-process-sell").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioConfirmationConfirmButton").EmitSignal(Button.SignalName.Pressed);
            if (library.Flows.Count != 0 || File.Exists(writable) || !File.Exists(history) || !Find<Button>(window, "ScenarioCatalogContinueButton").Disabled || new ScenarioConfigurationLibrary(builtin, user).Flows.Count != 0)
                return Fail("流程删除没有持久移除目录、清理全部可写配置或损伤历史报告");
            if (!await CheckPartialDeletion(parent, main, ui, window, builtin, root)) return false;
            File.WriteAllText(Path.Combine(root, "ui-widget-rects.json"), JsonSerializer.Serialize(rectangles, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception error) { return Fail("流程编辑场景异常：" + error); }
        finally
        {
            main.QueueFree();
            await Frames(parent);
        }
    }

    private static async Task<bool> CheckPartialDeletion(Node parent, Main main, Control ui, DeveloperToolsWindow original,
        IReadOnlyDictionary<string, byte[]> builtin, string root)
    {
        string user = Path.Combine(root, "partial-delete-" + Guid.NewGuid().ToString("N"));
        var library = new ScenarioConfigurationLibrary(builtin, user);
        ScenarioConfigurationDraft source = library.Open(library.ListConfigurations("buy-process-sell")[0]);
        string first = library.SaveAs(source, "部分删除一").Entry!.Id;
        string second = library.SaveAs(source, "部分删除二").Entry!.Id;
        // 文件名位置被真实目录占据：文件删除可以完成，持久目录记录写入必须失败。
        Directory.CreateDirectory(Path.Combine(user, ".hidden-flows.json"));
        var window = new DeveloperToolsWindow(main.Game, main.Driver, () => { }, library);
        ui.AddChild(window);
        original.Hide();
        window.ShowRaised();
        try
        {
            await Frames(parent);
            Find<Button>(window, "ScenarioSelectFlow_buy-process-sell").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioCatalogContinueButton").EmitSignal(Button.SignalName.Pressed);
            Choose(Find<OptionButton>(window, "ScenarioProfileChoice"), "部分删除一");
            SetText(Find<LineEdit>(window, "ScenarioField_parameters_quantity"), "非法整数");
            Find<Button>(window, "ScenarioEditorBackButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioDeleteFlow_buy-process-sell").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(window, "ScenarioConfirmationConfirmButton").EmitSignal(Button.SignalName.Pressed);
            if (File.Exists(first) || File.Exists(second) || library.Flows.Count != 1 ||
                Find<OptionButton>(window, "ScenarioProfileChoice").ItemCount != 3 ||
                Find<LineEdit>(window, "ScenarioField_parameters_quantity").Text == "非法整数" ||
                !Find<Label>(window, "ScenarioFeedbackLabel").Text.Contains("配置操作失败"))
                return Fail("流程部分删除失败未同步实际目录、未清失效草稿或伪报完整成功");
            return true;
        }
        finally
        {
            window.QueueFree();
            await Frames(parent);
            original.ShowRaised();
        }
    }

    private static async Task<bool> CheckOtherShape(Node parent, DeveloperToolsWindow window)
    {
        var schema = new ScenarioConfigurationSchema(typeof(OtherShape));
        var draft = new ScenarioConfigurationDraft(schema, Encoding.UTF8.GetBytes("{\"enabled\":true,\"notes\":\"测试\",\"items\":[{\"count\":2}]}"));
        var form = new ScenarioConfigurationForm();
        window.Body.AddChild(form);
        form.BindDraft(draft);
        await Frames(parent);
        var check = Find<CheckBox>(form, "ScenarioField_enabled");
        check.ButtonPressed = false;
        check.EmitSignal(BaseButton.SignalName.Toggled, false);
        SetText(Find<LineEdit>(form, "ScenarioField_items_0_count"), "4294967295");
        if ((bool)draft.GetValue("enabled")! || (uint)draft.GetValue("items[0].count")! != uint.MaxValue)
            return Fail("同一表单没有处理另一种布尔与uint32数组形状");
        window.Body.RemoveChild(form);
        form.QueueFree();
        return true;
    }

    public sealed class OtherShape
    {
        [JsonPropertyName("enabled"), ConfigField("启用额外检查")]
        public bool Enabled { get; set; }
        [JsonPropertyName("notes"), ConfigField("备注")]
        public string Notes { get; set; } = "";
        [JsonPropertyName("items"), ConfigField("检查项")]
        public List<OtherItem> Items { get; set; } = new();
    }
    public sealed class OtherItem
    {
        [JsonPropertyName("count"), ConfigField("数量")]
        public uint Count { get; set; }
    }

    private static async Task CheckSize(Node parent, Control window, Vector2I size, string root, string name)
    {
        GetWindow(parent).Size = size;
        await Frames(parent);
        if (!Inside(window, Find<Button>(window, "ScenarioSaveAsButton"))) throw new InvalidOperationException("大窗口固定保存入口不可见");
        await Capture(parent, root, name);
    }
    private static Window GetWindow(Node parent) => parent.GetWindow();
    private static bool Inside(Control window, Control child) => window.GetGlobalRect().Encloses(child.GetGlobalRect());
    private static Dictionary<string, object> Rectangles(Node root, params string[] names) => names.ToDictionary(name => name, name =>
    {
        Rect2 rect = Find<Control>(root, name).GetGlobalRect();
        return (object)new { x = rect.Position.X, y = rect.Position.Y, width = rect.Size.X, height = rect.Size.Y };
    });
    private static void Choose(OptionButton choice, string caseId)
    {
        int index = Enumerable.Range(0, choice.ItemCount).Single(value => choice.GetItemText(value).StartsWith(caseId + " · ", StringComparison.Ordinal));
        choice.Select(index);
        choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }
    private static void SetText(LineEdit input, string text)
    {
        input.Text = text;
        input.EmitSignal(LineEdit.SignalName.TextChanged, text);
    }
    private static void SendEscape()
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Input.FlushBufferedEvents();
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = false });
        Input.FlushBufferedEvents();
    }
    private static async Task Click(Node parent, Button button)
    {
        await Frames(parent);
        Vector2 point = button.GetGlobalRect().GetCenter();
        Viewport viewport = parent.GetViewport();
        foreach (bool pressed in new[] { true, false })
        {
            var input = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed };
            Input.ParseInputEvent(input.XformedBy(viewport.GetFinalTransform()));
            Input.FlushBufferedEvents();
            await Frames(parent);
        }
    }
    private static async Task Capture(Node parent, string root, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(parent);
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        parent.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(root, name));
    }
    private static async Task Frames(Node parent)
    {
        for (int index = 0; index < 3; index++) await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static T Find<T>(Node root, string name) where T : Node => (T)(root.FindChild(name, true, false) ?? throw new InvalidOperationException("控件缺失：" + name));
    private static bool Fail(string message) { GD.PushError(message); return false; }
}
