using System;
using Godot;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class DraggableWindow : PanelContainer
{
    private readonly bool _avoidBottomBar;
    private bool _dragging;
    private Vector2 _dragOffset;

    public VBoxContainer Body { get; }

    public DraggableWindow(string name, string title, Vector2 position, Vector2 size,
        bool avoidBottomBar = false, Action? close = null)
    {
        Name = name;
        Position = position;
        Size = size;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        _avoidBottomBar = avoidBottomBar;
        VisibilityChanged += () => { if (!Visible) _dragging = false; };
        AddThemeStyleboxOverride("panel", Style(Cream, 16));

        var outer = new VBoxContainer();
        AddChild(outer);
        var header = new PanelContainer { Name = "Header", CustomMinimumSize = new Vector2(0, 58) };
        header.AddThemeStyleboxOverride("panel", Style(Dark, 14));
        header.GuiInput += StartDrag;
        outer.AddChild(header);
        var headerMargin = WrapMargin(header, 16, 9);
        headerMargin.MouseFilter = MouseFilterEnum.Ignore;
        var headerRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        headerMargin.AddChild(headerRow);
        Label titleLabel = MakeLabel(title, 19, Cream);
        titleLabel.MouseFilter = MouseFilterEnum.Ignore;
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(titleLabel);
        Button closeButton = MakeButton("×", Mid, 32, 32);
        closeButton.Name = "CloseButton";
        closeButton.Pressed += close ?? Hide;
        headerRow.AddChild(closeButton);
        var contentMargin = WrapMargin(outer, 16, 14);
        contentMargin.SizeFlagsVertical = SizeFlags.ExpandFill;
        Body = new VBoxContainer { Name = "Body", SizeFlagsVertical = SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 10);
        contentMargin.AddChild(Body);
        Size = size;
    }

    public override void _Ready() => GetViewport().SizeChanged += ClampToViewport;

    public override void _ExitTree() => GetViewport().SizeChanged -= ClampToViewport;

    public override void _Input(InputEvent inputEvent)
    {
        if (!_dragging)
            return;
        if (inputEvent is InputEventMouseMotion motion)
        {
            Position = motion.Position + _dragOffset;
            ClampToViewport();
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent is InputEventMouseButton mouse &&
                 mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed)
        {
            _dragging = false;
            GetViewport().SetInputAsHandled();
        }
    }

    public void ShowRaised()
    {
        Show();
        GetParent().MoveChild(this, GetParent().GetChildCount() - 1);
        ClampToViewport();
    }

    public void ClampToViewport()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float bottom = _avoidBottomBar ? viewport.Y - 111f : viewport.Y;
        float maxX = Math.Max(8f, viewport.X - Size.X - 8f);
        float maxY = Math.Max(78f, bottom - Size.Y - 8f);
        Position = new Vector2(Math.Clamp(Position.X, 8f, maxX),
            Math.Clamp(Position.Y, 78f, maxY));
    }

    private void StartDrag(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            return;
        _dragging = true;
        _dragOffset = Position - GetViewport().GetMousePosition();
        GetParent().MoveChild(this, GetParent().GetChildCount() - 1);
        GetViewport().SetInputAsHandled();
    }
}
