using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Trading;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestBuildPlacement : Node
{
    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        if (passed) GD.Print("所有建筑的连续摆放、取消与原生输入检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        Window window = parent.GetWindow();
        Vector2I originalSize = window.Size;
        try
        {
            // headless 默认物理窗口可能只有64×64；先建立与目标格一致的基准视野。
            window.Size = new Vector2I(1280, 720);
            await Frames(parent);
            foreach (BuildingKind kind in new[] { BuildingKind.Farm, BuildingKind.Processor, BuildingKind.Road })
            {
                IEnumerable<CropKind> crops = kind == BuildingKind.Processor
                    ? Enum.GetValues<CropKind>() : new[] { CropKind.Wheat };
                foreach (CropKind crop in crops)
                    if (!await CheckType(parent, kind, crop)) return false;
            }
            return await CheckFunds(parent) && await CheckSameFrameInput(parent) && await CheckNativeInput(parent);
        }
        finally
        {
            window.Size = originalSize;
            await Frames(parent);
        }
    }

    private static Main CreateMain(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        main.GetNode<Timer>("TickTimer").Stop();
        return main;
    }

    private static async Task<bool> CheckType(Node parent, BuildingKind kind, CropKind crop)
    {
        Main main = CreateMain(parent);
        try
        {
            Start(main, kind, crop);
            if (main.Game.IsPaused) return Fail("进入摆放将运行中的经营暂停");
            main.Game.SetPaused(true);
            await Frames(parent);
            var map = main.GetNode<WorldMap>("WorldMap");
            Button cancel = Find<Button>(main, "CancelPlacementButton");
            Control detail = Find<Control>(main, "DetailWindow");
            Vector2I first = new(186, 192), second = new(183, 192);
            int cost = FarmGame.GetBuildingCostCents(kind);
            Start(main, kind, crop);
            Label hint = Find<Label>(main, "BuildHint");
            if (!hint.Text.Contains($"占地 {BuildingFootprint.Get(kind).Offsets.Count} 格") || !main.Game.IsPaused)
                return Fail("占地提示没有读取属性或进入摆放改变了暂停状态");
            map.EmitSignal(WorldMap.SignalName.SelectionChanged, first);
            map.EmitSignal(WorldMap.SignalName.SelectionChanged, second);
            if (main.Game.MoneyCents != 5000 - 2 * cost || main.Game.GetPlot(first).Building != kind ||
                main.Game.GetPlot(second).Building != kind || !cancel.Visible || detail.Visible)
                return Fail($"{kind}/{crop}连续建造没有一次扣费、保持模式或误弹详情");
            int money = main.Game.MoneyCents;
            map.EmitSignal(WorldMap.SignalName.SelectionChanged, first);
            if (main.Game.MoneyCents != money || !cancel.Visible ||
                !Find<Label>(main, "MessageLabel").Text.Contains("已有建筑"))
                return Fail("冲突失败扣费或退出摆放");

            foreach (int method in new[] { 0, 1, 2 })
            {
                Start(main, kind, crop);
                await Move(parent, main, Screen(map, new Vector2I(195, 198)));
                if (!CheckFooterLayout(main)) return false;
                if (map.PlacementPreviewAnchor == null)
                {
                    var camera = main.GetNode<CameraController>("Camera2D");
                    return Fail($"取消验收前没有真实候选预览：类型{kind}/{crop}，取消{method}，" +
                        $"实际鼠标{main.GetViewport().GetMousePosition()}，目标{Screen(map, new Vector2I(195, 198))}，" +
                        $"视口{main.GetViewport().GetVisibleRect()}，窗口{main.GetWindow().Size}，" +
                        $"拖动{camera.IsDragging}，在窗{camera.IsMouseInsideWindow}，" +
                        $"hover{main.GetViewport().GuiGetHoveredControl()?.Name}，取消按钮{cancel.Visible}");
                }
                if (method == 0) Send(main, new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
                else if (method == 1) Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
                else cancel.EmitSignal(Button.SignalName.Pressed);
                Input.FlushBufferedEvents();
                await Frames(parent);
                if (method == 0) Send(main, new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
                if (method == 1) Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = false });
                if (cancel.Visible || map.PlacementPreviewAnchor != null || main.Game.MoneyCents != money)
                    return Fail($"{kind}/{crop}取消方式{method}没有清除状态或改变资源");
            }
            map.EmitSignal(WorldMap.SignalName.SelectionChanged, first);
            if (!detail.Visible || main.Game.MoneyCents != money)
                return Fail("取消后选择仍触发建造或没有恢复详情");
            return true;
        }
        finally { main.Free(); }
    }

    private static async Task<bool> CheckFunds(Node parent)
    {
        Main main = CreateMain(parent);
        try
        {
            main.Game.SetPaused(true);
            var request = new TradeOrderRequest(new CommodityId(CropKind.Radish, CommodityKind.Raw),
                TradeOrderSide.Buy, TradeOrderFrequency.Once, TradeOrderQuantityMode.Fixed, 0,
                TradeOrderBudgetMode.FixedBudget, 4300, 0, CashReserveMode.Amount, 0,
                new IReadOnlyList<TradeOrderCondition>[]
                { new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Less, 1) } });
            TradeOrderCommandResult order = main.Game.CreateTradeOrder(request);
            if (!order.Success || main.Game.FrozenMoneyCents == 0) return Fail("冻结资金夹具创建失败");
            Start(main, BuildingKind.Farm);
            await Frames(parent);
            var map = main.GetNode<WorldMap>("WorldMap");
            Label cost = Find<Label>(main, "BuildCost");
            if (!CheckFooterLayout(main)) return false;
            if (!cost.Text.Contains("10.00 金币") || !cost.Text.Contains("可用") ||
                cost.GetThemeColor("font_color").G >= 0.6f)
                return Fail("冻结资金未反映到标准费用与可用金币红色提示");
            int money = main.Game.MoneyCents, frozen = main.Game.FrozenMoneyCents;
            map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(186, 192));
            if (main.Game.MoneyCents != money || main.Game.FrozenMoneyCents != frozen ||
                main.Game.GetPlot(new Vector2I(186, 192)).Building != BuildingKind.None)
                return Fail("执行时动用冻结金币或失败留下设施");
            main.Game.CancelTradeOrder(order.Id);
            await Frames(parent);
            if (cost.Text.Contains("可用") || cost.GetThemeColor("font_color") != UiElements.Ink)
                return Fail("取消冻结后悬停费用未及时恢复");
            return true;
        }
        finally { main.Free(); }
    }

    private static async Task<bool> CheckSameFrameInput(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        var map = main.GetNode<WorldMap>("WorldMap");
        var confirmations = new List<(Vector2I Cell, Vector2I? Preview, int Money, BuildingKind Building)>();
        void BeforePlacement(Vector2I cell) => confirmations.Add((cell, map.PlacementPreviewAnchor,
            main.Game.MoneyCents, main.Game.GetPlot(cell).Building));
        // 挂树前订阅真实选择信号，保证观察先于 Main._Ready 注册的经营提交回调。
        map.SelectionChanged += BeforePlacement;
        parent.AddChild(main);
        main.GetNode<Timer>("TickTimer").Stop();
        main.Game.SetPaused(true);
        try
        {
            await Frames(parent);
            Start(main, BuildingKind.Farm);
            await Move(parent, main, Screen(map, new Vector2I(183, 192)));
            Vector2I first = new(186, 192);
            Vector2 target = Screen(map, first);
            // 连续发送，不让 _Process 在新位置移动和松开之间刷新候选。
            Send(main, new InputEventMouseMotion { Position = target, GlobalPosition = target });
            Send(main, new InputEventMouseButton
            {
                Position = target,
                GlobalPosition = target,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                ButtonMask = MouseButtonMask.Left
            });
            Send(main, new InputEventMouseButton
            {
                Position = target,
                GlobalPosition = target,
                ButtonIndex = MouseButton.Left,
                Pressed = false
            });
            if (confirmations.Count != 1 || confirmations[0] != (first, (Vector2I?)first, 5000, BuildingKind.None) ||
                main.Game.GetBuildingSpace(first)?.AnchorCell != first || main.Game.MoneyCents != 4000)
                return Fail("同帧移动和松开没有在提交前同步同一候选，或实际建造目标/扣费错误");

            Transform2D transform = map.GetGlobalTransformWithCanvas();
            Vector2 before = transform * MapCoordinates.GridPositionToLocal(new Vector2(198.43f, 192));
            Vector2 after = transform * MapCoordinates.GridPositionToLocal(new Vector2(198.57f, 192));
            Vector2I pressedCell = map.ScreenToCell(before), releasedCell = map.ScreenToCell(after);
            if (before.DistanceTo(after) >= 8 || pressedCell == releasedCell)
                return Fail("短按跨格夹具没有跨格或达到拖动阈值");
            await Move(parent, main, before);
            Vector2 actualBefore = main.GetViewport().GetMousePosition();
            if (map.PlacementPreviewAnchor != pressedCell) return Fail("短按跨格前没有真实旧候选");
            Send(main, new InputEventMouseButton
            {
                Position = before,
                GlobalPosition = before,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                ButtonMask = MouseButtonMask.Left
            });
            Send(main, new InputEventMouseMotion
            {
                Position = after,
                GlobalPosition = after,
                Relative = after - before,
                ButtonMask = MouseButtonMask.Left
            });
            Send(main, new InputEventMouseButton
            {
                Position = after,
                GlobalPosition = after,
                ButtonIndex = MouseButton.Left,
                Pressed = false
            });
            Vector2 actualAfter = main.GetViewport().GetMousePosition();
            if (actualBefore.DistanceTo(actualAfter) >= 8 || map.ScreenToCell(actualBefore) == map.ScreenToCell(actualAfter))
                return Fail("系统鼠标取整后短按跨格夹具失效");
            if (confirmations.Count != 2 || confirmations[1] != (releasedCell, (Vector2I?)releasedCell, 4000, BuildingKind.None) ||
                main.Game.GetBuildingSpace(releasedCell)?.AnchorCell != releasedCell || main.Game.MoneyCents != 3000 ||
                !Find<Button>(main, "CancelPlacementButton").Visible || Find<Control>(main, "DetailWindow").Visible)
                return Fail("小于8像素跨格短按没有在提交前同步释放格，或误拖动/重复扣费");
            return true;
        }
        finally
        {
            map.SelectionChanged -= BeforePlacement;
            main.Free();
        }
    }

    private static async Task<bool> CheckNativeInput(Node parent)
    {
        Main main = CreateMain(parent);
        Window window = parent.GetWindow();
        Vector2I originalSize = window.Size;
        Vector2I originalMouse = DisplayServer.GetName() == "headless" ? default : DisplayServer.MouseGetPosition();
        try
        {
            window.Size = new Vector2I(1280, 720);
            window.EmitSignal(Window.SignalName.MouseEntered);
            main.Game.SetPaused(true);
            await Frames(parent);
            var map = main.GetNode<WorldMap>("WorldMap");
            var camera = main.GetNode<CameraController>("Camera2D");
            Vector2I empty = new(186, 192);
            Start(main, BuildingKind.Farm);
            Vector2 point = Screen(map, empty);
            await Move(parent, main, point);
            if (map.PlacementPreviewAnchor != empty) return Fail("悬停候选与鼠标格不一致");
            if (!CheckFooterLayout(main)) return false;
            int money = main.Game.MoneyCents, count = main.Game.GetBuildingSpaces().Count;
            int redraws = map.ChunkRedrawCount;
            await Capture(parent, "valid.png");
            await Move(parent, main, point + new Vector2(12, 0));
            await Move(parent, main, point);
            if (main.Game.MoneyCents != money || main.Game.GetBuildingSpaces().Count != count ||
                map.ChunkRedrawCount != redraws)
                return Fail("悬停修改经营状态或重建地图块");
            await ClickAt(parent, main, point);
            if (main.Game.GetPlot(empty).Building != BuildingKind.Farm || main.Game.MoneyCents != money - 1000 ||
                !Find<Button>(main, "CancelPlacementButton").Visible || Find<Control>(main, "DetailWindow").Visible)
                return Fail("真实短按未建造一次并保持模式");
            await Move(parent, main, Screen(map, empty + new Vector2I(-1, 0)));
            if (!CheckFooterLayout(main)) return false;
            await Capture(parent, "conflict.png");
            money = main.Game.MoneyCents;
            count = main.Game.GetBuildingSpaces().Count;
            await ClickAt(parent, main, point);
            if (main.Game.MoneyCents != money || main.Game.GetBuildingSpaces().Count != count)
                return Fail("真实冲突点击扣费或生成设施");

            Vector2 uiPoint = Find<Control>(main, "BuildHint").GetGlobalRect().GetCenter();
            await Move(parent, main, uiPoint);
            if (map.PlacementPreviewAnchor != null) return Fail("界面上未隐藏预览");
            await ClickAt(parent, main, uiPoint);
            await Press(parent, main, uiPoint, true);
            await Press(parent, main, point, false);
            await Press(parent, main, point, true);
            await Press(parent, main, uiPoint, false);
            if (main.Game.MoneyCents != money || main.Game.GetBuildingSpaces().Count != count)
                return Fail("UI点击或跨UI按下松开穿透建造");

            Vector2 topGap = new(340, 50);
            await Move(parent, main, topGap);
            if (map.PlacementPreviewAnchor != map.ScreenToCell(topGap))
                return Fail("顶部独立状态面板之间的透明间隙仍阻挡地图候选");

            await Move(parent, main, point);
            Vector2 before = camera.GlobalPosition;
            await Press(parent, main, point, true);
            Send(main, new InputEventMouseMotion
            {
                Position = point + new Vector2(65, 30),
                GlobalPosition = point + new Vector2(65, 30),
                Relative = new Vector2(65, 30),
                ButtonMask = MouseButtonMask.Left
            });
            await Frames(parent);
            if (!camera.IsDragging || map.PlacementPreviewAnchor != null) return Fail("拖动时预览没有隐藏");
            await Press(parent, main, point + new Vector2(65, 30), false);
            if (camera.GlobalPosition.IsEqualApprox(before) || main.Game.MoneyCents != money ||
                main.Game.GetBuildingSpaces().Count != count) return Fail("拖动松开误建或未平移镜头");
            await Move(parent, main, point);
            if (map.PlacementPreviewAnchor != MouseCell(main, map)) return Fail("镜头移动后候选滞后");
            await Capture(parent, "camera.png");
            before = camera.GlobalPosition;
            Send(main, new InputEventMouseButton
            {
                Position = point,
                GlobalPosition = point,
                ButtonIndex = MouseButton.Middle,
                Pressed = true,
                ButtonMask = MouseButtonMask.Middle
            });
            Send(main, new InputEventMouseMotion
            {
                Position = point + new Vector2(45, 15),
                GlobalPosition = point + new Vector2(45, 15),
                Relative = new Vector2(45, 15),
                ButtonMask = MouseButtonMask.Middle
            });
            await Frames(parent);
            if (!camera.IsDragging || map.PlacementPreviewAnchor != null) return Fail("中键拖动没有隐藏预览");
            Send(main, new InputEventMouseButton
            {
                Position = point + new Vector2(45, 15),
                GlobalPosition = point + new Vector2(45, 15),
                ButtonIndex = MouseButton.Middle,
                Pressed = false
            });
            await Move(parent, main, point);
            if (camera.GlobalPosition.IsEqualApprox(before))
                return Fail("中键拖动没有平移镜头");
            if (main.Game.MoneyCents != money || main.Game.GetBuildingSpaces().Count != count)
                return Fail($"中键松开误建：金币{main.Game.MoneyCents}/{money}，建筑{main.Game.GetBuildingSpaces().Count}/{count}");
            if (map.PlacementPreviewAnchor != MouseCell(main, map))
                return Fail("中键松开未恢复实际鼠标下的候选");
            Send(main, new InputEventMouseButton
            {
                Position = point,
                GlobalPosition = point,
                ButtonIndex = MouseButton.WheelUp,
                Pressed = true
            });
            await Frames(parent);
            if (map.PlacementPreviewAnchor != MouseCell(main, map)) return Fail("缩放后候选滞后");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, Pressed = true });
            await Frames(parent);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, Pressed = false });
            await Frames(parent);
            if (map.PlacementPreviewAnchor != MouseCell(main, map)) return Fail("键盘平移后候选滞后");
            window.Size = new Vector2I(1440, 900);
            await Frames(parent);
            await Move(parent, main, point);
            if (map.PlacementPreviewAnchor != MouseCell(main, map)) return Fail("窗口改变后候选滞后");
            if (!CheckFooterLayout(main)) return false;
            window.EmitSignal(Window.SignalName.MouseExited);
            await Frames(parent);
            if (map.PlacementPreviewAnchor != null) return Fail("离开窗口没有隐藏预览");
            window.EmitSignal(Window.SignalName.MouseEntered);
            camera.GlobalPosition = map.GetGridWorldPosition(new Vector2(383, 191));
            await Frames(parent);
            await Move(parent, main, Screen(map, new Vector2I(383, 191)));
            if (map.PlacementPreviewAnchor != new Vector2I(383, 191)) return Fail("地图边缘候选被挪位");
            if (!CheckFooterLayout(main)) return false;
            await Capture(parent, "edge.png");
            if (!main.Game.IsPaused || main.Game.Calendar.ElapsedSeconds != 0 || main.Game.MoneyCents != money ||
                main.Game.GetBuildingSpaces().Count != count) return Fail("镜头与预览操作改变暂停经营状态");
            return true;
        }
        finally
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, Pressed = false });
            main.Free();
            window.Size = originalSize;
            if (DisplayServer.GetName() != "headless") Input.WarpMouse(originalMouse - window.Position);
            await Frames(parent);
        }
    }

    private static void Start(Main main, BuildingKind kind, CropKind crop = CropKind.Wheat)
    {
        Find<Button>(main, "BuildButton").EmitSignal(Button.SignalName.Pressed);
        string tab = kind == BuildingKind.Processor ? "ProcessorTab" : kind == BuildingKind.Road ? "RoadTab" : "FarmTab";
        Find<Button>(main, tab).EmitSignal(Button.SignalName.Pressed);
        string card = kind == BuildingKind.Processor ? $"ProcessorCard{crop}" : kind == BuildingKind.Road ? "RoadCard" : "FarmCard";
        Find<Button>(main, card).EmitSignal(Button.SignalName.Pressed);
    }

    private static bool CheckFooterLayout(Main main)
    {
        Rect2 viewport = main.GetViewport().GetVisibleRect();
        Rect2 footer = Find<Control>(main, "BottomBar").GetGlobalRect();
        Rect2 activity = Find<Control>(main, "MessagePanel").GetGlobalRect();
        if (!viewport.Encloses(footer)) return Fail("底栏伸出可见窗口");
        if (!viewport.Encloses(activity)) return Fail("近况和摆放反馈伸出可见窗口");
        foreach (string name in new[] { "BuildButton", "InventoryButton", "MarketButton", "CultivationButton", "BuildHint", "BuildCost", "CancelPlacementButton" })
        {
            Control control = Find<Control>(main, name);
            if (!control.IsVisibleInTree()) continue;
            Rect2 rect = control.GetGlobalRect();
            Rect2 panel = name is "BuildHint" or "BuildCost" or "CancelPlacementButton" ? activity : footer;
            if (!panel.Encloses(rect)) return Fail($"经营控件{name}伸出所属面板或被裁切");
            string text = control is Label label ? label.Text : ((Button)control).Text;
            Font font = control.GetThemeFont("font");
            int fontSize = control.GetThemeFontSize("font_size");
            string[] lines = text.Split('\n');
            if (control is Label measured && measured.GetLineCount() != lines.Length)
                return Fail($"经营提示{name}发生额外折行");
            foreach (string line in lines)
                if (font.GetStringSize(line, fontSize: fontSize).X > control.Size.X)
                    return Fail($"底栏{name}宽度无法容纳文字");
            if (font.GetHeight(fontSize) * lines.Length > control.Size.Y)
                return Fail($"底栏{name}高度无法完整显示文字");
        }
        Rect2 hint = Find<Control>(main, "BuildHint").GetGlobalRect();
        Rect2 cost = Find<Control>(main, "BuildCost").GetGlobalRect();
        Rect2 cancel = Find<Control>(main, "CancelPlacementButton").GetGlobalRect();
        if (hint.End.Y > cost.Position.Y || cost.End.Y > cancel.Position.Y)
            return Fail("近况面板的提示、费用和取消按钮相互重叠");
        return true;
    }

    private static Vector2 Screen(WorldMap map, Vector2I cell) =>
        map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);

    private static Vector2I MouseCell(Main main, WorldMap map) =>
        map.ScreenToCell(main.GetViewport().GetMousePosition());

    private static void Send(Main main, InputEventMouse inputEvent)
    {
        var transformed = (InputEventMouse)inputEvent.XformedBy(main.GetViewport().GetFinalTransform());
        transformed.GlobalPosition = main.GetViewport().GetFinalTransform() * inputEvent.GlobalPosition;
        if (DisplayServer.GetName() != "headless" && inputEvent is InputEventMouseMotion)
            Input.WarpMouse(transformed.Position);
        Input.ParseInputEvent(transformed);
        Input.FlushBufferedEvents();
    }

    private static async Task Move(Node parent, Main main, Vector2 point)
    {
        Send(main, new InputEventMouseMotion { Position = point, GlobalPosition = point });
        await Frames(parent);
    }

    private static async Task Press(Node parent, Main main, Vector2 point, bool pressed)
    {
        Send(main, new InputEventMouseButton
        {
            Position = point,
            GlobalPosition = point,
            ButtonIndex = MouseButton.Left,
            Pressed = pressed,
            ButtonMask = pressed ? MouseButtonMask.Left : 0
        });
        await Frames(parent);
    }

    private static async Task ClickAt(Node parent, Main main, Vector2 point)
    {
        await Move(parent, main, point);
        await Press(parent, main, point, true);
        await Press(parent, main, point, false);
    }

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static async Task Capture(Node parent, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://build/issue87-validation");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + name);
    }

    private static T Find<T>(Node parent, string name) where T : Node =>
        parent.FindChild(name, true, false) as T ?? throw new InvalidOperationException($"缺少界面节点：{name}");
    private static bool Fail(string message) { GD.PushError("放置预览：" + message); return false; }
}
