using Tessitura.Core;
using Tessitura.Editing;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class TextEntryParserTests
{
    private static readonly EventId Target = new(Guid.NewGuid());

    [Theory]
    [InlineData("mf", DynamicLevel.Mf)]
    [InlineData("PP", DynamicLevel.Pp)]
    [InlineData(" fff ", DynamicLevel.Fff)]
    [InlineData("p", DynamicLevel.P)]
    public void DynamicsAreRecognized(string text, DynamicLevel level)
    {
        Assert.True(TextEntryParser.TryParse(text, TextEntryKind.Dynamic, Target, out Attachment? attachment));
        Assert.Equal(new DynamicAttachment(Target, level), attachment);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("")]
    [InlineData("mf7")]
    [InlineData("1")]
    public void NonDynamicsAreRejected(string text) =>
        Assert.False(TextEntryParser.TryParse(text, TextEntryKind.Dynamic, Target, out _));

    [Theory]
    [InlineData("q=120", NoteValue.Quarter, 0, 120)]
    [InlineData("q = 96", NoteValue.Quarter, 0, 96)]
    [InlineData("e.=60", NoteValue.Eighth, 1, 60)]
    [InlineData("h=52.5", NoteValue.Half, 0, 52.5)]
    [InlineData("♩=80", NoteValue.Quarter, 0, 80)]
    [InlineData("=100", NoteValue.Quarter, 0, 100)]
    public void TemposUseTheBeatUnitAndCount(string text, NoteValue value, int dots, double bpm)
    {
        Assert.True(TextEntryParser.TryParse(text, TextEntryKind.Tempo, Target, out Attachment? attachment));
        Assert.Equal(new TempoAttachment(Target, new Duration(value, dots), bpm), attachment);
    }

    [Theory]
    [InlineData("q=10")]
    [InlineData("q=1000")]
    [InlineData("120")]
    [InlineData("allegro")]
    public void InvalidTemposAreRejected(string text) =>
        Assert.False(TextEntryParser.TryParse(text, TextEntryKind.Tempo, Target, out _));

    [Theory]
    [InlineData("Cmaj7", Step.C, 0, "maj7", null, 0)]
    [InlineData("F#m7/A", Step.F, 1, "m7", Step.A, 0)]
    [InlineData("Bb", Step.B, -1, "", null, 0)]
    [InlineData("D7sus4", Step.D, 0, "7sus4", null, 0)]
    [InlineData("G/B", Step.G, 0, "", Step.B, 0)]
    [InlineData("Ebdim7", Step.E, -1, "dim7", null, 0)]
    public void ChordSymbolsKeepRootQualityAndBass(string text, Step root, int alter, string quality, Step? bass, int bassAlter)
    {
        Assert.True(TextEntryParser.TryParse(text, TextEntryKind.Text, Target, out Attachment? attachment));
        Assert.Equal(new ChordSymbolAttachment(Target, root, alter, quality, bass, bassAlter), attachment);
    }

    [Theory]
    [InlineData("dolce")]
    [InlineData("Allegro")]
    [InlineData("Hello")]
    [InlineData("mf")]
    public void OtherTextBecomesPlainText(string text)
    {
        Assert.True(TextEntryParser.TryParse(text, TextEntryKind.Text, Target, out Attachment? attachment));
        Assert.Equal(new TextAttachment(Target, text), attachment);
    }

    [Fact]
    public void ChordSymbolDisplayUsesProperAccidentals() =>
        Assert.Equal("F♯m7/A", new ChordSymbolAttachment(Target, Step.F, 1, "m7", Step.A).Display);

    [Theory]
    [InlineData("1:glo-", 1, "glo", LyricSyllabic.Begin, LyricExtender.None)]
    [InlineData("3:aleluya", 3, "aleluya", LyricSyllabic.Single, LyricExtender.None)]
    [InlineData("2:melisma~", 2, "melisma", LyricSyllabic.Single, LyricExtender.Start)]
    [InlineData("1:~>", 1, "", LyricSyllabic.Single, LyricExtender.Continue)]
    [InlineData("1:~", 1, "", LyricSyllabic.Single, LyricExtender.Stop)]
    public void LyricEntriesPreserveVerseSyllableAndExtender(
        string text, int verse, string syllable, LyricSyllabic syllabic, LyricExtender extender)
    {
        Assert.True(TextEntryParser.TryParse(text, TextEntryKind.Lyric, Target, out Attachment? attachment));
        Assert.Equal(new LyricAttachment(Target, verse, syllable, syllabic, extender), attachment);
    }

    [Theory]
    [InlineData("0:la")]
    [InlineData("1:")]
    [InlineData("x:la")]
    public void InvalidLyricEntriesAreRejected(string text) =>
        Assert.False(TextEntryParser.TryParse(text, TextEntryKind.Lyric, Target, out _));
}
