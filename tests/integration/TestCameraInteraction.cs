using Godot;

public partial class TestCameraInteraction : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks(this);
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        bool passed = Check(main);
        main.Free();
        return passed;
    }

    private static bool Check(Main main)
    {
        const string actions = "CanvasLayer/ActionsPanel/MarginContainer/ScrollContainer/Actions/";
        var map = main.GetNode<WorldMap>("WorldMap");
        var camera = main.GetNode<CameraController>("Camera2D");
        var plot = main.GetNode<Label>(actions + "PlotLabel");
        var unlock = main.GetNode<Button>(actions + "UnlockButton");

        if (!camera.Zoom.IsEqualApprox(new Vector2(1.25f, 1.25f)))
            return Fail("主场景未使用较近的默认镜头");
        camera._UnhandledInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelDown,
            Pressed = true,
        });
        if (!camera.Zoom.IsEqualApprox(new Vector2(1.25f, 1.25f)))
            return Fail("镜头可以拉远超过已确认的地块大小");
        Vector2 firstWorld = new(-64f, 2016f);
        Vector2 firstScreen = map.GetGlobalTransformWithCanvas() * firstWorld;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = firstScreen });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = firstScreen });
        if (!plot.Text.Contains("未解锁") || unlock.Disabled)
            return Fail("左键点击未选中地图土地");
        Vector2 cameraBeforeDrag = camera.Position;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = firstScreen });
        camera._UnhandledInput(new InputEventMouseMotion { Position = firstScreen + new Vector2(100f, 0f), Relative = new Vector2(100f, 0f) });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = firstScreen + new Vector2(100f, 0f) });
        if (camera.Position == cameraBeforeDrag || !plot.Text.Contains("62, 64"))
            return Fail("左键拖动未移动镜头，或误选其他土地");
        Vector2 cameraAfterRelease = camera.Position;
        camera._UnhandledInput(new InputEventMouseMotion { Position = firstScreen + new Vector2(120f, 0f), Relative = new Vector2(20f, 0f) });
        if (camera.Position != cameraAfterRelease)
            return Fail("松开左键后移动鼠标仍会拖动镜头");
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = firstScreen });
        camera._UnhandledInput(new InputEventMouseMotion { Position = firstScreen + new Vector2(100f, 0f), Relative = new Vector2(100f, 0f) });
        camera._Process(0);
        Vector2 cameraAfterMissedRelease = camera.Position;
        camera._UnhandledInput(new InputEventMouseMotion { Position = firstScreen + new Vector2(120f, 0f), Relative = new Vector2(20f, 0f) });
        if (camera.Position != cameraAfterMissedRelease)
            return Fail("左键释放事件未传入镜头时仍会拖动镜头");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(63, 63));
        Vector2 nextClickScreen = map.GetGlobalTransformWithCanvas() * firstWorld;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = nextClickScreen });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = nextClickScreen });
        if (!plot.Text.Contains("62, 64"))
            return Fail("左键拖动松开后无法再次短按选格");
        Vector2 outsideScreen = map.GetGlobalTransformWithCanvas() * new Vector2(9000f, 9000f);
        map.SelectAtScreenPosition(outsideScreen);
        if (!plot.Text.Contains("62, 64"))
            return Fail("地图外的位置仍可被选中");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
