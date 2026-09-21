using System;
using Godot;

public partial class CameraController : Camera2D
{
    private const float PanSpeed = 700f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 2f;
    private const float ClickDistance = 8f;

    private WorldMap _worldMap = null!;
    private bool _middleDragging;
    private bool _leftPressed;
    private bool _leftDragging;
    private Vector2 _leftPressPosition;

    public override void _Ready()
    {
        _worldMap = GetNode<WorldMap>("../WorldMap");
    }

    public override void _Process(double delta)
    {
        if (_leftPressed && !Input.IsMouseButtonPressed(MouseButton.Left))
            _leftPressed = false;
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
            Position += direction.Normalized() * PanSpeed * (float)delta / Zoom.X;
            ClampPosition();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
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
                else if (_leftPressed)
                {
                    if (!_leftDragging && mouse.Position.DistanceTo(_leftPressPosition) < ClickDistance)
                        _worldMap.SelectAtScreenPosition(mouse.Position);
                    _leftPressed = false;
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
                SetZoom(Zoom.X * 1.1f);
                GetViewport().SetInputAsHandled();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown)
            {
                SetZoom(Zoom.X / 1.1f);
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
        }
    }

    private void SetZoom(float value)
    {
        float amount = Math.Clamp(value, MinZoom, MaxZoom);
        Zoom = new Vector2(amount, amount);
        ClampPosition();
    }

    private void ClampPosition()
    {
        Position = _worldMap.ClampCameraCenter(Position);
    }
}
