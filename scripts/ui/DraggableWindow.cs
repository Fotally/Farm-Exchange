using System;
using Godot;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class DraggableWindow : PanelContainer
{
    private readonly Vector2 _preferredSize;
    private readonly Label _titleLabel;
    private bool _dragging;
    private Vector2 _dragOffset;
    private int _initialAlignment;
    private bool _userMoved;

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
        AddThemeStyleboxOverride("panel", Frame(Paper, 5));

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 0);
        AddFrameLining(this).AddChild(outer);
        bool detail = name == "DetailWindow";
        var header = new PanelContainer { Name = "Header", CustomMinimumSize = new Vector2(0, detail ? 84 : 64) };
        header.AddThemeStyleboxOverride("panel", Style(new Color("e6d3a4"), 0));
        header.GuiInput += StartDrag;
        outer.AddChild(header);
        var headerMargin = WrapMargin(header, 17, 10);
        headerMargin.MouseFilter = MouseFilterEnum.Ignore;
        var headerRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        headerMargin.AddChild(headerRow);
        var titles = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 4);
        headerRow.AddChild(titles);
        Label category = MakeLabel(detail ? "FIELD NOTES" : "FARM EXCHANGE", detail ? 12 : 9, Muted);
        category.MouseFilter = MouseFilterEnum.Ignore;
        titles.AddChild(category);
        _titleLabel = MakeLabel(title, detail ? 24 : 19, Ink);
        _titleLabel.Name = "WindowTitleLabel";
        _titleLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        _titleLabel.MouseFilter = MouseFilterEnum.Ignore;
        _titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titles.AddChild(_titleLabel);
        Button closeButton = MakeQuietButton("", detail ? 34 : 28, detail ? 34 : 28);
        closeButton.Icon = UiIcons.Texture(UiIcon.Close);
        closeButton.ExpandIcon = true;
        closeButton.AddThemeConstantOverride("icon_max_width", detail ? 22 : 18);
        closeButton.AddThemeColorOverride("icon_normal_color", Ink);
        closeButton.AddThemeColorOverride("icon_hover_color", Ink);
        StyleBoxFlat closeStyle = Style(Paper, 0);
        closeStyle.ContentMarginLeft = 2;
        closeStyle.ContentMarginRight = 2;
        closeStyle.ContentMarginTop = 2;
        closeStyle.ContentMarginBottom = 2;
        closeButton.AddThemeStyleboxOverride("normal", closeStyle);
        closeButton.AddThemeStyleboxOverride("hover", closeStyle);
        closeButton.TooltipText = "关闭窗口";
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
        var contentMargin = WrapMargin(scroll, 17, 17);
        contentMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contentMargin.SizeFlagsVertical = SizeFlags.ExpandFill;
        Body = new VBoxContainer { Name = "Body", SizeFlagsVertical = SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 10);
        contentMargin.AddChild(Body);
        Size = size;
    }

    public override void _Ready()
    {
        UiScaling.Bind(this);
        UiScaling.Changed += OnScaleChanged;
        GetViewport().SizeChanged += OnViewportChanged;
        OnViewportChanged();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= OnViewportChanged;
        UiScaling.Changed -= OnScaleChanged;
    }

    internal void SetTitle(string title) => _titleLabel.Text = title;

    internal void SetInitialPlacement(bool right) => _initialAlignment = right ? 2 : 1;

    private void OnViewportChanged()
    {
        ClampToViewport();
        if (!_userMoved && _initialAlignment != 0)
        {
            Vector2 viewport = GetViewport().GetVisibleRect().Size;
            float scale = UiScaling.OverallScale(this);
            Position = _initialAlignment == 2
                ? new Vector2(viewport.X - Size.X - 31f * scale, 143f * scale)
                : new Vector2((viewport.X - Size.X) / 2f, (viewport.Y - Size.Y) / 2f - 20f * scale);
            ClampToViewport();
        }
    }

    private void OnScaleChanged(Control changed)
    {
        if (changed == this || changed.IsAncestorOf(this)) OnViewportChanged();
    }

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
        float scale = UiScaling.OverallScale(this);
        float top = 143f * scale;
        float bottom = viewport.Y - 180f * scale;
        Size = new Vector2(Math.Min(_preferredSize.X * scale, viewport.X - 62f * scale),
            Math.Min(_preferredSize.Y * scale, bottom - top));
        float maxX = Math.Max(31f * scale, viewport.X - Size.X - 31f * scale);
        float maxY = Math.Max(top, bottom - Size.Y);
        Position = new Vector2(Math.Clamp(Position.X, 31f * scale, maxX),
            Math.Clamp(Position.Y, top, maxY));
    }

    private void StartDrag(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            return;
        _dragging = true;
        _userMoved = true;
        _dragOffset = Position - GetViewport().GetMousePosition();
        GetParent().MoveChild(this, GetParent().GetChildCount() - 1);
        GetViewport().SetInputAsHandled();
    }
}
