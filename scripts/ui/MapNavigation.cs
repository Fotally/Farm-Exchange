using System;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.World;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>读取本局真实地形与设施，提供常驻小地图和独立浏览的大地图。</summary>
 * <remarks>仅保存地图视图和绘制缓存；主场景负责开关优先级、工具取消和实际镜头定位。</remarks>
 */
public partial class MapNavigation : Control
{
    private readonly MapNavigationDrawing _drawing = new();
    private MapNavigationCanvas _mini = null!;
    private MapNavigationCanvas _atlas = null!;
    private PanelContainer _miniPanel = null!;
    private Control _overlay = null!;
    private Label _zoomLabel = null!;
    private FarmGame? _game;
    private Vector2 _cameraCenter;
    private readonly Vector2[] _cameraCorners = new Vector2[4];
    private MapNavigationCanvas? _gestureView;
    private MouseButton _gestureButton;
    private Vector2 _pressPosition;
    private Vector2 _pressCenter;
    private bool _dragged;

    /**
     * <summary>查询大地图当前是否展开。</summary>
     */
    public bool IsExpanded { get; private set; }
    /**
     * <summary>请求宿主检查优先级并打开大地图。</summary>
     */
    public event Action? OpenRequested;
    /**
     * <summary>请求宿主关闭大地图。</summary>
     */
    public event Action? CloseRequested;
    /**
     * <summary>请求主镜头定位到地图本地坐标，由镜头负责最终边界约束。</summary>
     */
    public event Action<Vector2>? NavigateRequested;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = SharedTheme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildInterface();
        GetWindow().MouseExited += CancelGesture;
        GetWindow().FocusExited += CancelGesture;
        UiScaling.Changed += OnScalingChanged;
    }

    public override void _ExitTree()
    {
        GetWindow().MouseExited -= CancelGesture;
        GetWindow().FocusExited -= CancelGesture;
        UiScaling.Changed -= OnScalingChanged;
        _drawing.Dispose();
    }

    /**
     * <summary>绑定已准备完成的真实局，重置地图浏览状态和缓存。</summary>
     * <param name="game">本局经营入口；调用时控件已进入场景树。</param>
     */
    public void Initialize(FarmGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        _drawing.Initialize(game.Terrain);
        _atlas.ResetView();
        _mini.ResetView();
        UpdateZoomLabel();
        SetExpanded(false);
        RefreshMap();
    }

    /**
     * <summary>由宿主应用开关结果，关闭时终止手势并保留本局查看位置和倍率。</summary>
     * <param name="expanded">是否展开大地图。</param>
     */
    public void SetExpanded(bool expanded)
    {
        CancelGesture();
        IsExpanded = expanded;
        _overlay.Visible = expanded;
        _miniPanel.Visible = !expanded;
    }

    /**
     * <summary>刷新真实镜头中心和完整渲染视口四角，仅在变化时重绘视野框。</summary>
     * <param name="mapLocalCenter">真实镜头中心的地图本地坐标。</param>
     * <param name="mapLocalViewportCorners">按周向排列的四个地图本地坐标；本方法复制数组。</param>
     */
    public void UpdateCameraView(Vector2 mapLocalCenter, Vector2[] mapLocalViewportCorners)
    {
        ArgumentNullException.ThrowIfNull(mapLocalViewportCorners);
        if (mapLocalViewportCorners.Length != 4)
            throw new ArgumentException("视野框必须包含四个角。", nameof(mapLocalViewportCorners));
        bool changed = _cameraCenter != mapLocalCenter;
        _cameraCenter = mapLocalCenter;
        for (int index = 0; index < 4; index++)
        {
            changed |= _cameraCorners[index] != mapLocalViewportCorners[index];
            _cameraCorners[index] = mapLocalViewportCorners[index];
        }
        if (changed)
        {
            _mini.QueueRedraw();
            _atlas.QueueRedraw();
        }
    }

    /**
     * <summary>读取真实空间快照，引用不变时跳过设施缓存更新。</summary>
     */
    public void RefreshMap()
    {
        if (_game != null && _drawing.Refresh(_game.GetBuildingSpaces()))
        {
            _mini.RefreshFacilities();
            _atlas.RefreshFacilities();
        }
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_gestureView == null) return;
        if (inputEvent is InputEventMouseMotion motion)
        {
            ContinueGesture(motion.Position);
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent is InputEventMouseButton button && !button.Pressed && button.ButtonIndex == _gestureButton)
        {
            ContinueGesture(button.Position);
            MapNavigationCanvas view = _gestureView;
            bool navigate = view == _atlas && !_dragged && _gestureButton == MouseButton.Left;
            CancelGesture();
            if (navigate) NavigateAt(view, button.Position - view.GlobalPosition);
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildInterface()
    {
        _miniPanel = new PanelContainer { Name = "MiniMapPanel", MouseFilter = MouseFilterEnum.Stop, GrowVertical = GrowDirection.Begin };
        _miniPanel.AddThemeStyleboxOverride("panel", Frame(Paper, 4));
        AddChild(_miniPanel);
        _miniPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        _miniPanel.OffsetLeft = 32;
        _miniPanel.OffsetRight = 332;
        _miniPanel.OffsetTop = -254;
        _miniPanel.OffsetBottom = -32;
        VBoxContainer miniColumn = new();
        WrapMargin(_miniPanel, 10, 8).AddChild(miniColumn);
        HBoxContainer miniHeader = new();
        miniColumn.AddChild(miniHeader);
        Label miniTitle = MakeLabel("农场地图", 16, Ink);
        miniTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        miniHeader.AddChild(miniTitle);
        AddButton(miniHeader, "ExpandMap", "展开  M", () => OpenRequested?.Invoke());
        _mini = NewCanvas("MiniMap", false);
        _mini.CustomMinimumSize = new Vector2(260, 140);
        miniColumn.AddChild(_mini);

        _overlay = new ColorRect { Name = "ExpandedOverlay", Color = new Color("38452de8"), MouseFilter = MouseFilterEnum.Stop, Visible = false };
        AddChild(_overlay);
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MarginContainer outerMargin = WrapMargin(_overlay, 36, 28);
        outerMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        PanelContainer frame = new();
        frame.AddThemeStyleboxOverride("panel", Frame(Paper, 6));
        outerMargin.AddChild(frame);
        VBoxContainer column = new();
        column.AddThemeConstantOverride("separation", 14);
        WrapMargin(frame, 22, 16).AddChild(column);
        HBoxContainer header = new();
        column.AddChild(header);
        VBoxContainer heading = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(heading);
        heading.AddChild(MakeLabel("农场地图", 28, Ink));
        heading.AddChild(MakeLabel("看见全貌，找到下一片田地。", 14, Muted));
        AddButton(header, "CloseMap", "返回农场  M / Esc  ×", () => CloseRequested?.Invoke());
        HBoxContainer toolbar = new();
        column.AddChild(toolbar);
        Label overview = MakeLabel($"全域概览  ·  {FarmGame.MapSize} × {FarmGame.MapSize} 格", 14, Muted);
        overview.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        toolbar.AddChild(overview);
        AddButton(toolbar, "ZoomOut", "−", () => ChangeZoom(_atlas.ViewZoom / 1.25f, _atlas.Size / 2));
        _zoomLabel = MakeLabel("100%", 16, Ink);
        _zoomLabel.CustomMinimumSize = new Vector2(70, 0);
        _zoomLabel.HorizontalAlignment = HorizontalAlignment.Center;
        toolbar.AddChild(_zoomLabel);
        AddButton(toolbar, "ZoomIn", "+", () => ChangeZoom(_atlas.ViewZoom * 1.25f, _atlas.Size / 2));
        _atlas = NewCanvas("AtlasMap", true);
        _atlas.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(_atlas);
        HBoxContainer footer = new();
        footer.AddThemeConstantOverride("separation", 24);
        column.AddChild(footer);
        HFlowContainer legend = new() { Name = "MapLegend", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        legend.AddThemeConstantOverride("h_separation", 16);
        legend.AddThemeConstantOverride("v_separation", 10);
        footer.AddChild(legend);
        foreach ((string name, Color color) in MapNavigationDrawing.Legend)
        {
            HBoxContainer entry = new();
            entry.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore });
            Label label = MakeLabel(name, 15, Ink);
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            entry.AddChild(label);
            legend.AddChild(entry);
        }
        HBoxContainer actions = new() { Name = "MapViewActions", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        actions.AddThemeConstantOverride("separation", 12);
        footer.AddChild(actions);
        AddButton(actions, "ShowWholeMap", "查看全图", () => { _atlas.ResetView(); UpdateZoomLabel(); });
        AddButton(actions, "LocateCamera", "定位当前镜头", () => _atlas.SetCenter(_cameraCenter));
        column.AddChild(MakeLabel("左键短按定位 · 左键 / 右键拖动浏览 · 滚轮缩放    |    关闭后保留本次查看位置与倍率", 14, Muted));
    }

    private MapNavigationCanvas NewCanvas(string name, bool expanded)
    {
        var canvas = new MapNavigationCanvas(_drawing, _cameraCorners, expanded) { Name = name };
        canvas.GuiInput += input => OnMapInput(canvas, input);
        return canvas;
    }

    private static void AddButton(Control parent, string name, string title, Action action)
    {
        Button button = MakeSecondaryButton(title, 36, 38);
        button.Name = name;
        button.FocusMode = FocusModeEnum.None;
        button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        button.Pressed += action;
        parent.AddChild(button);
    }

    private void OnMapInput(MapNavigationCanvas view, InputEvent input)
    {
        if (input is not InputEventMouseButton button) return;
        view.AcceptEvent();
        if (!button.Pressed) return;
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (view == _atlas && _gestureView == null)
                ChangeZoom(_atlas.ViewZoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.25f : .8f), button.Position);
            return;
        }
        if (button.ButtonIndex != MouseButton.Left && !(view == _atlas && button.ButtonIndex == MouseButton.Right)) return;
        if (!view.ContainsMapPoint(button.Position)) return;
        _gestureView = view;
        _gestureButton = button.ButtonIndex;
        _pressPosition = view.GlobalPosition + button.Position;
        _pressCenter = view.ViewCenter;
        _dragged = false;
        if (view == _mini) NavigateAt(view, button.Position);
    }

    private void ContinueGesture(Vector2 screenPosition)
    {
        MapNavigationCanvas view = _gestureView!;
        Vector2 delta = screenPosition - _pressPosition;
        _dragged |= delta.Length() >= 8;
        if (view == _mini)
            NavigateAt(view, screenPosition - view.GlobalPosition);
        else if (_dragged)
            view.SetCenter(_pressCenter - delta / view.MapScale);
    }

    private void NavigateAt(MapNavigationCanvas view, Vector2 point)
    {
        if (view.ContainsMapPoint(point)) NavigateRequested?.Invoke(view.ToMapLocal(point));
    }

    private void ChangeZoom(float zoom, Vector2 anchor)
    {
        _atlas.ZoomAt(zoom, anchor);
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel() => _zoomLabel.Text = $"{_atlas.ViewZoom * 100:0}%";
    private void CancelGesture() => _gestureView = null;
    private void OnScalingChanged(Control changed)
    {
        _mini.RefreshLayout();
        _atlas.RefreshLayout();
    }
}
