using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;
using FarmExchange.Time;

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
        if (!CheckPlacementBeforeMutation(parent)) return false;
        Main main = OpenMain(parent);
        bool passed = CheckEditing(main) && CheckManual(main);
        main.QueueFree();
        return passed && CheckRemovalWithCropWindow(parent) && CheckWinterSpringDate(parent);
    }

    private static bool CheckPlacementBeforeMutation(Node parent)
    {
        Main main = OpenMain(parent);
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "落位前完整排程";
        Drop(timeline, CropKind.Wheat, 0);
        Click(window, "SaveCultivationPlanButton");
        int id = main.Game.GetCultivationPlans()[0].Entries[0].Id;
        bool passed = true;
        bool Reject(CropKind crop, int day, string reason, int movingId = 0)
        {
            Vector2 point = DropPoint(timeline, crop, day);
            var data = CultivationTimeline.DragData(crop, movingId);
            bool accepted = timeline._CanDropData(point, data);
            if (accepted) return Fail("放置前没有拒绝：" + reason);
            // 即使直接收到过期的落位回调，也不得先修改草稿。
            timeline._DropData(point, data);
            Click(window, "SaveCultivationPlanButton");
            CultivationPlanSnapshot plan = main.Game.GetCultivationPlans()[0];
            return plan.Entries.Count == 1 && plan.Entries[0].Id == id && plan.Entries[0].StartDay == 0 &&
                !string.IsNullOrEmpty(Find<Label>(window, "CultivationFeedback").Text) ||
                Fail("拒绝落位改变了原草稿或正式表：" + reason);
        }
        passed &= Reject(CropKind.Wheat, 0, "同格重叠");
        passed &= Reject(CropKind.Wheat, 8, "部分覆盖");
        passed &= Reject(CropKind.Radish, 16, "异种零日间隔");
        passed &= Reject(CropKind.Corn, 252, "禁生季起点");
        Drop(timeline, CropKind.Wheat, 1, id);
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 1)
            passed = Fail("替换自身被错误判为重叠");
        main.QueueFree();
        return passed;
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
        // 冲突从未落入草稿，保存仍保留原条。
        Click(window, "SaveCultivationPlanButton");
        if (game.GetCultivationPlans()[0].Entries.Count != 1) return Fail("拒绝冲突改变草稿");
        Click(window, "RemoveCultivationEntryButton");
        Click(window, "SaveCultivationPlanButton");
        if (game.GetCultivationPlans()[0].Entries.Count != 0) return Fail("移除选中的原条未生效");
        Drop(timeline, CropKind.Wheat, 82);
        Click(window, "SaveCultivationPlanButton");
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
        bool freshPassed = await CheckFreshUnnamedEditing(parent);
        bool identityPassed = CheckEntryIdentityAfterDeletion(parent);
        Main main = OpenMain(parent);
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "共享冬春轮作";
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        await Frames(parent);
        uint beforeDrag = main.Game.Calendar.ElapsedSeconds;
        bool holdingPreview = await Drag(parent, Find<Control>(window, "CultivationCropWheat").GetGlobalRect().GetCenter(),
            timeline.GlobalPosition + DropPoint(timeline, CropKind.Wheat, 326), capturePreview: true);
        if (!holdingPreview || main.Game.Calendar.ElapsedSeconds != beforeDrag)
        { main.QueueFree(); return Fail("预览截帧时未保持原生拖动，或动效推进了经营日期"); }
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans().Count != 1 || main.Game.GetCultivationPlans()[0].Entries.Count != 1 ||
            main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 326)
        { main.QueueFree(); return Fail("原生作物拖入未建立冬春整条"); }
        int entryId = main.Game.GetCultivationPlans()[0].Entries[0].Id;
        await Drag(parent, TimelinePoint(timeline, 2), timeline.GlobalPosition + DropPoint(timeline, CropKind.Wheat, 82));
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans()[0].Entries[0].Id != entryId ||
            main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 82)
        { main.QueueFree(); return Fail("冬春条的春季片段没有拖动整条"); }
        await Drag(parent, TimelinePoint(timeline, 90), timeline.GlobalPosition + DropPoint(timeline, CropKind.Wheat, 326));
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
            string directory = ProjectSettings.GlobalizePath("res://build/issue86-ui-validation");
            DirAccess.MakeDirRecursiveAbsolute(directory);
            parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/cultivation-window.png");
        }
        main.QueueFree();
        await Frames(parent);
        return await CheckPausedSaveMap(parent) && await CheckNativePlacement(parent) && passed && freshPassed && identityPassed;
    }

    private static bool CheckEntryIdentityAfterDeletion(Node parent)
    {
        Main main = OpenMain(parent);
        FarmGame game = main.Game;
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        Vector2I cell = game.GetBuildingSpaces().First(s => s.Building == BuildingKind.Farm).AnchorCell;
        game.SetRawReserve(CropKind.Radish, 1000);
        game.SetFarmCrop(cell, CropKind.Radish);
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "删除后独立新轮";
        SetMode(window, 1);
        Drop(timeline, CropKind.Radish, 0);
        Click(window, "SaveCultivationPlanButton");
        int oldId = game.GetCultivationPlans()[0].Entries[0].Id;
        Find<CheckBox>(window, $"CultivationFarm{cell.X}_{cell.Y}").ButtonPressed = true;
        Click(window, "ApplyCultivationPlanButton");
        game.SetPaused(false);
        while (game.Calendar.ElapsedDays < 6) game.AdvanceTick(isRaining: true);
        game.SetPaused(true);
        bool passed = game.GetRawStock(CropKind.Radish) >= 6 || Fail("编号回归夹具未完成首轮萝卜");
        Click(window, "RemoveCultivationEntryButton");
        Click(window, "SaveCultivationPlanButton");
        window.Refresh(game);
        ItemList plans = Find<ItemList>(window, "CultivationPlanList");
        plans.Select(0);
        plans.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
        Drop(timeline, CropKind.Radish, 10);
        Click(window, "SaveCultivationPlanButton");
        CultivationEntry replacement = game.GetCultivationPlans()[0].Entries[0];
        if (replacement.Id <= oldId) passed = Fail("保存空表并重选后新增复用了旧条编号");
        if (game.GetFarmCultivation(cell).PreparedTimeUnits != 10L * GameTimeUnits.PerDay)
            passed = Fail("已执行旧条删除后日10新条错误地跳到明年");
        main.QueueFree();
        return passed;
    }

    private static async Task<bool> CheckFreshUnnamedEditing(Node parent)
    {
        Main main = OpenMain(parent);
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        LineEdit name = Find<LineEdit>(window, "CultivationPlanName");
        await Frames(parent);
        bool passed = true;
        if (name.Text.Length != 0) passed = Fail("首次无名草稿被替换为默认名称");
        int dropped = 0;
        timeline.EntryDropped += (_, _, _) => dropped++;
        await Drag(parent, Find<Control>(window, "CultivationCropPotato").GetGlobalRect().GetCenter(),
            timeline.GlobalPosition + DropPoint(timeline, CropKind.Potato, 10));
        if (dropped != 1) passed = Fail("首次未输入名称的合法原生马铃薯拖放被拒绝");
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans().Count != 0 ||
            !Find<Label>(window, "CultivationFeedback").Text.Contains("名称"))
            passed = Fail("无名草稿保存未明确拒绝");
        CheckBox farm = Descendants(window).OfType<CheckBox>().First();
        farm.ButtonPressed = true;
        farm.GrabFocus();
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color",
            "font_hover_pressed_color", "font_focus_color" })
        {
            Color color = farm.GetThemeColor(state);
            double contrast = Contrast(color, UiElements.Cream);
            if (contrast < 4.5) passed = Fail($"农田勾选文字状态 {state} 对比不足：{contrast:F2}");
        }
        if (DisplayServer.GetName() != "headless")
        {
            passed = await CheckCheckedFarmAppearance(parent, farm) && passed;
        }
        name.Text = "先排程后命名";
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans().Count != 1 || main.Game.GetCultivationPlans()[0].Entries.Count != 1)
            passed = Fail("命名后首次原生草稿未保存");
        else
        {
            name.Text = "";
            int entryId = main.Game.GetCultivationPlans()[0].Entries[0].Id;
            Drop(timeline, CropKind.Potato, 20, entryId);
            Drop(timeline, CropKind.Radish, 40);
            if (dropped != 3) passed = Fail("清空已保存表名称后不能移动或新增条");
            Click(window, "SaveCultivationPlanButton");
            if (main.Game.GetCultivationPlans()[0].Entries.Count != 1 ||
                main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 10)
                passed = Fail("清空名称后保存改变了正式表");
            if (timeline._CanDropData(DropPoint(timeline, CropKind.Radish, 20), CultivationTimeline.DragData(CropKind.Radish, 0)) ||
                timeline._CanDropData(DropPoint(timeline, CropKind.Radish, 34), CultivationTimeline.DragData(CropKind.Radish, 0)) ||
                timeline._CanDropData(DropPoint(timeline, CropKind.Corn, 252), CultivationTimeline.DragData(CropKind.Corn, 0)))
                passed = Fail("无名草稿跳过了真实排程约束");
            name.Text = "重新命名";
            Click(window, "SaveCultivationPlanButton");
            if (main.Game.GetCultivationPlans()[0].Entries.Count != 2 ||
                main.Game.GetCultivationPlans()[0].Entries.First(e => e.Id == entryId).StartDay != 20)
                passed = Fail("重新命名后未保留新增和移动的草稿");
        }
        main.QueueFree();
        await Frames(parent);
        return passed;
    }

    private static async Task<bool> CheckCheckedFarmAppearance(Node parent, CheckBox farm)
    {
        Window window = parent.GetWindow();
        Vector2I originalSize = window.Size;
        Vector2I originalMouse = DisplayServer.MouseGetPosition();
        string directory = ProjectSettings.GlobalizePath("res://build/issue86-followup-validation");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        bool passed = true;
        try
        {
            foreach ((Vector2I size, string file) in new[] {
                (new Vector2I(1280, 720), "fresh-unnamed-checked.png"),
                (new Vector2I(1600, 900), "enlarged-checked.png") })
            {
                window.Size = size;
                await Frames(parent);
                Vector2 position = parent.GetViewport().GetFinalTransform() * farm.GetGlobalRect().GetCenter();
                Input.WarpMouse(position);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = position, GlobalPosition = position });
                await Frames(parent);
                if (farm.GetDrawMode() != BaseButton.DrawMode.HoverPressed || !farm.HasFocus())
                    passed = Fail("真实悬停勾选田未进入已选/悬停/焦点状态");
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + file);
            }
        }
        finally
        {
            window.Size = originalSize;
            Input.WarpMouse(originalMouse - window.Position);
        }
        await Frames(parent);
        return passed;
    }

    private static double Contrast(Color foreground, Color background)
    {
        static double Linear(float channel) => channel <= 0.04045f ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        double front = Luminance(foreground);
        double back = Luminance(background);
        return (Math.Max(front, back) + 0.05) / (Math.Min(front, back) + 0.05);
    }

    private static async Task<bool> CheckNativePlacement(Node parent)
    {
        Main main = OpenMain(parent);
        CultivationWindow window = Find<CultivationWindow>(main, "CultivationWindow");
        CultivationTimeline timeline = Find<CultivationTimeline>(window, "CultivationTimeline");
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "原生放置拒绝";
        await Frames(parent);
        Drop(timeline, CropKind.Wheat, 0);
        Click(window, "SaveCultivationPlanButton");
        int dropped = 0;
        timeline.EntryDropped += (_, _, _) => dropped++;
        bool passed = true;
        foreach ((CropKind crop, int day) in new[] { (CropKind.Wheat, 0), (CropKind.Wheat, 8),
            (CropKind.Radish, 16), (CropKind.Corn, 252) })
        {
            await Drag(parent, Find<Control>(window, $"CultivationCrop{crop}").GetGlobalRect().GetCenter(),
                timeline.GlobalPosition + DropPoint(timeline, crop, day));
            if (dropped != 0 || string.IsNullOrEmpty(Find<Label>(window, "CultivationFeedback").Text))
                passed = Fail("原生非法拖放没有在落位前拒绝：" + crop + "/" + day);
            Click(window, "SaveCultivationPlanButton");
            if (main.Game.GetCultivationPlans()[0].Entries.Count != 1 ||
                main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 0)
                passed = Fail("原生拒绝改变草稿或正式表");
        }
        int id = main.Game.GetCultivationPlans()[0].Entries[0].Id;
        await Drag(parent, TimelinePoint(timeline, 4), timeline.GlobalPosition + DropPoint(timeline, CropKind.Wheat, 326));
        Click(window, "SaveCultivationPlanButton");
        if (dropped != 1 || main.Game.GetCultivationPlans()[0].Entries[0].Id != id ||
            main.Game.GetCultivationPlans()[0].Entries[0].StartDay != 326)
            passed = Fail("原生移动整条被误判与自身冲突");
        // 年度首尾：原条覆盖春初至日6，异种日6无缓冲、日7有一天缓冲。
        if (timeline._CanDropData(DropPoint(timeline, CropKind.Radish, 5), CultivationTimeline.DragData(CropKind.Radish, 0)) ||
            timeline._CanDropData(DropPoint(timeline, CropKind.Radish, 6), CultivationTimeline.DragData(CropKind.Radish, 0)) ||
            !timeline._CanDropData(DropPoint(timeline, CropKind.Radish, 7), CultivationTimeline.DragData(CropKind.Radish, 0)) ||
            !timeline._CanDropData(DropPoint(timeline, CropKind.Wheat, 6), CultivationTimeline.DragData(CropKind.Wheat, 0)))
            passed = Fail("冬春首尾的重叠或间隔预检不一致");
        Drop(timeline, CropKind.Radish, 20);
        Drop(timeline, CropKind.Wheat, 40);
        await Frames(parent);
        if (timeline._GetTooltip(TimelinePoint(timeline, 45) - timeline.GlobalPosition) != "")
            passed = Fail("能放下完整信息的条仍使用悬浮提示");
        if (DisplayServer.GetName() != "headless")
        {
            passed = await CheckNativeTooltip(parent, timeline, 21, "完整 4 天", "1月21日", "1月25日", "short-radish-tooltip.png") && passed;
            passed = await CheckNativeTooltip(parent, timeline, 2, "完整 16 天", "12月19日", "次年 1月7日", "winter-spring-tooltip.png") && passed;
        }
        Click(window, "NewCultivationPlanButton");
        Find<LineEdit>(window, "CultivationPlanName").Text = "合法跨禁生风险";
        await Drag(parent, Find<Control>(window, "CultivationCropCorn").GetGlobalRect().GetCenter(),
            timeline.GlobalPosition + DropPoint(timeline, CropKind.Corn, 166));
        Drop(timeline, CropKind.Sunflower, 120);
        if (!Find<Label>(window, "CultivationFeedback").Text.Contains("风险"))
            passed = Fail("原生适季起点跨禁生条没有保留风险提示");
        Click(window, "SaveCultivationPlanButton");
        if (main.Game.GetCultivationPlans().Count != 2 ||
            !main.Game.GetCultivationPlans()[1].Entries.Any(e => e.Crop == CropKind.Corn && e.StartDay == 166) ||
            !main.Game.GetCultivationPlans()[1].Entries.Any(e => e.Crop == CropKind.Sunflower && e.StartDay == 120))
            passed = Fail("跨禁生风险或奇数周期中心落位日期未正确保存");
        await Frames(parent);
        if (main.Game.Calendar.ElapsedSeconds != 0) passed = Fail("原生拖放或悬浮提示推进了暂停的经营");
        if (DisplayServer.GetName() != "headless")
        {
            await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            parent.GetViewport().GetTexture().GetImage().SavePng(
                ProjectSettings.GlobalizePath("res://build/issue86-ui-validation/risk-outline.png"));
        }
        main.QueueFree();
        await Frames(parent);
        return passed;
    }

    private static async Task<bool> CheckNativeTooltip(Node parent, CultivationTimeline timeline, int day,
        string cycle, string start, string end, string screenshot)
    {
        Vector2I original = DisplayServer.MouseGetPosition();
        Viewport viewport = parent.GetViewport();
        bool passed = true;
        string expected = timeline._GetTooltip(TimelinePoint(timeline, day) - timeline.GlobalPosition);
        if (!expected.Contains(cycle) || !expected.Contains(start) || !expected.Contains(end))
            return Fail("窄片段悬浮内容未包含完整原条日期与周期");
        void Move(Vector2 point)
        {
            Vector2 actual = viewport.GetFinalTransform() * point;
            Input.WarpMouse(actual);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = actual, GlobalPosition = actual });
        }
        Label? VisibleTooltip() => Descendants(parent.GetTree().Root).OfType<Label>()
            .FirstOrDefault(label => label.Text == expected && label.IsVisibleInTree());
        try
        {
            Move(TimelinePoint(timeline, day));
            double delay = ProjectSettings.GetSetting("gui/timers/tooltip_delay_sec", 0.5).AsDouble();
            await parent.ToSignal(parent.GetTree().CreateTimer(delay + 0.15), SceneTreeTimer.SignalName.Timeout);
            await Frames(parent);
            Label? tooltip = VisibleTooltip();
            if (tooltip == null) passed = Fail("真实鼠标悬停后没有出现完整提示");
            else
            {
                Rect2 tooltipRect = tooltip.GetGlobalRect();
                Window? tooltipWindow = tooltip.GetWindow();
                Rect2 rect = tooltipWindow == parent.GetWindow() ? tooltipRect :
                    new Rect2(tooltipWindow.Position, tooltipWindow.Size);
                if (!new Rect2(Vector2.Zero, parent.GetWindow().Size).Encloses(rect))
                    passed = Fail("原生悬浮信息超出游戏窗口");
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string directory = ProjectSettings.GlobalizePath("res://build/issue86-ui-validation");
                DirAccess.MakeDirRecursiveAbsolute(directory);
                viewport.GetTexture().GetImage().SavePng(directory + "/" + screenshot);
            }
            Move(timeline.GlobalPosition + new Vector2(10, 35));
            await Frames(parent);
            if (VisibleTooltip() != null) passed = Fail("鼠标移开后悬浮提示没有消失");
        }
        finally { Input.WarpMouse(original - parent.GetWindow().Position); }
        return passed;
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
        Vector2 point = DropPoint(timeline, crop, day);
        var data = CultivationTimeline.DragData(crop, id);
        if (timeline._CanDropData(point, data)) timeline._DropData(point, data);
    }

    private static Vector2 DropPoint(CultivationTimeline timeline, CropKind crop, int day)
    {
        float centerDay = (day + FarmGame.GetCrop(crop).GrowthDays / 2f) % 336;
        return new Vector2(42 + timeline.PixelsPerDay * (centerDay % 84 + 0.1f),
            40 + (int)(centerDay / 84) * 50);
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
            if (capturePreview)
            {
                Label? previewLabel = Descendants(parent.GetTree().Root).OfType<Label>()
                    .FirstOrDefault(node => node.Text == "小麦 · 16天" && node.GetParent() is PanelContainer);
                if (previewLabel?.GetParent() is not PanelContainer preview ||
                    (preview.GetGlobalRect().GetCenter() - viewport.GetMousePosition()).Length() > 1.5f)
                    holdingPreview = Fail("原生拖动预览没有以完整条中心跟随鼠标");
            }
            if (capturePreview && DisplayServer.GetName() != "headless")
            {
                await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                holdingPreview = holdingPreview && viewport.GuiIsDragging();
                if (holdingPreview)
                {
                    string directory = ProjectSettings.GlobalizePath("res://build/issue86-ui-validation");
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

    private static System.Collections.Generic.IEnumerable<Node> Descendants(Node parent)
    {
        foreach (Node child in parent.GetChildren(includeInternal: true))
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Click(Node parent, string name) => Find<Button>(parent, name).EmitSignal(Button.SignalName.Pressed);
    private static T Find<T>(Node parent, string name) where T : Node => (T)parent.FindChild(name, true, false);
    private static bool Fail(string message) { GD.PushError("耕作表窗口：" + message); return false; }
}
