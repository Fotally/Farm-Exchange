using System;
using System.Collections.Generic;
using System.Linq;

namespace FarmExchange.Terrain;

internal static class TerrainGeometry
{
    internal static TerrainNumber Cross(TerrainPoint a, TerrainPoint b) => a.ExactX * b.ExactY - a.ExactY * b.ExactX;
    internal static TerrainNumber Turn(TerrainPoint a, TerrainPoint b, TerrainPoint c) => Cross(TerrainPoint.Subtract(b, a), TerrainPoint.Subtract(c, a));
    internal static TerrainNumber Dot(TerrainPoint a, TerrainPoint b) => a.ExactX * b.ExactX + a.ExactY * b.ExactY;
    internal static TerrainNumber LengthSquared(TerrainPoint a) => Dot(a, a);
    internal static TerrainNumber PointDistanceSquared(TerrainPoint p, TerrainPoint a, TerrainPoint b)
    {
        TerrainPoint delta = TerrainPoint.Subtract(b, a), relative = TerrainPoint.Subtract(p, a);
        TerrainNumber projection = Dot(relative, delta), length = LengthSquared(delta);
        if (length == 0 || projection <= 0) return LengthSquared(relative);
        if (projection >= length) return LengthSquared(TerrainPoint.Subtract(p, b));
        TerrainNumber cross = Cross(delta, relative);
        return cross * cross / length;
    }
    internal static bool SegmentsIntersect(TerrainPoint a, TerrainPoint b, TerrainPoint c, TerrainPoint d)
    {
        TerrainNumber aa = Turn(a, b, c), ab = Turn(a, b, d), ba = Turn(c, d, a), bb = Turn(c, d, b);
        if (aa == 0 && OnSegment(c, a, b) || ab == 0 && OnSegment(d, a, b) || ba == 0 && OnSegment(a, c, d) || bb == 0 && OnSegment(b, c, d)) return true;
        return (aa < 0 && ab > 0 || aa > 0 && ab < 0) && (ba < 0 && bb > 0 || ba > 0 && bb < 0);
    }
    private static bool OnSegment(TerrainPoint p, TerrainPoint a, TerrainPoint b) => p.ExactX >= TerrainNumber.Min(a.ExactX, b.ExactX) && p.ExactX <= TerrainNumber.Max(a.ExactX, b.ExactX) && p.ExactY >= TerrainNumber.Min(a.ExactY, b.ExactY) && p.ExactY <= TerrainNumber.Max(a.ExactY, b.ExactY);
    internal static TerrainNumber SegmentDistanceSquared(TerrainPoint a, TerrainPoint b, TerrainPoint c, TerrainPoint d)
    {
        if (SegmentsIntersect(a, b, c, d)) return 0;
        return TerrainNumber.Min(TerrainNumber.Min(PointDistanceSquared(a, c, d), PointDistanceSquared(b, c, d)), TerrainNumber.Min(PointDistanceSquared(c, a, b), PointDistanceSquared(d, a, b)));
    }
    internal static TerrainNumber TwiceArea(IReadOnlyList<TerrainPoint> polygon)
    {
        TerrainNumber sum = 0;
        for (int i = 0; i < polygon.Count; i++) sum += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        return sum;
    }
    internal static TerrainPoint[] ConvexHull(IEnumerable<TerrainPoint> input)
    {
        TerrainPoint[] points = input.Distinct().OrderBy(p => p.ExactX).ThenBy(p => p.ExactY).ToArray();
        if (points.Length < 3) return points;
        var hull = new List<TerrainPoint>();
        foreach (TerrainPoint point in points)
        {
            while (hull.Count >= 2 && Turn(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        int lower = hull.Count;
        for (int i = points.Length - 2; i >= 0; i--)
        {
            while (hull.Count > lower && Turn(hull[^2], hull[^1], points[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(points[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }
    internal static TerrainPoint[] Envelope(IReadOnlyList<TerrainPoint> polygon, TerrainNumber radius)
    {
        TerrainPoint[] corners = { new(-radius, -radius), new(radius, -radius), new(radius, radius), new(-radius, radius) };
        return ConvexHull(ConvexHull(polygon).SelectMany(p => corners.Select(c => TerrainPoint.Add(p, c))));
    }
    internal static bool ConvexPolygonsTouch(IReadOnlyList<TerrainPoint> a, IReadOnlyList<TerrainPoint> b)
    {
        return !Separated(a, b) && !Separated(b, a);
        static bool Separated(IReadOnlyList<TerrainPoint> axes, IReadOnlyList<TerrainPoint> other)
        {
            for (int i = 0; i < axes.Count; i++)
                if (other.All(p => Turn(axes[i], axes[(i + 1) % axes.Count], p) < 0)) return true;
            return false;
        }
    }
}
