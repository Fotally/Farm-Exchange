using System;
using Godot;

namespace FarmExchange.World;

public partial class CameraController : Camera2D
{
    private const float PanSpeed = 700f;
    private const float MinZoom = 1.25f;
    private const float MaxZoom = 2f;
    private const float ClickDistance = 8f;

    private WorldMap _worldMap = null!;
    private float _viewZoom;
    private bool _middleDragging;
    private bool _leftPressed;
    private bool _leftDragging;
    private Vector2 _leftPressPosition;
    private bool _inputEnabled = true;

    internal float PlayerZoom => _viewZoom;

    /**
     * <summary>查询左键或中键是否正在平移镜头。</summary>
     */
    public bool IsDragging => _leftDragging || _middleDragging;

    /**
     * <summary>查询鼠标是否仍在游戏窗口内。</summary>
     */
    public bool IsMouseInsideWindow { get; private set; } = true;

    /**
     * <summary>通知已通过界面与拖动检查的地图左键短按。</summary>
     * <remarks>参数为逻辑视口像素，包含地图外位置；由宿主唯一分派当前模式意图，镜头不执行选格。</remarks>
     */
    public event Action<Vector2>? MapShortClicked;

    /**
     * <summary>通知未按住鼠标且未拖动时的真实地图指针移动。</summary>
     * <remarks>参数为逻辑视口像素；镜头、缩放、窗口更新与释放不会产生本通知。</remarks>
     */
    public event Action<Vector2>? MapPointerMoved;

    public override void _Ready()
    {
        _worldMap = GetNode<WorldMap>("../WorldMap");
        _viewZoom = Zoom.X;
        GetViewport().SizeChanged += ApplyViewportScale;
        GetWindow().MouseEntered += OnMouseEntered;
        GetWindow().MouseExited += OnMouseExited;
        GetWindow().FocusExited += EndPointerGesture;
        ApplyViewportScale();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ApplyViewportScale;
        GetWindow().MouseEntered -= OnMouseEntered;
        GetWindow().MouseExited -= OnMouseExited;
        GetWindow().FocusExited -= EndPointerGesture;
    }

    public override void _Process(double delta)
    {
        if (!_inputEnabled) return;
        if (_leftPressed && !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _leftPressed = false;
            _leftDragging = false;
        }
        if (_middleDragging && !Input.IsMouseButtonPressed(MouseButton.Middle))
            _middleDragging = false;
        if (HasEditableKeyboardFocus())
            return;
        Vector2 direction = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
            direction.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
            direction.X += 1f;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
            direction.Y -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
            direction.Y += 1f;
        if (direction != Vector2.Zero)
        {
            Position += direction.Normalized() * PanSpeed * (float)delta / _viewZoom;
            ClampPosition();
        }
    }

    private bool HasEditableKeyboardFocus() => GetViewport().GuiGetFocusOwner() switch
    {
        LineEdit lineEdit => lineEdit.Editable,
        TextEdit textEdit => textEdit.Editable,
        _ => false,
    };

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton mouse)
            return;
        if (mouse.ButtonIndex == MouseButton.Left &&
            (mouse.Pressed || GetViewport().GuiGetHoveredControl() != null))
        {
            // 每次原始按下先清旧意图，只有未被界面接收的按下才能由 _UnhandledInput 重新登记。
            _leftPressed = false;
            _leftDragging = false;
        }
        if (mouse.ButtonIndex == MouseButton.Middle && !mouse.Pressed)
            _middleDragging = false;
    }

    private void OnMouseEntered() => IsMouseInsideWindow = true;

    private void OnMouseExited()
    {
        IsMouseInsideWindow = false;
        EndPointerGesture();
    }

    private void EndPointerGesture()
    {
        _leftPressed = false;
        _leftDragging = false;
        _middleDragging = false;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!_inputEnabled) return;
        if (inputEvent is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.Pressed)
                {
                    _leftPressed = true;
                    _leftDragging = false;
                    _leftPressPosition = mouse.Position;
                }
                else
                {
                    if (_leftPressed && !_leftDragging && mouse.Position.DistanceTo(_leftPressPosition) < ClickDistance)
                        MapShortClicked?.Invoke(mouse.Position);
                    _leftPressed = false;
                    _leftDragging = false;
                }
                GetViewport().SetInputAsHandled();
            }
            else if (mouse.ButtonIndex == MouseButton.Middle)
            {
                _middleDragging = mouse.Pressed;
                GetViewport().SetInputAsHandled();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp)
            {
                SetZoom(_viewZoom * 1.1f);
                GetViewport().SetInputAsHandled();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown)
            {
                SetZoom(_viewZoom / 1.1f);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (inputEvent is InputEventMouseMotion motion)
        {
            if (_leftPressed && !_leftDragging && motion.Position.DistanceTo(_leftPressPosition) >= ClickDistance)
                _leftDragging = true;
            if (_leftDragging || _middleDragging)
            {
                Position -= motion.Relative / Zoom;
                ClampPosition();
                GetViewport().SetInputAsHandled();
            }
            else if (!_leftPressed)
                MapPointerMoved?.Invoke(motion.Position);
        }
    }

    /**
     * <summary>启停主镜头键鼠输入，并结束之前的指针手势。</summary>
     * <param name="enabled">是否允许主地图平移、缩放与短按选择。</param>
     * <remarks>不修改镜头位置、玩家倍率或经营时间；关闭输入时仍允许显式导航定位。</remarks>
     */
    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        EndPointerGesture();
    }

    /**
     * <summary>将主镜头定位到地图本地位置，并沿用地图边界限制。</summary>
     * <param name="mapLocalPosition">WorldMap 本地坐标，单位为世界像素。</param>
     * <remarks>保留玩家倍率，不选择地块或执行经营命令。</remarks>
     */
    public void NavigateTo(Vector2 mapLocalPosition)
    {
        EndPointerGesture();
        GlobalPosition = _worldMap.ToGlobal(mapLocalPosition);
        ClampPosition();
        ForceUpdateScroll();
    }

    private void SetZoom(float value)
    {
        _viewZoom = Math.Clamp(value, MinZoom, MaxZoom);
        ApplyViewportScale();
    }

    private void ApplyViewportScale()
    {
        Vector2 stretch = GetViewport().GetStretchTransform().Scale;
        Zoom = new Vector2(_viewZoom, _viewZoom) / stretch;
        ClampPosition();
    }

    private void ClampPosition()
    {
        GlobalPosition = _worldMap.ClampGlobalCameraCenter(GlobalPosition);
    }
}
