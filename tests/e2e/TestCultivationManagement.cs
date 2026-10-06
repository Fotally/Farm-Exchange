using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestCultivationManagement : Node
{
    public override async void _Ready()
    {
        try
        {
            bool passed = TestCultivationDeletion.RunChecks() && await RunChecksAsync(this);
            if (passed) GD.Print("共享年度表列表可读性、删除与引用解除检查通过");
            GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        Window window = parent.GetWindow();
        Vector2I originalSize = window.Size;
        bool graphical = DisplayServer.GetName() != "headless";
        Vector2I originalMouse = graphical ? DisplayServer.MouseGetPosition() : default;
        Main? main = null;
        try
        {
            window.Size = new Vector2I(1920, 1080);
            await Frames(parent);
            main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            parent.AddChild(main);
            main.SetProcess(false);
            Click(main, "PauseButton");
            uint seconds = main.Game.Calendar.ElapsedSeconds;
            Click(main, "CultivationButton");
            CultivationWindow cultivation = Find<CultivationWindow>(main, "CultivationWindow");
            Click(cultivation, "NewCultivationPlanButton");
            Find<LineEdit>(cultivation, "CultivationPlanName").Text = "共享轮作表";
            Click(cultivation, "SaveCultivationPlanButton");
            await Frames(parent);
            ItemList plans = Find<ItemList>(cultivation, "CultivationPlanList");
            if (main.Game.GetCultivationPlans().Count != 1 || plans.ItemCount != 1)
                return Fail("真实共享表保存没有更新年度表列表");
            bool passed = true;
            foreach ((Vector2I size, float overall, float font) in new[]
            {
                (new Vector2I(1920, 1080), 1f, 1f),
                (new Vector2I(2560, 1440), 1f, 1f),
                (new Vector2I(3840, 2160), 1f, 1f),
                (new Vector2I(1920, 1080), 0.75f, 0.85f),
                (new Vector2I(1920, 1080), 0.75f, 1.2f),
                (new Vector2I(2560, 1440), 1.25f, 1.2f),
            })
            {
                window.Size = size;
                UiScaling.SetOverallScale(cultivation, overall);
                UiScaling.SetFontScale(cultivation, font);
                await Frames(parent);
                passed = CheckContrast(plans) && passed;
                string profile = FormattableString.Invariant($"{size.X}-overall-{overall}-font-{font}");
                Vector2 away = Find<Control>(cultivation, "Header").GetGlobalRect().GetCenter().Round();
                Vector2 point = (plans.GlobalPosition + plans.GetItemRect(0).GetCenter()).Round();
                plans.DeselectAll();
                plans.ReleaseFocus();
                await Move(parent, away);
                await Capture(parent, "normal-" + profile);
                await Move(parent, point);
                if (plans.GetItemAtPosition(plans.GetLocalMousePosition(), true) != 0 || plans.IsSelected(0))
                    passed = Fail("真实鼠标未进入未选中的共享表名称");
                await Capture(parent, "hover-" + profile);
                await Press(parent, point, true);
                await Press(parent, point, false);
                if (!plans.IsSelected(0) || !plans.HasFocus())
                    passed = Fail("原生点击没有选中共享表并聚焦列表");
                await Capture(parent, "selected-hover-focus-" + profile);
                await Move(parent, away);
                if (!plans.IsSelected(0) || !plans.HasFocus())
                    passed = Fail("移开指针丢失共享表选择或列表焦点");
                await Capture(parent, "selected-focus-" + profile);
            }
            if (!main.Game.IsPaused || main.Game.Calendar.ElapsedSeconds != seconds)
                passed = Fail("列表选中、悬停或倍率检查改变暂停经营日期");
            window.Size = new Vector2I(1920, 1080);
            UiScaling.SetOverallScale(cultivation, 1f);
            UiScaling.SetFontScale(cultivation, 1f);
            await Frames(parent);
            passed = await CheckDeletion(parent, main, cultivation) && passed;
            return passed;
        }
        finally
        {
            main?.QueueFree();
            await Frames(parent);
            window.Size = originalSize;
            await Frames(parent);
            if (graphical) Input.WarpMouse(originalMouse - DisplayServer.WindowGetPosition());
        }
    }

    private static async Task<bool> CheckDeletion(Node parent, Main main, CultivationWindow cultivation)
    {
        ItemList plans = Find<ItemList>(cultivation, "CultivationPlanList");
        LineEdit name = Find<LineEdit>(cultivation, "CultivationPlanName");
        Button delete = Find<Button>(cultivation, "DeleteCultivationPlanButton");
        bool passed = true;
        Click(cultivation, "NewCultivationPlanButton");
        name.Text = "未保存草稿";
        name.GrabFocus();
        name.CaretColumn = 2;
        cultivation.Refresh(main.Game);
        await Frames(parent);
        if (!delete.Disabled || name.Text != "未保存草稿" || !name.HasFocus() || name.CaretColumn != 2)
            passed = Fail("未保存草稿仍可删除表，或普通刷新丢失草稿焦点");
        plans.Select(0);
        plans.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
        name.Text = "未保存改名";
        name.GrabFocus();
        name.CaretColumn = 2;
        cultivation.ShowDeleteResult(main.Game.DeleteCultivationPlan(int.MaxValue));
        cultivation.Refresh(main.Game);
        await Frames(parent);
        if (delete.Disabled || name.Text != "未保存改名" || !name.HasFocus() || name.CaretColumn != 2 ||
            main.Game.GetCultivationPlans().Count != 1)
            passed = Fail("真实删除拒绝丢失已保存表草稿、焦点或配置");
        await Capture(parent, "delete-empty-before-1920");
        await ClickDelete(parent, cultivation, delete);
        if (main.Game.GetCultivationPlans().Count != 0 || plans.ItemCount != 0 || !delete.Disabled || name.Text != "新年度表")
            passed = Fail("原生删除空表没有同步真实列表、选择和编辑器");
        name.Text = "多田共享轮作";
        CultivationTimeline timeline = Find<CultivationTimeline>(cultivation, "CultivationTimeline");
        foreach ((CropKind crop, int day) in new[] { (CropKind.Radish, 0), (CropKind.Wheat, 5) })
        {
            Vector2 point = new(42 + timeline.PixelsPerDay * (day + FarmGame.GetCrop(crop).GrowthDays / 2f + 0.5f), 40);
            var data = CultivationTimeline.DragData(crop, 0);
            if (!timeline._CanDropData(point, data)) return Fail("删除夹具的真实作物草稿被排程拒绝");
            timeline._DropData(point, data);
        }
        Click(cultivation, "SaveCultivationPlanButton");
        var saved = main.Game.GetCultivationPlans().Single();
        Vector2I[] farms = main.Game.GetBuildingSpaces().Where(space => space.Building == BuildingKind.Farm)
            .Take(2).Select(space => space.AnchorCell).ToArray();
        foreach (Vector2I farm in farms) Find<CheckBox>(cultivation, $"CultivationFarm{farm.X}_{farm.Y}").ButtonPressed = true;
        Click(cultivation, "ApplyCultivationPlanButton");
        main.Game.SetPaused(false);
        for (int tick = 0; tick < 2; tick++) main.Game.AdvanceTick();
        Click(main, "PauseButton");
        main.GetNode<WorldMap>("WorldMap").SyncFromGame();
        await Frames(parent);
        if (saved.Entries.Count != 2 || main.Game.GetCultivationPlans().Single().ReferencingFarms != 2 ||
            farms.Any(farm => main.Game.GetPlot(farm).Crop != CropStage.Growing) || !delete.TooltipText.Contains("2 块"))
            passed = Fail("删除夹具没有真实多田引用、生长中的本轮或引用影响提示");
        PlotSnapshot[] plots = farms.Select(main.Game.GetPlot).ToArray();
        uint seconds = main.Game.Calendar.ElapsedSeconds;
        int money = main.Game.MoneyCents;
        string date = Find<Label>(main, "CalendarLabel").Text;
        await Capture(parent, "delete-referenced-before-1920");
        await ClickDelete(parent, cultivation, delete);
        if (plans.ItemCount != 0 || main.Game.GetCultivationPlans().Count != 0 || !delete.Disabled ||
            !plots.SequenceEqual(farms.Select(main.Game.GetPlot)) || farms.Any(farm => main.Game.GetFarmCultivation(farm) != default) ||
            main.Game.Calendar.ElapsedSeconds != seconds || !main.Game.IsPaused || main.Game.MoneyCents != money ||
            Find<Label>(main, "CalendarLabel").Text != date ||
            !Find<Label>(cultivation, "CultivationFeedback").Text.Contains("当前作物保留"))
            passed = Fail("删除引用表没有同步列表和解除引用，或修改本轮、金币、日期或真实反馈");
        await Capture(parent, "delete-referenced-after-1920");
        name.Text = "删除后新空表";
        Click(cultivation, "SaveCultivationPlanButton");
        var empty = main.Game.GetCultivationPlans().Single();
        if (empty.Id <= saved.Id || empty.Entries.Count != 0)
            passed = Fail("删除成功后草稿残留旧作物条，或保存复用了旧表编号");
        main.Game.DeleteCultivationPlan(empty.Id);
        cultivation.Refresh(main.Game);
        await Frames(parent);
        if (plans.ItemCount != 0 || !delete.Disabled || name.Text != "新年度表")
            passed = Fail("所选保存表被外部删除后刷新没有清除失效选择");
        return passed;
    }

    private static async Task ClickDelete(Node parent, CultivationWindow window, Button button)
    {
        Rect2 scroll = Find<ScrollContainer>(window, "WindowScroll").GetGlobalRect();
        if (!scroll.Encloses(button.GetGlobalRect())) throw new InvalidOperationException("默认共享表删除入口不在真实可见区域");
        Vector2 point = button.GetGlobalRect().GetCenter().Round();
        await Move(parent, point);
        await Press(parent, point, true);
        await Press(parent, point, false);
    }

    private static bool CheckContrast(ItemList list)
    {
        Color panel = ((StyleBoxFlat)list.GetThemeStylebox("panel")).BgColor;
        Color Layer(string name, Color under) => list.GetThemeStylebox(name) is StyleBoxFlat style
            ? under.Blend(style.BgColor) : under;
        Color selected = Layer("selected", panel);
        Color focused = Layer("selected_focus", panel);
        bool passed = true;
        foreach ((string colorName, Color background) in new[]
        {
            ("font_color", panel),
            ("font_hovered_color", Layer("hovered", panel)),
            ("font_selected_color", selected),
            ("font_selected_color", focused),
            ("font_hovered_selected_color", Layer("hovered_selected", selected)),
            ("font_hovered_selected_color", Layer("hovered_selected_focus", focused)),
        })
        {
            Color text = list.GetThemeColor(colorName);
            double contrast = Contrast(text, background);
            if (contrast < 4.5)
                passed = Fail($"{colorName} 实际字色{text}与背景{background}对比{contrast:F2}，低于4.5");
        }
        return passed;
    }

    private static double Contrast(Color first, Color second)
    {
        static double Linear(float value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        static double Light(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        double a = Light(first), b = Light(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static async Task Move(Node parent, Vector2 point)
    {
        if (DisplayServer.GetName() != "headless") Input.WarpMouse(point);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
        Input.FlushBufferedEvents();
        await Frames(parent);
    }

    private static async Task Press(Node parent, Vector2 point, bool pressed)
    {
        Input.ParseInputEvent(new InputEventMouseButton
        {
            Position = point,
            GlobalPosition = point,
            ButtonIndex = MouseButton.Left,
            Pressed = pressed,
            ButtonMask = pressed ? MouseButtonMask.Left : 0,
        });
        Input.FlushBufferedEvents();
        await Frames(parent);
    }

    private static async Task Capture(Node parent, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://build/issue94-cultivation-fix-validation");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + name + ".png");
    }

    private static async Task Frames(Node parent)
    {
        for (int frame = 0; frame < 3; frame++) await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Click(Node parent, string name) => Find<Button>(parent, name).EmitSignal(Button.SignalName.Pressed);
    private static T Find<T>(Node parent, string name) where T : Node => parent.FindChild(name, true, false) as T ??
        throw new InvalidOperationException("缺少共享表控件：" + name);
    private static bool Fail(string message) { GD.PushError("共享年度表管理：" + message); return false; }
}
