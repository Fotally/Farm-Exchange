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
        main.SetProcess(false);
        var map = main.GetNode<WorldMap>("WorldMap");
        var camera = main.GetNode<CameraController>("Camera2D");
        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        Vector2 originalCenter = camera.GlobalPosition;
        try
        {
            window.Size = new Vector2I(1920, 1080);
            await Frames(parent);
            if (!await CheckEditingInputAsync(parent, main, camera)) return false;
            foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(3840, 2160), new Vector2I(1280, 720) })
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
            foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440), new Vector2I(3840, 2160), new Vector2I(1280, 720) })
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
            main.SetProcess(false);
            await Frames(parent);
            if (!CheckView(window, main.GetNode<WorldMap>("WorldMap"), main.GetNode<CameraController>("Camera2D"),
                main.GetNode<Control>("CanvasLayer/UiRoot"), 1.25f))
                return Fail("大窗口冷启动没有保持原农田大小");
            GD.Print("镜头尺寸检查：1280兼容/1920基准/2560/3840、往返、玩家缩放、输入与大窗口冷启动通过");
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

    private static async Task<bool> CheckEditingInputAsync(Node parent, Main main, CameraController camera)
    {
        var open = (Button)main.FindChild("CultivationButton", true, false);
        var name = (LineEdit)main.FindChild("CultivationPlanName", true, false);
        var reserve = (LineEdit)main.FindChild("RawReserveWheatInput", true, false);
        Vector2 originalCenter = camera.GlobalPosition;
        if (!await CheckProcessingFramesAsync(parent, main, name, reserve, camera)) return false;
        camera.SetProcess(false);
        try
        {
            open.EmitSignal(Button.SignalName.Pressed);
            name.GrabFocus();
            await Frames(parent);
            SendKey(Key.A, true, control: true);
            SendKey(Key.A, false, control: true);
            SendKey(Key.D, true, 'd');
            await Frames(parent);
            Vector2 before = camera.GlobalPosition;
            camera._Process(0.1);
            Vector2 movement = camera.GlobalPosition - before;
            GD.Print($"镜头编辑输入：年度表名称={name.Text}，焦点={name.HasFocus()}，镜头位移={movement}");
            await SaveEditingScreenshotAsync(parent, "cultivation-name.png");
            if (!name.HasFocus() || name.Text != "d")
                return Fail("真实年度表名称输入没有经 GUI 焦点与按键正常编辑");
            bool passed = true;
            if (!movement.IsEqualApprox(Vector2.Zero))
                passed = Fail($"编辑年度表名称时镜头平移：实际 {movement}，预期 (0, 0)");
            SendKey(Key.D, false);
            passed = await CheckNameKeysAsync(parent, name, camera) && passed;
            ((Button)main.FindChild("InventoryButton", true, false)).EmitSignal(Button.SignalName.Pressed);
            reserve.GrabFocus();
            await Frames(parent);
            SendKey(Key.A, true, control: true);
            SendKey(Key.A, false, control: true);
            SendKey(Key.Key1, true, '1');
            SendKey(Key.Key1, false);
            SendKey(Key.Key2, true, '2');
            SendKey(Key.Key2, false);
            SendKey(Key.Key3, true, '3');
            SendKey(Key.Key3, false);
            SendKey(Key.Left, true);
            await Frames(parent);
            before = camera.GlobalPosition;
            camera._Process(0.1);
            movement = camera.GlobalPosition - before;
            GD.Print($"镜头编辑输入：库存底线={reserve.Text}，光标={reserve.CaretColumn}，焦点={reserve.HasFocus()}，镜头位移={movement}");
            await SaveEditingScreenshotAsync(parent, "inventory-number.png");
            if (!reserve.HasFocus() || reserve.Text != "123" || reserve.CaretColumn != 2)
                return Fail("真实库存底线数字输入没有经 GUI 焦点正常编辑或移动光标");
            if (!movement.IsEqualApprox(Vector2.Zero))
                passed = Fail($"编辑库存底线数字时镜头平移：实际 {movement}，预期 (0, 0)");
            SendKey(Key.Left, false);
            passed = await CheckFocusTransitionsAsync(parent, main, name, reserve, camera) && passed;
            passed = await CheckOtherEditorsAsync(parent, main, camera) && passed;
            if (passed) GD.Print("镜头编辑焦点检查：真实名称/数字输入、WASD/方向键/全选、窗口切换与持键、只读/按钮/TextEdit/SpinBox、鼠标释放及缩放通过");
            return passed;
        }
        finally
        {
            SendKey(Key.A, false);
            SendKey(Key.D, false);
            SendKey(Key.Left, false);
            SendKey(Key.Right, false);
            SendKey(Key.W, false);
            SendKey(Key.S, false);
            name.ReleaseFocus();
            reserve.ReleaseFocus();
            ((Control)main.FindChild("CultivationWindow", true, false)).Hide();
            ((Control)main.FindChild("InventoryWindow", true, false)).Hide();
            camera.GlobalPosition = originalCenter;
            camera.SetProcess(true);
        }
    }

    private static async Task<bool> CheckProcessingFramesAsync(Node parent, Main main, LineEdit name,
        LineEdit reserve, CameraController camera)
    {
        Vector2 originalCenter = camera.GlobalPosition;
        bool originalProcessing = camera.IsProcessing();
        camera.SetProcess(true);
        try
        {
            ((Button)main.FindChild("CultivationButton", true, false)).EmitSignal(Button.SignalName.Pressed);
            name.GrabFocus();
            await Frames(parent);
            SendKey(Key.A, true, control: true);
            SendKey(Key.A, false, control: true);
            Vector2 before = camera.GlobalPosition;
            SendKey(Key.D, true, 'd');
            await Frames(parent);
            Vector2 movement = camera.GlobalPosition - before;
            GD.Print($"镜头真实处理帧：年度表名称={name.Text}，焦点={name.HasFocus()}，自动处理={camera.IsProcessing()}，镜头位移={movement}");
            if (!camera.IsProcessing() || !name.HasFocus() || name.Text != "d")
                return Fail("真实处理帧的名称编辑没有保持相机自动处理及实际 GUI 焦点");
            bool passed = true;
            if (!movement.IsEqualApprox(Vector2.Zero))
                passed = Fail($"真实处理帧编辑年度表名称时镜头移动：实际 {movement}，预期 (0, 0)");

            name.ReleaseFocus();
            before = camera.GlobalPosition;
            await Frames(parent);
            if (camera.GlobalPosition.X <= before.X)
                passed = Fail("释放名称编辑焦点后，仍按住 D 的真实处理帧没有恢复平移");
            SendKey(Key.D, false);
            before = camera.GlobalPosition;
            await Frames(parent);
            if (!camera.GlobalPosition.IsEqualApprox(before))
                passed = Fail("真实处理帧释放 D 后镜头仍在平移");

            ((Button)main.FindChild("InventoryButton", true, false)).EmitSignal(Button.SignalName.Pressed);
            reserve.GrabFocus();
            await Frames(parent);
            SendKey(Key.A, true, control: true);
            SendKey(Key.A, false, control: true);
            foreach ((Key key, char character) in new[] { (Key.Key1, '1'), (Key.Key2, '2'), (Key.Key3, '3') })
            {
                SendKey(key, true, character);
                SendKey(key, false);
            }
            before = camera.GlobalPosition;
            SendKey(Key.Left, true);
            await Frames(parent);
            movement = camera.GlobalPosition - before;
            GD.Print($"镜头真实处理帧：库存底线={reserve.Text}，光标={reserve.CaretColumn}，焦点={reserve.HasFocus()}，自动处理={camera.IsProcessing()}，镜头位移={movement}");
            if (!camera.IsProcessing() || !reserve.HasFocus() || reserve.Text != "123" || reserve.CaretColumn != 2)
                return Fail("真实处理帧的数字编辑没有保持相机自动处理及正常文字光标");
            if (!movement.IsEqualApprox(Vector2.Zero))
                passed = Fail($"真实处理帧编辑库存底线时镜头移动：实际 {movement}，预期 (0, 0)");
            ((Control)main.FindChild("InventoryWindow", true, false)).Hide();
            before = camera.GlobalPosition;
            await Frames(parent);
            if (reserve.HasFocus() || camera.GlobalPosition.X >= before.X)
                passed = Fail("隐藏数字编辑窗口后，仍按住方向键的真实处理帧没有恢复平移");
            SendKey(Key.Left, false);
            before = camera.GlobalPosition;
            await Frames(parent);
            if (!camera.GlobalPosition.IsEqualApprox(before))
                passed = Fail("真实处理帧释放方向键后镜头仍有残留平移");
            return passed;
        }
        finally
        {
            SendKey(Key.A, false);
            SendKey(Key.D, false);
            SendKey(Key.Left, false);
            name.ReleaseFocus();
            reserve.ReleaseFocus();
            ((Control)main.FindChild("CultivationWindow", true, false)).Hide();
            ((Control)main.FindChild("InventoryWindow", true, false)).Hide();
            camera.GlobalPosition = originalCenter;
            camera.SetProcess(originalProcessing);
        }
    }

    private static async Task<bool> CheckOtherEditorsAsync(Node parent, Main main, CameraController camera)
    {
        var editors = new Control { Position = new Vector2(12, 140) };
        var text = new TextEdit { Size = new Vector2(160, 72) };
        var readOnly = new LineEdit { Position = new Vector2(0, 80), Size = new Vector2(160, 36), Editable = false, Text = "只读" };
        var spin = new SpinBox { Position = new Vector2(0, 124), Size = new Vector2(160, 36), MaxValue = 999, Value = 123 };
        editors.AddChild(text);
        editors.AddChild(readOnly);
        editors.AddChild(spin);
        main.GetNode<Control>("CanvasLayer/UiRoot").AddChild(editors);
        try
        {
            await Frames(parent);
            text.GrabFocus();
            if (!(await KeyMovementAsync(parent, camera, Key.D, 'd')).IsEqualApprox(Vector2.Zero) ||
                !text.HasFocus() || text.Text != "d")
                return Fail("可编辑 TextEdit 没有正常接收文字并阻止键盘平移");
            text.Editable = false;
            if (!(await KeyMovementAsync(parent, camera, Key.D, 'd')).IsEqualApprox(new Vector2(56, 0)) ||
                !text.HasFocus() || text.Text != "d")
                return Fail("只读 TextEdit 焦点阻止了地图平移或仍然修改文本");
            readOnly.GrabFocus();
            if (!(await KeyMovementAsync(parent, camera, Key.D, 'd')).IsEqualApprox(new Vector2(56, 0)) ||
                !readOnly.HasFocus() || readOnly.Text != "只读")
                return Fail("只读 LineEdit 焦点阻止了地图平移或仍然修改文本");

            LineEdit spinInput = spin.GetLineEdit();
            spinInput.GrabFocus();
            await Frames(parent);
            if (parent.GetViewport().GuiGetFocusOwner() != spinInput || !spinInput.Editable)
                return Fail("SpinBox 没有将实际编辑焦点交给可编辑子 LineEdit");
            await KeyMovementAsync(parent, camera, Key.End);
            if (!(await KeyMovementAsync(parent, camera, Key.Left)).IsEqualApprox(Vector2.Zero) ||
                spinInput.Text != "123" || spinInput.CaretColumn != 2)
                return Fail("SpinBox 子编辑框的方向键没有只移动数字光标");
            if (!(await KeyMovementAsync(parent, camera, Key.A, control: true)).IsEqualApprox(Vector2.Zero) ||
                spinInput.GetSelectedText() != "123" ||
                !(await KeyMovementAsync(parent, camera, Key.Key9, '9')).IsEqualApprox(Vector2.Zero) || spinInput.Text != "9")
                return Fail("SpinBox 子编辑框全选或数字编辑时镜头移动");
            text.Editable = true;
            text.GrabFocus();
            await Frames(parent);
            return text.HasFocus() && CheckEditingMouseInput(camera);
        }
        finally
        {
            editors.Free();
        }
    }

    private static bool CheckEditingMouseInput(CameraController camera)
    {
        Vector2 point = new(100, 100);
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
        camera._UnhandledInput(new InputEventMouseMotion { Position = point + new Vector2(16, 0), Relative = new Vector2(16, 0) });
        if (!camera.IsDragging) return Fail("编辑焦点错误阻止了原鼠标拖动");
        camera._Process(0);
        Vector2 afterRelease = camera.GlobalPosition;
        camera._UnhandledInput(new InputEventMouseMotion { Position = point + new Vector2(32, 0), Relative = new Vector2(16, 0) });
        if (camera.IsDragging || !camera.GlobalPosition.IsEqualApprox(afterRelease))
            return Fail("编辑焦点使左键物理释放校正被跳过");
        camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true });
        camera._Process(0);
        if (camera.IsDragging) return Fail("编辑焦点使中键物理释放校正被跳过");
        Wheel(camera, MouseButton.WheelUp, 1);
        if (!(camera.Zoom * camera.GetViewport().GetStretchTransform().Scale).IsEqualApprox(new Vector2(1.375f, 1.375f)))
            return Fail("编辑焦点错误阻止了原滚轮缩放");
        Wheel(camera, MouseButton.WheelDown, 1);
        return true;
    }

    private static async Task SaveEditingScreenshotAsync(Node parent, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = ProjectSettings.GlobalizePath("res://build/issue136-validation/" + name);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        parent.GetViewport().GetTexture().GetImage().SavePng(path);
    }

    private static async Task<bool> CheckNameKeysAsync(Node parent, LineEdit name, CameraController camera)
    {
        foreach ((Key key, char character) in new[] { (Key.W, 'w'), (Key.A, 'a'), (Key.S, 's') })
        {
            if (!(await KeyMovementAsync(parent, camera, key, character)).IsEqualApprox(Vector2.Zero))
                return Fail("年度表名称输入 WASD 时镜头移动：" + key);
        }
        if (name.Text != "dwas") return Fail("WASD 没有正常输入年度表名称");
        if (!(await KeyMovementAsync(parent, camera, Key.Left)).IsEqualApprox(Vector2.Zero) || name.CaretColumn != 3 ||
            !(await KeyMovementAsync(parent, camera, Key.Right)).IsEqualApprox(Vector2.Zero) || name.CaretColumn != 4)
            return Fail("年度表名称方向键没有只移动光标");
        if (!(await KeyMovementAsync(parent, camera, Key.A, control: true)).IsEqualApprox(Vector2.Zero) ||
            name.GetSelectedText() != "dwas")
            return Fail("年度表名称 Ctrl+A 没有只选择文本");
        if (!(await KeyMovementAsync(parent, camera, Key.W, 'w')).IsEqualApprox(Vector2.Zero) || name.Text != "w")
            return Fail("年度表名称全选后输入没有正常替换文本");
        return true;
    }

    private static async Task<bool> CheckFocusTransitionsAsync(Node parent, Main main, LineEdit name,
        LineEdit reserve, CameraController camera)
    {
        var cultivation = (Control)main.FindChild("CultivationWindow", true, false);
        var inventory = (Control)main.FindChild("InventoryWindow", true, false);
        ((Button)inventory.FindChild("CloseButton", true, false)).EmitSignal(Button.SignalName.Pressed);
        await Frames(parent);
        if (reserve.HasFocus() || !(await KeyMovementAsync(parent, camera, Key.D)).IsEqualApprox(new Vector2(56, 0)))
            return Fail("关闭数字编辑窗口后键盘平移没有按原速度恢复");
        var button = (Button)main.FindChild("CultivationButton", true, false);
        button.GrabFocus();
        if (!button.HasFocus() || !(await KeyMovementAsync(parent, camera, Key.D)).IsEqualApprox(new Vector2(56, 0)))
            return Fail("普通按钮焦点阻止了地图键盘平移");
        button.ReleaseFocus();

        Vector2 before = camera.GlobalPosition;
        SendKey(Key.D, true);
        await Frames(parent);
        camera._Process(0.1);
        if (!(camera.GlobalPosition - before).IsEqualApprox(new Vector2(56, 0)))
            return Fail("无编辑焦点的持键平移速度不正确");
        button.EmitSignal(Button.SignalName.Pressed);
        name.GrabFocus();
        await Frames(parent);
        before = camera.GlobalPosition;
        camera._Process(0.1);
        if (!name.HasFocus() || !camera.GlobalPosition.IsEqualApprox(before))
            return Fail("按住方向进入年度表编辑后没有立即停止平移");
        ((Button)main.FindChild("InventoryButton", true, false)).EmitSignal(Button.SignalName.Pressed);
        reserve.GrabFocus();
        await Frames(parent);
        camera._Process(0.1);
        if (cultivation.Visible || !reserve.HasFocus() || !camera.GlobalPosition.IsEqualApprox(before))
            return Fail("持键切换实际编辑窗口时镜头产生位移");
        inventory.Hide();
        await Frames(parent);
        camera._Process(0.1);
        if (reserve.HasFocus() || !(camera.GlobalPosition - before).IsEqualApprox(new Vector2(56, 0)))
            return Fail("仍持键时隐藏编辑窗口没有恢复地图平移");
        button.EmitSignal(Button.SignalName.Pressed);
        name.GrabFocus();
        await Frames(parent);
        SendKey(Key.D, false);
        await Frames(parent);
        name.ReleaseFocus();
        before = camera.GlobalPosition;
        camera._Process(0.1);
        if (!camera.GlobalPosition.IsEqualApprox(before))
            return Fail("按键在编辑框中释放后镜头仍有残留平移");
        cultivation.Hide();

        SendKey(Key.D, true);
        SendKey(Key.W, true);
        await Frames(parent);
        before = camera.GlobalPosition;
        camera._Process(0.1);
        SendKey(Key.D, false);
        SendKey(Key.W, false);
        Vector2 diagonal = camera.GlobalPosition - before;
        // 6128 附近的镜头坐标经过等距格换算，保留小于 0.001 世界像素的浮点容差。
        if (diagonal.DistanceTo(new Vector2(39.59798f, -39.59798f)) >= 0.001f)
            return Fail($"退出编辑后对角平移没有保持原方向归一化：实际 {diagonal}");
        return true;
    }

    private static async Task<Vector2> KeyMovementAsync(Node parent, CameraController camera, Key key,
        uint unicode = 0, bool control = false)
    {
        SendKey(key, true, unicode, control);
        try
        {
            await Frames(parent);
            Vector2 before = camera.GlobalPosition;
            camera._Process(0.1);
            return camera.GlobalPosition - before;
        }
        finally
        {
            SendKey(key, false, control: control);
        }
    }

    private static void SendKey(Key key, bool pressed, uint unicode = 0, bool control = false)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Unicode = unicode,
            Pressed = pressed,
            CommandOrControlAutoremap = control,
        });
        Input.FlushBufferedEvents();
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
