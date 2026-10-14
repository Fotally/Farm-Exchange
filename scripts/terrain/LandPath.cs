using System;
using System.Collections.Generic;
using Godot;

namespace FarmExchange.Terrain;

/**
 * <summary>完整不可变陆地路线，统一累计最大轴差长度与位置采样。</summary>
 */
public sealed class LandPath : IEquatable<LandPath>
{
    private readonly LandPosition[] _points;
    private readonly double[] _distances;
    public IReadOnlyList<LandPosition> Points { get; }
    public double TotalLength => _distances[^1];
    internal LandPath(IReadOnlyList<LandPosition> points)
    {
        if (points.Count == 0) throw new ArgumentException("路线至少包含起点。", nameof(points));
        var distinct = new List<LandPosition>();
        foreach (LandPosition point in points)
            if (distinct.Count == 0 || distinct[^1] != point) distinct.Add(point);
        _points = distinct.ToArray();
        Points = Array.AsReadOnly(_points);
        _distances = new double[_points.Length];
        for (int i = 1; i < _points.Length; i++)
            _distances[i] = _distances[i - 1] + Math.Max(Math.Abs(_points[i].X - _points[i - 1].X), Math.Abs(_points[i].Y - _points[i - 1].Y));
    }
    /**
     * <summary>按整趟累计距离取连续位置，超过终点时返回原始终点。</summary>
     * <param name="distance">非负整数基础格距离。</param>
     * <returns>同一路线上不可变位置。</returns>
     */
    public LandPosition GetPositionAtDistance(int distance) => Sample(distance);
    /**
     * <summary>按展示插值距离采样，使用有限 double 距离。</summary>
     * <param name="distance">非负且有限的基础格距离。</param>
     * <returns>同一路线上不可变位置。</returns>
     */
    public LandPosition GetPositionAtDistance(double distance) => Sample(distance);
    private LandPosition Sample(double distance)
    {
        if (!double.IsFinite(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));
        if (distance >= _distances[^1]) return _points[^1];
        int index = Segment(distance);
        double fraction = (distance - _distances[index]) / (_distances[index + 1] - _distances[index]);
        return LandPosition.Add(_points[index], LandPosition.Scale(LandPosition.Subtract(_points[index + 1], _points[index]), fraction));
    }
    private int Segment(double distance)
    {
        int index = Array.BinarySearch(_distances, distance);
        return Math.Min(index < 0 ? ~index - 1 : index, _points.Length - 2);
    }
    public Vector2 GetDirectionAtDistance(double distance)
    {
        if (!double.IsFinite(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));
        if (_points.Length == 1) return Vector2.Zero;
        int i = Segment(Math.Min(distance, _distances[^1]));
        return LandPosition.Subtract(_points[i + 1], _points[i]).ToVector2().Normalized();
    }
    public int GetTravelTicks(int speed)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
        return checked((int)Math.Ceiling(_distances[^1] / speed));
    }
    public bool Equals(LandPath? other)
    {
        if (other is null || _points.Length != other._points.Length) return false;
        for (int i = 0; i < _points.Length; i++) if (_points[i] != other._points[i]) return false;
        return true;
    }
    public override bool Equals(object? obj) => obj is LandPath other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (LandPosition point in _points) hash.Add(point);
        return hash.ToHashCode();
    }
}

public enum LandPathStatus { Success, InvalidStart, InvalidTarget, Unreachable }
public readonly record struct LandPathResult(LandPathStatus Status, LandPath? Path);
