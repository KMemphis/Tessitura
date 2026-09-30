using FsCheck.Xunit;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class DurationTests
{
    [Fact]
    public void DoubleDottedQuarterIsSevenSixteenths()
    {
        Assert.Equal(new Fraction(7, 16), new Duration(NoteValue.Quarter, 2).Length);
    }

    [Property(MaxTest = 500)]
    public void OneDotAddsHalfOfBaseLength(int value)
    {
        NoteValue noteValue = (NoteValue)(1 << (int)(Math.Abs((long)value) % 8));
        Fraction baseLength = new Duration(noteValue, 0).Length;

        Assert.Equal(baseLength + baseLength / new Fraction(2, 1), new Duration(noteValue, 1).Length);
    }

    [Fact]
    public void InvalidValueAndDotCountAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Duration((NoteValue)3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Duration(NoteValue.Quarter, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Duration(NoteValue.HundredTwentyEighth, 56));
    }
}
