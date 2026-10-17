using System;
using System.Threading.Tasks;
using Godot;
using FarmExchange.World;

// 共享真实输入注入与绘制等待；地图行为仍由正式 Main 和控件处理。
internal static class TestMapInput
{
    internal static T Find<T>(Node root, string name) where T : Node =>
        root.FindChild(name, true, false) as T ?? throw new InvalidOperationException("缺少控件：" + name);

    internal static async Task Frames(Node parent, int count = 3)
    {
        for (int index = 0; index < count; index++)
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    internal static void Send(Node owner, InputEvent input)
    {
        if (input is InputEventMouse mouse)
        {
            mouse.GlobalPosition = mouse.Position;
            input = mouse.XformedBy(owner.GetViewport().GetFinalTransform());
            if (input is InputEventMouseMotion motion && DisplayServer.GetName() != "headless")
                Input.WarpMouse(motion.Position);
        }
        Input.ParseInputEvent(input);
        Input.FlushBufferedEvents();
    }

    internal static void Key(Node owner, Godot.Key key, bool echo = false, bool control = false,
        bool shift = false, bool alt = false, bool meta = false)
    {
        Send(owner, new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = true,
            Echo = echo,
            CtrlPressed = control,
            ShiftPressed = shift,
            AltPressed = alt,
            MetaPressed = meta
        });
        Send(owner, new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = false,
            CtrlPressed = control,
            ShiftPressed = shift,
            AltPressed = alt,
            MetaPressed = meta
        });
    }

    internal static void Move(Node owner, Vector2 position, MouseButtonMask buttons = (MouseButtonMask)0,
        Vector2 relative = default) =>
        Send(owner, new InputEventMouseMotion { Position = position, Relative = relative, ButtonMask = buttons });

    internal static void MouseButtonEvent(Node owner, Vector2 position, MouseButton button, bool pressed) =>
        Send(owner, new InputEventMouseButton { Position = position, ButtonIndex = button, Pressed = pressed });

    internal static void Click(Node owner, Vector2 position)
    {
        Move(owner, position);
        MouseButtonEvent(owner, position, MouseButton.Left, true);
        MouseButtonEvent(owner, position, MouseButton.Left, false);
    }

    // 经营/详情夹具从镜头的已接受输入入口进入；GUI、窗口及拖动门禁另由原生输入用例覆盖。
    // 不直接发送地图选中通知或调用 Main 私有回调，生产与道路使用同一正式模式分派。
    internal static void ShortClickCell(WorldMap map, Vector2I cell) =>
        ShortClick(map.GetNode<CameraController>("../Camera2D"),
            map.GetGlobalTransformWithCanvas() * MapCoordinates.CellToLocalCenter(cell));

    internal static void ShortClick(CameraController camera, Vector2 position)
    {
        camera._UnhandledInput(new InputEventMouseButton
        {
            Position = position,
            ButtonIndex = MouseButton.Left,
            Pressed = true
        });
        camera._UnhandledInput(new InputEventMouseButton
        {
            Position = position,
            ButtonIndex = MouseButton.Left,
            Pressed = false
        });
    }

    internal static void Wheel(Node owner, Vector2 position, bool up)
    {
        Move(owner, position);
        MouseButtonEvent(owner, position, up ? MouseButton.WheelUp : MouseButton.WheelDown, true);
        MouseButtonEvent(owner, position, up ? MouseButton.WheelUp : MouseButton.WheelDown, false);
    }

    internal static async Task Capture(Node owner, string directory, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(owner);
        await owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = ProjectSettings.GlobalizePath("res://" + directory);
        DirAccess.MakeDirRecursiveAbsolute(path);
        if (owner.GetViewport().GetTexture().GetImage().SavePng(path + "/" + name) != Error.Ok)
            throw new InvalidOperationException("截图保存失败：" + name);
    }
}
