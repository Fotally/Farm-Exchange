using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestUiVisualLayout : Node
{
    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        if (passed) GD.Print("像素田园主界面、窗口边界与主题检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        Vector2I originalSize = parent.GetWindow().Size;
        bool passed = true;
        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(1600, 900) })
        {
            parent.GetWindow().Size = size;
            await Frames(parent);
            var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            parent.AddChild(main);
            main.GetNode<Timer>("TickTimer").Stop();
            try
            {
                await Frames(parent);
                passed = CheckMain(main) && passed;
                await Capture(parent, "main-" + size.X + ".png");
                var map = main.GetNode<WorldMap>("WorldMap");
                map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(189, 189));
                await Frames(parent);
                passed = CheckWindow(Find<DraggableWindow>(main, "DetailWindow")) && passed;
                await Capture(parent, "farm-detail-" + size.X + ".png");
                passed = await CheckReachable(parent, Find<DraggableWindow>(main, "DetailWindow"),
                    "WindowScroll", "RemoveButton") && passed;
                Click(main, "ChangeCropButton");
                await Frames(parent);
                passed = CheckWindow(Find<DraggableWindow>(main, "CropWindow")) && passed;
                await Capture(parent, "crop-" + size.X + ".png");
                passed = await CheckReachable(parent, Find<DraggableWindow>(main, "CropWindow"),
                    "CropScroll", "CropCardRadish") && passed;
                HideWindows(main);
                map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(189, 192));
                await Frames(parent);
                await Capture(parent, "processor-detail-" + size.X + ".png");
                HideWindows(main);
                main.Game.TryPlace(new Vector2I(198, 192), BuildingKind.Road, CropKind.Wheat);
                map.SyncFromGame();
                map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(198, 192));
                await Frames(parent);
                await Capture(parent, "road-detail-" + size.X + ".png");
                foreach (var entry in new[]
                {
                    ("BuildButton", "BuildWindow", "build"),
                    ("InventoryButton", "InventoryWindow", "inventory"),
                    ("MarketButton", "MarketWindow", "market"),
                    ("CultivationButton", "CultivationWindow", "cultivation"),
                })
                {
                    HideWindows(main);
                    Click(main, entry.Item1);
                    await Frames(parent);
                    passed = CheckWindow(Find<DraggableWindow>(main, entry.Item2)) && passed;
                    await Capture(parent, entry.Item3 + "-" + size.X + ".png");
                    if (entry.Item2 == "BuildWindow")
                    {
                        ScrollContainer outer = Find<ScrollContainer>(Find<DraggableWindow>(main, "BuildWindow"), "WindowScroll");
                        if (outer.ScrollVertical != 0 || !outer.GetGlobalRect().Encloses(
                            Find<Label>(main, "BuildCatalogHint").GetGlobalRect()))
                            passed = Fail("建造费用及取消说明没有默认完整显示");
                        passed = await CheckReachable(parent, Find<DraggableWindow>(main, entry.Item2),
                            "BuildScroll", "ProcessorCardRadish") && passed;
                    }
                }
                HideWindows(main);
                Click(main, "MarketButton");
                Click(main, "OpenTradeOrdersButton");
                await Frames(parent);
                DraggableWindow orders = Find<DraggableWindow>(main, "TradeOrdersWindow");
                passed = CheckWindow(orders) && passed;
                LineEdit quantity = Find<LineEdit>(orders, "OrderQuantityInput");
                if (quantity.GetThemeColor("font_color") != UiElements.Ink ||
                    quantity.GetThemeColor("caret_color") != UiElements.Ink)
                    passed = Fail("数量输入文字与光标没有继承深棕色主题");
                await Capture(parent, "orders-" + size.X + ".png");
                HideWindows(main);
                Click(main, "PauseButton");
                string activity = Find<Label>(main, "ActivityLabel").Text;
                if (!activity.Contains("3 块农田") || !activity.Contains("2 处加工场地") ||
                    !activity.Contains("1 格道路") || !activity.Contains("仍可交易"))
                    passed = Fail("近况未读取真实建筑数量或暂停状态");
                main.Game.TryPlace(new Vector2I(198, 189), BuildingKind.Farm, CropKind.Wheat);
                Click(main, "PauseButton");
                if (!Find<Label>(main, "ActivityLabel").Text.Contains("4 块农田"))
                    passed = Fail("建造后的近况没有更新真实实例数量");
            }
            finally
            {
                main.QueueFree();
                await Frames(parent);
            }
        }
        parent.GetWindow().Size = originalSize;
        await Frames(parent);
        return passed;
    }

    private static bool CheckMain(Main main)
    {
        Rect2 viewport = main.GetViewport().GetVisibleRect();
        Rect2 previous = default;
        foreach (string name in new[] { "BrandPanel", "CalendarPanel", "ResourcesPanel", "MessagePanel", "BottomBar" })
            if (!viewport.Encloses(Find<Control>(main, name).GetGlobalRect()))
                return Fail(name + "超出主画面");
        foreach (string name in new[] { "BuildButton", "InventoryButton", "MarketButton", "CultivationButton" })
        {
            Rect2 rect = Find<Button>(main, name).GetGlobalRect();
            if (!Find<Control>(main, "BottomBar").GetGlobalRect().Encloses(rect) ||
                (previous.Size != Vector2.Zero && previous.Intersects(rect)))
                return Fail("底部经营入口越界或重叠：" + name);
            previous = rect;
        }
        Button pause = Find<Button>(main, "PauseButton");
        if (pause.GetThemeColor("font_color") != UiElements.Ink ||
            pause.GetThemeColor("font_focus_color") != UiElements.Ink ||
            pause.GetThemeColor("font_pressed_color") != UiElements.Ink)
            return Fail("纸色次按钮的普通、焦点与按下文字必须深色");
        if (Find<Control>(main, "TopBar").MouseFilter != Control.MouseFilterEnum.Ignore)
            return Fail("透明顶部间隙阻挡地图输入");
        return true;
    }

    private static bool CheckWindow(DraggableWindow window)
    {
        Rect2 viewport = window.GetViewport().GetVisibleRect();
        Rect2 rect = window.GetGlobalRect();
        if (!window.IsVisibleInTree() || rect.Position.Y < 94 || rect.End.Y > viewport.End.Y - 90 ||
            !viewport.Encloses(rect)) return Fail(window.Name + "未避让顶部状态和底部经营入口");
        Control header = Find<Control>(window, "Header");
        if (!rect.Encloses(header.GetGlobalRect())) return Fail(window.Name + "标题栏被裁切");
        StyleBoxFlat panel = (StyleBoxFlat)window.GetThemeStylebox("panel");
        if (panel.BgColor != UiElements.Paper || panel.CornerRadiusTopLeft != 0)
            return Fail(window.Name + "未使用方形木框纸面主题");
        return true;
    }

    private static void HideWindows(Main main)
    {
        foreach (Node child in main.GetNode<Control>("CanvasLayer/UiRoot").GetChildren())
            if (child is DraggableWindow window) window.Hide();
    }

    private static async Task<bool> CheckReachable(Node parent, DraggableWindow window, string scrollName, string controlName)
    {
        ScrollContainer scroll = Find<ScrollContainer>(window, scrollName);
        Control control = Find<Control>(window, controlName);
        scroll.EnsureControlVisible(control);
        await Frames(parent);
        if (!scroll.GetGlobalRect().Encloses(control.GetGlobalRect()))
            return Fail(window.Name + "无法滚动到操作：" + controlName);
        scroll.ScrollVertical = 0;
        await Frames(parent);
        return true;
    }

    private static async Task Capture(Node parent, string file)
    {
        if (DisplayServer.GetName() == "headless") return;
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://build/issue94-validation");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + file);
    }

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Click(Node parent, string name) => Find<Button>(parent, name).EmitSignal(Button.SignalName.Pressed);
    private static T Find<T>(Node parent, string name) where T : Node => (T)parent.FindChild(name, true, false);
    private static bool Fail(string message) { GD.PushError("UI 视觉布局：" + message); return false; }
}
