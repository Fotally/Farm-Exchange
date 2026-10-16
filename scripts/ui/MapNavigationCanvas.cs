using System;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.World;

namespace FarmExchange.UI;

// 两个视图共用真实绘图快照；只有大图拥有可变化的浏览位置与倍率。
internal partial class MapNavigationCanvas : Control
{
    private readonly MapNavigationDrawing _drawing;
    private readonly Vector2[] _cameraCorners;
    private readonly bool _expanded;
    private Node2D _content = null!;
    private ArrayMesh? _facilities;
    private float _meshScale;
    internal ReadOnlySpan<Vector2> CameraCorners => _cameraCorners;
    internal float ViewZoom { get; private set; } = 1;
    internal Vector2 ViewCenter { get; private set; } = MapNavigationDrawing.Center;
    internal float MapScale => Math.Max(.0001f, Math.Min((Size.X - 24 * UiScaling.OverallScale(this)) / MapNavigationDrawing.Bounds.Size.X,
        (Size.Y - 24 * UiScaling.OverallScale(this)) / MapNavigationDrawing.Bounds.Size.Y)) * ViewZoom;

    internal MapNavigationCanvas(MapNavigationDrawing drawing, Vector2[] cameraCorners, bool expanded)
    {
        _drawing = drawing;
        _cameraCorners = cameraCorners;
        _expanded = expanded;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public override void _Ready()
    {
        _content = new Node2D { Name = "CachedMap", TextureFilter = TextureFilterEnum.Nearest };
        AddChild(_content);
        _content.ShowBehindParent = true;
        _content.Draw += DrawContent;
        Resized += RefreshLayout;
        RefreshLayout();
    }

    public override void _ExitTree() => _facilities?.Dispose();

    internal void ResetView()
    {
        ViewZoom = 1;
        ViewCenter = MapNavigationDrawing.Center;
        RefreshLayout();
    }

    internal void SetCenter(Vector2 center)
    {
        ViewCenter = MapCoordinates.ClampLocalCenter(center);
        RefreshLayout();
    }

    internal void ZoomAt(float zoom, Vector2 anchor)
    {
        Vector2 before = ToMapLocal(anchor);
        ViewZoom = Math.Clamp(zoom, 1, 12);
        ViewCenter = MapCoordinates.ClampLocalCenter(before - (anchor - Size / 2) / MapScale);
        RefreshLayout();
    }

    internal Vector2 ToMapLocal(Vector2 point) => ViewCenter + (point - Size / 2) / MapScale;
    private Vector2 FromMapLocal(Vector2 point) => Size / 2 + (point - ViewCenter) * MapScale;

    internal bool ContainsMapPoint(Vector2 point)
    {
        Vector2 local = ToMapLocal(point);
        float x = local.X / 32, y = local.Y / 16;
        float col = (x + y) / 2, row = (y - x) / 2;
        return col >= -.5f && row >= -.5f && col <= FarmGame.MapSize - .5f && row <= FarmGame.MapSize - .5f;
    }

    internal void RefreshLayout()
    {
        if (_content == null) return;
        _content.Position = Size / 2 - ViewCenter * MapScale;
        _content.Scale = Vector2.One * MapScale;
        if (!Mathf.IsEqualApprox(_meshScale, MapScale)) RefreshFacilities();
        QueueRedraw();
    }

    internal void RefreshFacilities()
    {
        if (_content == null) return;
        _facilities?.Dispose();
        _meshScale = MapScale;
        _facilities = _drawing.BuildFacilities(MapScale);
        _content.QueueRedraw();
    }

    private void DrawContent()
    {
        if (_drawing.TerrainTexture == null) return;
        // 正方形采样纹理按 MapCoordinates 的同一等距基向量变换。
        _content.DrawSetTransformMatrix(new Transform2D(new Vector2(32, 16), new Vector2(-32, 16),
            MapCoordinates.GridPositionToLocal(new Vector2(-.5f, -.5f))));
        _content.DrawTextureRect(_drawing.TerrainTexture, new Rect2(Vector2.Zero, Vector2.One * FarmGame.MapSize), false);
        _content.DrawSetTransformMatrix(Transform2D.Identity);
        if (_facilities != null && _facilities.GetSurfaceCount() > 0) _content.DrawMesh(_facilities, null);
    }

    public override void _Draw()
    {
        Vector2[] edge = MapCoordinates.GridRectangleOutline(Vector2I.Zero, FarmGame.MapSize, FarmGame.MapSize);
        var outline = new Vector2[5];
        var view = new Vector2[5];
        for (int index = 0; index < 4; index++)
        {
            outline[index] = FromMapLocal(edge[index]);
            view[index] = FromMapLocal(_cameraCorners[index]);
        }
        outline[4] = outline[0];
        view[4] = view[0];
        float overall = UiScaling.OverallScale(this);
        DrawPolyline(outline, new Color("78895b"), Math.Max(1, overall), true);
        DrawPolyline(view, UiElements.Dark, 4 * overall, true);
        DrawPolyline(view, UiElements.Cream, 2 * overall, true);
        if (_expanded)
        {
            Vector2 center = Vector2.Zero;
            foreach (Vector2 corner in _cameraCorners) center += corner / 4;
            DrawCircle(FromMapLocal(center), 2.5f * overall, UiElements.Cream);
        }
    }
}
