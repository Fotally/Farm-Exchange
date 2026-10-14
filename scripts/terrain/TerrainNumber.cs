using System;
using System.Numerics;

namespace FarmExchange.Terrain;

// 一次静态地形生成的有理数，不参与连续路线与运行位置。
internal readonly struct TerrainNumber : IComparable<TerrainNumber>, IEquatable<TerrainNumber>
{
    internal readonly BigInteger Numerator;
    private readonly BigInteger _denominator;
    internal BigInteger Denominator => _denominator.IsZero ? BigInteger.One : _denominator;
    internal TerrainNumber(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException();
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        if (numerator.IsZero) { Numerator = BigInteger.Zero; _denominator = BigInteger.One; return; }
        if (denominator.IsOne) { Numerator = numerator; _denominator = denominator; return; }
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        _denominator = denominator / divisor;
    }
    internal static TerrainNumber FromDecimal(decimal value)
    {
        int[] parts = decimal.GetBits(value);
        BigInteger numerator = (BigInteger)(uint)parts[0] + ((BigInteger)(uint)parts[1] << 32) + ((BigInteger)(uint)parts[2] << 64);
        if (parts[3] < 0) numerator = -numerator;
        return new(numerator, BigInteger.Pow(10, (parts[3] >> 16) & 255));
    }
    internal double ToDouble()
    {
        if (Numerator.IsZero) return 0;
        BigInteger absolute = BigInteger.Abs(Numerator);
        int numeratorShift = checked((int)Math.Max(0, absolute.GetBitLength() - 54));
        int denominatorShift = checked((int)Math.Max(0, Denominator.GetBitLength() - 54));
        double ratio = (double)(absolute >> numeratorShift) / (double)(Denominator >> denominatorShift);
        return (Numerator.Sign < 0 ? -1 : 1) * Math.ScaleB(ratio, numeratorShift - denominatorShift);
    }
    internal static TerrainNumber Min(TerrainNumber a, TerrainNumber b) => a < b ? a : b;
    internal static TerrainNumber Max(TerrainNumber a, TerrainNumber b) => a > b ? a : b;
    public int CompareTo(TerrainNumber other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public bool Equals(TerrainNumber other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is TerrainNumber other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    public static implicit operator TerrainNumber(int n) => new(n, 1);
    public static TerrainNumber operator +(TerrainNumber a, TerrainNumber b) => a.Numerator.IsZero ? b : b.Numerator.IsZero ? a : a.Denominator == b.Denominator ? new(a.Numerator + b.Numerator, a.Denominator) : new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static TerrainNumber operator -(TerrainNumber a, TerrainNumber b) => b.Numerator.IsZero ? a : a.Denominator == b.Denominator ? new(a.Numerator - b.Numerator, a.Denominator) : new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static TerrainNumber operator -(TerrainNumber a) => new(-a.Numerator, a.Denominator);
    public static TerrainNumber operator *(TerrainNumber a, TerrainNumber b) => a.Numerator.IsZero || b.Numerator.IsZero ? default : new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static TerrainNumber operator /(TerrainNumber a, TerrainNumber b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public static bool operator <(TerrainNumber a, TerrainNumber b) => a.CompareTo(b) < 0;
    public static bool operator >(TerrainNumber a, TerrainNumber b) => a.CompareTo(b) > 0;
    public static bool operator <=(TerrainNumber a, TerrainNumber b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TerrainNumber a, TerrainNumber b) => a.CompareTo(b) >= 0;
    public static bool operator ==(TerrainNumber a, TerrainNumber b) => a.Equals(b);
    public static bool operator !=(TerrainNumber a, TerrainNumber b) => !a.Equals(b);
}
