using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestUiScaling : Node
{
    public override async void _Ready()
    {
        try
        {
            bool passed = await RunChecksAsync(this);
            if (passed) GD.Print("UI 整体/字体倍率、真实输入与固定像素尺寸检查通过");
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
        Vector2I originalSize = parent.GetWindow().Size;
        Main? main = null;
        try
        {
            parent.GetWindow().Size = new Vector2I(1920, 1080);
            await Frames(parent);
            bool reparentingPassed = await CheckReparenting(parent);
            main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            parent.AddChild(main);
            main.SetProcess(false);
            main.Game.SetPaused(true);
            uint beforeSeconds = main.Game.Calendar.ElapsedSeconds;
            Click(main, "InventoryButton");
            await Frames(parent);
            InventoryWindow inventory = Find<InventoryWindow>(main, "InventoryWindow");
            MarketWindow market = Find<MarketWindow>(main, "MarketWindow");
            LineEdit input = Find<LineEdit>(inventory, "RawReserveWheatInput");
            Label caption = Find<Label>(inventory, "RawReserveWheatLabel");
            float baseWidth = input.CustomMinimumSize.X;
            int baseFont = caption.GetThemeFontSize("font_size");
            HScrollBar horizontalScroll = Find<ScrollContainer>(inventory, "InventoryScroll").GetHScrollBar();
            Vector2 marketMinimum = market.GetCombinedMinimumSize();
            int marketFont = Find<Label>(market, "MarketDates").GetThemeFontSize("font_size");
            input.Text = "123";
            input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
            input.GrabFocus();
            input.CaretColumn = 2;
            bool passed = reparentingPassed;
            foreach (float overall in new[] { 0.75f, 1f, 1.25f })
                foreach (float font in new[] { 0.85f, 1f, 1.2f })
                {
                    UiScaling.SetOverallScale(inventory, overall);
                    UiScaling.SetFontScale(inventory, font);
                    inventory.Refresh(main.Game);
                    await Frames(parent);
                    if (Math.Abs(input.CustomMinimumSize.X - baseWidth * overall) > 0.1f ||
                        caption.GetThemeFontSize("font_size") != (int)Math.Round(baseFont * overall * font))
                        passed = Fail($"整体 {overall} / 字体 {font} 没有按基准尺寸及相对字号生效");
                    if (input.Text != "123" || !input.HasFocus() || input.CaretColumn != 2)
                        passed = Fail("设置倍率及经营刷新丢失输入草稿、焦点或光标");
                    Rect2 rect = input.GetGlobalRect();
                    UiScaling.SetOverallScale(inventory, overall);
                    UiScaling.SetFontScale(inventory, font);
                    await Frames(parent);
                    if (!input.GetGlobalRect().IsEqualApprox(rect)) passed = Fail("重复设置相同倍率累积改写几何");
                    if (!market.GetCombinedMinimumSize().IsEqualApprox(marketMinimum) ||
                        Find<Label>(market, "MarketDates").GetThemeFontSize("font_size") != marketFont)
                        passed = Fail("库存的局部倍率污染市场窗口");
                    if (horizontalScroll.GetThemeIcon("increment").GetSize() != Vector2.Zero ||
                        horizontalScroll.GetThemeIcon("decrement").GetSize() != Vector2.Zero)
                        passed = Fail("倍率设置把原生无箭头滚动条的零尺寸图标改成可见位图");
                    passed = CheckBounds(inventory) && passed;
                    await Capture(parent, $"inventory-{Number(overall)}-font-{Number(font)}.png");
                }
            input.ReleaseFocus();
            UiScaling.SetOverallScale(inventory, 1f);
            UiScaling.SetFontScale(inventory, 1f);
            Control ui = main.GetNode<Control>("CanvasLayer/UiRoot");
            UiScaling.SetOverallScale(ui, 1.25f);
            UiScaling.SetOverallScale(inventory, 0.75f);
            UiScaling.SetFontScale(ui, 0.85f);
            UiScaling.SetFontScale(inventory, 1.2f);
            await Frames(parent);
            if (Math.Abs(input.CustomMinimumSize.X - baseWidth * 1.25f * 0.75f) > 0.1f ||
                caption.GetThemeFontSize("font_size") != (int)Math.Round(baseFont * 1.25f * 0.75f * 0.85f * 1.2f))
                passed = Fail("父子 UI 的局部整体/字体倍率没有组合");
            UiScaling.SetOverallScale(ui, 1f);
            UiScaling.SetFontScale(ui, 1f);
            UiScaling.SetOverallScale(inventory, 1f);
            UiScaling.SetFontScale(inventory, 1f);
            inventory.Hide();
            BuildCatalogWindow catalog = Find<BuildCatalogWindow>(main, "BuildWindow");
            Button farmCard = Find<Button>(catalog, "FarmCard");
            int originalIconWidth = farmCard.GetThemeConstant("icon_max_width");
            UiScaling.SetOverallScale(catalog, 0.75f);
            await Frames(parent);
            if (farmCard.Icon == null || farmCard.GetThemeConstant("icon_max_width") != (int)Math.Round(originalIconWidth * 0.75f))
                passed = Fail("整体倍率没有同步建筑卡片图标尺寸");
            UiScaling.SetOverallScale(catalog, 1.25f);
            await Frames(parent);
            if (farmCard.GetThemeConstant("icon_max_width") != (int)Math.Round(originalIconWidth * 1.25f))
                passed = Fail("建筑图标重复设置倍率没有使用原始宽度");
            UiScaling.SetOverallScale(catalog, 1f);
            WorldMap facilityMap = main.GetNode<WorldMap>("WorldMap");
            facilityMap.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(189, 189));
            DraggableWindow detail = Find<DraggableWindow>(main, "DetailWindow");
            foreach (float font in new[] { 0.85f, 1f, 1.2f })
            {
                UiScaling.SetFontScale(detail, font);
                await Frames(parent);
                passed = TestUiVisualLayout.CheckFacilityText(Find<Control>(detail, "FarmDetailsPanel"), new[]
                { "FarmCropTitle", "FarmCurrentStatus", "FarmHarvestQuantity", "FarmHarvestQuantityUnit", "FarmRawPrice", "FarmRawPriceUnit" },
                    "FarmWaterStatus") && passed;
                await Capture(parent, "farm-font-" + Number(font) + ".png");
            }
            UiScaling.SetFontScale(detail, 1f);
            detail.Hide();

            Click(main, "CultivationButton");
            CultivationWindow cultivation = Find<CultivationWindow>(main, "CultivationWindow");
            UiScaling.SetOverallScale(cultivation, 1.25f);
            UiScaling.SetFontScale(cultivation, 1.2f);
            Vector2I newFarm = new(0, 0);
            if (main.Game.BuildFarm(newFarm) != null) passed = Fail("动态农田勾选夹具建造失败");
            cultivation.Refresh(main.Game);
            await Frames(parent);
            CheckBox check = Find<CheckBox>(cultivation, "CultivationFarm0_0");
            if (check.GetThemeFontSize("font_size") != (int)Math.Round(12 * 1.25f * 1.2f) ||
                Math.Abs(check.CustomMinimumSize.X - 170 * 1.25f) > 0.1f)
                passed = Fail("新增农田勾选没有继承窗口当前倍率");
            int originalCheckWidth = ThemeDB.GetDefaultTheme().GetIcon("unchecked", "CheckBox").GetWidth();
            if (check.GetThemeIcon("unchecked").GetWidth() != (int)Math.Round(originalCheckWidth * 1.25f))
                passed = Fail("整体倍率没有同步原生农田勾选图标");
            foreach (float overall in new[] { 0.75f, 1f, 1.25f })
                foreach (float font in new[] { 0.85f, 1f, 1.2f })
                {
                    UiScaling.SetOverallScale(cultivation, overall);
                    UiScaling.SetFontScale(cultivation, font);
                    Click(cultivation, "NewCultivationPlanButton");
                    Find<LineEdit>(cultivation, "CultivationPlanName").Text = $"倍率 {overall}/{font}";
                    await Frames(parent);
                    CultivationTimeline timeline = Find<CultivationTimeline>(cultivation, "CultivationTimeline");
                    float vertical = Math.Max(overall, (int)Math.Round(12 * overall * font) / 12f);
                    Vector2 landing = timeline.GlobalPosition + new Vector2(42 * overall + timeline.PixelsPerDay * 28.5f, 40 * vertical);
                    Vector2 sizeBefore = timeline.Size;
                    bool dragging = await Drag(parent, Find<Control>(cultivation, "CultivationCropWheat").GetGlobalRect().GetCenter(),
                        landing, timeline, sizeBefore, (int)Math.Round(13 * overall * font));
                    if (!dragging) passed = Fail("倍率改变后真实拖放预览、轨道或鼠标中心不一致");
                    Click(cultivation, "SaveCultivationPlanButton");
                    CultivationPlanSnapshot? saved = main.Game.GetCultivationPlans().LastOrDefault();
                    if (saved == null || saved.Entries.Count != 1 || saved.Entries[0].StartDay != 20)
                        passed = Fail($"倍率 {overall}/{font} 原生拖放没有准确落在日20");
                    await Capture(parent, $"cultivation-{Number(overall)}-font-{Number(font)}.png");
                    ScrollContainer outer = Find<ScrollContainer>(cultivation, "WindowScroll");
                    outer.EnsureControlVisible(Find<Button>(cultivation, "SaveCultivationPlanButton"));
                    await Frames(parent);
                    if (!outer.GetGlobalRect().Encloses(Find<Button>(cultivation, "SaveCultivationPlanButton").GetGlobalRect()))
                        passed = Fail("字体变大后无法滚动到年度表保存操作");
                    outer.ScrollVertical = 0;
                    await Frames(parent);
                }
            passed = await CheckScaledTooltip(parent, cultivation) && passed;
            UiScaling.SetOverallScale(cultivation, 1f);
            UiScaling.SetFontScale(cultivation, 1f);
            cultivation.Hide();
            Click(main, "MarketButton");
            Click(main, "OpenTradeOrdersButton");
            TradeOrdersWindow orders = Find<TradeOrdersWindow>(main, "TradeOrdersWindow");
            UiScaling.SetOverallScale(orders, 0.75f);
            UiScaling.SetFontScale(orders, 1.2f);
            Click(orders, "AddOrderGroupButton");
            await Frames(parent);
            LineEdit dynamicInput = Find<LineEdit>(orders, "OrderConditionValue2");
            if (Math.Abs(dynamicInput.CustomMinimumSize.X - 145 * 0.75f) > 0.1f ||
                dynamicInput.GetThemeFontSize("font_size") != (int)Math.Round(UiElements.SharedTheme.DefaultFontSize * 0.75f * 1.2f))
                passed = Fail("动态条件编辑器没有继承当前整体与字体倍率");
            orders.Hide();
            Click(main, "InventoryButton");
            UiScaling.SetOverallScale(inventory, 1.25f);
            UiScaling.SetFontScale(inventory, 0.85f);
            await Frames(parent);
            Vector2 fixedWindow = inventory.Size;
            Vector2 fixedInput = input.Size;
            WorldMap map = main.GetNode<WorldMap>("WorldMap");
            foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(3840, 2160) })
            {
                parent.GetWindow().Size = size;
                await Frames(parent);
                if (!inventory.Size.IsEqualApprox(fixedWindow) || !input.Size.IsEqualApprox(fixedInput) ||
                    !parent.GetViewport().GetFinalTransform().Scale.IsEqualApprox(Vector2.One))
                    passed = Fail("分辨率扩大改变设定的 UI 像素尺寸或自动拉伸画布");
                Transform2D mapTransform = map.GetGlobalTransformWithCanvas();
                if (!new Vector2(mapTransform.BasisXform(new Vector2(64, 0)).Length(),
                    mapTransform.BasisXform(new Vector2(0, 32)).Length()).IsEqualApprox(new Vector2(80, 40)))
                    passed = Fail("UI 显示配置改变原地图基础格像素策略");
                await Capture(parent, "fixed-inventory-" + size.X + ".png");
            }
            passed = await DragWindow(parent, inventory) && passed;
            if (main.Game.Calendar.ElapsedSeconds != beforeSeconds)
                passed = Fail("UI 倍率、窗口分辨率或原生交互推进了暂停的经营日期");
            return passed;
        }
        finally
        {
            main?.QueueFree();
            await Frames(parent);
            parent.GetWindow().Size = originalSize;
            await Frames(parent);
        }
    }

    private static async Task<bool> CheckReparenting(Node parent)
    {
        var host = new Control { Name = "ScalingReparentFixture", Visible = false };
        var first = new Control();
        var second = new Control();
        host.AddChild(first);
        host.AddChild(second);
        var originalTheme = new Theme { DefaultFontSize = 17 };
        originalTheme.SetFontSize("font_size", "TooltipLabel", 19);
        originalTheme.SetFontSize("font_size", "PopupMenu", 21);
        var originalStyle = new StyleBoxFlat { BgColor = new Color("986b49"), ContentMarginLeft = 4 };
        var moving = new PanelContainer { Theme = originalTheme, CustomMinimumSize = new Vector2(100, 40) };
        moving.AddThemeStyleboxOverride("panel", originalStyle);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 7);
        var label = new Label { Text = "原始字号" };
        label.AddThemeFontSizeOverride("font_size", 20);
        var option = new OptionButton();
        option.AddItem("原始选项");
        var originalPopupTheme = new Theme();
        originalPopupTheme.SetFontSize("font_size", "PopupMenu", 23);
        Color popupInk = new("334d35");
        originalPopupTheme.SetColor("font_color", "PopupMenu", popupInk);
        var popupStyle = new StyleBoxFlat
        {
            BgColor = new Color("ddd2bc"),
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
        };
        popupStyle.SetBorderWidthAll(2);
        originalPopupTheme.SetStylebox("panel", "PopupMenu", popupStyle);
        originalPopupTheme.SetConstant("v_separation", "PopupMenu", 4);
        using var popupIconImage = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        popupIconImage.Fill(popupInk);
        originalPopupTheme.SetIcon("checked", "PopupMenu", ImageTexture.CreateFromImage(popupIconImage));
        var customOption = new OptionButton();
        customOption.AddItem("独立菜单主题");
        customOption.GetPopup().Theme = originalPopupTheme;
        content.AddChild(label);
        content.AddChild(option);
        content.AddChild(customOption);
        moving.AddChild(content);
        first.AddChild(moving);
        parent.AddChild(host);
        bool PopupMatches(int fontSize, int margin, int separation, int iconSize)
        {
            PopupMenu popup = customOption.GetPopup();
            Theme popupTheme = popup.Theme;
            return popupTheme.GetFontSize("font_size", "PopupMenu") == fontSize &&
                popupTheme.GetColor("font_color", "PopupMenu") == popupInk &&
                popup.GetThemeStylebox("panel") is StyleBoxFlat style && style.BgColor == popupStyle.BgColor &&
                style.BorderWidthLeft == 2 && style.BorderWidthRight == 2 &&
                style.ContentMarginLeft == margin && style.ContentMarginRight == margin &&
                popup.GetThemeConstant("v_separation") == separation &&
                popup.GetThemeIcon("checked").GetSize() == new Vector2(iconSize, iconSize) &&
                originalPopupTheme.GetFontSize("font_size", "PopupMenu") == 23 &&
                popupStyle.ContentMarginLeft == 8 && popupStyle.BorderWidthLeft == 2 &&
                originalPopupTheme.GetConstant("v_separation", "PopupMenu") == 4 &&
                originalPopupTheme.GetIcon("checked", "PopupMenu").GetSize() == new Vector2(8, 8);
        }
        try
        {
            UiScaling.SetOverallScale(first, 1f);
            if (!PopupMatches(23, 8, 4, 8)) return Fail("首次登记覆盖了OptionButton独立菜单主题或基准字号");
            UiScaling.SetOverallScale(second, 0.75f);
            UiScaling.SetFontScale(second, 0.85f);
            UiScaling.SetOverallScale(moving, 1.25f);
            await Frames(parent);
            if (!PopupMatches(29, 10, 5, 10)) return Fail("整体倍率没有同步独立菜单的真实样式、间距和图标");
            UiScaling.SetFontScale(moving, 1.2f);
            await Frames(parent);
            if (moving.CustomMinimumSize.X != 125 || label.GetThemeFontSize("font_size") != 30 || !PopupMatches(34, 10, 5, 10))
                return Fail("重新挂树夹具未实际完成首次整体及字体缩放");
            first.RemoveChild(moving);
            if (moving.Theme != originalTheme || moving.CustomMinimumSize.X != 100 ||
                moving.GetThemeStylebox("panel") != originalStyle ||
                !label.HasThemeFontSizeOverride("font_size") || label.GetThemeFontSize("font_size") != 20 ||
                content.GetThemeConstant("separation") != 7 || customOption.GetPopup().Theme != originalPopupTheme)
                return Fail("退出场景树没有恢复原始主题、尺寸或既有局部覆盖");
            first.AddChild(moving);
            await Frames(parent);
            if (moving.CustomMinimumSize.X != 100 || label.GetThemeFontSize("font_size") != 20 || !PopupMatches(23, 8, 4, 8))
                return Fail("同一子树重挂原父后累计了旧局部倍率");
            UiScaling.SetOverallScale(first, 1.25f);
            UiScaling.SetFontScale(first, 1.2f);
            first.RemoveChild(moving);
            second.AddChild(moving);
            await Frames(parent);
            if (moving.CustomMinimumSize.X != 75 || label.GetThemeFontSize("font_size") != 13 ||
                label.GetThemeFontSize("font_size", "TooltipLabel") != 12 ||
                option.GetPopup().Theme.GetFontSize("font_size", "PopupMenu") != 13 || !PopupMatches(15, 6, 3, 6))
                return Fail("跨父重挂没有继承新倍率，或将旧父/提示/菜单字号作为新基准");
            UiScaling.SetOverallScale(moving, 1.25f);
            UiScaling.SetFontScale(moving, 1.2f);
            UiScaling.SetOverallScale(moving, 1f);
            UiScaling.SetFontScale(moving, 1f);
            await Frames(parent);
            if (moving.CustomMinimumSize.X != 75 || label.GetThemeFontSize("font_size") != 13 ||
                !moving.HasThemeStyleboxOverride("panel") ||
                ((StyleBoxFlat)moving.GetThemeStylebox("panel")).BgColor != originalStyle.BgColor || !PopupMatches(15, 6, 3, 6))
                return Fail("跨父子树设置回1没有恢复新父倍率下的原始排版和局部样式");
            second.RemoveChild(moving);
            if (moving.Theme != originalTheme || label.Theme != null ||
                label.GetThemeFontSize("font_size") != 20 || content.GetThemeConstant("separation") != 7 ||
                customOption.GetPopup().Theme != originalPopupTheme)
                return Fail("再次退出后原始自身主题或局部覆盖未保留");
            first.AddChild(moving);
            return true;
        }
        finally
        {
            if (moving.GetParent() == null) moving.Free();
            host.Free();
            await Frames(parent);
        }
    }

    private static async Task<bool> CheckScaledTooltip(Node parent, CultivationWindow window)
    {
        if (DisplayServer.GetName() == "headless") return true;
        UiScaling.SetOverallScale(window, 1.25f);
        UiScaling.SetFontScale(window, 1.2f);
        Click(window, "NewCultivationPlanButton");
        await Frames(parent);
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        float vertical = Math.Max(1.25f, (int)Math.Round(12 * 1.25f * 1.2f) / 12f);
        Vector2 local = new(42 * 1.25f + timeline.PixelsPerDay * 22.5f, 40 * vertical);
        var data = CultivationTimeline.DragData(CropKind.Radish, 0);
        if (!timeline._CanDropData(local, data)) return Fail("缩放后的短条悬浮夹具不能落位");
        timeline._DropData(local, data);
        await Frames(parent);
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string tooltipText = timeline._GetTooltip(local);
        Vector2I original = DisplayServer.MouseGetPosition();
        try
        {
            Vector2 actual = parent.GetViewport().GetFinalTransform() * (timeline.GlobalPosition + local);
            Input.WarpMouse(actual);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = actual, GlobalPosition = actual });
            double delay = ProjectSettings.GetSetting("gui/timers/tooltip_delay_sec", 0.5).AsDouble();
            await parent.ToSignal(parent.GetTree().CreateTimer(delay + 0.15), SceneTreeTimer.SignalName.Timeout);
            Label? tooltip = Descendants(parent.GetTree().Root).OfType<Label>().FirstOrDefault(l =>
                l.Text == tooltipText && l.IsVisibleInTree());
            if (string.IsNullOrEmpty(tooltipText) || tooltip == null || tooltip.GetThemeFontSize("font_size") !=
                (int)Math.Round(UiElements.SharedTheme.DefaultFontSize * 1.25f * 1.2f))
                return Fail("原生窄条提示没有继承独立字体倍率");
            await Capture(parent, "scaled-native-tooltip.png");
            return true;
        }
        finally
        {
            Input.WarpMouse(original - DisplayServer.WindowGetPosition());
            Input.ParseInputEvent(new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero });
            await Frames(parent);
        }
    }

    private static async Task<bool> DragWindow(Node parent, DraggableWindow window)
    {
        Viewport viewport = parent.GetViewport();
        bool graphical = DisplayServer.GetName() != "headless";
        Vector2I original = graphical ? DisplayServer.MouseGetPosition() : default;
        Vector2 start = Find<Control>(window, "Header").GetGlobalRect().GetCenter().Round();
        Vector2 movement = new(60, 24);
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
            Send(new InputEventMouseMotion { Position = start, GlobalPosition = start });
            await Frames(parent);
            Vector2 before = window.Position;
            Send(new InputEventMouseButton { Position = start, GlobalPosition = start, ButtonIndex = MouseButton.Left, Pressed = true });
            await Frames(parent);
            Send(new InputEventMouseMotion { Position = start + movement, GlobalPosition = start + movement, Relative = movement, ButtonMask = MouseButtonMask.Left });
            await Frames(parent);
            Send(new InputEventMouseButton { Position = start + movement, GlobalPosition = start + movement, ButtonIndex = MouseButton.Left, Pressed = false });
            await Frames(parent);
            return (window.Position - before).IsEqualApprox(movement) && CheckBounds(window) ||
                Fail($"缩放窗口的真实标题栏命中与拖动屏幕距离不一致：位置 {before}→{window.Position}，" +
                    $"目标 {start}→{start + movement}，实际鼠标 {viewport.GetMousePosition()}，变换 {viewport.GetFinalTransform()}，" +
                    $"窗口 {parent.GetWindow().Size}@{DisplayServer.WindowGetPosition()} 屏幕 {DisplayServer.ScreenGetSize()} 屏幕鼠标 {DisplayServer.MouseGetPosition()}");
        }
        finally
        {
            if (graphical) Input.WarpMouse(original - DisplayServer.WindowGetPosition());
        }
    }

    private static async Task<bool> Drag(Node parent, Vector2 from, Vector2 to, CultivationTimeline timeline,
        Vector2 sizeBefore, int previewFont)
    {
        Viewport viewport = parent.GetViewport();
        bool graphical = DisplayServer.GetName() != "headless";
        Vector2I original = graphical ? DisplayServer.MouseGetPosition() : default;
        void Send(InputEventMouse input)
        {
            var actual = (InputEventMouse)input.XformedBy(viewport.GetFinalTransform());
            actual.GlobalPosition = viewport.GetFinalTransform() * input.GlobalPosition;
            if (graphical && input is InputEventMouseMotion) Input.WarpMouse(actual.Position);
            Input.ParseInputEvent(actual);
        }
        try
        {
            Send(new InputEventMouseMotion { Position = from, GlobalPosition = from });
            Send(new InputEventMouseButton { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true });
            Send(new InputEventMouseMotion { Position = from + new Vector2(18, 0), GlobalPosition = from + new Vector2(18, 0), Relative = new Vector2(18, 0), ButtonMask = MouseButtonMask.Left });
            await Frames(parent);
            Send(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from - new Vector2(18, 0), ButtonMask = MouseButtonMask.Left });
            await Frames(parent);
            Label? preview = Descendants(parent.GetTree().Root).OfType<Label>().FirstOrDefault(c =>
                c.Text == "小麦 · 16天" && c.GetParent() is PanelContainer);
            bool valid = viewport.GuiIsDragging() && timeline.Size.IsEqualApprox(sizeBefore) &&
                preview != null && preview.GetThemeFontSize("font_size") == previewFont &&
                (((PanelContainer)preview.GetParent()).GetGlobalRect().GetCenter() - viewport.GetMousePosition()).Length() <= 1.5f;
            Send(new InputEventMouseButton { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false });
            await Frames(parent);
            return valid;
        }
        finally
        {
            if (graphical) Input.WarpMouse(original - DisplayServer.WindowGetPosition());
        }
    }

    private static System.Collections.Generic.IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        { yield return child; foreach (Node descendant in Descendants(child)) yield return descendant; }
    }
    private static bool CheckBounds(Control window) => window.GetViewport().GetVisibleRect().Encloses(window.GetGlobalRect()) || Fail("缩放后窗口超出真实视口");
    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static async Task Capture(Node parent, string file)
    {
        if (DisplayServer.GetName() == "headless") return;
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://build/issue94-scale-validation");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + file);
    }
    private static async Task Frames(Node parent)
    { for (int i = 0; i < 3; i++) await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Click(Node parent, string name) => Find<Button>(parent, name).EmitSignal(Button.SignalName.Pressed);
    private static T Find<T>(Node parent, string name) where T : Node =>
        parent.FindChild(name, true, false) as T ??
        throw new InvalidOperationException($"{parent.GetPath()} 下缺少 {typeof(T).Name} 节点 {name}");
    private static bool Fail(string message) { GD.PushError("UI 倍率：" + message); return false; }
}
