using Godot;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestCoreLoop : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks(this);
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
        return passed;
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
            HasVisibleInternalTime(ui))
            return Fail("初始界面显示错误或暴露内部时间");

        build.EmitSignal(Button.SignalName.Pressed);
        if (!buildWindow.Visible || detail.Visible)
            return Fail("建造入口未打开目录");
        Find<Button>(buildWindow, "FarmCard").EmitSignal(Button.SignalName.Pressed);
        if (buildWindow.Visible || !cancel.Visible)
            return Fail("选中农田后未进入摆放状态");

        Vector2I farm = new(62, 64);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (main.Game.GetPlot(farm).Building != BuildingKind.Farm ||
            main.Game.MoneyCents != 4000 || !detail.Visible || cancel.Visible ||
            !ContainsVisibleText(detail, "原材料售价：2.50 金币") ||
            !ContainsVisibleText(detail, "生长周期：浇水后 5 秒成熟") ||
            !ContainsVisibleText(detail, "原料库存："))
            return Fail("农田摆放、收费或详情显示错误");

        detail.Position = new Vector2(760, 90);
        var header = Find<Control>(detail, "Header");
        header.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        });
        main._Input(new InputEventMouseMotion { Position = new Vector2(30, 20) });
        main._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        Vector2 remembered = detail.Position;
        if (remembered == new Vector2(760, 90))
            return Fail("详情标题栏拖动没有改变窗口位置");
        Find<Button>(detail, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (!detail.Visible || detail.Position != remembered)
            return Fail("详情窗口关闭后未记住位置");

        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        if (!cropWindow.Visible || !Find<Button>(cropWindow, "CropCardCorn").Text.Contains("2.50 金币"))
            return Fail("作物菜单未显示玉米原料首日售价");
        cropWindow.Position = new Vector2(400, 120);
        Find<Button>(cropWindow, "CropCardCorn").EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetPlot(farm).CropKind != CropKind.Corn || cropWindow.Visible)
            return Fail("农田改种失败");
        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        if (cropWindow.Position != new Vector2(400, 120))
            return Fail("作物菜单重新打开后未记住位置");
        Find<Button>(cropWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);

        int[] growthSeconds = { 5, 6, 7, 6, 9, 10 };
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(cropWindow, $"CropCard{crop.Kind}").EmitSignal(Button.SignalName.Pressed);
            if (!ContainsVisibleText(detail, $"生长周期：浇水后 {growthSeconds[(int)crop.Kind]} 秒成熟"))
                return Fail($"{crop.CropName}农田详情未显示正确生长周期");
        }
        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(cropWindow, "CropCardCorn").EmitSignal(Button.SignalName.Pressed);

        build.EmitSignal(Button.SignalName.Pressed);
        Find<Button>(buildWindow, "ProcessorTab").EmitSignal(Button.SignalName.Pressed);
        if (!Find<Button>(buildWindow, "ProcessorCardCorn").Text.Contains("10.00"))
            return Fail("加工场地目录未显示建造费");
        Find<Button>(buildWindow, "ProcessorCardCorn").EmitSignal(Button.SignalName.Pressed);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (!cancel.Visible || main.Game.MoneyCents != 4000 || !message.Text.Contains("已有建筑"))
            return Fail("占用地块仍建造或扣费");
        Vector2I processor = new(66, 64);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, processor);
        if (main.Game.GetPlot(processor).Building != BuildingKind.Processor ||
            main.Game.MoneyCents != 3000 || cancel.Visible ||
            ContainsVisibleText(detail, "生长周期"))
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

        for (int i = 0; i < 60; i++)
            timer.EmitSignal(Timer.SignalName.Timeout);
        if (main.Game.GetProductStock(CropKind.Corn) == 0)
            return Fail("新建玉米加工场地没有自动产出");
        Find<Button>(ui, "MarketButton").EmitSignal(Button.SignalName.Pressed);
        Button sell = Find<Button>(marketWindow, "SellButton");
        if (!marketWindow.Visible || sell.Disabled || ContainsVisibleText(marketWindow, "待定") ||
            !ContainsVisibleText(marketWindow, "玉米原料"))
            return Fail("市场窗口未区分原料和可售加工品");
        int beforeSale = main.Game.MoneyCents;
        sell.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.MoneyCents <= beforeSale || main.Game.GetProductStock(CropKind.Corn) != 0)
            return Fail("市场出售没有更新金币与库存");

        CropKind rawKind = CropKind.Wheat;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            if (crop.Kind == CropKind.Corn ||
                crop.Kind == main.Game.GetPlot(new Vector2I(63, 64)).CropKind ||
                crop.Kind == main.Game.GetPlot(new Vector2I(64, 64)).CropKind)
                continue;
            rawKind = crop.Kind;
            break;
        }
        Find<Button>(marketWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(cropWindow, $"CropCard{rawKind}").EmitSignal(Button.SignalName.Pressed);
        for (int i = 0; i < 30; i++)
            timer.EmitSignal(Timer.SignalName.Timeout);
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

    private static bool HasVisibleInternalTime(Node parent) =>
        ContainsVisibleText(parent, "tick") || ContainsVisibleText(parent, "第 1 天");

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
