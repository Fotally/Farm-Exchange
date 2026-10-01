using System.Globalization;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestCoreLoop : Node
{
    public override async void _Ready()
    {
        bool passed = RunChecks(this) && await RunLayoutChecks(this);
        if (passed)
            GD.Print("主场景建造与窗口操作检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        bool passed = Check(main);
        main.QueueFree();
        return passed && CheckRainUi(parent) && CheckReserveUi(parent);
    }

    public static async Task<bool> RunLayoutChecks(Node parent)
    {
        var window = new InventoryWindow();
        parent.AddChild(window);
        window.Refresh(new FarmGame(12345));
        window.ShowRaised();
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool passed = true;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            Label label = Find<Label>(window, $"RawReserve{crop.Kind}Label");
            LineEdit input = Find<LineEdit>(window, $"RawReserve{crop.Kind}Input");
            Button apply = Find<Button>(window, $"SetRawReserve{crop.Kind}Button");
            Rect2 labelRect = label.GetGlobalRect();
            Rect2 inputRect = input.GetGlobalRect();
            Rect2 buttonRect = apply.GetGlobalRect();
            if (label.GetLineCount() != 1 || input.Size.Y < 36 || input.Size.Y > 40 ||
                apply.Size.Y < 36 || apply.Size.Y > 40 || labelRect.End.X > inputRect.Position.X ||
                inputRect.End.X > buttonRect.Position.X)
            {
                passed = Fail($"{crop.CropName}保留底线布局发生换行、异常高度或重叠");
                break;
            }
        }
        ScrollContainer scroll = Find<ScrollContainer>(window, "InventoryScroll");
        LineEdit lastInput = Find<LineEdit>(window, "RawReserveRadishInput");
        scroll.EnsureControlVisible(lastInput);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        Rect2 viewportRect = scroll.GetGlobalRect();
        Rect2 lastRect = lastInput.GetGlobalRect();
        if (scroll.ScrollVertical <= 0 || lastRect.Position.Y < viewportRect.Position.Y ||
            lastRect.End.Y > viewportRect.End.Y)
            passed = Fail("库存正常滚动无法完整显示最后一种作物的输入框");
        window.QueueFree();
        return passed;
    }

    private static bool CheckRainUi(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        main.Game.SetFarmCrop(new Vector2I(63, 63), CropKind.Radish);
        main.Game.SetFarmCrop(new Vector2I(65, 63), CropKind.Radish);
        main.Game.AdvanceTick(isRaining: true);
        Vector2I wetFarm = new(65, 63);
        main.GetNode<WorldMap>("WorldMap").EmitSignal(
            WorldMap.SignalName.SelectionChanged, wetFarm);
        Control detail = Find<Control>(main.GetNode<Control>("CanvasLayer/UiRoot"), "DetailWindow");
        bool visible = main.Game.GetFarmDetails(wetFarm).Status ==
            FarmStatus.WaitingForWorkerWithWater &&
            ContainsVisibleText(detail, "待播种 · 已有水分");
        main.QueueFree();
        return visible || Fail("雨后未播种农田的已湿润状态没有显示在详情中");
    }

    private static bool CheckReserveUi(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        bool passed = CheckReserveControls(main);
        main.QueueFree();
        return passed;
    }

    private static bool CheckReserveControls(Main main)
    {
        Vector2I[] initial = { new(63, 63), new(64, 63), new(65, 63), new(63, 64), new(64, 64) };
        foreach (Vector2I cell in initial) main.Game.RemoveBuilding(cell);
        Vector2I farm = new(4, 4);
        Vector2I processor = new(5, 4);
        main.Game.SetRawReserve(CropKind.Radish, 6);
        main.Game.BuildFarm(farm);
        main.Game.SetFarmCrop(farm, CropKind.Radish);
        main.Game.AdvanceTick(isRaining: true);
        for (int i = 0; i < 206; i++) main.Game.AdvanceTick();
        main.Game.RemoveBuilding(farm);
        main.Game.BuildProcessor(processor, CropKind.Radish);

        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        main.GetNode<WorldMap>("WorldMap").EmitSignal(WorldMap.SignalName.SelectionChanged, processor);
        var detail = Find<Control>(ui, "DetailWindow");
        if (!ContainsVisibleText(detail, "等待原料超过保留底线"))
            return Fail("加工详情未显示保留底线等待原因");
        Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
        var window = Find<Control>(ui, "InventoryWindow");
        var input = Find<LineEdit>(window, "RawReserveRadishInput");
        var apply = Find<Button>(window, "SetRawReserveRadishButton");
        var scroll = Find<ScrollContainer>(window, "InventoryScroll");
        var timer = main.GetNode<Timer>("TickTimer");
        if (input.Text != "6")
            return Fail("库存输入未显示经营模块的已提交底线");

        input.GrabFocus();
        EnterText(input, "123");
        input.CaretColumn = 2;
        scroll.ScrollVertical = 20;
        int scrollPosition = scroll.ScrollVertical;
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (input.Text != "123" || !input.HasFocus() || input.CaretColumn != 2 ||
            scroll.ScrollVertical != scrollPosition ||
            !object.ReferenceEquals(input, Find<LineEdit>(window, "RawReserveRadishInput")) ||
            !object.ReferenceEquals(apply, Find<Button>(window, "SetRawReserveRadishButton")) ||
            main.Game.GetRawReserve(CropKind.Radish) != 6)
            return Fail("经营刷新丢失底线输入、光标、焦点、滚动或控件身份");
        input.ReleaseFocus();
        timer.EmitSignal(Timer.SignalName.Timeout);
        Find<Button>(window, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
        if (input.Text != "123")
            return Fail("输入失焦或窗口关闭重开丢失未提交草稿");

        EnterText(input, "5");
        apply.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetRawReserve(CropKind.Radish) != 5 || main.Game.GetRawStock(CropKind.Radish) != 6 ||
            main.Game.GetPlot(processor).RemainingSeconds != 0 ||
            !ContainsVisibleText(detail, "待领取原料"))
            return Fail("设置底线未提交或在领取阶段前启动了加工");
        foreach (string invalid in new[] { "", "-1", "1.5", "2147483648" })
        {
            EnterText(input, invalid);
            apply.EmitSignal(Button.SignalName.Pressed);
            if (main.Game.GetRawReserve(CropKind.Radish) != 5 || main.Game.GetRawStock(CropKind.Radish) != 6 ||
                input.Text != invalid || !ContainsVisibleText(window, "请输入 0 到 2147483647 之间的整数"))
                return Fail("非法底线输入改变了经营状态或丢失错误提示/文本");
        }
        EnterText(input, "5");
        input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text);
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (main.Game.GetRawStock(CropKind.Radish) != 5 || main.Game.GetPlot(processor).RemainingSeconds != 26 ||
            !ContainsVisibleText(detail, "加工中"))
            return Fail("回车提交后下次经营领取未正确启动加工");
        EnterText(input, "99");
        apply.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetRawReserve(CropKind.Radish) != 99 || main.Game.GetRawStock(CropKind.Radish) != 5 ||
            main.Game.GetPlot(processor).RemainingSeconds != 26)
            return Fail("界面提高底线退回了加工投入或重置进度");
        return true;
    }

    private static void EnterText(LineEdit input, string text)
    {
        input.Text = text;
        input.EmitSignal(LineEdit.SignalName.TextChanged, text);
    }

    private static bool Check(Main main)
    {
        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        var map = main.GetNode<WorldMap>("WorldMap");
        var build = Find<Button>(ui, "BuildButton");
        var detail = Find<Control>(ui, "DetailWindow");
        var buildWindow = Find<Control>(ui, "BuildWindow");
        var cropWindow = Find<Control>(ui, "CropWindow");
        var marketWindow = Find<Control>(ui, "MarketWindow");
        var inventoryWindow = Find<Control>(ui, "InventoryWindow");
        var cancel = Find<Button>(ui, "CancelPlacementButton");
        var message = Find<Label>(ui, "MessageLabel");
        var timer = main.GetNode<Timer>("TickTimer");

        if (main.Game.MoneyCents != 5000 || detail.Visible || buildWindow.Visible || cancel.Visible ||
            !ContainsVisibleText(ui, "春 · 第 1 年 · 1 月 · 1 日"))
            return Fail("初始界面或日期显示错误");
        Button pause = Find<Button>(ui, "PauseButton");
        pause.EmitSignal(Button.SignalName.Pressed);
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (main.Game.Calendar.ElapsedSeconds != 0 || pause.Text != "继续")
            return Fail("顶部暂停按钮未停止经营");
        pause.EmitSignal(Button.SignalName.Pressed);
        if (pause.Text != "暂停")
            return Fail("顶部继续按钮未恢复经营");

        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(90, 90));
        if (!detail.Visible || ContainsVisibleText(detail, "生长周期") ||
            ContainsVisibleText(detail, "加工周期") || ContainsVisibleText(detail, "库存"))
            return Fail("空地详情显示了生产或库存信息");

        build.EmitSignal(Button.SignalName.Pressed);
        if (!buildWindow.Visible || detail.Visible)
            return Fail("建造入口未打开目录");
        buildWindow.Position = new Vector2(9999, 9999);
        build.EmitSignal(Button.SignalName.Pressed);
        Vector2 viewportSize = main.GetViewport().GetVisibleRect().Size;
        if (buildWindow.Position.X + buildWindow.Size.X > viewportSize.X - 8f ||
            buildWindow.Position.Y + buildWindow.Size.Y > viewportSize.Y - 111f - 8f)
            return Fail("建造目录超出视窗或遮挡底栏");
        buildWindow.Position = new Vector2(30, 252);
        Find<Button>(buildWindow, "FarmCard").EmitSignal(Button.SignalName.Pressed);
        if (buildWindow.Visible || !cancel.Visible)
            return Fail("选中农田后未进入摆放状态");

        Vector2I farm = new(62, 64);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (main.Game.GetPlot(farm).Building != BuildingKind.Farm ||
            main.Game.MoneyCents != 4000 || !detail.Visible || cancel.Visible ||
            !ContainsVisibleText(detail, "原材料售价：2.50 金币") ||
            !ContainsVisibleText(detail, "生长周期：获得水后 16 天成熟") ||
            !ContainsVisibleText(detail, "原料库存：") ||
            ContainsVisibleText(detail, "加工品库存"))
            return Fail("农田摆放、收费或详情显示错误");

        detail.Position = new Vector2(760, 90);
        var header = Find<Control>(detail, "Header");
        header.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        });
        detail._Input(new InputEventMouseMotion { Position = new Vector2(30, 20) });
        detail._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        Vector2 remembered = detail.Position;
        if (remembered == new Vector2(760, 90))
            return Fail("详情标题栏拖动没有改变窗口位置");
        Find<Button>(detail, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (!detail.Visible || detail.Position != remembered)
            return Fail("详情窗口关闭后未记住位置");

        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        if (!cropWindow.Visible || !Find<Button>(cropWindow, "CropCardCorn").Text.Contains("2.50 金币") ||
            Find<Button>(cropWindow, "CropCardCorn").Text.Contains("玉米粉库存"))
            return Fail("作物菜单没有只显示玉米原料价格与库存");
        cropWindow.Position = new Vector2(400, 120);
        Find<Button>(cropWindow, "CropCardCorn").EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetPlot(farm).CropKind != CropKind.Corn || cropWindow.Visible)
            return Fail("农田改种失败");
        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        if (cropWindow.Position != new Vector2(400, 120))
            return Fail("作物菜单重新打开后未记住位置");
        Find<Button>(cropWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);

        int[] growthDays = { 16, 20, 12, 14, 17, 40, 4 };
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(cropWindow, $"CropCard{crop.Kind}").EmitSignal(Button.SignalName.Pressed);
            if (!ContainsVisibleText(detail, $"生长周期：获得水后 {growthDays[(int)crop.Kind]} 天成熟"))
                return Fail($"{crop.CropName}农田详情未显示正确生长周期");
            if (crop.Kind == CropKind.Sugarcane &&
                !ContainsVisibleText(detail, "当前季节不适宜播种"))
                return Fail("春季选择甘蔗后详情未显示不适季原因");
        }
        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(cropWindow, "CropCardRadish").EmitSignal(Button.SignalName.Pressed);

        build.EmitSignal(Button.SignalName.Pressed);
        Find<Button>(buildWindow, "ProcessorTab").EmitSignal(Button.SignalName.Pressed);
        if (!Find<Button>(buildWindow, "ProcessorCardCorn").Text.Contains("10.00"))
            return Fail("加工场地目录未显示建造费");
        Find<Button>(buildWindow, "ProcessorCardRadish").EmitSignal(Button.SignalName.Pressed);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (!cancel.Visible || main.Game.MoneyCents != 4000 || !message.Text.Contains("已有建筑"))
            return Fail("占用地块仍建造或扣费");
        Vector2I processor = new(66, 64);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, processor);
        if (main.Game.GetPlot(processor).Building != BuildingKind.Processor ||
            main.Game.MoneyCents != 3000 || cancel.Visible ||
            !ContainsVisibleText(detail, "等待萝卜") ||
            !ContainsVisibleText(detail, "加工周期：投入原料后 0.5 天完成") ||
            !ContainsVisibleText(detail, "腌萝卜加工品售价：0.50 金币") ||
            !ContainsVisibleText(detail, "腌萝卜加工品库存：0") ||
            ContainsVisibleText(detail, "生长周期") ||
            ContainsVisibleText(detail, "原料库存") ||
            ContainsVisibleText(detail, "原材料售价"))
            return Fail("加工场地摆放与收费错误");

        Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
        if (!inventoryWindow.Visible || !ContainsVisibleText(inventoryWindow, "玉米原料"))
            return Fail("库存窗口未显示分类库存");
        inventoryWindow.Position = new Vector2(260, 110);
        Find<Button>(inventoryWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
        if (inventoryWindow.Position != new Vector2(260, 110))
            return Fail("库存窗口重新打开后未记住位置");
        build.EmitSignal(Button.SignalName.Pressed);
        if (inventoryWindow.Visible || !buildWindow.Visible)
            return Fail("建造目录打开后库存窗口仍遮挡操作");
        Find<Button>(buildWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);

        bool sawProcessing = false;
        for (int i = 0; i < 250; i++)
        {
            timer.EmitSignal(Timer.SignalName.Timeout);
            sawProcessing |= ContainsVisibleText(detail, "加工中");
        }
        int radishProducts = main.Game.GetProductStock(CropKind.Radish);
        string radishProductPrice = (main.Game.GetProductPriceCents(CropKind.Radish) / 100m)
            .ToString("0.00", CultureInfo.InvariantCulture);
        if (!sawProcessing || radishProducts == 0 ||
            !ContainsVisibleText(detail, $"腌萝卜加工品库存：{radishProducts}") ||
            !ContainsVisibleText(detail, $"腌萝卜加工品售价：{radishProductPrice} 金币"))
            return Fail("萝卜加工场地没有显示随经营更新的售价与库存");
        Find<Button>(ui, "MarketButton").EmitSignal(Button.SignalName.Pressed);
        Button sell = Find<Button>(marketWindow, "SellButton");
        if (!marketWindow.Visible || sell.Disabled || ContainsVisibleText(marketWindow, "待定") ||
            !ContainsVisibleText(marketWindow, "玉米原料"))
            return Fail("市场窗口未区分原料和可售加工品");
        ScrollContainer marketScroll = Find<ScrollContainer>(marketWindow, "MarketScroll");
        Button radishRawButton = Find<Button>(marketWindow, "SellRawRadishButton");
        marketScroll.ScrollVertical = 30;
        int scrollPosition = marketScroll.ScrollVertical;
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (marketScroll.ScrollVertical != scrollPosition ||
            !object.ReferenceEquals(sell, Find<Button>(marketWindow, "SellButton")) ||
            !object.ReferenceEquals(radishRawButton, Find<Button>(marketWindow, "SellRawRadishButton")))
            return Fail("经营刷新重置了市场滚动位置或操作按钮");
        int beforeSale = main.Game.MoneyCents;
        sell.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.MoneyCents <= beforeSale || main.Game.GetProductStock(CropKind.Radish) != 0 ||
            !ContainsVisibleText(detail, "腌萝卜加工品库存：0"))
            return Fail("市场出售没有更新金币与库存");

        CropKind rawKind = CropKind.Radish;
        int rawStock = main.Game.GetRawStock(rawKind);
        if (rawStock == 0)
            return Fail("未加工的原料没有进入市场可售库存");
        Find<Button>(ui, "MarketButton").EmitSignal(Button.SignalName.Pressed);
        Button sellRaw = Find<Button>(marketWindow, $"SellRaw{rawKind}Button");
        if (sellRaw.Disabled || !ContainsVisibleText(marketWindow, $"{FarmGame.GetCrop(rawKind).CropName}原料"))
            return Fail("市场未提供按品种出售原料的入口");
        int rawPrice = main.Game.GetRawPriceCents(rawKind);
        beforeSale = main.Game.MoneyCents;
        sellRaw.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.MoneyCents != beforeSale + rawStock * rawPrice ||
            main.Game.GetRawStock(rawKind) != 0 ||
            !Find<Button>(marketWindow, $"SellRaw{rawKind}Button").Disabled)
            return Fail("市场原料出售没有按当日价格更新金币与按钮状态");
        return true;
    }

    private static T Find<T>(Node parent, string name) where T : Node =>
        parent.FindChild(name, true, false) as T ??
        throw new System.InvalidOperationException($"缺少界面节点：{name}");

    private static bool ContainsVisibleText(Node parent, string text)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is Label label && label.IsVisibleInTree() && label.Text.Contains(text))
                return true;
            if (ContainsVisibleText(child, text))
                return true;
        }
        return false;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
