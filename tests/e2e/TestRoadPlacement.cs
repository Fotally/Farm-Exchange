using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Trading;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestRoadPlacement : Node
{
    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        if (passed) GD.Print("道路五材质未点击起点预览、两次短按、轨迹顺序、原子拒绝、投影拆除及输入生命周期通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        if (!CheckGesture()) return false;
        Window window = parent.GetWindow();
        Vector2I originalSize = window.Size;
        window.Size = new Vector2I(1920, 1080);
        await Frames(parent);
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.InitialTerrainRules = TerrainTestFixtures.AllLandRules;
        parent.AddChild(main);
        Find<Button>(main, "PauseButton").EmitSignal(Button.SignalName.Pressed);
        try
        {
            return await CheckStartPreview(parent, main) && await CheckRemovalStartPreview(parent, main) &&
                await CheckTools(parent, main) && await CheckInput(parent, main) &&
                await CheckOutsideMap(parent, main);
        }
        catch (InvalidOperationException error)
        {
            return Fail(error.Message);
        }
        finally
        {
            main.Free();
            window.Size = originalSize;
            await Frames(parent);
        }
    }

    private static async Task<bool> CheckStartPreview(Node parent, Main main)
    {
        WorldMap map = main.GetNode<WorldMap>("WorldMap");
        Vector2I cell = new(202, 189);
        int money = main.Game.MoneyCents;
        var spaces = main.Game.GetBuildingSpaces();
        int[] raw = FarmGame.Crops.Select(crop => main.Game.GetRawStock(crop.Kind)).ToArray();
        int[] products = FarmGame.Crops.Select(crop => main.Game.GetProductStock(crop.Kind)).ToArray();
        foreach (RoadMaterial material in Enum.GetValues<RoadMaterial>())
        {
            Start(main, material);
            await MoveIntoWindow(parent, main, Screen(map, cell));
            RoadAppearance.RoadTile? preview = map.GetDisplayedRoadAppearance(cell);
            if (preview is not { Added: true } road || road.Material != material ||
                !Find<Label>(main, "BuildHint").Text.Contains("短按选择起点"))
                return Fail("选择道路材质后、第一次点击前未显示该材质单格起点预览");
            if (main.Game.MoneyCents != money || !ReferenceEquals(spaces, main.Game.GetBuildingSpaces()) ||
                main.Game.GetPlot(cell).Building != BuildingKind.None ||
                !raw.SequenceEqual(FarmGame.Crops.Select(crop => main.Game.GetRawStock(crop.Kind))) ||
                !products.SequenceEqual(FarmGame.Crops.Select(crop => main.Game.GetProductStock(crop.Kind))))
                return Fail("未点击的道路起点预览修改金币或正式占用");
        }
        await Capture(parent, main, "start-preview-valid.png");
        // 截图占用来自真实开局设施；此时尚无任何准备性建造，UI与模型同为50金币、0道路。
        await MoveIntoWindow(parent, main, Screen(map, new Vector2I(189, 190)));
        if (!Find<Label>(main, "BuildHint").Text.Contains("已有建筑") ||
            !Find<Label>(main, "BuildCost").Text.Contains("1.00 金币"))
            return Fail("未点击的开局农田占用预览缺少真实占用或费用提示");
        await Capture(parent, main, "start-preview-occupied.png");
        Vector2I next = cell + Vector2I.Right;
        await MoveIntoWindow(parent, main, Screen(map, next));
        if (map.GetDisplayedRoadAppearance(cell) != null ||
            map.GetDisplayedRoadAppearance(next) is not { Added: true, Material: RoadMaterial.Slab })
            return Fail("未点击起点预览未随鼠标移动或残留上一格");

        Vector2I oldRoad = cell + Vector2I.Left;
        Start(main, RoadMaterial.Dirt);
        await NativeClick(parent, main, Screen(map, oldRoad));
        await NativeClick(parent, main, Screen(map, oldRoad));
        if (main.Game.GetBuildingSpace(oldRoad)?.RoadMaterial != RoadMaterial.Dirt)
            return Fail("单格起点连接夹具无法登记旧道路");
        Start(main, RoadMaterial.Slab);
        await MoveIntoWindow(parent, main, Screen(map, cell));
        if (map.GetDisplayedRoadAppearance(cell) is not { Added: true, Mask: 8, Material: RoadMaterial.Slab } ||
            map.GetDisplayedRoadAppearance(oldRoad) is not { Added: false, Mask: 2, Material: RoadMaterial.Dirt })
            return Fail("未点击的单格与相邻异材质旧路未按共同规则预览连接");
        money = main.Game.MoneyCents;
        spaces = main.Game.GetBuildingSpaces();
        await MoveIntoWindow(parent, main, Screen(map, oldRoad));
        if (map.GetDisplayedRoadAppearance(cell) != null || map.GetDisplayedRoadAppearance(oldRoad) is not { Added: false, Mask: 0 } ||
            !Find<Label>(main, "BuildHint").Text.Contains("已有建筑") || !Find<Label>(main, "BuildCost").Text.Contains("1.00 金币"))
            return Fail("未点击的起点占用预览覆盖旧路或缺少占用与标准费用反馈");

        await MoveIntoWindow(parent, main, Screen(map, cell));
        Vector2 ui = Find<Button>(main, "BuildButton").GetGlobalRect().GetCenter();
        Send(main, new InputEventMouseMotion { Position = ui });
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(cell) != null || map.GetDisplayedRoadAppearance(oldRoad)?.Mask != 0)
            return Fail("界面遮挡起点预览后留下候选或旧路临时连接");
        await MoveIntoWindow(parent, main, Screen(map, cell));
        main.GetWindow().EmitSignal(Window.SignalName.MouseExited);
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(cell) != null) return Fail("离窗未隐藏尚未点击的起点预览");
        main.GetWindow().EmitSignal(Window.SignalName.MouseEntered);
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(cell) is not { Added: true }) return Fail("回到地图未恢复单格起点预览");

        Vector2 dragStart = Screen(map, cell);
        Send(main, new InputEventMouseButton
        {
            Position = dragStart,
            ButtonIndex = MouseButton.Middle,
            Pressed = true,
            ButtonMask = MouseButtonMask.Middle
        });
        Send(main, new InputEventMouseMotion
        {
            Position = dragStart + new Vector2(100, 0),
            Relative = new Vector2(100, 0),
            ButtonMask = MouseButtonMask.Middle
        });
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(cell) != null || map.GetDisplayedRoadAppearance(oldRoad)?.Mask != 0)
            return Fail("拖镜头时未隐藏未点击的起点预览或恢复旧路连接");
        Send(main, new InputEventMouseButton
        {
            Position = dragStart + new Vector2(100, 0),
            ButtonIndex = MouseButton.Middle,
            Pressed = false
        });
        await MoveIntoWindow(parent, main, Screen(map, cell));
        if (map.GetDisplayedRoadAppearance(cell) is not { Added: true } ||
            !Find<Label>(main, "BuildHint").Text.Contains("短按选择起点"))
            return Fail("拖镜头松开后未恢复单格预览或意外设定起点");

        var order = main.Game.CreateTradeOrder(new TradeOrderRequest(new CommodityId(CropKind.Radish, CommodityKind.Raw),
            TradeOrderSide.Buy, TradeOrderFrequency.Once, TradeOrderQuantityMode.Fixed, 0,
            TradeOrderBudgetMode.FixedBudget, main.Game.AvailableMoneyCents - 50, 0, CashReserveMode.Amount, 0,
            new IReadOnlyList<TradeOrderCondition>[]
            { new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Less, 1) } }));
        if (!order.Success) return Fail("起点费用反馈冻结资金夹具失败");
        await Frames(parent);
        if (!Find<Label>(main, "BuildCost").Text.Contains("可用") ||
            Find<Label>(main, "BuildCost").GetThemeColor("font_color") == UiElements.Ink ||
            map.GetDisplayedRoadAppearance(cell) is not { Added: true } || main.Game.MoneyCents != money ||
            !ReferenceEquals(spaces, main.Game.GetBuildingSpaces()))
            return Fail("尚未点击时缺少可用资金提示或预览修改正式状态");
        main.Game.CancelTradeOrder(order.Id);

        // 第一次点击仍只锁起点；此前移动不能预先确定本段L顺序。
        CameraController camera = main.GetNode<CameraController>("Camera2D");
        Click(camera, Screen(map, cell));
        if (main.Game.MoneyCents != money || main.Game.GetPlot(cell).Building != BuildingKind.None)
            return Fail("单格起点预览使第一次点击直接建造");
        Click(camera, Screen(map, cell + new Vector2I(2, 1)));
        if (!Find<Label>(main, "MessageLabel").Text.Contains("确定转弯顺序"))
            return Fail("第一点击前的鼠标运动被计入本段轨迹");
        main._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(cell) is not { Added: true, Material: RoadMaterial.Slab } ||
            !Find<Label>(main, "BuildHint").Text.Contains("短按选择起点"))
            return Fail("取消当前段后未恢复所选材质的鼠标单格预览");
        Click(camera, Screen(map, cell));
        Click(camera, Screen(map, cell));
        await MoveIntoWindow(parent, main, Screen(map, next));
        if (main.Game.MoneyCents != money - 100 ||
            map.GetDisplayedRoadAppearance(next) is not { Added: true, Material: RoadMaterial.Slab } ||
            !Find<Label>(main, "BuildHint").Text.Contains("短按选择起点"))
            return Fail("成功提交后未清当前段并恢复单格起点预览");
        Find<Button>(main, "CancelPlacementButton").EmitSignal(Button.SignalName.Pressed);
        if (map.GetDisplayedRoadAppearance(next) != null) return Fail("退出工具后残留单格起点预览");
        Find<Button>(main, "BuildButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadRemovalCard").EmitSignal(Button.SignalName.Pressed);
        await NativeClick(parent, main, Screen(map, oldRoad));
        await NativeClick(parent, main, Screen(map, cell));
        if (main.Game.GetPlot(cell).Building != BuildingKind.None || main.Game.GetPlot(oldRoad).Building != BuildingKind.None ||
            Find<Label>(main, "ActivityLabel").Text.Split('\n')[0] != "道路 0 格" ||
            !Find<Control>(main, "ResourcesPanel").FindChildren("*", "Label", true, false).OfType<Label>()
                .Any(label => label.Text == UiElements.FormatCoins(main.Game.AvailableMoneyCents)))
            return Fail("起点预览夹具清理没有通过真实拆路同步模型和UI");
        return true;
    }

    private static async Task<bool> CheckRemovalStartPreview(Node parent, Main main)
    {
        WorldMap map = main.GetNode<WorldMap>("WorldMap");
        Vector2I left = new(201, 189), middle = new(202, 189), right = new(203, 189);
        Start(main, RoadMaterial.Brick);
        await NativeClick(parent, main, Screen(map, left));
        await NativeClick(parent, main, Screen(map, right));
        StartRemoval(main);
        var spaces = main.Game.GetBuildingSpaces();
        int money = main.Game.MoneyCents;
        uint seconds = main.Game.Calendar.ElapsedSeconds;
        int[] stocks = FarmGame.Crops.SelectMany(crop => new[]
            { main.Game.GetRawStock(crop.Kind), main.Game.GetProductStock(crop.Kind) }).ToArray();
        await MoveIntoWindow(parent, main, Screen(map, middle));
        if (map.GetDisplayedRoadAppearance(middle) is not { Removed: true, Material: RoadMaterial.Brick } ||
            map.GetDisplayedRoadAppearance(left) is not { Removed: false, Mask: 0 } ||
            map.GetDisplayedRoadAppearance(right) is not { Removed: false, Mask: 0 } ||
            !Find<Label>(main, "BuildHint").Text.Contains("短按选择起点"))
            return Fail("拆路首次单格预览没有标记真实道路并预览两侧断开");
        await Capture(parent, main, "removal-start-preview.png");
        foreach (Vector2I cell in new[] { middle + Vector2I.Down,
            spaces.First(space => space.Building == BuildingKind.Farm).WorkCell,
            spaces.First(space => space.Building == BuildingKind.Processor).WorkCell })
        {
            await MoveIntoWindow(parent, main, Screen(map, cell));
            if (map.GetDisplayedRoadAppearance(cell) != null ||
                map.GetDisplayedRoadAppearance(middle) is not { Removed: false, Mask: 10 })
                return Fail("空格或生产设施被拆路预览误标，或移动后旧路未恢复连接");
        }
        await MoveIntoWindow(parent, main, Screen(map, middle));
        Send(main, new InputEventMouseMotion { Position = Find<Button>(main, "BuildButton").GetGlobalRect().GetCenter() });
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(middle) is not { Removed: false, Mask: 10 })
            return Fail("界面遮挡没有清除拆路首次预览");
        await MoveIntoWindow(parent, main, Screen(map, middle));
        main.GetWindow().EmitSignal(Window.SignalName.MouseExited);
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(middle) is not { Removed: false, Mask: 10 })
            return Fail("离窗没有清除拆路首次预览");
        main.GetWindow().EmitSignal(Window.SignalName.MouseEntered);
        await MoveIntoWindow(parent, main, Screen(map, middle));
        if (map.GetDisplayedRoadAppearance(middle) is not { Removed: true })
            return Fail("返回窗口没有恢复拆路单格预览");
        Vector2 point = Screen(map, middle);
        Send(main, new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Middle, Pressed = true });
        Send(main, new InputEventMouseMotion { Position = point + new Vector2(60, 0), Relative = new Vector2(60, 0), ButtonMask = MouseButtonMask.Middle });
        await Frames(parent);
        if (map.GetDisplayedRoadAppearance(middle) is not { Removed: false }) return Fail("拖镜头未隐藏拆路单格预览");
        Send(main, new InputEventMouseButton { Position = point + new Vector2(60, 0), ButtonIndex = MouseButton.Middle, Pressed = false });
        await MoveIntoWindow(parent, main, Screen(map, middle));
        if (!ReferenceEquals(spaces, main.Game.GetBuildingSpaces()) || main.Game.MoneyCents != money ||
            !stocks.SequenceEqual(FarmGame.Crops.SelectMany(crop => new[]
                { main.Game.GetRawStock(crop.Kind), main.Game.GetProductStock(crop.Kind) })) ||
            !main.Game.IsPaused || main.Game.Calendar.ElapsedSeconds != seconds)
            return Fail("拆路首次预览修改正式占用、金币、库存或暂停经营状态");
        await NativeClick(parent, main, Screen(map, middle));
        if (!ReferenceEquals(spaces, main.Game.GetBuildingSpaces())) return Fail("拆路第一次短按直接提交");
        main._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        await Frames(parent);
        if (!Find<Button>(main, "CancelPlacementButton").Visible || map.GetDisplayedRoadAppearance(middle) is not { Removed: true } ||
            !Find<Label>(main, "BuildHint").Text.Contains("短按选择起点"))
            return Fail("取消拆路段没有恢复单格预览");
        await NativeClick(parent, main, Screen(map, middle));
        await NativeClick(parent, main, Screen(map, middle));
        await MoveIntoWindow(parent, main, Screen(map, right));
        if (main.Game.GetPlot(middle).Building != BuildingKind.None ||
            map.GetDisplayedRoadAppearance(right) is not { Removed: true } ||
            !Find<Label>(main, "BuildHint").Text.Contains("短按选择起点") || main.Game.MoneyCents != money)
            return Fail("拆路成功后没有恢复单格预览或发生退款");
        await NativeClick(parent, main, Screen(map, left));
        await NativeClick(parent, main, Screen(map, right));
        Find<Button>(main, "CancelPlacementButton").EmitSignal(Button.SignalName.Pressed);
        return true;
    }

    private static bool CheckGesture()
    {
        var gesture = new RoadSegmentGesture();
        if (gesture.GetCandidate().Cells.Count != 0) return Fail("空段生成了候选");
        Vector2I start = new(20, 20), end = new(23, 24);
        gesture.TrackPointer(end);
        RoadSegmentGesture.Candidate first = gesture.GetCandidate();
        if (first.Started || first.CanSubmit || first.Cells.Count != 1 || first.Cells[0] != end || gesture.HasTurnOrder)
            return Fail("新建首次候选没有集中提供单格或提前锁定轨迹");
        gesture.ClearHover();
        if (gesture.GetCandidate().Cells.Count != 0) return Fail("隐藏首次候选仍保留费用候选");
        gesture.Begin(start);
        gesture.TrackPointer(new Vector2I(23, 20));
        gesture.UpdateEndpoint(end);
        Vector2I[] xPath = gesture.GetCandidate().Cells.ToArray();
        if (xPath.Length != 8 || xPath.Distinct().Count() != 8 || xPath[3] != new Vector2I(23, 20))
            return Fail("先x轴的L形没有保持轨迹或重复计转角");
        gesture.TrackPointer(new Vector2I(20, 24));
        gesture.UpdateEndpoint(end);
        if (!gesture.GetCandidate().Cells.SequenceEqual(xPath)) return Fail("首次明确轴被后续移动改写");
        gesture.Begin(start);
        gesture.TrackPointer(new Vector2I(20, 24));
        gesture.UpdateEndpoint(end);
        if (gesture.GetCandidate().Cells[4] != new Vector2I(20, 24) || gesture.GetCandidate().Cells.SequenceEqual(xPath))
            return Fail("相同端点的先y轴轨迹仍套用先x顺序");
        gesture.Begin(start);
        gesture.UpdateEndpoint(new Vector2I(23, 24));
        if (gesture.HasTurnOrder || gesture.GetCandidate().CanSubmit) return Fail("帧更新或镜头位移擅自确定了轨迹");
        gesture.TrackPointer(new Vector2I(23, 23));
        if (gesture.HasTurnOrder || gesture.GetCandidate().CanSubmit) return Fail("等量对角移动擅自锁定轴");
        var removal = new RoadSegmentGesture(removing: true);
        removal.UpdateEndpoint(start);
        if (removal.GetCandidate().Started || removal.GetCandidate().CanSubmit ||
            removal.GetCandidate().Cells.Count != 1 || removal.GetCandidate().Cells[0] != start || removal.HasTurnOrder)
            return Fail("拆路首次单格预览提前设置起点或拆除轴");
        removal.Begin(start);
        removal.TrackPointer(new Vector2I(22, 25));
        if (removal.GetCandidate().Cells.Count != 6 || removal.GetCandidate().Cells[5] != new Vector2I(20, 25))
            return Fail("拆除未沿主要移动轴投影为直线");
        removal.TrackPointer(new Vector2I(26, 22));
        if (removal.GetCandidate().Cells.Count != 7 || removal.GetCandidate().Cells[6] != new Vector2I(26, 20))
            return Fail("拆除轴没有随真实主要移动方向更新");
        removal.Reset();
        removal.UpdateEndpoint(start);
        if (!removal.IsRemoving || removal.GetCandidate().Started || removal.GetCandidate().CanSubmit ||
            removal.GetCandidate().Cells.Count != 1 || removal.HasTurnOrder)
            return Fail("拆路清段没有恢复独立的单格候选");
        gesture.Begin(start);
        gesture.UpdateEndpoint(new Vector2I(18, 20));
        if (!gesture.GetCandidate().CanSubmit || gesture.GetCandidate().Cells.Count != 3)
            return Fail("反向直线必须先锁轴或遗漏端点");
        gesture.Reset();
        return gesture.StartCell == null && !gesture.HasTurnOrder && gesture.EndCell == default ||
            Fail("重置保留了上一段状态");
    }

    private static async Task<bool> CheckTools(Node parent, Main main)
    {
        WorldMap map = main.GetNode<WorldMap>("WorldMap");
        CameraController camera = main.GetNode<CameraController>("Camera2D");
        uint seconds = main.Game.Calendar.ElapsedSeconds;
        Find<Button>(main, "BuildButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        await Capture(parent, main, "road-material-catalog.png");
        foreach (RoadMaterial material in Enum.GetValues<RoadMaterial>())
        {
            Start(main, material);
            Vector2I cell = new(198 + (int)material, 196);
            await MoveIntoWindow(parent, main, Screen(map, cell));
            int money = main.Game.MoneyCents;
            Click(camera, Screen(map, cell));
            if (main.Game.MoneyCents != money || main.Game.GetPlot(cell).Building != BuildingKind.None)
                return Fail("第一短按提前登记道路或扣费");
            Click(camera, Screen(map, cell));
            if (main.Game.MoneyCents != money - 100 || main.Game.GetBuildingSpace(cell)?.RoadMaterial != material ||
                !Find<Button>(main, "CancelPlacementButton").Visible || Find<Button>(main, "RotatePlacementButton").Visible)
                return Fail("五材质同价建造、工具保留或道路无旋转入口不正确");
        }
        // 相同端点，两种明确轨迹分别提交，建成格必须匹配预览顺序。
        Vector2I start = new(198, 197), end = new(201, 199);
        foreach (bool xFirst in new[] { true, false })
        {
            Start(main, RoadMaterial.Brick);
            Click(camera, Screen(map, start));
            Vector2I corner = xFirst ? new Vector2I(end.X, start.Y) : new Vector2I(start.X, end.Y);
            PointerMoved(camera, Screen(map, corner));
            PointerMoved(camera, Screen(map, end));
            var expected = new RoadSegmentGesture();
            expected.Begin(start);
            expected.TrackPointer(corner);
            expected.UpdateEndpoint(end);
            var cells = expected.GetCandidate().Cells;
            int money = main.Game.MoneyCents;
            Send(main, new InputEventMouseMotion { Position = Screen(map, end) });
            await Frames(parent);
            if (main.Game.MoneyCents != money) return Fail("移动预览提前扣费");
            if (!await CheckActivityLayout(parent, main)) return false;
            if (xFirst) await Capture(parent, main, "road-l-preview.png");
            Click(camera, Screen(map, end));
            if (main.Game.MoneyCents != money - cells.Count * 100 ||
                cells.Any(cell => main.Game.GetBuildingSpace(cell)?.RoadMaterial != RoadMaterial.Brick))
                return Fail("真实L形提交与明确轨迹或整段费用不一致");
            StartRemoval(main);
            Click(camera, Screen(map, start));
            Click(camera, Screen(map, corner));
            Click(camera, Screen(map, corner));
            Click(camera, Screen(map, end));
            if (cells.Any(cell => main.Game.GetPlot(cell).Building != BuildingKind.None) ||
                main.Game.MoneyCents != money - cells.Count * 100)
                return Fail("L形夹具未通过两段正式直线拆路清理或错误退款");
        }
        // 尚未明确轴时只给可调整的提示；随后明确移动可继续同一段。
        Start(main, RoadMaterial.Slab);
        Click(camera, Screen(map, start));
        Click(camera, Screen(map, end));
        if (!Find<Label>(main, "MessageLabel").Text.Contains("确定转弯顺序") || main.Game.GetPlot(start).Building != BuildingKind.None)
            return Fail("未明确顺序的双轴点击被固定顺序提交");
        PointerMoved(camera, Screen(map, new Vector2I(201, 197)));
        Click(camera, Screen(map, end));
        if (main.Game.GetBuildingSpace(start)?.RoadMaterial != RoadMaterial.Slab) return Fail("未锁轴提示清除了起点");
        int retainedMoney = main.Game.MoneyCents;
        Vector2I occupiedStart = new(198, 198);
        Click(camera, Screen(map, occupiedStart));
        PointerMoved(camera, Screen(map, new Vector2I(198, 196)));
        Click(camera, Screen(map, new Vector2I(198, 196)));
        if (main.Game.MoneyCents != retainedMoney || main.Game.GetPlot(occupiedStart).Building != BuildingKind.None ||
            !Find<Label>(main, "MessageLabel").Text.Contains("已有建筑"))
            return Fail("整段冲突留下部分道路或没有明确拒绝");
        // 修改终点后原起点和轴仍存在，不重新选起点。
        Click(camera, Screen(map, occupiedStart));
        if (main.Game.MoneyCents != retainedMoney - 100 || main.Game.GetPlot(occupiedStart).Building != BuildingKind.Road)
            return Fail("失败没有保留当前段供调整");
        // 冻结资金不得支付整段，取消订单后同一段可再次提交。
        var order = main.Game.CreateTradeOrder(new TradeOrderRequest(new CommodityId(CropKind.Radish, CommodityKind.Raw),
            TradeOrderSide.Buy, TradeOrderFrequency.Once, TradeOrderQuantityMode.Fixed, 0,
            TradeOrderBudgetMode.FixedBudget, main.Game.AvailableMoneyCents - 50, 0, CashReserveMode.Amount, 0,
            new IReadOnlyList<TradeOrderCondition>[]
            { new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Less, 1) } }));
        if (!order.Success) return Fail("道路冻结资金夹具失败");
        Vector2I free = new(204, 196);
        Click(camera, Screen(map, free));
        retainedMoney = main.Game.MoneyCents;
        Click(camera, Screen(map, free));
        if (main.Game.MoneyCents != retainedMoney || main.Game.GetPlot(free).Building != BuildingKind.None ||
            !Find<Label>(main, "BuildCost").Text.Contains("可用")) return Fail("整段使用冻结资金或未提示可用金额");
        main.Game.CancelTradeOrder(order.Id);
        Click(camera, Screen(map, free));
        if (main.Game.GetPlot(free).Building != BuildingKind.Road) return Fail("资金失败丢失当前段");
        // 跨空格及生产设施的直线拆除只修改该线道路，不退款。
        Vector2I removalStart = new(187, 189), road = new(198, 189);
        Start(main, RoadMaterial.Gravel);
        Click(camera, Screen(map, removalStart));
        Click(camera, Screen(map, removalStart));
        Click(camera, Screen(map, road));
        Click(camera, Screen(map, road));
        if (main.Game.GetBuildingSpace(removalStart)?.RoadMaterial != RoadMaterial.Gravel ||
            main.Game.GetBuildingSpace(road)?.RoadMaterial != RoadMaterial.Gravel)
            return Fail("投影拆除夹具未通过正式两次短按准备两格道路");
        StartRemoval(main);
        retainedMoney = main.Game.MoneyCents;
        Click(camera, Screen(map, removalStart));
        Vector2I removalPointer = new(199, 190);
        PointerMoved(camera, Screen(map, removalPointer));
        if (!Find<Label>(main, "BuildCost").Text.Contains("待拆道路 2 格")) return Fail("待拆格数没有忽略空地与农田");
        Send(main, new InputEventMouseMotion { Position = Screen(map, removalPointer) });
        if (!await CheckActivityLayout(parent, main)) return false;
        await Capture(parent, main, "road-removal-preview.png");
        Click(camera, Screen(map, removalPointer));
        if (main.Game.GetPlot(removalStart).Building != BuildingKind.None || main.Game.GetPlot(road).Building != BuildingKind.None ||
            main.Game.GetPlot(new Vector2I(189, 189)).Building != BuildingKind.Farm || main.Game.MoneyCents != retainedMoney)
            return Fail("投影拆除误拆生产设施、退款或遗漏非连续道路");
        Click(camera, Screen(map, road));
        Click(camera, Screen(map, road));
        if (!Find<Label>(main, "MessageLabel").Text.Contains("没有可拆除的道路")) return Fail("空拆除没有明确提示");
        return main.Game.IsPaused && main.Game.Calendar.ElapsedSeconds == seconds || Fail("道路工具改变暂停或推进经营");
    }

    private static async Task<bool> CheckInput(Node parent, Main main)
    {
        var map = main.GetNode<WorldMap>("WorldMap");
        var camera = main.GetNode<CameraController>("Camera2D");
        Start(main, RoadMaterial.Cobble);
        Vector2I start = new(205, 191);
        // 前面的预览验收会真实拖动镜头；本用例重新定位，避免继承位移把100像素拖动终点推到窗口外。
        camera.NavigateTo(MapCoordinates.CellToLocalCenter(start));
        await Frames(parent);
        await NativeClick(parent, main, Screen(map, start));
        int money = main.Game.MoneyCents;
        Vector2 point = Screen(map, start);
        RequireMapPoint(main, point);
        RequireMapPoint(main, point + new Vector2(100, 0));
        Send(main, new InputEventMouseButton { Position = point, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
        Send(main, new InputEventMouseMotion { Position = point + new Vector2(100, 0), Relative = new Vector2(100, 0), ButtonMask = MouseButtonMask.Left });
        await Frames(parent);
        if (!camera.IsDragging) return Fail("道路工具抢走左键拖动");
        Send(main, new InputEventMouseButton { Position = point + new Vector2(100, 0), ButtonIndex = MouseButton.Left, Pressed = false });
        await Frames(parent);
        if (camera.IsDragging || main.Game.MoneyCents != money) return Fail("拖动松开提交道路或未结束拖动");
        // 拖动后不发motion，直接点双轴终点，应保持轴未确定。
        Click(camera, Screen(map, start + new Vector2I(2, 2)));
        if (!Find<Label>(main, "MessageLabel").Text.Contains("确定转弯顺序")) return Fail("镜头拖动/松开污染了首次明确轴");
        main._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        if (!Find<Button>(main, "CancelPlacementButton").Visible) return Fail("一级Esc退出了工具");
        main._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        if (Find<Button>(main, "CancelPlacementButton").Visible) return Fail("无段右键未退出工具");
        Start(main, RoadMaterial.Cobble);
        await NativeClick(parent, main, Screen(map, start));
        Find<Button>(main, "CancelPlacementButton").EmitSignal(Button.SignalName.Pressed);
        if (Find<Button>(main, "CancelPlacementButton").Visible || main.Game.MoneyCents != money)
            return Fail("取消按钮未直接退出工具或修改资源");
        Start(main, RoadMaterial.Cobble);
        // 使用没有打开窗口副作用的常驻资源面板，单独验证界面按下、地图松开的归属。
        Control resources = Find<Control>(main, "ResourcesPanel");
        if (!resources.IsVisibleInTree() || resources.MouseFilter == Control.MouseFilterEnum.Ignore)
            return Fail("跨界面输入夹具没有可接收鼠标的资源面板");
        Vector2 ui = resources.GetGlobalRect().GetCenter();
        RequireMapPoint(main, Screen(map, start));
        Send(main, new InputEventMouseMotion { Position = ui });
        Send(main, new InputEventMouseButton { Position = ui, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
        Send(main, new InputEventMouseMotion { Position = Screen(map, start), ButtonMask = MouseButtonMask.Left });
        Send(main, new InputEventMouseButton { Position = Screen(map, start), ButtonIndex = MouseButton.Left, Pressed = false });
        await Frames(parent);
        if (!Find<Label>(main, "BuildHint").Text.Contains("短按选择起点")) return Fail("跨界面输入选择了道路起点");
        Vector2 focusPoint = Screen(map, start);
        Send(main, new InputEventMouseMotion { Position = focusPoint });
        Send(main, new InputEventMouseButton { Position = focusPoint, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
        main.GetWindow().EmitSignal(Window.SignalName.FocusExited);
        Send(main, new InputEventMouseButton { Position = focusPoint, ButtonIndex = MouseButton.Left, Pressed = false });
        AssertRoadInputOwnership(main);
        if (!Find<Label>(main, "BuildHint").Text.Contains("短按选择起点"))
            return Fail("失焦后释放仍被分派为道路起点");
        await NativeClick(parent, main, Screen(map, start));
        main.GetWindow().EmitSignal(Window.SignalName.MouseExited);
        await Frames(parent);
        if (!Find<Label>(main, "BuildHint").Text.Contains("短按终点提交")) return Fail("离窗丢失当前段");
        main.GetWindow().EmitSignal(Window.SignalName.MouseEntered);
        main._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        if (!Find<Button>(main, "CancelPlacementButton").Visible) return Fail("一级右键未保留材质工具");
        await NativeClick(parent, main, Screen(map, start));
        await NativeClick(parent, main, Screen(map, start));
        return main.Game.GetBuildingSpace(start)?.RoadMaterial == RoadMaterial.Cobble && main.Game.MoneyCents == money - 100 ||
            Fail("取消当前段后沿用了旧轨迹或丢失材质");
    }

    private static async Task<bool> CheckOutsideMap(Node parent, Main main)
    {
        var map = main.GetNode<WorldMap>("WorldMap");
        var camera = main.GetNode<CameraController>("Camera2D");
        Vector2I edge = new(383, 191), outside = new(384, 191);
        camera.NavigateTo(MapCoordinates.CellToLocalCenter(edge));
        Start(main, RoadMaterial.Dirt);
        await Frames(parent);
        // CellToLocalCenter 只接收地图内格；沿有效边格与内邻格的屏幕差外推地图外鼠标点。
        Vector2 edgeScreen = Screen(map, edge);
        Vector2 outsideScreen = edgeScreen + (edgeScreen - Screen(map, edge - Vector2I.Right));
        if (map.ScreenToCell(outsideScreen) != outside)
            return Fail("越界道路鼠标夹具未落在预期地图外格");
        int money = main.Game.MoneyCents;
        var spaces = main.Game.GetBuildingSpaces();
        await NativeClick(parent, main, Screen(map, edge));
        await NativeClick(parent, main, outsideScreen);
        if (main.Game.MoneyCents != money || !ReferenceEquals(spaces, main.Game.GetBuildingSpaces()) ||
            !Find<Label>(main, "MessageLabel").Text.Contains("地图外") ||
            !Find<Label>(main, "BuildHint").Text.Contains("短按终点提交"))
            return Fail("地图外道路终点未完整提交拒绝，或失败清除了原起点/修改了资源");
        // 同一个未成功的段允许缩回地图内，随后拆路也必须完整拒绝越界范围。
        await NativeClick(parent, main, Screen(map, edge));
        if (main.Game.GetPlot(edge).Building != BuildingKind.Road || main.Game.MoneyCents != money - 100)
            return Fail("地图外失败没有保留起点供原段调整");
        StartRemoval(main);
        await NativeClick(parent, main, Screen(map, edge));
        await NativeClick(parent, main, outsideScreen);
        if (main.Game.GetPlot(edge).Building != BuildingKind.Road || main.Game.MoneyCents != money - 100 ||
            !Find<Label>(main, "MessageLabel").Text.Contains("地图外"))
            return Fail("地图外拆路未整段拒绝或改变了有效段内道路");
        await NativeClick(parent, main, Screen(map, edge));
        return main.Game.GetPlot(edge).Building == BuildingKind.None && main.Game.MoneyCents == money - 100 ||
            Fail("越界拆除失败后不能调整同一段或发生退款");
    }

    private static void AssertRoadInputOwnership(Main main)
    {
        if (main.GetNode<WorldMap>("WorldMap").SelectedCell != null ||
            Find<Control>(main, "DetailWindow").Visible)
            throw new InvalidOperationException("道路短按错误分派普通选择或弹出设施详情");
    }

    private static void Start(Main main, RoadMaterial material)
    {
        Find<Button>(main, "BuildButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, material == RoadMaterial.Dirt ? "RoadCard" : $"RoadCard{material}").EmitSignal(Button.SignalName.Pressed);
    }

    private static void StartRemoval(Main main)
    {
        Find<Button>(main, "BuildButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "RoadRemovalCard").EmitSignal(Button.SignalName.Pressed);
    }

    private static void Click(CameraController camera, Vector2 position)
    {
        RequireMapPoint(camera.GetParent<Main>(), position);
        if (!camera.IsMouseInsideWindow) throw new InvalidOperationException("道路短按夹具尚未真实移鼠进入窗口");
        TestMapInput.ShortClick(camera, position);
        AssertRoadInputOwnership(camera.GetParent<Main>());
    }

    private static async Task NativeClick(Node parent, Main main, Vector2 position)
    {
        RequireMapPoint(main, position);
        await MoveIntoWindow(parent, main, position);
        Send(main, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left });
        Send(main, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = false });
        AssertRoadInputOwnership(main);
        await Frames(parent);
    }

    private static async Task MoveIntoWindow(Node parent, Main main, Vector2 position)
    {
        RequireMapPoint(main, position);
        if (DisplayServer.GetName() != "headless")
        {
            main.GetWindow().GrabFocus();
            await Frames(parent);
            if (!main.GetWindow().HasFocus())
                throw new InvalidOperationException("道路图形输入夹具未取得真实窗口焦点");
        }
        Send(main, new InputEventMouseMotion { Position = position });
        await Frames(parent);
        if (!main.GetNode<CameraController>("Camera2D").IsMouseInsideWindow)
            throw new InvalidOperationException("原生道路鼠标移动后，窗口尚未收到进入事件");
    }

    private static void Send(Main main, InputEventMouse inputEvent)
    {
        inputEvent.GlobalPosition = inputEvent.Position;
        var transformed = (InputEventMouse)inputEvent.XformedBy(main.GetViewport().GetFinalTransform());
        transformed.GlobalPosition = transformed.Position;
        if (DisplayServer.GetName() != "headless" && inputEvent is InputEventMouseMotion)
            Input.WarpMouse(transformed.Position);
        Input.ParseInputEvent(transformed);
        Input.FlushBufferedEvents();
    }

    private static void PointerMoved(CameraController camera, Vector2 position)
    {
        RequireMapPoint(camera.GetParent<Main>(), position);
        camera._UnhandledInput(new InputEventMouseMotion { Position = position });
    }

    private static void RequireMapPoint(Main main, Vector2 position)
    {
        Rect2 viewport = main.GetViewport().GetVisibleRect();
        if (!viewport.HasPoint(position))
            throw new InvalidOperationException($"道路地图输入夹具位于窗口外：位置{position}，视口{viewport}");
        Control? blocker = FindBlockingUi(main.GetNode<Control>("CanvasLayer/UiRoot"), position);
        if (blocker != null)
            throw new InvalidOperationException($"道路地图输入夹具被界面遮挡：位置{position}，控件{blocker.GetPath()}，范围{blocker.GetGlobalRect()}");
    }

    private static Control? FindBlockingUi(Control control, Vector2 position)
    {
        if (!control.IsVisibleInTree()) return null;
        if (control.MouseFilter != Control.MouseFilterEnum.Ignore)
            return control.GetGlobalRect().HasPoint(position) ? control : null;
        foreach (Control child in control.GetChildren().OfType<Control>().Reverse())
        {
            Control? blocker = FindBlockingUi(child, position);
            if (blocker != null) return blocker;
        }
        return null;
    }

    private static void AssertVisibleResources(Main main, string evidence)
    {
        var spaces = main.Game.GetBuildingSpaces();
        int roads = spaces.Count(space => space.Building == BuildingKind.Road);
        string visibleRoads = Find<Label>(main, "ActivityLabel").Text.Split('\n')[0];
        string expectedCoins = UiElements.FormatCoins(main.Game.AvailableMoneyCents);
        var labels = Find<Control>(main, "ResourcesPanel").FindChildren("*", "Label", true, false).OfType<Label>();
        string expectedTooltip = $"总余额 {UiElements.FormatCoins(main.Game.MoneyCents)} · 冻结 {UiElements.FormatCoins(main.Game.FrozenMoneyCents)}";
        Label activity = Find<Label>(main, "ActivityLabel");
        Label farms = Find<Label>(main, "FarmCountLabel");
        Label processors = Find<Label>(main, "ProcessorCountLabel");
        if (!activity.IsVisibleInTree() || !farms.IsVisibleInTree() || !processors.IsVisibleInTree() ||
            visibleRoads != $"道路 {roads} 格" ||
            !labels.Any(label => label.IsVisibleInTree() && label.Text == expectedCoins && label.TooltipText == expectedTooltip) ||
            farms.Text != $"{spaces.Count(space => space.Building == BuildingKind.Farm)} 块农田" ||
            processors.Text != $"{spaces.Count(space => space.Building == BuildingKind.Processor)} 处加工场地")
            throw new InvalidOperationException($"截图{evidence}可见金币/道路与模型不一致：模型{expectedCoins}金币/{roads}道路，可见{visibleRoads}");
    }

    private static async Task Capture(Node parent, Main main, string name)
    {
        if (DisplayServer.GetName() != "headless")
        {
            await Frames(parent);
            await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        AssertVisibleResources(main, name);
        GD.Print($"截图资源一致：{name}，{UiElements.FormatCoins(main.Game.AvailableMoneyCents)}金币，" +
            $"{main.Game.GetBuildingSpaces().Count(space => space.Building == BuildingKind.Road)}道路");
        if (DisplayServer.GetName() == "headless") return;
        string directory = ProjectSettings.GlobalizePath("res://build/issue143-review-fixes/evidence");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + name);
    }

    private static async Task<bool> CheckActivityLayout(Node parent, Main main)
    {
        Control panel = Find<Control>(main, "MessagePanel");
        foreach (float fontScale in new[] { 1f, 1.2f, 1f })
        {
            UiScaling.SetFontScale(panel, fontScale);
            await Frames(parent);
            Rect2 panelRect = panel.GetGlobalRect();
            foreach (string name in new[] { "BuildHint", "BuildCost", "CancelPlacementButton" })
                if (!panelRect.Encloses(Find<Control>(main, name).GetGlobalRect()))
                    return Fail($"道路提示{name}在字体{fontScale}时被近况面板裁切");
#if DEBUG
            Rect2 developer = Find<Button>(main, "DeveloperToolsButton").GetGlobalRect();
            if (developer.Position.Y < panelRect.End.Y || !main.GetViewport().GetVisibleRect().Encloses(developer))
                return Fail($"开发入口在字体{fontScale}时遮挡道路操作提示或伸出窗口");
#endif
        }
        return true;
    }

    private static Vector2 Screen(WorldMap map, Vector2I cell) => map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);
    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static T Find<T>(Node node, string name) where T : Node => node.FindChild(name, true, false) as T ?? throw new InvalidOperationException(name);
    private static bool Fail(string message) { GD.PushError("道路操作：" + message); return false; }
}
