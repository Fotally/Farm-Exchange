using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
        string root = ProjectSettings.GlobalizePath("res://build/issue104-visual-validation");
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
            if (!CheckNavigation(window, 0) || !CheckCatalogVisual(Find<ScenarioCatalogStep>(window, "ScenarioCatalogStep"), "buy-process-sell", false)) return false;
            if (Find<Label>(window, "ScenarioFeedbackLabel").IsVisibleInTree()) return Fail("空反馈仍占用正文高度");
            Label title = Find<Label>(window, "WindowTitleLabel");
            Control badgePanel = Find<Control>(window, "ScenarioDevelopmentBadge");
            Label badge = badgePanel.GetChildren().OfType<Label>().Single();
            if (title.Text != "开发测试 · 流程与配置" || title.GetThemeFontSize("font_size") != 21 ||
                badge.Text != "开发工具" || badge.GetThemeFontSize("font_size") != 13 || badge.GetLineCount() != 1 ||
                Math.Abs(badgePanel.Size.X - 72) > 4 || Math.Abs(Find<Control>(window, "Header").Size.Y - 68) > 2)
                return Fail("开发窗口标题或工具标识未匹配C原型");
            rectangles["catalog"] = Rectangles(window, "ScenarioSelectFlow_buy-process-sell", "ScenarioDeleteFlow_buy-process-sell", "ScenarioCatalogContinueButton", "ScenarioFlowSearch", "ScenarioCategory_0", "ScenarioCategory_1");
            await Capture(parent, root, "catalog-1920.png");
            if (!await CheckCatalogCapacity(parent, window, library.Flows[0], root)) return false;
            await Click(parent, Find<Button>(window, "ScenarioDeleteFlow_buy-process-sell"));
            if (!Find<Control>(window, "ScenarioConfirmationPanel").Visible || !Find<Button>(window, "ScenarioCatalogContinueButton").Disabled)
                return Fail("垃圾桶同时选择了流程或没有确认弹窗");
            rectangles["flowDeleteDialog"] = Rectangles(window, "ScenarioConfirmationCancelButton", "ScenarioConfirmationConfirmButton");
            await Capture(parent, root, "flow-delete-1920.png");
            await Click(parent, Find<Button>(window, "ScenarioConfirmationCancelButton"));
            if (library.Flows.Count != 1 || Directory.Exists(user)) return Fail("流程取消删除仍修改配置库");
            var search = Find<LineEdit>(window, "ScenarioFlowSearch");
            SetText(search, "找不到的流程");
            if (cards.FindChild("ScenarioSelectFlow_buy-process-sell", true, false) != null) return Fail("搜索无结果仍显示旧卡片");
            SetText(search, "委托");
            await Click(parent, Find<Button>(window, "ScenarioSelectFlow_buy-process-sell"));
            if (!CheckCatalogVisual(Find<ScenarioCatalogStep>(window, "ScenarioCatalogStep"), "buy-process-sell", true)) return false;
            await Capture(parent, root, "catalog-selected-1920.png");
            await Click(parent, Find<Button>(window, "ScenarioCatalogContinueButton"));
            if (!CheckNavigation(window, 1)) return false;
            var profiles = Find<OptionButton>(window, "ScenarioProfileChoice");
            if (profiles.ItemCount != 3 || !Find<Button>(window, "ScenarioOverwriteButton").Disabled || !Find<Button>(window, "ScenarioDeleteConfigurationButton").Disabled)
                return Fail("内置只读示例仍允许覆盖或删除");
            Label[] headings = Find<ScenarioConfigurationForm>(window, "ScenarioConfigurationForm").FindChildren("*", "Label", true, false).Cast<Label>().ToArray();
            if (headings.Count(label => label.Text == "运行对象") != 1 || headings.Count(label => label.Text == "时间安排") != 1)
                return Fail("自动表单重复显示相同的分组或父子字段标题");
            if (!CheckEditorVisual(window, library.Flows[0]) ||
                !CheckFormGroups(Find<ScenarioConfigurationForm>(window, "ScenarioConfigurationForm"), ScenarioConfigurationSchema.BuyProcessSell)) return false;
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
            if (!Find<Control>(window, "ScenarioConfirmationPanel").Visible) return Fail("切换配置未确认放弃未保存修改");
            await Click(parent, Find<Button>(window, "ScenarioConfirmationConfirmButton"));
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
            if (!CheckNavigation(window, 2)) return false;
            if (!CheckResultVisual(window, library.Flows[0], "界面保存验证")) return false;
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
            await Capture(parent, root, "result-passed-1920.png");
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
            if (!CheckFormGroups(Find<ScenarioConfigurationForm>(window, "ScenarioConfigurationForm"), ScenarioConfigurationSchema.BuyProcessSell)) return false;
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
            rectangles["configDeleteDialog"] = Rectangles(window, "ScenarioConfirmationCancelButton", "ScenarioConfirmationConfirmButton");
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
        if (!CheckFormGroups(form, schema)) return false;
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

    private static bool CheckNavigation(DeveloperToolsWindow window, int active, float font = 1)
    {
        Button[] steps = Enumerable.Range(1, 3).Select(index => Find<Button>(window, "ScenarioStep" + index + "Button")).ToArray();
        if (steps.Any(button => button.SizeFlagsHorizontal != Control.SizeFlags.ExpandFill ||
            button.Alignment != HorizontalAlignment.Left || button.Text.Contains('●') || button.GetThemeFontSize("font_size") != (int)Math.Round(16 * font)) ||
            steps.Max(button => button.Size.X) - steps.Min(button => button.Size.X) > 1)
            return Fail("分步导航未平均铺满、左对齐或仍使用圆点表示当前步骤");
        for (int index = 0; index < steps.Length; index++)
        {
            if (steps[index].GetThemeStylebox("normal") is not StyleBoxFlat style ||
                style.BgColor != (index == active ? UiElements.Mid : new Color("f0dfb4")) ||
                steps[index].GetThemeColor("font_color") != (index == active ? new Color("fff3d8") : UiElements.Ink))
                return Fail("分步导航没有使用当前绿色浅字、其余纸色深字");
            if (index > 0 && Math.Abs(steps[index].GlobalPosition.X - steps[index - 1].GetGlobalRect().End.X - 13) > 1)
                return Fail("分步导航间距没有保持C原型的13像素");
        }
        if (font == 1 && steps.Any(button => Math.Abs(button.Size.Y - 43) > 2))
            return Fail("分步导航基准高度与C原型43像素明显不符");
        return true;
    }

    private static bool CheckEditorVisual(DeveloperToolsWindow window, ScenarioFlowDefinition flow)
    {
        Label title = Find<Label>(window, "ScenarioEditorFlowTitle");
        Label description = Find<Label>(window, "ScenarioEditorFlowDescription");
        Label profileLabel = Find<Label>(window, "ScenarioProfileLabel");
        OptionButton profiles = Find<OptionButton>(window, "ScenarioProfileChoice");
        Button trash = Find<Button>(window, "ScenarioDeleteConfigurationButton");
        if (title.Text != flow.Name || title.GetThemeFontSize("font_size") != 20 ||
            description.Text != flow.Description || description.GetThemeFontSize("font_size") != 14 ||
            profileLabel.Text != "配置" || profileLabel.GetThemeFontSize("font_size") != 14)
            return Fail("配置页缺少所选流程标题、简介或配置选择标签");
        if (Math.Abs(profiles.Size.X - 335) > 8 || Math.Abs(profiles.Size.Y - 39) > 2 ||
            Math.Abs(trash.Size.X - 35) > 1 || Math.Abs(trash.Size.Y - 35) > 1 ||
            profileLabel.GetLineCount() != 1 || Math.Abs(profileLabel.Size.X - 28) > 4 ||
            profileLabel.GetGlobalRect().End.X > profiles.GlobalPosition.X ||
            profiles.GetGlobalRect().End.X > trash.GlobalPosition.X ||
            Math.Abs(trash.GlobalPosition.X - profiles.GetGlobalRect().End.X) > 16)
        {
            RecordVisualFailure(window, "editor-layout-failure", new
            {
                Window = LayoutMeasurement(window),
                ProfileLabel = LabelMeasurement(profileLabel),
                Profiles = new { Layout = LayoutMeasurement(profiles), Font = profiles.GetThemeFontSize("font_size"), profiles.ClipText, profiles.FitToLongestItem },
                Trash = new { Layout = LayoutMeasurement(trash), VerticalFlags = trash.SizeFlagsVertical.ToString(), Font = trash.GetThemeFontSize("font_size") },
                Status = LabelMeasurement(Find<Label>(window, "ScenarioDraftStatusLabel")),
                Title = LabelMeasurement(title),
                Description = LabelMeasurement(description),
                Intro = LayoutMeasurement(title.GetParent<Control>()),
                ProfileRow = LayoutMeasurement(profiles.GetParent<Control>())
            });
            return Fail("配置选择没有保持约335像素下拉与邻接35像素垃圾桶");
        }
        if (!CheckStepFooter(window, "ScenarioEditorFooterSeparator", "ScenarioSaveAsButton")) return false;
        if (!CheckInputStyle(profiles, false, 1)) return false;
        return true;
    }

    private static bool CheckFormGroups(ScenarioConfigurationForm form, ScenarioConfigurationSchema schema)
    {
        GridContainer groups = Find<GridContainer>(form, "ScenarioConfigurationGroups");
        string[] names = schema.Fields.Where(field => !field.IsReadOnly).GroupBy(field => field.Group)
            .OrderBy(group => group.Any(ContainsArray)).Select(group => group.Key).ToArray();
        Control[] panes = groups.GetChildren().OfType<Control>().ToArray();
        if (panes.Length != names.Length || groups.Columns != names.Length ||
            groups.GetThemeConstant("h_separation") != 25 || groups.GetThemeConstant("v_separation") != 25)
        {
            RecordFormFailure(form, groups, panes, names);
            return Fail("通用表单没有按字段分组元数据生成并排列与25像素间距");
        }
        for (int index = 0; index < panes.Length; index++)
        {
            if (names[index].Length > 0 && panes[index].FindChildren("*", "Label", true, false).Cast<Label>().Count(label => label.Text == names[index]) != 1)
            {
                RecordFormFailure(form, groups, panes, names);
                return Fail("表单分组标题遗漏、重复或元数据顺序不符");
            }
            if (Math.Abs(panes[index].GlobalPosition.Y - panes[0].GlobalPosition.Y) > 1 ||
                panes[index].Size.X < 250 ||
                index > 0 && Math.Abs(panes[index].GlobalPosition.X - panes[index - 1].GetGlobalRect().End.X - 25) > 1)
            {
                RecordFormFailure(form, groups, panes, names);
                return Fail("配置分组堆成纵向窄栏，或列间距与顶面对齐不正确");
            }
        }
        if (panes.Length == 3 && Math.Abs(panes[2].Size.X / panes[0].Size.X - 1.15) > 0.05)
        {
            RecordFormFailure(form, groups, panes, names);
            return Fail("配置三组没有保留第三列1.15的宽度比例");
        }
        var rules = DescribeInputNames(schema.Fields, "ScenarioField_").ToArray();
        float font = UiScaling.FontScale(form);
        foreach (Control input in form.FindChildren("ScenarioField_*", "", true, false).OfType<Control>().Where(control => control is LineEdit or OptionButton))
        {
            var matching = rules.Where(rule => Regex.IsMatch(input.Name.ToString(), rule.Pattern, RegexOptions.CultureInvariant)).ToArray();
            if (matching.Length != 1) return Fail("无法从字段描述唯一定位生成控件：" + input.Name);
            if (!CheckInputStyle(input, matching[0].Date, font)) return false;
        }
        return true;
    }

    private static IEnumerable<(string Pattern, bool Date)> DescribeInputNames(IReadOnlyList<ConfigFieldDescriptor> fields, string prefix)
    {
        foreach (ConfigFieldDescriptor field in fields)
        {
            string name = prefix + Regex.Escape(ScenarioConfigurationForm.ControlName(field.Name));
            if (field.Kind is ConfigFieldKind.Object or ConfigFieldKind.Array)
            {
                string childPrefix = name + (field.Kind == ConfigFieldKind.Array ? @"_\d+_" : "_");
                foreach (var child in DescribeInputNames(field.Children, childPrefix)) yield return child;
            }
            else if (field.Kind == ConfigFieldKind.GameDate) yield return ("^" + name + "_[0-2]$", true);
            else if (field.Kind != ConfigFieldKind.Boolean) yield return ("^" + name + "$", false);
        }
    }

    private static bool CheckInputStyle(Control input, bool date, float font)
    {
        bool appearance = input.GetThemeFontSize("font_size") == (int)Math.Round(16 * font) &&
            input.GetThemeStylebox("normal") is StyleBoxFlat style && style.BgColor == new Color("fff7e3") &&
            input.GetThemeColor("font_color") == UiElements.Ink;
        bool geometry = !input.IsVisibleInTree() ||
            (font != 1 || Math.Abs(input.Size.Y - 39) <= 2) &&
            (!date || Math.Abs(input.Size.X - 62) <= 1 && (input.SizeFlagsHorizontal & Control.SizeFlags.Expand) == 0);
        if (!appearance || !geometry)
        {
            RecordVisualFailure(input, "input-style-failure", new
            {
                Name = input.Name.ToString(),
                Layout = LayoutMeasurement(input),
                Font = input.GetThemeFontSize("font_size"),
                ExpectedFont = Math.Round(16 * font),
                Date = date,
                HorizontalFlags = input.SizeFlagsHorizontal.ToString(),
                NormalBackground = input.GetThemeStylebox("normal") is StyleBoxFlat normal ? normal.BgColor.ToHtml() : "非平面样式",
                Color = input.GetThemeColor("font_color").ToHtml()
            });
            return Fail("生成字段或配置选择的字体、纸面样式或日期宽度与C原型不符");
        }
        return true;
    }

    private static bool ContainsArray(ConfigFieldDescriptor field) => field.Kind == ConfigFieldKind.Array || field.Children.Any(ContainsArray);

    private static void RecordFormFailure(ScenarioConfigurationForm form, GridContainer groups, Control[] panes, string[] names) =>
        RecordVisualFailure(form, "form-layout-failure", new
        {
            Form = LayoutMeasurement(form),
            Groups = LayoutMeasurement(groups),
            groups.Columns,
            ExpectedGroups = names,
            HorizontalGap = groups.GetThemeConstant("h_separation"),
            VerticalGap = groups.GetThemeConstant("v_separation"),
            Panes = panes.Select(pane => new { Layout = LayoutMeasurement(pane), Labels = pane.FindChildren("*", "Label", true, false).Cast<Label>().Select(LabelMeasurement).ToArray() }).ToArray()
        });

    private static bool CheckStepFooter(DeveloperToolsWindow window, string separatorName, string buttonName)
    {
        HSeparator separator = Find<HSeparator>(window, separatorName);
        Button button = Find<Button>(window, buttonName);
        if (separator.GetThemeStylebox("separator") is not StyleBoxLine style || style.Thickness != 1 ||
            separator.GetGlobalRect().End.Y >= button.GlobalPosition.Y ||
            button.GlobalPosition.Y - separator.GetGlobalRect().End.Y > 18)
            return Fail("当前步骤固定页脚缺少单线分隔或按钮上方间距不正确");
        return true;
    }

    private static bool CheckResultVisual(DeveloperToolsWindow window, ScenarioFlowDefinition flow, string caseId)
    {
        Label title = Find<Label>(window, "ScenarioResultFlowTitle");
        Label summary = Find<Label>(window, "ScenarioRunConfigurationLabel");
        Control result = Find<Control>(window, "ScenarioResultPanel");
        Label progress = Find<Label>(window, "ScenarioProgressLabel");
        Label report = Find<Label>(window, "ScenarioReportPathLabel");
        if (title.Text != flow.Name || title.GetThemeFontSize("font_size") != 24 ||
            !summary.Text.Contains(caseId, StringComparison.Ordinal) || summary.GetThemeFontSize("font_size") != 14)
        {
            RecordResultFailure(window, title, summary, result, progress, report);
            return Fail("结果页没有显示所选流程标题及当前配置摘要");
        }
        if (result.GetThemeStylebox("panel") is not StyleBoxFlat style || style.BgColor != new Color("e4dfbc") ||
            style.BorderWidthLeft != 1 || style.BorderWidthTop != 1 ||
            !Inside(result, progress) || !Inside(result, report) ||
            progress.GlobalPosition.X - result.GlobalPosition.X < 15)
        {
            RecordResultFailure(window, title, summary, result, progress, report);
            return Fail("真实进度与报告没有显示在单线纸色结果框内");
        }
        foreach (string name in new[] { "ScenarioStartButton", "ScenarioAbortButton", "ScenarioResultBackButton" })
            if (!Inside(window, Find<Button>(window, name))) return Fail("结果页固定操作区被正文挤出：" + name);
        if (!Find<Button>(window, "DevelopmentRate16Button").IsVisibleInTree()) return Fail("结果页丢失已确认开发倍率入口");
        if (!CheckStepFooter(window, "ScenarioResultFooterSeparator", "ScenarioStartButton")) return false;
        return true;
    }

    private static void RecordResultFailure(DeveloperToolsWindow window, Label title, Label summary, Control result, Label progress, Label report) =>
        RecordVisualFailure(window, "result-layout-failure", new
        {
            Title = LabelMeasurement(title),
            Summary = LabelMeasurement(summary),
            Panel = LayoutMeasurement(result),
            Progress = LabelMeasurement(progress),
            Report = LabelMeasurement(report),
            Footer = LayoutMeasurement(Find<Control>(window, "ScenarioResultFooterSeparator")),
            Start = LayoutMeasurement(Find<Button>(window, "ScenarioStartButton")),
            Abort = LayoutMeasurement(Find<Button>(window, "ScenarioAbortButton"))
        });

    private static bool CheckCatalogVisual(ScenarioCatalogStep catalog, string flowId, bool selected, float font = 1)
    {
        Control sidebar = Find<Control>(catalog, "ScenarioCatalogSidebar");
        Control search = Find<Control>(catalog, "ScenarioFlowSearch");
        GridContainer grid = Find<GridContainer>(catalog, "ScenarioFlowCards");
        Button card = Find<Button>(catalog, "ScenarioSelectFlow_" + flowId);
        Button trash = Find<Button>(card, "ScenarioDeleteFlow_" + flowId);
        Label[] labels = card.FindChildren("*", "Label", true, false).Cast<Label>().ToArray();
        if (Math.Abs(sidebar.Size.X - 285) > 1 || Math.Abs(search.Size.Y - 39) > 1 ||
            Math.Abs(grid.GlobalPosition.X - sidebar.GetGlobalRect().End.X - 30) > 1 ||
            Math.Abs(card.GlobalPosition.Y - search.GlobalPosition.Y - 2) > 1)
            return Fail("C目录搜索侧栏285像素、28像素列距或顶面对齐不正确");
        int horizontalGap = grid.GetThemeConstant("h_separation");
        if (grid.Columns != 2 || Math.Abs(horizontalGap - 9) > 1 || Math.Abs(grid.GetThemeConstant("v_separation") - 9) > 1 ||
            Math.Abs(card.Size.X - (grid.Size.X - horizontalGap) / 2) > 1 || card.Size.Y < 112 || font == 1 && Math.Abs(card.Size.Y - 112) > 2)
            return Fail("流程卡片没有保持半列宽度、112像素基准高度或原型9像素间距");
        if (labels.Length != 2 || labels[0].GetThemeFontSize("font_size") != (int)Math.Round(15 * font) ||
            labels[1].GetThemeFontSize("font_size") != (int)Math.Round(12 * font))
            return Fail("流程名称与简介未按15/12及字体倍率排版");
        if (card.GetThemeStylebox("normal") is not StyleBoxFlat style ||
            style.BorderWidthLeft != (selected ? 4 : 2) || style.BorderWidthRight != 2 ||
            style.BorderWidthTop != 2 || style.BorderWidthBottom != 2 ||
            card.FindChildren("*", "PanelContainer", true, false).Count != 0 ||
            card.FindChildren("*", "Button", true, false).Count != 1)
            return Fail("流程卡片出现内外双框，或选中左边标记与单框不符");
        if (!Inside(card, trash) || !trash.Size.IsEqualApprox(new Vector2(29, 29)) ||
            Math.Abs(card.GetGlobalRect().End.X - trash.GetGlobalRect().End.X - 9) > 1 ||
            Math.Abs(card.GetGlobalRect().End.Y - trash.GetGlobalRect().End.Y - 8) > 1)
            return Fail("垃圾桶没有作为29像素独立操作放在卡片右下角");
        if (catalog.FindChild("ScenarioFlowCategory", true, false) != null ||
            Find<Control>(catalog, "ScenarioFlowCategories") is not HFlowContainer)
            return Fail("分类仍为下拉，未采用可换行按钮");
        var categories = Find<HFlowContainer>(catalog, "ScenarioFlowCategories");
        Button[] filters = categories.GetChildren().OfType<Button>().ToArray();
        if (filters.Length == 0 || filters[0].Text != "全部" ||
            filters.Any(button => button.GetThemeFontSize("font_size") != (int)Math.Round(14 * font)) ||
            categories.GetThemeConstant("h_separation") != 7 || categories.GetThemeConstant("v_separation") != 7 ||
            filters[0].GetThemeStylebox("normal") is not StyleBoxFlat activeCategory || activeCategory.BgColor != UiElements.Mid ||
            filters[0].GetThemeColor("font_color") != UiElements.Cream)
            return Fail("分类按钮字号、间距或当前分类的绿色选中态不符C原型");
        if (Find<Control>(catalog, "ScenarioFooterSeparator") is not HSeparator ||
            catalog.ContinueButton.Text != "继续配置 →" || catalog.ContinueButton.GetThemeFontSize("font_size") != (int)Math.Round(16 * font))
            return Fail("目录底部没有分隔线或继续操作不符C布局");
        if (font == 1 && catalog.GetViewport().GetVisibleRect().Size.IsEqualApprox(new Vector2(1920, 1080)))
        {
            Node owner = catalog;
            while (owner is not DeveloperToolsWindow) owner = owner.GetParent();
            var window = (DeveloperToolsWindow)owner;
            Button step = Find<Button>(catalog.GetParent(), "ScenarioStep1Button");
            if (!window.Size.IsEqualApprox(new Vector2(1360, 740)) ||
                Math.Abs(step.GlobalPosition.Y - window.GlobalPosition.Y - 93) > 4 ||
                Math.Abs(search.GlobalPosition.X - window.GlobalPosition.X - 27) > 4 ||
                Math.Abs(search.GlobalPosition.Y - window.GlobalPosition.Y - 154) > 4 ||
                Math.Abs(card.GlobalPosition.X - window.GlobalPosition.X - 342) > 4 ||
                Math.Abs(card.GlobalPosition.Y - window.GlobalPosition.Y - 156) > 4 ||
                Math.Abs(catalog.ContinueButton.GetGlobalRect().End.Y - window.GlobalPosition.Y - 715) > 4 ||
                Math.Abs(Find<Control>(catalog, "ScenarioFooterSeparator").GlobalPosition.Y - window.GlobalPosition.Y - 657) > 4 ||
                Math.Abs(catalog.ContinueButton.Size.X - 119) > 12)
            {
                Control header = Find<Control>(window, "Header");
                Control headerMargin = header.GetChild<Control>(0);
                Control headerRow = headerMargin.GetChild<Control>(0);
                Control titles = Find<Control>(window, "WindowTitleLabel").GetParent<Control>();
                Control badge = Find<Control>(window, "ScenarioDevelopmentBadge");
                var measurements = new
                {
                    Window = Rectangle(window),
                    Step = Rectangle(step),
                    Search = Rectangle(search),
                    Card = Rectangle(card),
                    Footer = Rectangle(Find<Control>(catalog, "ScenarioFooterSeparator")),
                    Continue = Rectangle(catalog.ContinueButton),
                    Header = LayoutMeasurement(header),
                    HeaderMargin = LayoutMeasurement(headerMargin),
                    HeaderRow = LayoutMeasurement(headerRow),
                    Titles = LayoutMeasurement(titles),
                    TitleLabels = titles.GetChildren().OfType<Label>().Select(LabelMeasurement).ToArray(),
                    Badge = LayoutMeasurement(badge),
                    BadgeLabels = badge.GetChildren().OfType<Label>().Select(LabelMeasurement).ToArray(),
                    Close = new { Layout = LayoutMeasurement(Find<Button>(window, "CloseButton")), ExpandIcon = Find<Button>(window, "CloseButton").ExpandIcon },
                    Body = LayoutMeasurement(window.Body),
                    WindowScroll = LayoutMeasurement(Find<Control>(window, "WindowScroll")),
                    BodyParent = LayoutMeasurement(window.Body.GetParent<Control>())
                };
                string json = JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true });
                string directory = ProjectSettings.GlobalizePath("res://build/issue104-visual-validation");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "catalog-layout-failure.json"), json);
                if (DisplayServer.GetName() != "headless")
                    catalog.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "catalog-layout-failure.png"));
                GD.Print("目录布局失败实测：" + json);
                return Fail("1080P目录正文或固定继续入口与C原型的实际位置明显不符");
            }
        }
        return true;
    }

    private static async Task<bool> CheckCatalogCapacity(Node parent, DeveloperToolsWindow window, ScenarioFlowDefinition definition, string root)
    {
        ScenarioCatalogStep original = Find<ScenarioCatalogStep>(window, "ScenarioCatalogStep");
        original.Hide();
        var catalog = new ScenarioCatalogStep();
        window.Body.AddChild(catalog);
        string[] categories = { "生产与交易", "田间作业", "工人与移动", "日历与计划" };
        ScenarioFlowDefinition[] fixtures = Enumerable.Range(0, 12).Select(index => definition with
        {
            Id = "capacity-" + index,
            Name = "流程" + (index + 1) + " · 照料安排",
            Description = "仅用于验证分类搜索和共享目录的测试数据。",
            Category = categories[index % categories.Length]
        }).ToArray();
        string? chosen = null;
        string? deleted = null;
        catalog.Selected += id => chosen = id;
        catalog.DeleteRequested += id => deleted = id;
        try
        {
            var prototypeFlows = new List<ScenarioFlowDefinition> { definition };
            (string Category, string[] Names)[] prototypeCategories =
            {
                ("农田生产", new[] { "播种与供水", "季末作物处理", "连续复种检查" }),
                ("加工场地", new[] { "原料领取与底线", "连续批次加工", "多场地原料竞争" }),
                ("交易与委托", new[] { "一次限价成交", "卖单冻结与撤销", "持续交易策略" }),
                ("工人与地图", new[] { "工人移动与任务", "场地选择与拆除", "远距离工人分配" }),
                ("日历与计划", new[] { "年度表执行", "冬春日期环绕", "报价日期推进" })
            };
            foreach ((string category, string[] names) in prototypeCategories)
                foreach (string name in names)
                    prototypeFlows.Add(definition with
                    {
                        Id = "prototype-" + prototypeFlows.Count,
                        Name = name,
                        Description = "目录容量示意，用于查看分类、搜索与长列表布局。",
                        Category = category
                    });
            catalog.ShowFlows(prototypeFlows, definition.Id);
            await Frames(parent);
            if (!CheckCatalogVisual(catalog, definition.Id, true)) return false;
            await Capture(parent, root, "catalog-capacity-prototype-1920.png");
            catalog.ShowFlows(fixtures, null);
            await Frames(parent);
            if (!CheckCatalogVisual(catalog, "capacity-0", false)) return false;
            Button first = Find<Button>(catalog, "ScenarioSelectFlow_capacity-0");
            Button second = Find<Button>(catalog, "ScenarioSelectFlow_capacity-1");
            if (Math.Abs(first.GlobalPosition.Y - second.GlobalPosition.Y) > 1 ||
                Math.Abs(second.GlobalPosition.X - first.GetGlobalRect().End.X - 9) > 1)
                return Fail("容量夹具的前两张流程没有在同一行排列");
            Button[] filters = Find<Control>(catalog, "ScenarioFlowCategories").GetChildren().OfType<Button>().ToArray();
            if (filters.Length != 5 || filters.Select(button => button.GlobalPosition.Y).Distinct().Count() < 2)
                return Fail("多分类按钮未根据285像素侧栏自然换行");
            await Capture(parent, root, "catalog-capacity-1920.png");
            await Click(parent, Find<Button>(catalog, "ScenarioDeleteFlow_capacity-1"));
            if (deleted != "capacity-1" || chosen != null) return Fail("容量夹具垃圾桶误触发流程选择");
            await Click(parent, Find<Button>(catalog, "ScenarioCategory_1"));
            SetText(Find<LineEdit>(catalog, "ScenarioFlowSearch"), "流程5");
            await Frames(parent);
            if (Find<GridContainer>(catalog, "ScenarioFlowCards").GetChildren().OfType<Button>().Count() != 1 ||
                catalog.FindChild("ScenarioSelectFlow_capacity-4", true, false) == null)
                return Fail("分类与名称搜索没有组合筛选对应流程");
            await Capture(parent, root, "catalog-category-search-1920.png");
            SetText(Find<LineEdit>(catalog, "ScenarioFlowSearch"), "共享目录");
            await Frames(parent);
            if (Find<GridContainer>(catalog, "ScenarioFlowCards").GetChildren().OfType<Button>().Count() != 3)
                return Fail("简介搜索丢失当前分类，或未找到相应流程");
            catalog.SetLocked(true);
            if (Find<Control>(catalog, "ScenarioFlowCategories").GetChildren().OfType<Button>().Any(button => !button.Disabled) ||
                Find<LineEdit>(catalog, "ScenarioFlowSearch").Editable)
                return Fail("运行锁定没有禁用分类或搜索");
            catalog.SetLocked(false);
            SetText(Find<LineEdit>(catalog, "ScenarioFlowSearch"), "");
            await Click(parent, Find<Button>(catalog, "ScenarioCategory_0"));
            catalog.ShowFlows(fixtures, null);
            await Frames(parent);
            foreach (Vector2I size in new[] { new Vector2I(2560, 1440), new Vector2I(3840, 2160) })
            {
                GetWindow(parent).Size = size;
                await Frames(parent);
                if (!CheckCatalogVisual(catalog, "capacity-0", false)) return false;
                await Capture(parent, root, "catalog-capacity-" + size.X + ".png");
            }
            GetWindow(parent).Size = new Vector2I(1920, 1080);
            UiScaling.SetFontScale(window, 1.2f);
            await Frames(parent);
            if (!CheckCatalogVisual(catalog, "capacity-0", false, 1.2f) || !CheckNavigation(window, 0, 1.2f) ||
                !Inside(window, catalog.ContinueButton)) return false;
            await Capture(parent, root, "catalog-capacity-font-1.2.png");
            return true;
        }
        finally
        {
            UiScaling.SetFontScale(window, 1);
            GetWindow(parent).Size = new Vector2I(1920, 1080);
            window.Body.RemoveChild(catalog);
            catalog.QueueFree();
            original.Show();
            await Frames(parent);
        }
    }

    private static async Task CheckSize(Node parent, Control window, Vector2I size, string root, string name)
    {
        GetWindow(parent).Size = size;
        await Frames(parent);
        if (!Inside(window, Find<Button>(window, "ScenarioSaveAsButton"))) throw new InvalidOperationException("大窗口固定保存入口不可见");
        if (!CheckFormGroups(Find<ScenarioConfigurationForm>(window, "ScenarioConfigurationForm"), ScenarioConfigurationSchema.BuyProcessSell))
            throw new InvalidOperationException("扩大分辨率后配置分组布局改变");
        await Capture(parent, root, name);
    }
    private static Window GetWindow(Node parent) => parent.GetWindow();
    private static bool Inside(Control window, Control child) => window.GetGlobalRect().Encloses(child.GetGlobalRect());
    private static Dictionary<string, object> Rectangles(Node root, params string[] names) => names.ToDictionary(name => name, name =>
    {
        return Rectangle(Find<Control>(root, name));
    });
    private static object Rectangle(Control control)
    {
        Rect2 rect = control.GetGlobalRect();
        return new { x = rect.Position.X, y = rect.Position.Y, width = rect.Size.X, height = rect.Size.Y };
    }
    private static object LayoutMeasurement(Control control)
    {
        Vector2 minimum = control.GetCombinedMinimumSize();
        return new { Rectangle = Rectangle(control), MinimumWidth = minimum.X, MinimumHeight = minimum.Y, CustomMinimumWidth = control.CustomMinimumSize.X, CustomMinimumHeight = control.CustomMinimumSize.Y };
    }
    private static void RecordVisualFailure(Control control, string name, object measurements)
    {
        string directory = ProjectSettings.GlobalizePath("res://build/issue104-visual-validation");
        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        GD.Print(name + "实测：" + json);
        if (DisplayServer.GetName() != "headless") control.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
    }
    private static object LabelMeasurement(Label label) => new
    {
        label.Text,
        Layout = LayoutMeasurement(label),
        FontSize = label.GetThemeFontSize("font_size"),
        LineCount = label.GetLineCount(),
        Autowrap = label.AutowrapMode.ToString()
    };
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
