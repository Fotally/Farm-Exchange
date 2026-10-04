using System;
using Godot;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class DraggableWindow : PanelContainer
{
    private readonly Vector2 _preferredSize;
    private bool _dragging;
    private Vector2 _dragOffset;

    public VBoxContainer Body { get; }

    public DraggableWindow(string name, string title, Vector2 position, Vector2 size,
        Action? close = null)
    {
        Name = name;
        Position = position;
        Size = size;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        _preferredSize = size;
        Theme = SharedTheme;
        VisibilityChanged += () => { if (!Visible) _dragging = false; };
        StyleBoxFlat frame = Style(Paper, 0);
        frame.SetBorderWidthAll(3);
        AddThemeStyleboxOverride("panel", frame);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 0);
        AddChild(outer);
        var header = new PanelContainer { Name = "Header", CustomMinimumSize = new Vector2(0, 50) };
        header.AddThemeStyleboxOverride("panel", Style(new Color("e6d3a4"), 0));
        header.GuiInput += StartDrag;
        outer.AddChild(header);
        var headerMargin = WrapMargin(header, 16, 9);
        headerMargin.MouseFilter = MouseFilterEnum.Ignore;
        var headerRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        headerMargin.AddChild(headerRow);
        Label titleLabel = MakeLabel(title, 18, Ink);
        titleLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        titleLabel.MouseFilter = MouseFilterEnum.Ignore;
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(titleLabel);
        Button closeButton = MakeQuietButton("×", 32, 32);
        closeButton.Name = "CloseButton";
        closeButton.Pressed += close ?? Hide;
        headerRow.AddChild(closeButton);
        var scroll = new ScrollContainer
        {
            Name = "WindowScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        outer.AddChild(scroll);
        var contentMargin = WrapMargin(scroll, 14, 12);
        contentMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contentMargin.SizeFlagsVertical = SizeFlags.ExpandFill;
        Body = new VBoxContainer { Name = "Body", SizeFlagsVertical = SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 10);
        contentMargin.AddChild(Body);
        Size = size;
    }

    public override void _Ready()
    {
        GetViewport().SizeChanged += ClampToViewport;
        ClampToViewport();
    }

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
        // 所有经营窗口共用上下常驻操作区，Body 的外层滚动接收较小视窗。
        float bottom = viewport.Y - 90f;
        Size = new Vector2(Math.Min(_preferredSize.X, viewport.X - 16f),
            Math.Min(_preferredSize.Y, bottom - 94f));
        float maxX = Math.Max(8f, viewport.X - Size.X - 8f);
        float maxY = Math.Max(94f, bottom - Size.Y);
        Position = new Vector2(Math.Clamp(Position.X, 8f, maxX),
            Math.Clamp(Position.Y, 94f, maxY));
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
