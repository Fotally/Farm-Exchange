using System;
using System.Threading.Tasks;
using Godot;

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
        Send(owner, new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true,
            Echo = echo, CtrlPressed = control, ShiftPressed = shift, AltPressed = alt, MetaPressed = meta });
        Send(owner, new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false,
            CtrlPressed = control, ShiftPressed = shift, AltPressed = alt, MetaPressed = meta });
    }

    internal static void Move(Node owner, Vector2 position, MouseButtonMask buttons = (MouseButtonMask)0) =>
        Send(owner, new InputEventMouseMotion { Position = position, ButtonMask = buttons });

    internal static void MouseButtonEvent(Node owner, Vector2 position, MouseButton button, bool pressed) =>
        Send(owner, new InputEventMouseButton { Position = position, ButtonIndex = button, Pressed = pressed });

    internal static void Click(Node owner, Vector2 position)
    {
        Move(owner, position);
        MouseButtonEvent(owner, position, MouseButton.Left, true);
        MouseButtonEvent(owner, position, MouseButton.Left, false);
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
