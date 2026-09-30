using System.Numerics;

namespace Tessitura.Core;

/// <summary>Represents an exact, reduced rational number.</summary>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    private readonly long _denominator;

    /// <summary>Initializes a reduced fraction with a positive denominator.</summary>
    /// <param name="num">The numerator.</param>
    /// <param name="den">The nonzero denominator.</param>
    /// <exception cref="DivideByZeroException">The denominator is zero.</exception>
    /// <exception cref="OverflowException">The reduced value cannot fit in two signed 64-bit integers.</exception>
    public Fraction(long num, long den) : this(new BigInteger(num), new BigInteger(den))
    {
    }

    private Fraction(BigInteger num, BigInteger den)
    {
        if (den.IsZero)
        {
            throw new DivideByZeroException("A fraction cannot have a zero denominator.");
        }

        if (num.IsZero)
        {
            Num = 0;
            _denominator = 1;
            return;
        }

        if (den.Sign < 0)
        {
            num = -num;
            den = -den;
        }

        BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(num), den);
        Num = checked((long)(num / divisor));
        _denominator = checked((long)(den / divisor));
    }

    /// <summary>Gets zero as a fraction.</summary>
    public static Fraction Zero => default;

    /// <summary>Gets one as a fraction.</summary>
    public static Fraction One => new(1, 1);

    /// <summary>Gets the reduced numerator.</summary>
    public long Num { get; }

    /// <summary>Gets the positive reduced denominator.</summary>
    public long Den => _denominator == 0 ? 1 : _denominator;

    /// <summary>Adds two fractions exactly.</summary>
    public static Fraction operator +(Fraction left, Fraction right) =>
        new((BigInteger)left.Num * right.Den + (BigInteger)right.Num * left.Den,
            (BigInteger)left.Den * right.Den);

    /// <summary>Subtracts one fraction from another exactly.</summary>
    public static Fraction operator -(Fraction left, Fraction right) =>
        new((BigInteger)left.Num * right.Den - (BigInteger)right.Num * left.Den,
            (BigInteger)left.Den * right.Den);

    /// <summary>Negates a fraction.</summary>
    public static Fraction operator -(Fraction value) => new(-(BigInteger)value.Num, value.Den);

    /// <summary>Multiplies two fractions exactly.</summary>
    public static Fraction operator *(Fraction left, Fraction right) =>
        new((BigInteger)left.Num * right.Num, (BigInteger)left.Den * right.Den);

    /// <summary>Divides one fraction by another exactly.</summary>
    public static Fraction operator /(Fraction left, Fraction right) =>
        new((BigInteger)left.Num * right.Den, (BigInteger)left.Den * right.Num);

    /// <summary>Tests value equality.</summary>
    public static bool operator ==(Fraction left, Fraction right) => left.Equals(right);

    /// <summary>Tests value inequality.</summary>
    public static bool operator !=(Fraction left, Fraction right) => !left.Equals(right);

    /// <summary>Tests whether the left value is less than the right value.</summary>
    public static bool operator <(Fraction left, Fraction right) => left.CompareTo(right) < 0;

    /// <summary>Tests whether the left value is greater than the right value.</summary>
    public static bool operator >(Fraction left, Fraction right) => left.CompareTo(right) > 0;

    /// <summary>Tests whether the left value is less than or equal to the right value.</summary>
    public static bool operator <=(Fraction left, Fraction right) => left.CompareTo(right) <= 0;

    /// <summary>Tests whether the left value is greater than or equal to the right value.</summary>
    public static bool operator >=(Fraction left, Fraction right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public bool Equals(Fraction other) => Num == other.Num && Den == other.Den;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Fraction other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Num, Den);

    /// <inheritdoc />
    public int CompareTo(Fraction other) =>
        ((BigInteger)Num * other.Den).CompareTo((BigInteger)other.Num * Den);

    /// <inheritdoc />
    public override string ToString() => Den == 1 ? Num.ToString() : $"{Num}/{Den}";
}
