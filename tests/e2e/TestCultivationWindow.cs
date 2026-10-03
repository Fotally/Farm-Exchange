using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestCultivationWindow : Node
{
    public override async void _Ready()
    {
        bool passed = RunChecks(this) && await RunLayoutChecks(this);
        if (passed) GD.Print("年度耕作表编辑、批量应用、手动接管与布局检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks(Node parent)
    {
        Main main = OpenMain(parent);
        bool passed = CheckEditing(main) && CheckManual(main);
        main.QueueFree();
        return passed && CheckRemovalWithCropWindow(parent) && CheckWinterSpringDate(parent);
    }

    private static Main OpenMain(Node parent)
    {
        Main main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        Click(main, "PauseButton");
        Click(main, "CultivationButton");
        return main;
    }

    private static bool CheckEditing(Main main)
    {
        FarmGame game = main.Game;
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        CultivationTimeline timeline = Find<CultivationTimeline>(main, "CultivationTimeline");
        uint before = game.Calendar.ElapsedSeconds;
        if (!window.Visible) return Fail("开局入口未打开年度耕作表");
        if (timeline._CanDropData(new Vector2(-1, 30), CultivationTimeline.DragData(CropKind.Wheat, 0)) ||
            timeline._CanDropData(new Vector2(50, 30), 1) ||
            timeline._GetDragData(Vector2.Zero).VariantType != Variant.Type.Nil)
            return Fail("空白或非法落点产生作物拖放");
        Click(window, "ApplyCultivationPlanButton");
        if (!Find<Label>(window, "CultivationFeedback").Text.Contains("先保存")) return Fail("未保存草稿可应用");
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "冬春轮作";
        Drop(timeline, CropKind.Wheat, 326);
        if (!Find<Label>(window, "CultivationEntryInfo").Text.Contains("冬春跨年")) return Fail("冬春条未显示连续关系");
        Click(window, "SaveCultivationPlanButton");
        if (game.GetCultivationPlans().Count != 1 || game.GetCultivationPlans()[0].Entries[0].StartDay != 326)
            return Fail("拖入冬春条未通过共享表命令保存");
        foreach (var space in game.GetBuildingSpaces().Where(s => s.Building == BuildingKind.Farm))
            Find<CheckBox>(window, $"CultivationFarm{space.AnchorCell.X}_{space.AnchorCell.Y}").ButtonPressed = true;
        Click(window, "ApplyCultivationPlanButton");
        if (game.GetCultivationPlans()[0].ReferencingFarms != 3 ||
            !Find<Label>(window, "CultivationImpact").Text.Contains("3")) return Fail("批量勾选未共享同一年度表");
        Find<LineEdit>(window, "CultivationPlanName").Text = "未保存草稿";
        window.Refresh(game);
        if (Find<LineEdit>(window, "CultivationPlanName").Text != "未保存草稿") return Fail("经营刷新覆盖了名称草稿");
        int id = game.GetCultivationPlans()[0].Entries[0].Id;
        Drop(timeline, CropKind.Wheat, 82, id);
        Click(window, "SaveCultivationPlanButton");
        if (game.GetCultivationPlans()[0].Entries[0].Id != id ||
            game.GetCultivationPlans()[0].Entries[0].StartDay != 82 ||
            !Find<Label>(window, "CultivationFeedback").Text.Contains("已保存")) return Fail("移动未保留整条ID");
        Drop(timeline, CropKind.Corn, 83);
        Click(window, "SaveCultivationPlanButton");
        if (game.GetCultivationPlans()[0].Entries.Count != 1 ||
            string.IsNullOrEmpty(Find<Label>(window, "CultivationFeedback").Text)) return Fail("冲突草稿被提交或未显示拒绝原因");
        Click(window, "RemoveCultivationEntryButton");
        Click(window, "SaveCultivationPlanButton");
        if (game.GetCultivationPlans()[0].Entries.Count != 1) return Fail("移除冲突条未更新草稿");
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "夏末风险";
        Drop(timeline, CropKind.Corn, 166);
        if (!Find<Label>(window, "CultivationFeedback").Text.Contains("风险")) return Fail("越过禁生边界未提示风险");
        SetMode(window, 1);
        Click(window, "SaveCultivationPlanButton");
        if (game.GetCultivationPlans().Count != 2 || game.GetCultivationPlans()[1].Mode != CultivationMode.PrepareNext)
            return Fail("表级预备方式未保存");
        if (game.Calendar.ElapsedSeconds != before) return Fail("编辑和暂停推进了经营");
        main._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        return !window.Visible || Fail("Esc 未关闭年度耕作表");
    }

    private static bool CheckManual(Main main)
    {
        FarmGame game = main.Game;
        var farms = game.GetBuildingSpaces().Where(s => s.Building == BuildingKind.Farm).ToArray();
        foreach (var farm in farms) game.SetFarmCrop(farm.AnchorCell, CropKind.Wheat);
        game.SetPaused(false);
        for (int tick = 0; tick < 20; tick++) game.AdvanceTick(isRaining: true);
        game.ApplyCultivationPlan(game.GetCultivationPlans()[0].Id, farms.Select(f => f.AnchorCell).ToArray());
        game.SetPaused(true);
        main.GetNode<FarmExchange.World.WorldMap>("WorldMap").EmitSignal(
            FarmExchange.World.WorldMap.SignalName.SelectionChanged, farms[0].AnchorCell);
        PlotSnapshot before = game.GetPlot(farms[0].AnchorCell);
        if (before.Crop != CropStage.Growing) return Fail("手动接管夹具没有正在生长的本轮");
        Click(main, "PrepareCropButton");
        Label notice = Find<Label>(main, "ManualCultivationNotice");
        if (!notice.Text.Contains("保留当前作物") || !notice.Text.Contains("解除共享表")) return Fail("手动预备操作前未告知接管范围");
        Click(main, "CropCardRadish");
        PlotSnapshot after = game.GetPlot(farms[0].AnchorCell);
        if (after.CropKind != before.CropKind || after.Crop != before.Crop ||
            game.GetFarmCultivation(farms[0].AnchorCell).PlanId != null ||
            game.GetFarmCultivation(farms[1].AnchorCell).PlanId == null) return Fail("手动预备丢弃本轮或影响其他引用田");
        Click(main, "ChangeCropButton");
        if (!notice.Text.Contains("丢弃当前未收获作物")) return Fail("立即改种操作前未告知损失");
        Click(main, "CropCardWheat");
        if (game.GetPlot(farms[0].AnchorCell).CropKind != CropKind.Wheat ||
            game.GetFarmCultivation(farms[0].AnchorCell).PreparedCrop != null) return Fail("立即改种未清除预备安排");
        return true;
    }

    private static bool CheckRemovalWithCropWindow(Node parent)
    {
        Main main = OpenMain(parent);
        var farms = main.Game.GetBuildingSpaces().Where(s => s.Building == BuildingKind.Farm).ToArray();
        for (int mode = 0; mode < 2; mode++)
        {
            Vector2I cell = farms[mode].AnchorCell;
            main.GetNode<WorldMap>("WorldMap").EmitSignal(WorldMap.SignalName.SelectionChanged, cell);
            Click(main, mode == 0 ? "ChangeCropButton" : "PrepareCropButton");
            if (!Find<CropSelectionWindow>(main, "CropWindow").Visible)
            { main.QueueFree(); return Fail("移除夹具未打开手动选种窗口"); }
            CropSelectionWindow cropWindow = Find<CropSelectionWindow>(main, "CropWindow");
            if (Find<Label>(cropWindow, "ManualCultivationNotice").GetLineCount() != 2 ||
                cropWindow.GetCombinedMinimumSize().Y > parent.GetViewport().GetVisibleRect().Size.Y - 70 - 109)
            { main.QueueFree(); return Fail("手动告知初次布局撑高选种窗口"); }
            Click(Find<DraggableWindow>(main, "DetailWindow"), "RemoveButton");
            if (Find<CropSelectionWindow>(main, "CropWindow").Visible ||
                main.Game.GetPlot(cell).Building != BuildingKind.None)
            { main.QueueFree(); return Fail("移除选中田未关闭对应手动选种窗口"); }
            Click(main, "PauseButton");
            Click(main, "PauseButton");
        }
        main.QueueFree();
        return true;
    }

    private static bool CheckWinterSpringDate(Node parent)
    {
        Main main = OpenMain(parent);
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "春初衔接去年冬季";
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        Drop(timeline, CropKind.Wheat, 326);
        Click(window, "SaveCultivationPlanButton");
        Vector2I cell = new(189, 189);
        Find<CheckBox>(window, "CultivationFarm189_189").ButtonPressed = true;
        Click(window, "ApplyCultivationPlanButton");
        main.GetNode<WorldMap>("WorldMap").EmitSignal(WorldMap.SignalName.SelectionChanged, cell);
        string status = Find<Label>(main, "FarmCultivationStatus").Text;
        if (main.Game.GetFarmCultivation(cell).PreparedTimeUnits is not < 0 ||
            !status.Contains("上一年度 12月19日") || status.Contains("第0年"))
        { main.QueueFree(); return Fail("初年春初冬春条没有展示上一年度的真实月日"); }
        Click(main, "FarmCultivationButton");
        int id = main.Game.GetCultivationPlans()[0].Entries[0].Id;
        Drop(timeline, CropKind.Wheat, 8, id);
        Click(window, "SaveCultivationPlanButton");
        main.GetNode<WorldMap>("WorldMap").EmitSignal(WorldMap.SignalName.SelectionChanged, cell);
        status = Find<Label>(main, "FarmCultivationStatus").Text;
        bool passed = status.Contains("第1年 1月9日") && !status.Contains("上一年度");
        if (!passed) Fail("非负计划起点没有展示真实游戏年份");
        main.QueueFree();
        return passed;
    }

    public static async Task<bool> RunLayoutChecks(Node parent)
    {
        Main main = OpenMain(parent);
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "共享冬春轮作";
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        await Frames(parent);
        uint beforeDrag = main.Game.Calendar.ElapsedSeconds;
        bool holdingPreview = await Drag(parent, Find<Control>(window, "CultivationCropWheat").GetGlobalRect().GetCenter(),
            TimelinePoint(timeline, 326), capturePreview: true);
        if (!holdingPreview || main.Game.Calendar.ElapsedSeconds != beforeDrag)
        { main.QueueFree(); return Fail("预览截帧时未保持原生拖动，或动效推进了经营日期"); }
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans().Count != 1 || main.Game.GetCultivationPlans()[0].Entries.Count != 1 ||
            main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 326)
        { main.QueueFree(); return Fail("原生作物拖入未建立冬春整条"); }
        int entryId = main.Game.GetCultivationPlans()[0].Entries[0].Id;
        await Drag(parent, TimelinePoint(timeline, 2), TimelinePoint(timeline, 82));
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans()[0].Entries[0].Id != entryId ||
            main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 82)
        { main.QueueFree(); return Fail("冬春条的春季片段没有拖动整条"); }
        await Drag(parent, TimelinePoint(timeline, 90), TimelinePoint(timeline, 326));
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 326)
        { main.QueueFree(); return Fail("春夏条的夏季片段没有拖动整条"); }
        Drop(timeline, CropKind.Corn, 82);
        Drop(timeline, CropKind.Sugarcane, 140);
        Click(window, "SaveCultivationPlanButton");
        foreach (var space in main.Game.GetBuildingSpaces().Where(s => s.Building == BuildingKind.Farm))
            Find<CheckBox>(window, $"CultivationFarm{space.AnchorCell.X}_{space.AnchorCell.Y}").ButtonPressed = true;
        Click(window, "ApplyCultivationPlanButton");
        await Drag(parent, Find<Control>(window, "CultivationCropRadish").GetGlobalRect().GetCenter(),
            timeline.GlobalPosition + new Vector2(10, 35));
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans()[0].Entries.Count != 3)
        { main.QueueFree(); return Fail("非法落点添加了作物条"); }
        await Frames(parent);
        Rect2 rect = window.GetGlobalRect();
        Rect2 viewport = parent.GetViewport().GetVisibleRect();
        bool passed = rect.Position.Y >= 70 && rect.End.Y <= viewport.End.Y - 109 &&
            rect.Position.X >= 0 && rect.End.X <= viewport.End.X;
        if (!passed) Fail("年度表窗口超出1280×720可用区域");
        foreach (string node in new[] { "CultivationTimeline", "SaveCultivationPlanButton", "ApplyCultivationPlanButton" })
            if (!rect.Encloses(Find<Control>(window, node).GetGlobalRect())) passed = Fail("控件溢出：" + node);
        LineEdit name = Find<LineEdit>(window, "CultivationPlanName");
        name.Text = "焦点草稿";
        name.GrabFocus();
        name.CaretColumn = 2;
        window.Refresh(main.Game);
        await Frames(parent);
        if (name.Text != "焦点草稿" || !name.HasFocus() || name.CaretColumn != 2)
            passed = Fail("刷新覆盖草稿或焦点");
        name.ReleaseFocus();
        ItemList plans = Find<ItemList>(window, "CultivationPlanList");
        plans.Select(0);
        plans.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
        if (name.Text != "共享冬春轮作") passed = Fail("选择共享表未还原保存配置");
        for (int index = 0; index < 5; index++)
            if (!main.Game.TryPlace(new Vector2I(index * 3, 0), BuildingKind.Farm, CropKind.Wheat).Success)
                passed = Fail("农田列表滚动夹具建造失败");
        window.Refresh(main.Game);
        await Frames(parent);
        ScrollContainer farmScroll = Find<ScrollContainer>(window, "CultivationFarmScroll");
        CheckBox lastFarm = Find<CheckBox>(window, "CultivationFarm12_0");
        farmScroll.EnsureControlVisible(lastFarm);
        await Frames(parent);
        if (farmScroll.ScrollVertical <= 0 || !farmScroll.GetGlobalRect().Encloses(lastFarm.GetGlobalRect()))
            passed = Fail("农田列表不能滚动到最后一田");
        var firstFarm = main.Game.GetBuildingSpaces().First(s => s.AnchorCell == new Vector2I(189, 189));
        if (!Find<CheckBox>(window, $"CultivationFarm{firstFarm.AnchorCell.X}_{firstFarm.AnchorCell.Y}").ButtonPressed)
            passed = Fail("农田列表结构刷新丢失已有勾选");
        main.Game.RemoveBuilding(new Vector2I(12, 0));
        window.Refresh(main.Game);
        if (window.FindChild("CultivationFarm12_0", true, false) != null)
            passed = Fail("已移除农田仍出现在批量列表");
        farmScroll.ScrollVertical = 0;
        await Frames(parent);
        if (DisplayServer.GetName() != "headless")
        {
            string directory = ProjectSettings.GlobalizePath("res://build/issue42-validation");
            DirAccess.MakeDirRecursiveAbsolute(directory);
            parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/cultivation-window.png");
        }
        main.QueueFree();
        await Frames(parent);
        return await CheckPausedSaveMap(parent) && passed;
    }

    private static async Task<bool> CheckPausedSaveMap(Node parent)
    {
        Main main = OpenMain(parent);
        FarmGame game = main.Game;
        WorldMap map = main.GetNode<WorldMap>("WorldMap");
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        Vector2I cell = new(186, 189);
        if (!game.TryPlace(cell, BuildingKind.Farm, CropKind.Wheat).Success)
        { main.QueueFree(); return Fail("暂停保存地图夹具建造失败"); }
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "当前轮地图同步";
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        Drop(timeline, CropKind.Wheat, 0);
        Click(window, "SaveCultivationPlanButton");
        Find<CheckBox>(window, "CultivationFarm186_189").ButtonPressed = true;
        Click(window, "ApplyCultivationPlanButton");
        main._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        await Frames(parent);
        bool graphical = DisplayServer.GetName() != "headless";
        Color before = graphical ? MarkerPixel(parent, map, cell + Vector2I.One) : default;
        int redraws = map.ChunkRedrawCount;
        uint elapsed = game.Calendar.ElapsedSeconds;
        Click(main, "CultivationButton");
        int id = game.GetCultivationPlans()[0].Entries[0].Id;
        Drop(timeline, CropKind.Radish, 0, id);
        Click(window, "SaveCultivationPlanButton");
        bool passed = game.GetPlot(cell).CropKind == CropKind.Radish && game.GetPlot(cell).Crop == CropStage.None &&
            game.Calendar.ElapsedSeconds == elapsed;
        main._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        await Frames(parent);
        if (map.ChunkRedrawCount <= redraws) passed = false;
        if (!passed) Fail("暂停保存共享表没有同步当期空田地图外观");
        if (graphical && MarkerPixel(parent, map, cell + Vector2I.One).IsEqualApprox(before))
            passed = Fail("暂停保存改种后地图工作中心仍显示旧作物颜色");
        main.QueueFree();
        return passed;
    }

    private static Color MarkerPixel(Node parent, WorldMap map, Vector2I cell)
    {
        Vector2 pixel = map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);
        return parent.GetViewport().GetTexture().GetImage().GetPixel((int)pixel.X, (int)pixel.Y);
    }

    private static void Drop(CultivationTimeline timeline, CropKind crop, int day, int id = 0)
    {
        Vector2 point = new(42 + (timeline.Size.X - 50) * ((day % 84) + 0.1f) / 84,
            24 + (day / 84) * 50);
        var data = CultivationTimeline.DragData(crop, id);
        if (timeline._CanDropData(point, data)) timeline._DropData(point, data);
    }

    private static void SetMode(Node parent, int index)
    {
        OptionButton mode = Find<OptionButton>(parent, "CultivationPlanMode");
        mode.Select(index);
        mode.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }

    private static Vector2 TimelinePoint(CultivationTimeline timeline, int day) =>
        timeline.GlobalPosition + new Vector2(42 + (timeline.Size.X - 50) *
            ((day % 84) + 0.5f) / 84, 40 + (day / 84) * 50);

    private static async Task<bool> Drag(Node parent, Vector2 from, Vector2 to, bool capturePreview = false)
    {
        Viewport viewport = parent.GetViewport();
        bool graphical = DisplayServer.GetName() != "headless";
        Vector2I originalScreenMousePosition = graphical ? DisplayServer.MouseGetPosition() : default;
        void Send(InputEventMouse inputEvent)
        {
            var transformed = (InputEventMouse)inputEvent.XformedBy(viewport.GetFinalTransform());
            // XformedBy 保留 GlobalPosition，需与位置一同转换到窗口坐标。
            transformed.GlobalPosition = viewport.GetFinalTransform() * inputEvent.GlobalPosition;
            if (graphical && inputEvent is InputEventMouseMotion)
                Input.WarpMouse(transformed.Position);
            Input.ParseInputEvent(transformed);
        }
        try
        {
            Send(new InputEventMouseMotion { Position = from, GlobalPosition = from });
            Send(new InputEventMouseButton
            {
                Position = from,
                GlobalPosition = from,
                ButtonIndex = MouseButton.Left,
                ButtonMask = MouseButtonMask.Left,
                Pressed = true,
            });
            Send(new InputEventMouseMotion
            {
                Position = from + new Vector2(18, 0),
                GlobalPosition = from + new Vector2(18, 0),
                Relative = new Vector2(18, 0),
                ButtonMask = MouseButtonMask.Left,
            });
            await Frames(parent);
            Send(new InputEventMouseMotion
            {
                Position = to,
                GlobalPosition = to,
                Relative = to - from - new Vector2(18, 0),
                ButtonMask = MouseButtonMask.Left,
            });
            await Frames(parent);
            bool holdingPreview = !capturePreview || viewport.GuiIsDragging();
            if (capturePreview && DisplayServer.GetName() != "headless")
            {
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                holdingPreview = holdingPreview && viewport.GuiIsDragging();
                if (holdingPreview)
                {
                    string directory = ProjectSettings.GlobalizePath("res://build/issue42-validation");
                    DirAccess.MakeDirRecursiveAbsolute(directory);
                    viewport.GetTexture().GetImage().SavePng(directory + "/cultivation-drag-preview.png");
                }
            }
            Send(new InputEventMouseButton
            {
                Position = to,
                GlobalPosition = to,
                ButtonIndex = MouseButton.Left,
                Pressed = false,
            });
            await Frames(parent);
            return holdingPreview;
        }
        finally
        {
            if (graphical)
                Input.WarpMouse(originalScreenMousePosition - parent.GetWindow().Position);
        }
    }

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Click(Node parent, string name) => Find<Button>(parent, name).EmitSignal(Button.SignalName.Pressed);
    private static T Find<T>(Node parent, string name) where T : Node => (T)parent.FindChild(name, true, false);
    private static bool Fail(string message) { GD.PushError("耕作表窗口：" + message); return false; }
}
