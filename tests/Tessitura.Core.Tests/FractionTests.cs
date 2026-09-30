using FsCheck.Xunit;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class FractionTests
{
    [Property(MaxTest = 500)]
    public void ConstructorAlwaysReduces(int numerator, int denominator)
    {
        long safeDenominator = denominator == 0 ? 1 : denominator;
        Fraction value = new(numerator, safeDenominator);

        Assert.True(value.Den > 0);
        Assert.Equal(1L, GreatestCommonDivisor(Math.Abs(value.Num), value.Den));
        Assert.Equal((decimal)numerator / safeDenominator, (decimal)value.Num / value.Den);
    }

    [Property(MaxTest = 500)]
    public void AdditionHasZeroIdentity(int numerator, int denominator)
    {
        Fraction value = SmallFraction(numerator, denominator);
        Assert.Equal(value, value + Fraction.Zero);
        Assert.Equal(value, Fraction.Zero + value);
    }

    [Property(MaxTest = 500)]
    public void AdditionIsAssociative(int an, int ad, int bn, int bd, int cn, int cd)
    {
        Fraction a = SmallFraction(an, ad);
        Fraction b = SmallFraction(bn, bd);
        Fraction c = SmallFraction(cn, cd);

        Assert.Equal((a + b) + c, a + (b + c));
    }

    [Property(MaxTest = 500)]
    public void MultiplicationIsAssociative(int an, int ad, int bn, int bd, int cn, int cd)
    {
        Fraction a = SmallFraction(an, ad);
        Fraction b = SmallFraction(bn, bd);
        Fraction c = SmallFraction(cn, cd);

        Assert.Equal((a * b) * c, a * (b * c));
    }

    [Fact]
    public void ArithmeticAndComparisonUseExactValues()
    {
        Fraction half = new(1, 2);
        Fraction third = new(1, 3);

        Assert.Equal(new Fraction(5, 6), half + third);
        Assert.Equal(new Fraction(1, 6), half - third);
        Assert.Equal(new Fraction(1, 6), half * third);
        Assert.Equal(new Fraction(3, 2), half / third);
        Assert.True(half > third);
        Assert.True(third < half);
        Assert.Equal(new Fraction(-1, 2), -half);
    }

    [Fact]
    public void InvalidDenominatorAndDivisionByZeroThrow()
    {
        Assert.Throws<DivideByZeroException>(() => new Fraction(1, 0));
        Assert.Throws<DivideByZeroException>(() => Fraction.One / Fraction.Zero);
    }

    [Fact]
    public void DefaultAndExtremeValuesRemainCanonical()
    {
        Assert.Equal(new Fraction(0, 1), default(Fraction));
        Assert.Equal(1, default(Fraction).Den);
        Assert.Equal(new Fraction(long.MinValue, 1), new Fraction(long.MinValue, 1));
        Assert.Equal(Fraction.One, new Fraction(long.MinValue, long.MinValue));
        Assert.Throws<OverflowException>(() => new Fraction(1, long.MinValue));
        Assert.Throws<OverflowException>(() => -new Fraction(long.MinValue, 1));
    }

    private static Fraction SmallFraction(int numerator, int denominator)
    {
        long safeNumerator = numerator % 1000;
        long safeDenominator = Math.Abs((long)denominator % 1000) + 1;
        return new Fraction(safeNumerator, safeDenominator);
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
