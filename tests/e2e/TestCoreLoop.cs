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
            GD.Print("主场景操作检查通过");
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
        const string actions = "CanvasLayer/ActionsPanel/MarginContainer/ScrollContainer/Actions/";
        var map = main.GetNode<WorldMap>("WorldMap");
        var economy = main.GetNode<Label>(actions + "EconomyLabel");
        var plot = main.GetNode<Label>(actions + "PlotLabel");
        var unlock = main.GetNode<Button>(actions + "UnlockButton");
        var build = main.GetNode<Button>(actions + "BuildButton");
        var buildProcessor = main.GetNode<Button>(actions + "BuildProcessorButton");
        var cropOption = main.GetNode<OptionButton>(actions + "CropOption");
        var processorOption = main.GetNode<OptionButton>(actions + "ProcessorOption");
        var remove = main.GetNode<Button>(actions + "RemoveButton");
        var sell = main.GetNode<Button>(actions + "SellButton");
        var timer = main.GetNode<Timer>("TickTimer");

        if (!economy.Text.Contains("第 1 天") || !economy.Text.Contains("面粉售价：5.00 金币") ||
            !economy.Text.Contains("金币：50.00") || !economy.Text.Contains("今日涨跌：基准价"))
            return Fail("初始天数、面粉价格或金币未显示");

        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(63, 63));
        if (!plot.Text.Contains("农田") || !unlock.Disabled)
            return Fail("主场景未显示中心预置农田");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(63, 64));
        if (!plot.Text.Contains("等待") || !unlock.Disabled)
            return Fail("主场景未显示中心预置加工场地");
        Vector2I[] initialBuildings =
        {
            new(63, 63), new(64, 63), new(65, 63), new(63, 64), new(64, 64),
        };
        foreach (Vector2I cell in initialBuildings)
        {
            map.EmitSignal(WorldMap.SignalName.SelectionChanged, cell);
            remove.EmitSignal(Button.SignalName.Pressed);
        }

        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(62, 64));
        if (!plot.Text.Contains("未解锁") || unlock.Disabled)
            return Fail("未解锁土地没有正确显示或无法解锁");
        unlock.EmitSignal(Button.SignalName.Pressed);
        if (!economy.Text.Contains("免费土地：1") || build.Disabled)
            return Fail("免费解锁未更新界面");
        build.EmitSignal(Button.SignalName.Pressed);
        if (!plot.Text.Contains("农田"))
            return Fail("建造农田未更新界面");
        if (cropOption.Disabled || cropOption.ItemCount != FarmGame.Crops.Count)
            return Fail("农田未提供六种作物选择");
        cropOption.Select((int)CropKind.Corn);
        cropOption.EmitSignal(OptionButton.SignalName.ItemSelected, (long)CropKind.Corn);
        if (!plot.Text.Contains("玉米"))
            return Fail("农田作物切换未更新界面");

        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(65, 64));
        if (unlock.Disabled)
            return Fail("第二格土地不可解锁");
        unlock.EmitSignal(Button.SignalName.Pressed);
        if (buildProcessor.Disabled || processorOption.Disabled ||
            processorOption.ItemCount != FarmGame.Crops.Count)
            return Fail("六种加工场地不可选");
        processorOption.Select((int)CropKind.Corn);
        buildProcessor.EmitSignal(Button.SignalName.Pressed);
        if (!plot.Text.Contains("玉米加工坊"))
            return Fail("玉米加工坊状态未显示");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(62, 64));
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (!plot.Text.Contains("待浇水"))
            return Fail("工人未自动播种或界面未更新");
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (!plot.Text.Contains("生长中"))
            return Fail("工人未自动浇水或界面未更新");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(65, 64));
        for (int i = 0; i < FarmGame.GetCrop(CropKind.Corn).GrowthTicks; i++)
            timer.EmitSignal(Timer.SignalName.Timeout);
        if (!economy.Text.Contains("玉米：0 → 玉米粉：0") || !sell.Disabled ||
            !plot.Text.Contains("加工中"))
            return Fail("玉米未自动进入对应场地，或原料可以直接出售");
        for (int i = 0; i < FarmGame.GetCrop(CropKind.Corn).ProcessingTicks; i++)
            timer.EmitSignal(Timer.SignalName.Timeout);
        if (!economy.Text.Contains("第 2 天") || !economy.Text.Contains("今日涨跌：") ||
            !(economy.Text.Contains("（小幅）") || economy.Text.Contains("（中幅）") || economy.Text.Contains("（大幅）")) ||
            !economy.Text.Contains("玉米粉：1") || sell.Disabled)
            return Fail("玉米加工坊未产出可出售玉米粉");
        sell.EmitSignal(Button.SignalName.Pressed);
        if (economy.Text.Contains("金币：50.00") || !economy.Text.Contains("玉米粉：0"))
            return Fail("商店出售未更新界面");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
