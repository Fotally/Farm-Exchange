using System.Threading.Tasks;
using Godot;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestCameraInteraction : Node
{
    public override async void _Ready()
    {
        bool passed = RunChecks(this) && await RunResizeChecksAsync(this);
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

    public static async Task<bool> RunResizeChecksAsync(Node parent)
    {
        Window window = parent.GetTree().Root;
        Vector2I originalSize = window.Size;
        Window.ModeEnum originalMode = window.Mode;
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        window.Mode = Window.ModeEnum.Windowed;
        window.Size = new Vector2I(1280, 720);
        await Frames(parent);
        parent.AddChild(main);
        main.Game.SetPaused(true);
        main.GetNode<Timer>("TickTimer").Stop();
        var map = main.GetNode<WorldMap>("WorldMap");
        var camera = main.GetNode<CameraController>("Camera2D");
        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        Vector2 originalCenter = camera.GlobalPosition;
        try
        {
            foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(1280, 720) })
            {
                window.Size = size;
                await Frames(parent);
                if (!CheckView(window, map, camera, ui, 1.25f) || !camera.GlobalPosition.IsEqualApprox(originalCenter))
                    return Fail("窗口改变后农田大小、视野、界面比例或镜头中心不正确");
                if (!CheckResizeInput(map, camera)) return false;
                camera.GlobalPosition = originalCenter;
            }

            Wheel(camera, MouseButton.WheelUp, 1);
            await Frames(parent);
            foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(1280, 720) })
            {
                window.Size = size;
                await Frames(parent);
                if (!CheckView(window, map, camera, ui, 1.375f))
                    return Fail("窗口变化丢失玩家已选择的镜头倍率");
            }
            window.Size = new Vector2I(1920, 1080);
            await Frames(parent);
            Wheel(camera, MouseButton.WheelUp, 20);
            await Frames(parent);
            if (!CheckView(window, map, camera, ui, 2f)) return Fail("大窗口的最大倍率约束不正确");
            Wheel(camera, MouseButton.WheelDown, 20);
            await Frames(parent);
            if (!CheckView(window, map, camera, ui, 1.25f)) return Fail("大窗口的最小倍率约束不正确");

            if (DisplayServer.GetName() != "headless")
            {
                window.Size = new Vector2I(1280, 720);
                await Frames(parent);
                SaveScreenshot(parent, "map-window-1280.png");
                window.Mode = Window.ModeEnum.Fullscreen;
                await Frames(parent);
                if (!ScreenCellSize(window, map).IsEqualApprox(new Vector2(80, 40)))
                    return Fail("实际全屏后基础格大小变化");
                SaveScreenshot(parent, "map-fullscreen.png");
                window.Mode = Window.ModeEnum.Windowed;
                window.Size = new Vector2I(1920, 1080);
                await Frames(parent);
                SaveScreenshot(parent, "map-window-1920.png");
            }

            main.Free();
            window.Size = new Vector2I(1920, 1080);
            await Frames(parent);
            main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            parent.AddChild(main);
            main.Game.SetPaused(true);
            main.GetNode<Timer>("TickTimer").Stop();
            await Frames(parent);
            if (!CheckView(window, main.GetNode<WorldMap>("WorldMap"), main.GetNode<CameraController>("Camera2D"),
                main.GetNode<Control>("CanvasLayer/UiRoot"), 1.25f))
                return Fail("大窗口冷启动没有保持原农田大小");
            GD.Print("镜头尺寸检查：1280/1920/2560、往返、玩家缩放、输入与大窗口冷启动通过");
            return true;
        }
        finally
        {
            main.Free();
            window.Mode = originalMode;
            window.Size = originalSize;
            await Frames(parent);
        }
    }

    private static bool CheckView(Window window, WorldMap map, CameraController camera, Control ui, float zoom)
    {
        Vector2 expectedCell = new Vector2(64, 32) * zoom;
        Vector2 visibleWorld = window.GetVisibleRect().Size / camera.Zoom;
        Vector2 expectedWorld = (Vector2)window.Size / zoom;
        Transform2D uiTransform = window.GetStretchTransform() * ui.GetGlobalTransformWithCanvas();
        return ScreenCellSize(window, map).IsEqualApprox(expectedCell) && visibleWorld.IsEqualApprox(expectedWorld) &&
            uiTransform.Scale.IsEqualApprox(window.GetStretchTransform().Scale);
    }

    private static Vector2 ScreenCellSize(Window window, WorldMap map)
    {
        Transform2D transform = window.GetStretchTransform() * map.GetGlobalTransformWithCanvas();
        return new Vector2(transform.BasisXform(new Vector2(64, 0)).Length(), transform.BasisXform(new Vector2(0, 32)).Length());
    }

    private static bool CheckResizeInput(WorldMap map, CameraController camera)
    {
        Vector2I cell = new(186, 192);
        Vector2I selected = new(-1, -1);
        void OnSelected(Vector2I value) => selected = value;
        map.SelectionChanged += OnSelected;
        Vector2 viewportPoint = map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell);
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = viewportPoint });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = viewportPoint });
        map.SelectionChanged -= OnSelected;
        if (selected != cell) return Fail("窗口变化后点击未选中对应基础格");

        Vector2 before = camera.Position;
        Vector2 physicalDrag = new(100, 0);
        Vector2 viewportDrag = physicalDrag / camera.GetViewport().GetStretchTransform().Scale;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
        camera._UnhandledInput(new InputEventMouseMotion { Position = viewportPoint + viewportDrag, Relative = viewportDrag });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false });
        if (!(before - camera.Position).IsEqualApprox(new Vector2(80, 0)))
            return Fail("相同屏幕拖动距离在不同窗口大小下不一致");

        before = camera.Position;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, Pressed = true });
        Input.FlushBufferedEvents();
        camera._Process(0.1);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.D, Pressed = false });
        Input.FlushBufferedEvents();
        if (!(camera.Position - before).IsEqualApprox(new Vector2(56, 0)))
            return Fail($"键盘平移速度随窗口大小改变：实际 {camera.Position - before}，预期 (56, 0)");
        return true;
    }

    private static void Wheel(CameraController camera, MouseButton button, int count)
    {
        for (int index = 0; index < count; index++)
            camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true });
    }

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void SaveScreenshot(Node parent, string name)
    {
        string path = ProjectSettings.GlobalizePath("res://coverage/" + name);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        parent.GetViewport().GetTexture().GetImage().SavePng(path);
    }

    private static bool Check(Main main)
    {
        var map = main.GetNode<WorldMap>("WorldMap");
        var camera = main.GetNode<CameraController>("Camera2D");
        var detail = main.GetNode<Control>("CanvasLayer/UiRoot").FindChild("DetailWindow", true, false) as Control;
        if (detail == null)
            return Fail("主场景缺少选中详情窗口");

        if (!(camera.Zoom * camera.GetViewport().GetStretchTransform().Scale).IsEqualApprox(new Vector2(1.25f, 1.25f)))
            return Fail("主场景未使用较近的默认镜头");
        if (!camera.GlobalPosition.IsEqualApprox(map.GetGridWorldPosition(new Vector2(191.5f, 191.5f))))
            return Fail("初始镜头没有位于384地图中心");
        camera._UnhandledInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelDown,
            Pressed = true,
        });
        if (!(camera.Zoom * camera.GetViewport().GetStretchTransform().Scale).IsEqualApprox(new Vector2(1.25f, 1.25f)))
            return Fail("镜头可以拉远超过已确认的地块大小");
        Vector2 firstWorld = MapCoordinates.CellToLocalCenter(new Vector2I(186, 192));
        Vector2 firstScreen = map.GetGlobalTransformWithCanvas() * firstWorld;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = firstScreen });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = firstScreen });
        if (!detail.Visible || !ContainsText(detail, "地块 (186, 192)"))
            return Fail("左键点击未选中地图土地");
        Vector2 cameraBeforeDrag = camera.Position;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = firstScreen });
        camera._UnhandledInput(new InputEventMouseMotion { Position = firstScreen + new Vector2(100f, 0f), Relative = new Vector2(100f, 0f) });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = firstScreen + new Vector2(100f, 0f) });
        if (camera.Position == cameraBeforeDrag || !ContainsText(detail, "地块 (186, 192)"))
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
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(189, 189));
        Vector2 nextClickScreen = map.GetGlobalTransformWithCanvas() * firstWorld;
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = nextClickScreen });
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = nextClickScreen });
        if (!ContainsText(detail, "地块 (186, 192)"))
            return Fail("左键拖动松开后无法再次短按选格");
        Vector2 outsideScreen = map.GetGlobalTransformWithCanvas() * new Vector2(30000f, 30000f);
        map.SelectAtScreenPosition(outsideScreen);
        if (!ContainsText(detail, "地块 (186, 192)"))
            return Fail("地图外的位置仍可被选中");

        map.Position = new Vector2(73f, -41f);
        Vector2I translatedCell = new(186, 192);
        if (map.GetCellWorldCenter(translatedCell) != new Vector2(-119f, 6007f))
            return Fail("平移后的格中心没有应用地图节点变换");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(189, 189));
        Vector2 translatedScreen = map.GetGlobalTransformWithCanvas() *
            MapCoordinates.CellToLocalCenter(translatedCell);
        map.SelectAtScreenPosition(translatedScreen);
        if (!ContainsText(detail, "地块 (186, 192)"))
            return Fail("地图平移后选格与全局格中心不一致");
        camera.GlobalPosition = new Vector2(30073f, 6087f);
        camera._UnhandledInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelUp,
            Pressed = true,
        });
        if (camera.GlobalPosition != new Vector2(12329f, 6087f))
            return Fail("地图平移后镜头没有按全局坐标限制");
        return true;
    }

    private static bool ContainsText(Node parent, string text)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is Label label && label.Text.Contains(text))
                return true;
            if (ContainsText(child, text))
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
