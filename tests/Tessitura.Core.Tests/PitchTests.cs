using FsCheck.Xunit;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class PitchTests
{
    [Fact]
    public void MajorThirdAboveMiddleCIsE()
    {
        Pitch middleC = new(Step.C, 0, 4);
        Assert.Equal(new Pitch(Step.E, 0, 4), middleC.Transpose(new Interval(2, 4)));
    }

    [Fact]
    public void EnharmonicPitchesKeepDifferentSpellings()
    {
        Pitch bSharp = new(Step.B, 1, 3);
        Pitch middleC = new(Step.C, 0, 4);

        Assert.Equal(60, bSharp.MidiNumber);
        Assert.Equal(middleC.MidiNumber, bSharp.MidiNumber);
        Assert.NotEqual(middleC, bSharp);
    }

    [Property(MaxTest = 500)]
    public void TranspositionAndItsInverseRestoreSpelling(int step, int alter, int octave, int diatonic, int semitones)
    {
        Pitch original = new((Step)(Math.Abs((long)step) % 7), alter % 4, octave % 8);
        Interval interval = new(diatonic % 25, semitones % 48);

        Assert.Equal(original, original.Transpose(interval).Transpose(interval.Inverse()));
    }

    [Fact]
    public void DescendingIntervalCrossesOctaveBoundary()
    {
        Pitch middleC = new(Step.C, 0, 4);
        Assert.Equal(new Pitch(Step.B, 0, 3), middleC.Transpose(new Interval(-1, -1)));
    }
}
