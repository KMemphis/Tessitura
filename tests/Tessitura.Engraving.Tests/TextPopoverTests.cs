using System.Collections.Immutable;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayText = Tessitura.Engraving.DisplayLists.Text;

namespace Tessitura.Engraving.Tests;

public sealed class TextPopoverTests
{
    [Fact]
    public void TypingMfTempoAndAChordSymbolCreatesTheRightElementsWithHistory()
    {
        (ScoreInputController input, ScoreWindowShell shell, ActionRegistry actions, Chord note) = Create();
        TextPopover popover = shell.TextPopover!;
        input.SelectEvent(note.Id);

        Assert.True(actions.TryExecute("text.dynamic"));
        Assert.True(popover.IsOpen);
        popover.Text = "mf";
        Assert.True(popover.Submit());
        Assert.False(popover.IsOpen);
        actions.TryExecute("text.tempo");
        popover.Text = "q=120";
        Assert.True(popover.Submit());
        actions.TryExecute("text.text");
        popover.Text = "Cmaj7";
        Assert.True(popover.Submit());

        Assert.Contains(new DynamicAttachment(note.Id, DynamicLevel.Mf), input.CurrentScore.AttachmentList);
        Assert.Contains(new TempoAttachment(note.Id, new Duration(NoteValue.Quarter, 0), 120), input.CurrentScore.AttachmentList);
        Assert.Contains(new ChordSymbolAttachment(note.Id, Step.C, 0, "maj7"), input.CurrentScore.AttachmentList);
        input.Undo();
        Assert.DoesNotContain(input.CurrentScore.AttachmentList, a => a is ChordSymbolAttachment);
        Assert.Equal(2, input.CurrentScore.AttachmentList.Length);
    }

    [Fact]
    public void BadTextAndMissingSelectionKeepThePopoverOpenWithAnExplanation()
    {
        (ScoreInputController input, ScoreWindowShell shell, ActionRegistry actions, Chord note) = Create();
        TextPopover popover = shell.TextPopover!;

        actions.TryExecute("text.dynamic");
        popover.Text = "mf";
        Assert.False(popover.Submit());
        Assert.Contains("Selecciona", popover.Message);

        input.SelectEvent(note.Id);
        popover.Text = "banana";
        Assert.False(popover.Submit());
        Assert.True(popover.IsOpen);
        Assert.Contains("banana", popover.Message);
        Assert.Empty(input.CurrentScore.AttachmentList);
    }

    [Fact]
    public void DynamicsGoBelowTheStaffTempoAndChordSymbolsAbove()
    {
        (ScoreInputController input, _, _, Chord note) = Create();
        input.SelectEvent(note.Id);
        input.SubmitText(TextEntryKind.Dynamic, "f");
        input.SubmitText(TextEntryKind.Tempo, "q=120");
        input.SubmitText(TextEntryKind.Text, "F#m7");
        Smufl.SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        Score score = input.CurrentScore;

        ImmutableArray<DisplayLists.DrawingPrimitive> primitives = composer.Compose(score,
            new IncrementalScoreLayouter(metadata).Layout(score, style, composer.GetAvailableWidth(score)), 0).Page.Primitives;

        double staffTop = primitives.OfType<DisplayLists.Line>().First(l => l.ElementId.Value == Guid.Empty).Start.Y;
        DisplayGlyph dynamic = primitives.OfType<DisplayGlyph>().Single(g => g.Codepoint == metadata.GetGlyphCodepoint("dynamicForte"));
        DisplayGlyph beat = primitives.OfType<DisplayGlyph>().Single(g => g.Codepoint == metadata.GetGlyphCodepoint("metNoteQuarterUp"));
        Assert.True(dynamic.Origin.Y > staffTop + 4, "dynamics sit below the staff");
        Assert.True(beat.Origin.Y < staffTop, "tempo marks sit above the staff");
        Assert.Contains(primitives.OfType<DisplayText>(), t => t.Content == "= 120" && t.Origin.Y < staffTop);
        Assert.Contains(primitives.OfType<DisplayText>(), t => t.Content == "F♯m7" && t.Origin.Y < staffTop);
    }

    private static (ScoreInputController, ScoreWindowShell, ActionRegistry, Chord) Create()
    {
        Chord note = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(Step.C, 0, 5))], StemDirection.Auto);
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])], [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1,
                [note, new Rest(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Half, 1))])])));
        ScoreInputController input = new(score);
        ScoreWindowShell shell = new(new ScoreCanvas(), input);
        ActionRegistry actions = ActionRegistry.LoadOrCreate(input.CreateActions().AddRange(shell.CreateActions()),
            Path.Combine(Path.GetTempPath(), $"tessitura-pop-{Guid.NewGuid():N}.json"));
        shell.AttachActionRegistry(actions);
        return (input, shell, actions, note);
    }

    private static Smufl.SmuflMetadata LoadMetadata()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Tessitura.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return Smufl.SmuflMetadata.Load(Path.Combine(root, "assets", "fonts", "Bravura.json"), Path.Combine(root, "assets", "fonts", "smufl_glyph_names.json"));
    }
}

public sealed class SlurEditingTests
{
    [Fact]
    public void SlurActionJoinsTheFirstAndLastSelectedEventsAndCanBeUndone()
    {
        Chord[] notes = [.. Enumerable.Range(0, 3).Select(i => new Chord(new EventId(Guid.NewGuid()), new Fraction(i, 4), new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.C, 0, 4 + i))], StemDirection.Auto))];
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])], [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1,
                [.. notes, new Rest(new EventId(Guid.NewGuid()), new Fraction(3, 4), new Duration(NoteValue.Quarter, 0))])])));
        ScoreInputController input = new(score);
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        ActionRegistry actions = ActionRegistry.LoadOrCreate(input.CreateActions().AddRange(shell.CreateActions()),
            Path.Combine(Path.GetTempPath(), $"tessitura-slur-{Guid.NewGuid():N}.json"));
        shell.AttachActionRegistry(actions);

        input.SelectEvent(notes[0].Id);
        Assert.False(input.SlurSelection(), "one event cannot be slurred");
        input.SelectEvent(notes[2].Id, extendRange: true);
        Assert.True(actions.TryExecute("spanner.slur"));

        Spanner slur = Assert.Single(input.CurrentScore.SpannerList);
        Assert.Equal((notes[0].Id, notes[2].Id, SpannerKind.Slur), (slur.Start, slur.End, slur.Kind));
        input.Undo();
        Assert.Empty(input.CurrentScore.SpannerList);
    }
}
