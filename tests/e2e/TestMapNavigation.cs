using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.UI;
using FarmExchange.World;
using static TestMapInput;

public partial class TestMapNavigation : Node
{
    private const string Evidence = "build/issue153-validation/navigation";

    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        Vector2I originalSize = parent.GetWindow().Size;
        Main? main = null;
        try
        {
            parent.GetWindow().Size = new Vector2I(1920, 1080);
            await Frames(parent);
            main = await CreateMain(parent, true);
            await CheckCultivationDragCancellation(parent, main);
            await CheckButtonPressCancellation(parent, main);
            await CheckInput(parent, main);
            await CheckGestures(parent, main);
            await CheckMapChanges(parent, main);
            await CheckViewsAndLayout(parent, main);
            CheckDrawing(main);
            main.Free();
            main = await CreateMain(parent, false);
            MapNavigation maps = Find<MapNavigation>(main, "MapNavigation");
            Key(main, Godot.Key.M);
            await Frames(parent);
            var atlas = Find<MapNavigationCanvas>(maps, "AtlasMap");
            Require(maps.IsExpanded && atlas.ViewZoom == 1 && atlas.ViewCenter.IsEqualApprox(MapNavigationDrawing.Center),
                "真实新局没有重置地图倍率和查看位置");
            CheckDrawing(main);
            await Capture(parent, Evidence, "water-atlas.png");
            Key(main, Godot.Key.M);
            await Capture(parent, Evidence, "water-minimap.png");
            GD.Print("大小地图：真实输入、手势生命周期、暂停与工具取消、视野四角、独立倍率与新局重置、真实图层通过");
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("大小地图：" + error);
            return false;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(main)) main!.Free();
            parent.GetWindow().Size = originalSize;
            await Frames(parent);
        }
    }

    private static async Task<Main> CreateMain(Node parent, bool allLand)
    {
        Main main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        if (allLand) main.InitialTerrainRules = TerrainTestFixtures.AllLandRules;
        parent.AddChild(main);
        await main.ReadyTask;
        Find<Button>(main, "PauseButton").EmitSignal(Button.SignalName.Pressed);
        if (DisplayServer.GetName() != "headless") main.GetWindow().GrabFocus();
        await Frames(parent);
        Require(main.Game.IsPaused, "夹具未通过真实按钮暂停");
        return main;
    }

    private static async Task CheckCultivationDragCancellation(Node parent, Main main)
    {
        if (DisplayServer.GetName() != "headless")
            Require(main.GetWindow().HasFocus(), "年度表原生拖放夹具没有真实窗口焦点");
        Click(main, Find<Button>(main, "CultivationButton").GetGlobalRect().GetCenter());
        await Frames(parent);
        var maps = Find<MapNavigation>(main, "MapNavigation");
        var timeline = Find<CultivationTimeline>(main, "CultivationTimeline");
        var info = Find<Label>(main, "CultivationEntryInfo");
        var card = Find<Control>(main, "CultivationCropWheat");
        int drops = 0;
        void OnDrop(int id, CropKind crop, int day) => drops++;
        timeline.EntryDropped += OnDrop;
        try
        {
            foreach (string scenario in new[] { "正常对照", "条目待启动", "卡片待启动", "卡片已拖放", "重新拖放" })
            {
                bool entry = scenario == "条目待启动";
                bool active = scenario == "卡片已拖放";
                bool interrupt = entry || active || scenario == "卡片待启动";
                if (!entry)
                    Click(main, Find<Button>(main, "NewCultivationPlanButton").GetGlobalRect().GetCenter());
                await Frames(parent);
                Vector2 start = entry ? timeline.GlobalPosition + new Vector2(165, 38) : card.GetGlobalRect().GetCenter();
                Vector2 moved = start + new Vector2(50, 35);
                Vector2 destination = timeline.GlobalPosition + new Vector2(entry ? 340 : 165, 38);
                string before = info.Text;
                int previousDrops = drops;
                Move(main, start);
                await Frames(parent);
                Send(main, new InputEventMouseButton
                {
                    Position = start,
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    ButtonMask = MouseButtonMask.Left
                });
                await Frames(parent);
                Require(!main.GetViewport().GuiIsDragging() && Input.IsMouseButtonPressed(MouseButton.Left),
                    "年度表待启动拖放夹具未保持左键或提前越过阈值");
                if (interrupt && !active)
                {
                    Key(main, Godot.Key.M);
                    await Frames(parent);
                    Require(maps.IsExpanded && !main.GetViewport().GuiIsDragging(), "待启动拖放期间M未正常打开地图");
                }
                Move(main, moved, MouseButtonMask.Left, moved - start);
                await Frames(parent);
                bool draggingAfterMove = main.GetViewport().GuiIsDragging();
                if (active)
                {
                    Require(draggingAfterMove, "年度表已启动拖放夹具未越过原生阈值");
                    Key(main, Godot.Key.M);
                    await Frames(parent);
                    Require(maps.IsExpanded && !main.GetViewport().GuiIsDragging(), "开图没有取消已启动的年度表原生拖放");
                }
                if (interrupt)
                {
                    Key(main, Godot.Key.M);
                    await Frames(parent);
                    Require(!maps.IsExpanded, "待启动拖放回归无法关闭地图");
                    Require(main.GetNode<CameraController>("Camera2D").IsMouseInsideWindow,
                        "地图关闭后丢失原有在窗状态，必须额外移鼠才能恢复输入");
                }
                Move(main, destination, MouseButtonMask.Left, destination - moved);
                await Frames(parent);
                bool draggingBeforeDrop = main.GetViewport().GuiIsDragging();
                MouseButtonEvent(main, destination, MouseButton.Left, false);
                await Frames(parent);
                bool changed = info.Text != before;
                GD.Print($"年度表原生拖放：{scenario}，阈值后拖放={draggingAfterMove}，落位前拖放={draggingBeforeDrop}，" +
                    $"实际落位={drops - previousDrops}，草稿改变={changed}");
                if (interrupt)
                    Require((active || !draggingAfterMove) && !draggingBeforeDrop && drops == previousDrops && !changed,
                        $"开图未取消年度表{scenario}，旧按下在地图上启动或关图后改变草稿");
                else
                    Require(draggingAfterMove && draggingBeforeDrop && drops == previousDrops + 1 && changed,
                        "年度作物卡片正常原生拖放对照未成功，不能判定中断回归");
            }
        }
        finally
        {
            timeline.EntryDropped -= OnDrop;
            MouseButtonEvent(main, main.GetViewport().GetMousePosition(), MouseButton.Left, false);
            if (maps.IsExpanded) Key(main, Godot.Key.M);
            Find<DraggableWindow>(main, "CultivationWindow").Hide();
        }
    }

    private static async Task CheckButtonPressCancellation(Node parent, Main main)
    {
        var button = Find<Button>(main, "InventoryButton");
        var window = Find<DraggableWindow>(main, "InventoryWindow");
        var maps = Find<MapNavigation>(main, "MapNavigation");
        int presses = 0;
        void OnPressed() => presses++;
        button.Pressed += OnPressed;
        try
        {
            Require(!window.Visible, "背景按钮中断夹具的库存窗口已打开");
            Vector2 point = button.GetGlobalRect().GetCenter();
            Move(main, point);
            await Frames(parent);
            Send(main, new InputEventMouseButton
            {
                Position = point,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                ButtonMask = MouseButtonMask.Left
            });
            await Frames(parent);
            Require(presses == 0 && !window.Visible, "背景按钮夹具在按下阶段已触发动作");
            Key(main, Godot.Key.M);
            await Frames(parent);
            Require(maps.IsExpanded && presses == 0 && !window.Visible, "开图清理鼠标捕获时意外触发背景按钮");
            Key(main, Godot.Key.M);
            await Frames(parent);
            MouseButtonEvent(main, point, MouseButton.Left, false);
            await Frames(parent);
            Require(!maps.IsExpanded && presses == 0 && !window.Visible, "关图后的旧释放触发了背景按钮");
            Click(main, point);
            await Frames(parent);
            Require(presses == 1 && window.Visible, "清理旧捕获后背景按钮不能重新正常点击");
            Click(main, Find<Button>(window, "CloseButton").GetGlobalRect().GetCenter());
            await Frames(parent);
            Require(!window.Visible, "背景按钮回归未正常关闭库存窗口");
            var camera = main.GetNode<CameraController>("Camera2D");
            main.GetWindow().EmitSignal(Window.SignalName.MouseExited);
            Key(main, Godot.Key.M);
            await Frames(parent);
            Require(maps.IsExpanded && !camera.IsMouseInsideWindow, "离窗时开图伪造鼠标返回窗口");
            Key(main, Godot.Key.M);
            Require(!maps.IsExpanded && !camera.IsMouseInsideWindow, "离窗时关图伪造鼠标返回窗口");
            main.GetWindow().EmitSignal(Window.SignalName.MouseEntered);
        }
        finally
        {
            button.Pressed -= OnPressed;
            MouseButtonEvent(main, main.GetViewport().GetMousePosition(), MouseButton.Left, false);
            if (maps.IsExpanded) Key(main, Godot.Key.M);
            main.GetWindow().EmitSignal(Window.SignalName.MouseEntered);
        }
    }

    private static async Task CheckInput(Node parent, Main main)
    {
        MapNavigation maps = Find<MapNavigation>(main, "MapNavigation");
        CameraController camera = main.GetNode<CameraController>("Camera2D");
        Require(!maps.IsExpanded, "新局意外展开大地图");
        foreach (Action invalid in new Action[] {
            () => Key(main, Godot.Key.M, echo: true), () => Key(main, Godot.Key.M, control: true),
            () => Key(main, Godot.Key.M, shift: true), () => Key(main, Godot.Key.M, alt: true),
            () => Key(main, Godot.Key.M, meta: true) })
        {
            invalid();
            Require(!maps.IsExpanded, "M重复或修饰键打开大地图");
        }
        Find<Button>(main, "CultivationButton").EmitSignal(Button.SignalName.Pressed);
        var editor = Find<LineEdit>(main, "CultivationPlanName");
        editor.GrabFocus();
        await Frames(parent);
        Key(main, Godot.Key.M);
        Require(!maps.IsExpanded, "文本编辑时M打开地图");
        editor.ReleaseFocus();
        var window = Find<DraggableWindow>(main, "CultivationWindow");
        Vector2 start = Find<Control>(window, "Header").GetGlobalRect().GetCenter();
        Move(main, start);
        MouseButtonEvent(main, start, MouseButton.Left, true);
        Move(main, start + new Vector2(35, 0), MouseButtonMask.Left);
        Vector2 retainedPosition = window.Position;
        Key(main, Godot.Key.M);
        Require(maps.IsExpanded && window.Visible && main.Game.IsPaused, "开图未保留窗口或暂停状态");
        MouseButtonEvent(main, start + new Vector2(35, 0), MouseButton.Left, false);
        Vector2 cameraPosition = camera.Position;
        Vector2 cameraZoom = camera.Zoom;
        Key(main, Godot.Key.W);
        Key(main, Godot.Key.R);
        Require(camera.Position == cameraPosition && camera.Zoom == cameraZoom, "大图键盘穿透主镜头");
        Key(main, Godot.Key.Escape);
        Move(main, start + new Vector2(100, 80));
        await Frames(parent);
        Require(!maps.IsExpanded && window.Visible && window.Position.IsEqualApprox(retainedPosition),
            "Esc关闭背景窗口或开图前的标题拖动仍然存活");
        window.Hide();

#if DEBUG
        Find<Button>(main, "DeveloperToolsButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "ScenarioDeleteFlow_buy-process-sell").EmitSignal(Button.SignalName.Pressed);
        Control confirmation = Find<Control>(main, "ScenarioConfirmationPanel");
        Require(confirmation.IsVisibleInTree(), "正式流程删除意图未打开确认框");
        Key(main, Godot.Key.M);
        Require(!maps.IsExpanded, "确认框打开时允许展开大地图");
        Find<Button>(main, "ScenarioConfirmationCancelButton").EmitSignal(Button.SignalName.Pressed);
        Find<Control>(main, "DeveloperToolsWindow").Hide();
#endif

        foreach (bool road in new[] { false, true })
        {
            Find<Button>(main, "BuildButton").EmitSignal(Button.SignalName.Pressed);
            if (road) Find<Button>(main, "RoadTab").EmitSignal(Button.SignalName.Pressed);
            else Find<Button>(main, "AllBuildingsTab").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(main, road ? "RoadCard" : "FarmCard").EmitSignal(Button.SignalName.Pressed);
            var world = main.GetNode<WorldMap>("WorldMap");
            Vector2 screen = world.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(new Vector2I(203, 189));
            Move(main, screen);
            await Frames(parent);
            if (road) Click(main, screen);
            var spaces = main.Game.GetBuildingSpaces();
            Key(main, Godot.Key.M);
            Require(maps.IsExpanded && !Find<Button>(main, "CancelPlacementButton").Visible &&
                world.PlacementPreviewAnchor == null && world.GetDisplayedRoadAppearance(new Vector2I(203, 189)) == null,
                "开图没有取消建造候选或未完成道路段");
            Key(main, Godot.Key.Escape);
            Require(!Find<Button>(main, "CancelPlacementButton").Visible && ReferenceEquals(spaces, main.Game.GetBuildingSpaces()),
                "关图恢复了工具或草稿被提交");
        }
        Find<Button>(main, "PauseButton").EmitSignal(Button.SignalName.Pressed);
        uint seconds = main.Game.Calendar.ElapsedSeconds;
        Key(main, Godot.Key.M);
        main._Process(1.1);
        Require(maps.IsExpanded && !main.Game.IsPaused && main.Game.Calendar.ElapsedSeconds > seconds,
            "原本运行时开图停止经营");
        Key(main, Godot.Key.M);
        Require(!main.Game.IsPaused, "关图改变运行状态");
        Find<Button>(main, "PauseButton").EmitSignal(Button.SignalName.Pressed);
    }

    private static async Task CheckGestures(Node parent, Main main)
    {
        MapNavigation maps = Find<MapNavigation>(main, "MapNavigation");
        var mini = Find<MapNavigationCanvas>(maps, "MiniMap");
        var atlas = Find<MapNavigationCanvas>(maps, "AtlasMap");
        var camera = main.GetNode<CameraController>("Camera2D");
        int navigations = 0, mainClicks = 0;
        camera.MapShortClicked += _ => mainClicks++;
        maps.NavigateRequested += _ => navigations++;
        Vector2 zoom = camera.Zoom;
        Wheel(main, mini.GetGlobalRect().GetCenter(), true);
        Require(camera.Zoom == zoom && mini.ViewZoom == 1, "小地图滚轮穿透或改变全图倍率");
        Vector2 p = Point(mini, MapCoordinates.CellToLocalCenter(new Vector2I(150, 160)));
        Click(main, p);
        await Frames(parent);
        Require(!maps.IsExpanded && navigations > 0, "小地图短按未定位或意外展开");
        RequireCamera(main, MapCoordinates.CellToLocalCenter(new Vector2I(150, 160)));
        MouseButtonEvent(main, p, MouseButton.Left, true);
        Vector2 q = Point(mini, MapCoordinates.CellToLocalCenter(new Vector2I(220, 210)));
        Move(main, q, MouseButtonMask.Left);
        MouseButtonEvent(main, q, MouseButton.Left, false);
        RequireCamera(main, MapCoordinates.CellToLocalCenter(new Vector2I(220, 210)));
        foreach (Vector2I corner in new[] { Vector2I.Zero, new Vector2I(383, 0), new Vector2I(383, 383), new Vector2I(0, 383) })
        {
            Click(main, Point(mini, MapCoordinates.CellToLocalCenter(corner)));
            RequireCamera(main, MapCoordinates.CellToLocalCenter(corner));
        }
        int before = navigations;
        Click(main, mini.GlobalPosition + new Vector2(2, 2));
        Require(navigations == before, "菱形外点击触发导航");

        Key(main, Godot.Key.M);
        await Frames(parent);
        Require(atlas.ViewZoom == 1 && atlas.ViewCenter.IsEqualApprox(MapNavigationDrawing.Center), "首次打开未全图适配");
        p = atlas.GetGlobalRect().GetCenter() + new Vector2(90, 25);
        Vector2 anchor = atlas.ToMapLocal(p - atlas.GlobalPosition);
        Wheel(main, p, true);
        Require(Mathf.IsEqualApprox(atlas.ViewZoom, 1.25f) &&
            atlas.ToMapLocal(p - atlas.GlobalPosition).DistanceTo(anchor) < .1f && camera.Zoom == zoom,
            "大地图滚轮不以鼠标锚定或改变主镜头倍率");
        before = navigations;
        Vector2 destination = MapCoordinates.CellToLocalCenter(new Vector2I(180, 160));
        Click(main, Point(atlas, destination));
        Require(maps.IsExpanded && navigations == before + 1, "大图短按未唯一定位或关闭大图");
        RequireCamera(main, destination);
        foreach (MouseButton primary in new[] { MouseButton.Right, MouseButton.Left })
        {
            MouseButton secondary = primary == MouseButton.Right ? MouseButton.Left : MouseButton.Right;
            MouseButtonMask primaryMask = primary == MouseButton.Right ? MouseButtonMask.Right : MouseButtonMask.Left;
            void ChordButton(Vector2 position, MouseButton button, bool pressed, MouseButtonMask held) =>
                Send(main, new InputEventMouseButton
                {
                    Position = position,
                    ButtonIndex = button,
                    Pressed = pressed,
                    ButtonMask = held
                });
            p = atlas.GetGlobalRect().GetCenter();
            Vector2 firstMove = p + new Vector2(80, 30);
            Vector2 continuedMove = p + new Vector2(140, 50);
            Vector2 initialCenter = atlas.ViewCenter;
            Vector2 initialCamera = camera.GlobalPosition;
            before = navigations;
            Move(main, p);
            ChordButton(p, primary, true, primaryMask);
            Move(main, firstMove, primaryMask, firstMove - p);
            await Frames(parent);
            Vector2 draggedCenter = atlas.ViewCenter;
            Require(draggedCenter.DistanceTo(initialCenter) > 1, $"{primary}键双键夹具没有先形成实际拖动");
            ChordButton(firstMove, secondary, true, MouseButtonMask.Left | MouseButtonMask.Right);
            Require(Input.IsMouseButtonPressed(MouseButton.Left) && Input.IsMouseButtonPressed(MouseButton.Right),
                "双键夹具没有同时保持左右键按下");
            ChordButton(firstMove, secondary, false, primaryMask);
            await Frames(parent);
            Require(navigations == before && camera.GlobalPosition.IsEqualApprox(initialCamera) &&
                atlas.ViewCenter.IsEqualApprox(draggedCenter), $"{primary}键拖动中按放{secondary}键误定位或重置浏览位置");
            Move(main, continuedMove, primaryMask, continuedMove - firstMove);
            await Frames(parent);
            Require(atlas.ViewCenter.DistanceTo(draggedCenter) > 1 && navigations == before &&
                camera.GlobalPosition.IsEqualApprox(initialCamera), $"{secondary}键释放取消了原{primary}键浏览手势");
            ChordButton(continuedMove, primary, false, (MouseButtonMask)0);
            Vector2 stoppedCenter = atlas.ViewCenter;
            Move(main, continuedMove + new Vector2(20, 0), relative: new Vector2(20, 0));
            await Frames(parent);
            Require(navigations == before && camera.GlobalPosition.IsEqualApprox(initialCamera) &&
                atlas.ViewCenter.IsEqualApprox(stoppedCenter), $"{primary}键最终释放后误定位或遗留拖动");
        }
        p = atlas.GetGlobalRect().GetCenter() + new Vector2(70, 0);
        Vector2 afterChordDestination = atlas.ToMapLocal(p - atlas.GlobalPosition);
        before = navigations;
        Click(main, p);
        Require(navigations == before + 1, "双键交叠结束后正常短按未恢复唯一导航");
        RequireCamera(main, afterChordDestination);
        foreach (MouseButton button in new[] { MouseButton.Left, MouseButton.Right })
        {
            p = atlas.GetGlobalRect().GetCenter();
            Vector2 oldCenter = atlas.ViewCenter;
            before = navigations;
            MouseButtonEvent(main, p, button, true);
            Move(main, p + new Vector2(80, 30), button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right);
            Require(atlas.ViewCenter.DistanceTo(oldCenter) > 1, "大地图拖动没有浏览底图");
            Move(main, p, button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right);
            MouseButtonEvent(main, p, button, false);
            Require(navigations == before, "拖回起点误判短按导航");
            MouseButtonEvent(main, p, button, true);
            Vector2 outside = atlas.GlobalPosition + new Vector2(-12, atlas.Size.Y / 2);
            Move(main, outside, button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right);
            MouseButtonEvent(main, outside, button, false);
            Vector2 releasedCenter = atlas.ViewCenter;
            Move(main, p + new Vector2(20, 0));
            Require(navigations == before && atlas.ViewCenter == releasedCenter, "跨控件释放遗留手势或定位");
        }
        foreach (StringName signal in new[] { Window.SignalName.MouseExited, Window.SignalName.FocusExited })
        {
            p = atlas.GetGlobalRect().GetCenter();
            before = navigations;
            MouseButtonEvent(main, p, MouseButton.Left, true);
            main.GetWindow().EmitSignal(signal);
            MouseButtonEvent(main, p, MouseButton.Left, false);
            Require(navigations == before, "离窗或失焦后的释放误导航");
            main.GetWindow().EmitSignal(Window.SignalName.MouseEntered);
        }
        p = atlas.GetGlobalRect().GetCenter();
        before = navigations;
        MouseButtonEvent(main, p, MouseButton.Left, true);
        Key(main, Godot.Key.Escape);
        MouseButtonEvent(main, p, MouseButton.Left, false);
        Require(navigations == before && !maps.IsExpanded, "关闭大图后释放残留手势");
        Key(main, Godot.Key.M);
        await Frames(parent);
        p = atlas.GetGlobalRect().GetCenter();
        for (int i = 0; i < 20; i++) Wheel(main, p, true);
        Require(atlas.ViewZoom == 12, "大图最高倍率不是12倍");
        for (int i = 0; i < 24; i++) Wheel(main, p, false);
        Require(atlas.ViewZoom == 1, "大图最低倍率不是全图适配");
        MapButton(maps, "+").EmitSignal(Button.SignalName.Pressed);
        MapButton(maps, "+").EmitSignal(Button.SignalName.Pressed);
        MapButton(maps, "定位当前镜头").EmitSignal(Button.SignalName.Pressed);
        Require(Mathf.IsEqualApprox(atlas.ViewZoom, 1.5625f), "定位当前镜头改变大图倍率");
        Vector2 savedCenter = atlas.ViewCenter;
        float savedZoom = atlas.ViewZoom;
        Key(main, Godot.Key.M);
        Key(main, Godot.Key.M);
        Require(atlas.ViewCenter == savedCenter && atlas.ViewZoom == savedZoom, "同局重开丢失浏览位置和倍率");
        MapButton(maps, "查看全图").EmitSignal(Button.SignalName.Pressed);
        Require(atlas.ViewZoom == 1 && atlas.ViewCenter.IsEqualApprox(MapNavigationDrawing.Center), "全图按钮未归位");
        Key(main, Godot.Key.M);
        Require(mainClicks == 0, "大小地图手势穿透主地图短按分派");
    }

    private static async Task CheckViewsAndLayout(Node parent, Main main)
    {
        var maps = Find<MapNavigation>(main, "MapNavigation");
        var mini = Find<MapNavigationCanvas>(maps, "MiniMap");
        var atlas = Find<MapNavigationCanvas>(maps, "AtlasMap");
        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(1920, 1080) })
        {
            parent.GetWindow().Size = size;
            await Frames(parent);
            CheckCameraCorners(main, mini);
            CheckCameraCorners(main, atlas);
        }
        foreach (bool up in new[] { true, true, false, false })
        {
            Wheel(main, main.GetViewport().GetVisibleRect().GetCenter(), up);
            await Frames(parent);
            CheckCameraCorners(main, mini);
            CheckCameraCorners(main, atlas);
        }
        foreach ((float overall, float font, string name) in new[] { (1f, 1f, "default"), (1.25f, 1f, "ui-125"), (1f, 1.2f, "font-120") })
        {
            UiScaling.SetOverallScale(ui, overall);
            UiScaling.SetFontScale(ui, font);
            await Frames(parent);
            Rect2 miniRect = Find<Control>(maps, "MiniMapPanel").GetGlobalRect();
            Require(main.GetViewport().GetVisibleRect().Encloses(miniRect), "倍率调整后小地图超出窗口");
            Require(!miniRect.Intersects(Find<Control>(main, "MessagePanel").GetGlobalRect()) &&
                !miniRect.Intersects(Find<Button>(main, "BuildButton").GetGlobalRect()), "小地图遮挡近况或底部经营入口");
            await WaitForNavigation(parent, main);
            CheckMapButtons(maps, overall);
            await Capture(parent, Evidence, name + "-minimap.png");
            Key(main, Godot.Key.M);
            await Frames(parent);
            Require(atlas.Size.X > 0 && atlas.Size.Y > 0 && main.GetViewport().GetVisibleRect().Encloses(atlas.GetGlobalRect()),
                "倍率调整后大地图画布伸出窗口");
            CheckMapLegend(maps);
            CheckMapButtons(maps, overall);
            await Capture(parent, Evidence, name + "-atlas.png");
            Key(main, Godot.Key.Escape);
        }
        UiScaling.SetFontScale(ui, 1);
        UiScaling.SetOverallScale(ui, 1);
        await Frames(parent);
    }

    private static async Task CheckMapChanges(Node parent, Main main)
    {
        using var drawing = new MapNavigationDrawing();
        drawing.Initialize(main.Game.Terrain);
        drawing.Refresh(main.Game.GetBuildingSpaces());
        var world = main.GetNode<WorldMap>("WorldMap");
        Vector2I cell = new(210, 190);
        main.GetNode<CameraController>("Camera2D").NavigateTo(MapCoordinates.CellToLocalCenter(cell));
        await Frames(parent);
        Vector2 screen = world.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);
        Find<Button>(main, "BuildButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadCard").EmitSignal(Button.SignalName.Pressed);
        Move(main, screen);
        await Frames(parent);
        Require(world.GetDisplayedRoadAppearance(cell) is { Added: true } && !drawing.Refresh(main.Game.GetBuildingSpaces()),
            "真实鼠标候选提前进入正式地图图层");
        Click(main, screen);
        Require(!drawing.Refresh(main.Game.GetBuildingSpaces()), "第一短按提前进入地图图层");
        Click(main, screen);
        await WaitForNavigation(parent, main);
        Require(main.Game.GetPlot(cell).Building == BuildingKind.Road && drawing.Refresh(main.Game.GetBuildingSpaces()),
            "真实道路提交后地图没有接收新空间快照");
        using (ArrayMesh mesh = drawing.BuildFacilities(.02f))
            Require(mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color].AsColorArray().Contains(MapNavigationDrawing.Road),
                "真实建成道路没有绘图标记");
        Key(main, Godot.Key.M);
        var maps = Find<MapNavigation>(main, "MapNavigation");
        var atlas = Find<MapNavigationCanvas>(maps, "AtlasMap");
        await Frames(parent);
        MapButton(maps, "定位当前镜头").EmitSignal(Button.SignalName.Pressed);
        for (int index = 0; index < 6; index++) MapButton(maps, "+").EmitSignal(Button.SignalName.Pressed);
        // 主镜头移开真实道路：全图模式中的视野中心白点会覆盖单格道路，不能把被遮挡的图当成建造证据。
        var camera = main.GetNode<CameraController>("Camera2D");
        Vector2 away = MapCoordinates.CellToLocalCenter(cell + new Vector2I(0, 40));
        camera.NavigateTo(away);
        await Frames(parent);
        Require(main.Game.GetPlot(cell).Building == BuildingKind.Road, "建路截图前正式道路已被移除");
        await Capture(parent, Evidence, "built-road-atlas.png");
        CheckRoadPixel(parent, atlas, cell, true);
        Key(main, Godot.Key.M);
        camera.NavigateTo(MapCoordinates.CellToLocalCenter(cell));
        await Frames(parent);
        screen = world.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);
        Find<Button>(main, "BuildButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadRemovalCard").EmitSignal(Button.SignalName.Pressed);
        Click(main, screen);
        Click(main, screen);
        await WaitForNavigation(parent, main);
        Require(main.Game.GetPlot(cell).Building == BuildingKind.None && drawing.Refresh(main.Game.GetBuildingSpaces()),
            "真实拆除后地图没有接收空间变更");
        using (ArrayMesh mesh = drawing.BuildFacilities(.02f))
            Require(!mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color].AsColorArray().Contains(MapNavigationDrawing.Road),
                "拆除后地图残留道路标记");
        Find<Button>(main, "CancelPlacementButton").EmitSignal(Button.SignalName.Pressed);
        camera.NavigateTo(away);
        Key(main, Godot.Key.M);
        await Capture(parent, Evidence, "removed-road-atlas.png");
        CheckRoadPixel(parent, atlas, cell, false);
        MapButton(maps, "查看全图").EmitSignal(Button.SignalName.Pressed);
        Key(main, Godot.Key.M);
    }

    private static async Task WaitForNavigation(Node parent, Main main)
    {
        await main.Game.WaitForNavigationAsync(parent.GetTree());
        await Frames(parent);
        Require(main.Game.IsNavigationReady && !Find<Label>(main, "ActivityLabel").Text.Contains("正在更新道路"),
            "截图前真实道路导航或Main经营近况仍处于准备状态");
    }

    private static void CheckMapLegend(MapNavigation maps)
    {
        foreach (var (title, _) in MapNavigationDrawing.Legend)
        {
            Label label = maps.FindChildren("*", "Label", true, false).OfType<Label>().Single(node => node.Text == title);
            Font font = label.GetThemeFont("font");
            int fontSize = label.GetThemeFontSize("font_size");
            float textWidth = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, fontSize).X;
            Require(label.IsVisibleInTree() && label.GetLineCount() == 1 && label.Size.X + 1 >= textWidth &&
                label.Size.Y + 1 >= font.GetHeight(fontSize), $"图例「{title}」换行、竖排或截断");
            Require(label.GetParent<Control>().GetGlobalRect().Grow(1).Encloses(label.GetGlobalRect()),
                $"图例「{title}」伸出条目容器");
        }
    }

    private static void CheckMapButtons(MapNavigation maps, float overall)
    {
        foreach (Button button in maps.FindChildren("*", "Button", true, false).OfType<Button>().Where(node => node.IsVisibleInTree()))
        {
            Font font = button.GetThemeFont("font");
            int fontSize = button.GetThemeFontSize("font_size");
            float textWidth = font.GetStringSize(button.Text, HorizontalAlignment.Left, -1, fontSize).X;
            Vector2 margins = button.GetThemeStylebox("normal").GetMinimumSize();
            Require(button.Size.X + 1 >= textWidth + margins.X && button.Size.Y + 1 >= font.GetHeight(fontSize) + margins.Y &&
                button.Size.Y <= 64 * overall, $"地图按钮「{button.Text}」文字截断或行高异常：{button.Size}");
            Require(button.GetParent<Control>().GetGlobalRect().Grow(1).Encloses(button.GetGlobalRect()),
                $"地图按钮「{button.Text}」伸出容器");
        }
    }

    private static void CheckRoadPixel(Node parent, MapNavigationCanvas atlas, Vector2I cell, bool present)
    {
        if (DisplayServer.GetName() == "headless") return;
        Vector2 point = Point(atlas, MapCoordinates.CellToLocalCenter(cell));
        using Image image = parent.GetViewport().GetTexture().GetImage();
        bool roadPixel = false;
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                Color color = image.GetPixel(Mathf.RoundToInt(point.X) + x, Mathf.RoundToInt(point.Y) + y);
                Color road = MapNavigationDrawing.Road;
                roadPixel |= Math.Abs(color.R - road.R) < .04f && Math.Abs(color.G - road.G) < .04f && Math.Abs(color.B - road.B) < .04f;
            }
        Require(roadPixel == present, present ? "建路截图没有真实可见的道路像素" : "拆路截图残留道路像素");
    }

    private static void CheckDrawing(Main main)
    {
        using var drawing = new MapNavigationDrawing();
        drawing.Initialize(main.Game.Terrain);
        var spaces = main.Game.GetBuildingSpaces();
        Require(drawing.Refresh(spaces) && !drawing.Refresh(spaces), "静态空间快照没有跳过重复重建");
        using ArrayMesh mesh = drawing.BuildFacilities(.02f);
        var arrays = mesh.SurfaceGetArrays(0);
        Require(arrays[(int)Mesh.ArrayType.Vertex].AsVector2Array().Length > 0,
            "真实设施没有生成地图图层");
        Color[] colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        Require(colors.Contains(MapNavigationDrawing.Farm) && colors.Contains(MapNavigationDrawing.Processor), "地图缺少真实农田或加工图层");
        Vector2I cell = spaces[0].AnchorCell;
        Require(!main.Game.TryPlaceRoads(new[] { cell }, RoadMaterial.Dirt).Success && !drawing.Refresh(main.Game.GetBuildingSpaces()),
            "失败建造进入正式地图缓存");
        using Image terrain = drawing.TerrainTexture!.GetImage();
        Require(terrain.GetWidth() > FarmGame.MapSize && terrain.GetHeight() > FarmGame.MapSize, "真实岸线纹理未细分采样");
        if (!ReferenceEquals(main.InitialTerrainRules, TerrainTestFixtures.AllLandRules))
        {
            byte[] data = terrain.GetData();
            Require(Enumerable.Range(1, data.Length / 4 - 1).Any(i => data[i * 4] != data[0]), "真实含水局地图纹理没有水域差异");
        }
    }

    private static void CheckCameraCorners(Main main, MapNavigationCanvas canvas)
    {
        var world = main.GetNode<WorldMap>("WorldMap");
        Transform2D transform = world.GetGlobalTransformWithCanvas().AffineInverse();
        Rect2 screen = main.GetViewport().GetVisibleRect();
        Vector2[] expected = { screen.Position, new(screen.End.X, screen.Position.Y), screen.End, new(screen.Position.X, screen.End.Y) };
        for (int index = 0; index < 4; index++)
            Require(canvas.CameraCorners[index].DistanceTo(transform * expected[index]) < .1f, "视野框未采用完整实际视口四角");
    }

    private static void RequireCamera(Main main, Vector2 local)
    {
        var world = main.GetNode<WorldMap>("WorldMap");
        Vector2 expected = world.ClampGlobalCameraCenter(world.ToGlobal(local));
        Require(main.GetNode<CameraController>("Camera2D").GlobalPosition.DistanceTo(expected) < 2,
            "地图点击位置与真实镜头定位不一致");
    }

    internal static Vector2 Point(MapNavigationCanvas view, Vector2 local) =>
        view.GlobalPosition + view.Size / 2 + (local - view.ViewCenter) * view.MapScale;

    private static Button MapButton(MapNavigation root, string text) => root.FindChildren("*", "Button", true, false)
        .OfType<Button>().Single(button => button.Text == text);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
