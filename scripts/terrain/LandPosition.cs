using System;
using Godot;

namespace FarmExchange.Terrain;

/**
 * <summary>基础格平面上的不可变有限精度经营位置。</summary>
 * <remarks>连续任务保留 double 原值；Vector2 只用于引擎查询和显示。</remarks>
 */
public readonly struct LandPosition : IEquatable<LandPosition>
{
    public double X { get; }
    public double Y { get; }
    private LandPosition(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x), "位置必须有限。");
        X = x; Y = y;
    }
    public static LandPosition FromCell(Vector2I cell) => new(cell.X, cell.Y);
    public static LandPosition FromVector2(Vector2 point) => new(point.X, point.Y);
    public static LandPosition FromCoordinates(double x, double y) => new(x, y);
    public Vector2 ToVector2() => new((float)X, (float)Y);
    public bool Equals(LandPosition other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is LandPosition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(LandPosition a, LandPosition b) => a.Equals(b);
    public static bool operator !=(LandPosition a, LandPosition b) => !a.Equals(b);
    internal static LandPosition Add(LandPosition a, LandPosition b) => new(a.X + b.X, a.Y + b.Y);
    internal static LandPosition Subtract(LandPosition a, LandPosition b) => new(a.X - b.X, a.Y - b.Y);
    internal static LandPosition Scale(LandPosition a, double scale) => new(a.X * scale, a.Y * scale);
}
