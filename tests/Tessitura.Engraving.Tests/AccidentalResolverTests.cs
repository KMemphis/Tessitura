using Tessitura.Core;
using Tessitura.Engraving;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class AccidentalResolverTests
{
    public static TheoryData<string, AccidentalInput[], AccidentalMark[]> Cases => new()
    {
        { "natural in C major", [N(0, Step.C, 0)], [AccidentalMark.None] },
        { "new sharp", [N(0, Step.C, 1)], [AccidentalMark.Sharp] },
        { "repeated sharp", [N(0, Step.C, 1), N(0, Step.C, 1)], [AccidentalMark.Sharp, AccidentalMark.None] },
        { "cancelling sharp", [N(0, Step.C, 1), N(0, Step.C, 0)], [AccidentalMark.Sharp, AccidentalMark.Natural] },
        { "key signature sharp", [N(0, Step.F, 1, fifths: 1)], [AccidentalMark.None] },
        { "natural against sharp key", [N(0, Step.F, 0, fifths: 1)], [AccidentalMark.Natural] },
        { "repeat natural in sharp key", [N(0, Step.F, 0, fifths: 1), N(0, Step.F, 0, fifths: 1)], [AccidentalMark.Natural, AccidentalMark.None] },
        { "barline resets cancellation", [N(0, Step.F, 0, fifths: 1), N(1, Step.F, 0, fifths: 1)], [AccidentalMark.Natural, AccidentalMark.Natural] },
        { "tie across barline suppresses sharp", [N(0, Step.C, 1), N(1, Step.C, 1, tied: true)], [AccidentalMark.Sharp, AccidentalMark.None] },
        { "tie carries natural through barline", [N(0, Step.F, 0, fifths: 1), N(1, Step.F, 0, tied: true, fifths: 1), N(1, Step.F, 0, fifths: 1)], [AccidentalMark.Natural, AccidentalMark.None, AccidentalMark.None] },
        { "octaves are independent", [N(0, Step.C, 1), N(0, Step.C, 1, octave: 5)], [AccidentalMark.Sharp, AccidentalMark.Sharp] },
        { "enharmonic spellings are independent", [N(0, Step.C, 1), N(0, Step.D, -1)], [AccidentalMark.Sharp, AccidentalMark.Flat] },
        { "flat key signature", [N(0, Step.B, -1, fifths: -1)], [AccidentalMark.None] },
        { "natural against flat key", [N(0, Step.B, 0, fifths: -1)], [AccidentalMark.Natural] },
        { "key change resets state", [N(0, Step.F, 1), N(0, Step.F, 1, fifths: 1)], [AccidentalMark.Sharp, AccidentalMark.None] },
        { "double sharp", [N(0, Step.C, 2)], [AccidentalMark.DoubleSharp] },
        { "double flat", [N(0, Step.D, -2)], [AccidentalMark.DoubleFlat] },
        { "double sharp against sharp key", [N(0, Step.F, 2, fifths: 1)], [AccidentalMark.DoubleSharp] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ResolvesWrittenAccidentals(
        string scenario, AccidentalInput[] notes, AccidentalMark[] expected)
    {
        AccidentalResolver resolver = new();

        AccidentalMark[] actual = resolver.Resolve(notes).ToArray();
        Assert.True(expected.SequenceEqual(actual),
            $"{scenario}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
    }

    [Theory]
    [InlineData(-8)]
    [InlineData(8)]
    public void RejectsUnsupportedKeySignature(int fifths) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeySignature(fifths));

    private static AccidentalInput N(int measure, Step step, int alter, int octave = 4,
        bool tied = false, int fifths = 0) =>
        new(measure, new Pitch(step, alter, octave), tied, new KeySignature(fifths));
}
