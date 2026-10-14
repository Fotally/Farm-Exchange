using System;

namespace FarmExchange.Terrain;

/**
 * <summary>仅用于一次静态生成与硬约束验证的有理点。</summary>
 * <remarks>不进入工人连续移动、取消接续或运行路线。</remarks>
 */
internal readonly struct TerrainPoint : IEquatable<TerrainPoint>
{
    internal TerrainNumber ExactX { get; }
    internal TerrainNumber ExactY { get; }
    public double X => ExactX.ToDouble();
    public double Y => ExactY.ToDouble();
    internal TerrainPoint(TerrainNumber x, TerrainNumber y) { ExactX = x; ExactY = y; }
    public bool Equals(TerrainPoint other) => ExactX == other.ExactX && ExactY == other.ExactY;
    public override bool Equals(object? obj) => obj is TerrainPoint other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ExactX, ExactY);
    public static bool operator ==(TerrainPoint a, TerrainPoint b) => a.Equals(b);
    public static bool operator !=(TerrainPoint a, TerrainPoint b) => !a.Equals(b);
    internal static TerrainPoint Add(TerrainPoint a, TerrainPoint b) => new(a.ExactX + b.ExactX, a.ExactY + b.ExactY);
    internal static TerrainPoint Subtract(TerrainPoint a, TerrainPoint b) => new(a.ExactX - b.ExactX, a.ExactY - b.ExactY);
}
