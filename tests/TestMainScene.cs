using Godot;

public partial class TestMainScene : Node
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
        const string actions = "CanvasLayer/ActionsPanel/MarginContainer/Actions/";
        var map = main.GetNode<WorldMap>("WorldMap");
        var camera = main.GetNode<CameraController>("Camera2D");
        var economy = main.GetNode<Label>(actions + "EconomyLabel");
        var plot = main.GetNode<Label>(actions + "PlotLabel");
        var unlock = main.GetNode<Button>(actions + "UnlockButton");
        var build = main.GetNode<Button>(actions + "BuildButton");
        var buildMill = main.GetNode<Button>(actions + "BuildMillButton");
        var sell = main.GetNode<Button>(actions + "SellButton");
        var timer = main.GetNode<Timer>("TickTimer");

        if (!economy.Text.Contains("第 1 天") || !economy.Text.Contains("面粉售价：5.00 金币") ||
            !economy.Text.Contains("金币：0.00") || !economy.Text.Contains("今日涨跌：基准价"))
            return Fail("初始天数、面粉价格或金币未显示");

        if (!camera.Zoom.IsEqualApprox(new Vector2(1.25f, 1.25f)))
            return Fail("主场景未使用较近的默认镜头");
        camera._UnhandledInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelDown,
            Pressed = true,
        });
        if (!camera.Zoom.IsEqualApprox(new Vector2(1.25f, 1.25f)))
            return Fail("镜头可以拉远超过已确认的地块大小");

        Vector2 firstWorld = new(0f, 2048f);
        Vector2 firstScreen = map.GetGlobalTransformWithCanvas() * firstWorld;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = firstScreen });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = firstScreen });
        if (!plot.Text.Contains("未解锁") || unlock.Disabled)
            return Fail("左键点击未选中地图土地");
        Vector2 cameraBeforeDrag = camera.Position;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = firstScreen });
        camera._UnhandledInput(new InputEventMouseMotion { Position = firstScreen + new Vector2(100f, 0f), Relative = new Vector2(100f, 0f) });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = firstScreen + new Vector2(100f, 0f) });
        if (camera.Position == cameraBeforeDrag || !plot.Text.Contains("64, 64"))
            return Fail("左键拖动未移动镜头，或误选其他土地");
        Vector2 outsideScreen = map.GetGlobalTransformWithCanvas() * new Vector2(9000f, 9000f);
        map.SelectAtScreenPosition(outsideScreen);
        if (!plot.Text.Contains("64, 64"))
            return Fail("地图外的位置仍可被选中");
        unlock.EmitSignal(Button.SignalName.Pressed);
        if (!economy.Text.Contains("免费土地：1") || build.Disabled)
            return Fail("免费解锁未更新界面");
        build.EmitSignal(Button.SignalName.Pressed);
        if (!plot.Text.Contains("农田"))
            return Fail("建造农田未更新界面");

        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(65, 64));
        if (unlock.Disabled)
            return Fail("第二格土地不可解锁");
        unlock.EmitSignal(Button.SignalName.Pressed);
        if (buildMill.Disabled)
            return Fail("磨坊建造按钮不可用");
        buildMill.EmitSignal(Button.SignalName.Pressed);
        if (!plot.Text.Contains("磨坊"))
            return Fail("磨坊状态未显示");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(64, 64));
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (!plot.Text.Contains("待浇水"))
            return Fail("工人未自动播种或界面未更新");
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (!plot.Text.Contains("生长中"))
            return Fail("工人未自动浇水或界面未更新");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(65, 64));
        for (int i = 0; i < FarmGame.GrowthTicks; i++)
            timer.EmitSignal(Timer.SignalName.Timeout);
        if (!economy.Text.Contains("小麦：0") || !economy.Text.Contains("面粉：0") || !sell.Disabled ||
            !plot.Text.Contains("加工中"))
            return Fail("小麦未自动进入磨坊，或原料可以直接出售");
        for (int i = 0; i < FarmGame.MillingTicks; i++)
            timer.EmitSignal(Timer.SignalName.Timeout);
        if (!economy.Text.Contains("第 2 天") || !economy.Text.Contains("今日涨跌：") ||
            !(economy.Text.Contains("（小幅）") || economy.Text.Contains("（中幅）") || economy.Text.Contains("（大幅）")) ||
            !economy.Text.Contains("面粉：1") || sell.Disabled)
            return Fail("磨坊未产出可出售面粉");
        sell.EmitSignal(Button.SignalName.Pressed);
        if (economy.Text.Contains("金币：0.00") || !economy.Text.Contains("面粉：0"))
            return Fail("商店出售未更新界面");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
