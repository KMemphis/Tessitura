using System.Collections.Immutable;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ClipboardTests
{
    [Fact]
    public void PastingFourMeasuresOnAnotherStaffKeepsRhythmAndPitches()
    {
        Score score = CreateScore(measureCount: 4);
        ClipboardFragment fragment = CopyStaff(score, staff: 0);

        Score pasted = new PasteCommand(fragment, Fraction.Zero).Apply(score, new EditContext(1, 0, 1));

        Assert.Equal(4, pasted.Measures.Length);
        AssertValid(pasted, 2);
        Assert.Equal(Describe(pasted, 0, 1), Describe(pasted, 1, 1));
        Assert.Equal(Describe(score, 0, 1), Describe(pasted, 0, 1));
        HashSet<EventId> sourceIds = Ids(pasted, 0);
        Assert.DoesNotContain(Ids(pasted, 1), sourceIds.Contains);
        Assert.NotSame(score, pasted);
        Assert.All(Describe(score, 1, 1), line => Assert.StartsWith("rest", line));
    }

    [Fact]
    public void PasteRespectsDestinationVoiceAndPosition()
    {
        Score score = CreateScore(measureCount: 6);
        ClipboardFragment fragment = CopyStaff(score, staff: 0, lastMeasure: 1);
        Fraction destination = new(3, 1);

        Score pasted = new PasteCommand(fragment, destination).Apply(score, new EditContext(1, 3, 2));

        AssertValid(pasted, 2);
        List<string> source = Describe(score, 0, 1, upTo: new Fraction(2, 1));
        List<string> copied = Describe(pasted, 1, 2, from: destination, upTo: destination + new Fraction(2, 1),
            rebase: destination);
        Assert.Equal(source, copied);
        Assert.Equal(Describe(score, 1, 1), Describe(pasted, 1, 1));
    }

    [Fact]
    public void PasteRunningPastTheEndAppendsMeasures()
    {
        Score score = CreateScore(measureCount: 4);
        ClipboardFragment fragment = CopyStaff(score, staff: 0);

        Score pasted = new PasteCommand(fragment, new Fraction(2, 1)).Apply(score, new EditContext(1, 2, 1));

        Assert.Equal(6, pasted.Measures.Length);
        AssertValid(pasted, 2);
        Assert.Equal(Describe(score, 0, 1),
            Describe(pasted, 1, 1, from: new Fraction(2, 1), upTo: new Fraction(6, 1), rebase: new Fraction(2, 1)));
    }

    [Fact]
    public void PastingInsideANoteIsRefusedAndControllerHistoryStaysClean()
    {
        Score score = CreateScore(measureCount: 4);
        ScoreInputController input = new(score);
        input.SelectEvent(FirstEventId(score, 0), extendRange: false);
        Assert.True(input.CopySelection());
        input.SelectEvent(FirstEventId(score, 0));
        Assert.True(input.PasteClipboard());
        Score afterFirst = input.CurrentScore;
        // The second event of staff 0 starts at 1/4; a chord of the first paste covers it partly.
        Assert.Throws<InvalidOperationException>(() => new PasteCommand(
            CopyStaff(afterFirst, 0, lastMeasure: 0), new Fraction(1, 8)).Apply(afterFirst, new EditContext(0, 0, 1)));
        input.Undo();
        Assert.Same(score, input.CurrentScore);
    }

    [Fact]
    public void CopyAndPasteActionsWorkFromTheSelectionAndUndo()
    {
        Score score = CreateScore(measureCount: 4);
        ScoreInputController input = new(score);
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-clip-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = ActionRegistry.LoadOrCreate(
                input.CreateActions().AddRange(shell.CreateActions()), settings);
            shell.AttachActionRegistry(actions);
            EventId first = FirstEventId(score, 0);
            EventId last = LastEventId(score, 0);
            input.SelectEvent(first);
            input.SelectEvent(last, extendRange: true);

            Assert.False(input.CanPaste);
            Assert.True(actions.TryExecute("edit.copy"));
            Assert.True(input.CanPaste);
            input.SelectEvent(FirstEventId(score, 1));
            Assert.True(actions.TryExecute("edit.paste"));

            AssertValid(input.CurrentScore, 2);
            Assert.Equal(Describe(input.CurrentScore, 0, 1), Describe(input.CurrentScore, 1, 1));
            input.Undo();
            Assert.Same(score, input.CurrentScore);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    private static ClipboardFragment CopyStaff(Score score, int staff, int lastMeasure = int.MaxValue)
    {
        List<SelectionItem> items = [];
        for (int measure = 0; measure < score.Measures.Length && measure <= lastMeasure; measure++)
        {
            foreach (Voice voice in score.Content[new StaffMeasureKey(staff, measure)].Voices)
            {
                foreach (MusicEvent musicEvent in voice.Events)
                {
                    items.Add(new SelectionItem(musicEvent.Id));
                }
            }
        }

        return ClipboardFragment.Copy(score, new Selection([.. items]))!;
    }

    private static List<string> Describe(Score score, int staff, int voiceNumber,
        Fraction? from = null, Fraction? upTo = null, Fraction? rebase = null)
    {
        List<string> lines = [];
        Fraction start = Fraction.Zero;
        for (int measure = 0; measure < score.Measures.Length; measure++)
        {
            foreach (Voice voice in score.Content[new StaffMeasureKey(staff, measure)].Voices)
            {
                if (voice.Number != voiceNumber)
                {
                    continue;
                }

                foreach (MusicEvent e in voice.Events)
                {
                    Fraction onset = start + e.Onset;
                    if ((from is Fraction f && onset < f) || (upTo is Fraction u && onset >= u))
                    {
                        continue;
                    }

                    Fraction shown = rebase is Fraction r ? onset - r : onset;
                    string notes = e is Chord c
                        ? string.Join(",", c.Notes.Select(n => $"{n.Pitch.Step}{n.Pitch.Alter}{n.Pitch.Octave}{(n.TiedToNext ? "~" : "")}"))
                        : "";
                    lines.Add($"{(e is Chord ? "chord" : "rest")} {shown.Num}/{shown.Den} {e.Duration.Value}.{e.Duration.Dots} {notes}".TrimEnd());
                }
            }

            start += score.Measures[measure].TimeSignature.Length;
        }

        return lines;
    }

    private static HashSet<EventId> Ids(Score score, int staff)
    {
        HashSet<EventId> ids = [];
        foreach ((StaffMeasureKey key, StaffMeasure m) in score.Content)
        {
            if (key.StaffIndex == staff)
            {
                foreach (Voice v in m.Voices)
                {
                    foreach (MusicEvent e in v.Events)
                    {
                        ids.Add(e.Id);
                    }
                }
            }
        }

        return ids;
    }

    private static EventId FirstEventId(Score score, int staff) =>
        score.Content[new StaffMeasureKey(staff, 0)].Voices[0].Events[0].Id;

    private static EventId LastEventId(Score score, int staff) =>
        score.Content[new StaffMeasureKey(staff, score.Measures.Length - 1)].Voices[0].Events[^1].Id;

    private static void AssertValid(Score score, int staffCount)
    {
        for (int staff = 0; staff < staffCount; staff++)
        {
            for (int measure = 0; measure < score.Measures.Length; measure++)
            {
                Assert.True(ScoreValidator.IsMeasureValid(score.Measures[measure],
                    score.Content[new StaffMeasureKey(staff, measure)]), $"staff {staff} measure {measure}");
            }
        }
    }

    private static Score CreateScore(int measureCount)
    {
        ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>();
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int m = 0; m < measureCount; m++)
        {
            measures.Add(new Measure(m + 1, new TimeSignature(4, 4)));
            content[new StaffMeasureKey(0, m)] = new StaffMeasure([new Voice(1, SourceMeasure(m % 4))]);
            content[new StaffMeasureKey(1, m)] = new StaffMeasure([new Voice(1,
                [new Rest(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0))])]);
        }

        return new Score(new ScoreMetadata("T", ""),
            [new Instrument("Piano", [new Staff("R"), new Staff("L", Clef.Bass)])],
            measures.ToImmutable(), content.ToImmutable());
    }

    private static ImmutableArray<MusicEvent> SourceMeasure(int pattern)
    {
        EventId Id() => new(Guid.NewGuid());
        Chord Note(Fraction onset, NoteValue value, int dots, Step step, int alter, int octave, bool tie = false) =>
            new(Id(), onset, new Duration(value, dots), [new Note(new Pitch(step, alter, octave), tie)], StemDirection.Auto);
        switch (pattern)
        {
            case 0:
                return
                [
                    Note(Fraction.Zero, NoteValue.Quarter, 0, Step.C, 0, 4),
                    new Chord(Id(), new Fraction(1, 4), new Duration(NoteValue.Quarter, 0),
                        [new Note(new Pitch(Step.E, 0, 4)), new Note(new Pitch(Step.G, 1, 4))], StemDirection.Up),
                    // The normalizer writes rests beat by beat, so the source already uses that form.
                    new Rest(Id(), new Fraction(1, 2), new Duration(NoteValue.Quarter, 0)),
                    new Rest(Id(), new Fraction(3, 4), new Duration(NoteValue.Quarter, 0)),
                ];
            case 1:
                return
                [
                    Note(Fraction.Zero, NoteValue.Half, 1, Step.D, -1, 4, tie: true),
                    Note(new Fraction(3, 4), NoteValue.Quarter, 0, Step.F, 0, 4),
                ];
            case 2:
                List<MusicEvent> eighths = [];
                for (int i = 0; i < 8; i++)
                {
                    eighths.Add(Note(new Fraction(i, 8), NoteValue.Eighth, 0, (Step)(i % 7), 0, 4 + i / 7));
                }

                return [.. eighths];
            default:
                return [Note(Fraction.Zero, NoteValue.Whole, 0, Step.G, 0, 3)];
        }
    }
}
