using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Time;
using FarmExchange.Weather;

namespace FarmExchange.World;

// 只读天气表现：所有粒子由独立的固定视觉哈希派生，不消耗经营随机数。
internal partial class WeatherPresentation : Node2D
{
    private const int MaximumRainSamples = 180;
    private const float MinimumSpacingPixels = 95;
    private static readonly Color CloudShadow = new(.22f, .29f, .38f, .13f);
    private static readonly Color RainShadow = new(.19f, .27f, .38f, .18f);
    private readonly List<(Vector2[] Polygon, WeatherKind Kind)> _shadows = new();
    private readonly List<(Vector2 Landing, float Phase)> _rain = new();
    private IReadOnlyList<WeatherRegionSnapshot>? _regions;
    private Rect2 _view;
    internal double AnimationSeconds { get; private set; }
    internal int VisibleRegionCount => _shadows.Count;
    internal IReadOnlyList<(Vector2 Landing, float Phase)> RainSamples => _rain;

    internal void Update(IReadOnlyList<WeatherRegionSnapshot> regions, Rect2 view,
        double delta, bool paused, double rate)
    {
        bool changed = !ReferenceEquals(regions, _regions) || view != _view;
        if (changed)
        {
            _regions = regions;
            _view = view;
            _shadows.Clear();
            foreach (WeatherRegionSnapshot region in regions)
            {
                Vector2[] outline = MapCoordinates.GridRectangleOutline(region.Cells.Position,
                    region.Cells.Size.X, region.Cells.Size.Y);
                Vector2[] clipped = Clip(outline, view);
                if (clipped.Length >= 3) _shadows.Add((clipped, region.Kind));
            }
        }
        bool hasRain = _shadows.Exists(shadow => shadow.Kind == WeatherKind.Rain);
        if (!paused && hasRain)
            AnimationSeconds += delta * (SimulationDriver.IsPublicRateAllowed(rate) ? rate : 1);
        if (!changed && (paused || !hasRain)) return;
        _rain.Clear();
        if (hasRain) BuildRain(regions, view);
        QueueRedraw();
    }

    private void BuildRain(IReadOnlyList<WeatherRegionSnapshot> regions, Rect2 view)
    {
        float scale = GetGlobalTransformWithCanvas().X.Length();
        float spacing = Math.Max(MinimumSpacingPixels / scale,
            MathF.Sqrt(view.Size.X * view.Size.Y / MaximumRainSamples));
        int firstX = Mathf.FloorToInt(view.Position.X / spacing);
        int firstY = Mathf.FloorToInt(view.Position.Y / spacing);
        int lastX = Mathf.CeilToInt(view.End.X / spacing);
        int lastY = Mathf.CeilToInt(view.End.Y / spacing);
        for (int y = firstY; y < lastY && _rain.Count < MaximumRainSamples; y++)
            for (int x = firstX; x < lastX && _rain.Count < MaximumRainSamples; x++)
            {
                uint hash = Hash(x, y);
                Vector2 landing = new((x + .15f + (hash & 255) / 365f) * spacing,
                    (y + .15f + ((hash >> 8) & 255) / 365f) * spacing);
                if (!view.HasPoint(landing)) continue;
                Vector2I cell = MapCoordinates.LocalPositionToCell(landing);
                foreach (WeatherRegionSnapshot region in regions)
                {
                    if (region.Kind != WeatherKind.Rain || !region.Cells.HasPoint(cell)) continue;
                    float phase = (float)((AnimationSeconds * 2 + ((hash >> 16) & 255) / 255.0) % 1);
                    _rain.Add((landing, phase));
                    break;
                }
            }
    }

    public override void _Draw()
    {
        foreach (var shadow in _shadows)
            DrawColoredPolygon(shadow.Polygon, shadow.Kind == WeatherKind.Rain ? RainShadow : CloudShadow);
        foreach (var drop in _rain)
        {
            Vector2 landing = drop.Landing;
            if (drop.Phase < .8f)
            {
                Vector2 tip = landing + new Vector2(12, -34) * (1 - drop.Phase / .8f);
                Vector2 start = tip + new Vector2(5, -13);
                // 线条端点同样限制在当前真实雨区及可见范围内。
                if (InsideRain(tip) && InsideRain(start))
                    DrawLine(start, tip, new Color(.65f, .77f, .88f, .55f), 1.1f);
            }
            else
            {
                float fade = (1 - drop.Phase) / .2f;
                Vector2 left = landing + new Vector2(-3, 0);
                Vector2 right = landing + new Vector2(3, 0);
                if (InsideRain(left) && InsideRain(right))
                    DrawLine(left, right, new Color(.72f, .83f, .91f, fade * .45f), 1);
            }
        }
    }

    private bool InsideRain(Vector2 point)
    {
        if (!_view.HasPoint(point)) return false;
        Vector2I cell = MapCoordinates.LocalPositionToCell(point);
        foreach (WeatherRegionSnapshot region in _regions!)
            if (region.Kind == WeatherKind.Rain && region.Cells.HasPoint(cell)) return true;
        return false;
    }

    private static uint Hash(int x, int y)
    {
        uint value = unchecked((uint)x * 0x9e3779b9U ^ (uint)y * 0x85ebca6bU ^ 0x34a17d29U);
        value ^= value >> 16;
        value *= 0x7feb352dU;
        return value ^ (value >> 15);
    }

    private static Vector2[] Clip(Vector2[] polygon, Rect2 view)
    {
        var points = new List<Vector2>(polygon);
        for (int edge = 0; edge < 4 && points.Count > 0; edge++)
        {
            var output = new List<Vector2>();
            Vector2 previous = points[^1];
            bool previousInside = IsInside(previous, edge, view);
            foreach (Vector2 current in points)
            {
                bool currentInside = IsInside(current, edge, view);
                if (currentInside != previousInside)
                {
                    bool vertical = edge < 2;
                    float bound = edge switch { 0 => view.Position.X, 1 => view.End.X, 2 => view.Position.Y, _ => view.End.Y };
                    float t = vertical ? (bound - previous.X) / (current.X - previous.X) :
                        (bound - previous.Y) / (current.Y - previous.Y);
                    output.Add(previous.Lerp(current, t));
                }
                if (currentInside) output.Add(current);
                previous = current;
                previousInside = currentInside;
            }
            points = output;
        }
        return points.ToArray();
    }

    private static bool IsInside(Vector2 point, int edge, Rect2 view) => edge switch
    {
        0 => point.X >= view.Position.X,
        1 => point.X <= view.End.X,
        2 => point.Y >= view.Position.Y,
        _ => point.Y <= view.End.Y,
    };
}
