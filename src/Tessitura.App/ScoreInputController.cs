using System.Collections.Immutable;
using Avalonia.Input;
using Tessitura.Core;
using Tessitura.Editing;

namespace Tessitura.App;

/// <summary>Describes whether keystrokes select elements or enter musical content.</summary>
public enum ScoreInputMode
{
    /// <summary>Keystrokes use the selection and application actions.</summary>
    Selection,

    /// <summary>Letter and rhythm keys write into the score at the musical cursor.</summary>
    NoteEntry,
}

/// <summary>Identifies the musical position of the score's input cursor.</summary>
/// <param name="StaffIndex">The zero-based staff index.</param>
/// <param name="VoiceNumber">The one-based voice number.</param>
/// <param name="Position">The exact position on the score timeline in whole-note units.</param>
public readonly record struct ScoreInputCursor(int StaffIndex, int VoiceNumber, Fraction Position);

/// <summary>Applies keyboard note-entry actions to an immutable score history.</summary>
public sealed class ScoreInputController
{
    private readonly History _history;
    private readonly Stack<EditorStateSnapshot> _undoStates = new();
    private readonly Stack<EditorStateSnapshot> _redoStates = new();
    private EventId? _lastEventId;
    private EditContext? _lastContext;
    private Pitch? _lastPitch;
    private int _lastNoteIndex;

    /// <summary>Creates an input controller for the supplied score snapshot.</summary>
    /// <param name="initialScore">The initial score.</param>
    /// <param name="staffIndex">The zero-based staff where note entry starts.</param>
    /// <param name="voiceNumber">The one-based voice where note entry starts.</param>
    public ScoreInputController(Score initialScore, int staffIndex = 0, int voiceNumber = 1)
    {
        ArgumentNullException.ThrowIfNull(initialScore);
        if (initialScore.Measures.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The initial score must contain at least one measure.", nameof(initialScore));
        }

        _history = new History(initialScore);
        Cursor = new ScoreInputCursor(staffIndex, voiceNumber, Fraction.Zero);
        _ = new EditContext(staffIndex, 0, voiceNumber);
        int staffCount = 0;
        foreach (Instrument instrument in initialScore.Instruments)
        {
            staffCount = checked(staffCount + instrument.Staves.Length);
        }

        if (staffIndex >= staffCount)
        {
            throw new ArgumentOutOfRangeException(nameof(staffIndex));
        }

        if (!initialScore.Content.TryGetValue(new StaffMeasureKey(staffIndex, 0), out StaffMeasure? staffMeasure) ||
            !staffMeasure.Voices.Any(voice => voice.Number == voiceNumber))
        {
            throw new ArgumentException("The initial score does not contain the requested voice.", nameof(initialScore));
        }
    }

    /// <summary>Raised after the score, cursor, duration, or input mode changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Gets the current immutable score snapshot.</summary>
    public Score CurrentScore => _history.CurrentScore;

    /// <summary>Gets the current input mode.</summary>
    public ScoreInputMode Mode { get; private set; }

    /// <summary>Gets the musical input cursor.</summary>
    public ScoreInputCursor Cursor { get; private set; }

    /// <summary>Gets the duration used by the next note or rest action.</summary>
    public Duration CurrentDuration { get; private set; } = new(NoteValue.Quarter, 0);

    /// <summary>Gets the pitch of the most recently entered note, if any.</summary>
    public Pitch? LastEnteredPitch => _lastPitch;

    /// <summary>Creates the registered actions for note entry and its keyboard shortcuts.</summary>
    /// <returns>The actions that should be added to the application's action registry.</returns>
    public ImmutableArray<ActionDefinition> CreateActions()
    {
        ImmutableArray<ActionDefinition>.Builder actions = ImmutableArray.CreateBuilder<ActionDefinition>();
        actions.Add(new ActionDefinition("score.note-entry", "Modo de entrada de notas", "N", EnterNoteEntry));
        actions.Add(new ActionDefinition("score.selection-mode", "Modo de selección", "Esc", ExitNoteEntry));
        actions.Add(new ActionDefinition("score.note.c", "Escribir Do", "C", () => WriteNote(Step.C)));
        actions.Add(new ActionDefinition("score.note.d", "Escribir Re", "D", () => WriteNote(Step.D)));
        actions.Add(new ActionDefinition("score.note.e", "Escribir Mi", "E", () => WriteNote(Step.E)));
        actions.Add(new ActionDefinition("score.note.f", "Escribir Fa", "F", () => WriteNote(Step.F)));
        actions.Add(new ActionDefinition("score.note.g", "Escribir Sol", "G", () => WriteNote(Step.G)));
        actions.Add(new ActionDefinition("score.note.a", "Escribir La", "A", () => WriteNote(Step.A)));
        actions.Add(new ActionDefinition("score.note.b", "Escribir Si", "B", () => WriteNote(Step.B)));
        actions.Add(new ActionDefinition("score.duration.sixteenth", "Duración semicorchea", "3",
            () => SetDuration(NoteValue.Sixteenth)));
        actions.Add(new ActionDefinition("score.duration.eighth", "Duración corchea", "4",
            () => SetDuration(NoteValue.Eighth)));
        actions.Add(new ActionDefinition("score.duration.quarter", "Duración negra", "5",
            () => SetDuration(NoteValue.Quarter)));
        actions.Add(new ActionDefinition("score.duration.half", "Duración blanca", "6",
            () => SetDuration(NoteValue.Half)));
        actions.Add(new ActionDefinition("score.duration.whole", "Duración redonda", "7",
            () => SetDuration(NoteValue.Whole)));
        actions.Add(new ActionDefinition("score.duration.dot", "Añadir o quitar puntillo", ".", ToggleDot));
        actions.Add(new ActionDefinition("score.rest", "Escribir silencio", "0", WriteRest));
        actions.Add(new ActionDefinition("score.pitch.semitone-up", "Subir un semitono", "Up",
            () => TransposeLastPitch(1, 0, 1)));
        actions.Add(new ActionDefinition("score.pitch.semitone-down", "Bajar un semitono", "Down",
            () => TransposeLastPitch(-1, 0, -1)));
        actions.Add(new ActionDefinition("score.pitch.octave-up", "Subir una octava", "Ctrl+Up",
            () => TransposeLastPitch(12, 7, 0)));
        actions.Add(new ActionDefinition("score.pitch.octave-down", "Bajar una octava", "Ctrl+Down",
            () => TransposeLastPitch(-12, -7, 0)));
        actions.Add(new ActionDefinition("score.tie.toggle", "Alternar ligadura de unión", "T", ToggleTie));
        actions.Add(new ActionDefinition("score.undo", "Deshacer", "Ctrl+Z", Undo));
        actions.Add(new ActionDefinition("score.redo", "Rehacer", "Ctrl+Y", Redo));
        return actions.ToImmutable();
    }

    /// <summary>Enters keyboard note-entry mode.</summary>
    public void EnterNoteEntry()
    {
        if (Mode == ScoreInputMode.NoteEntry)
        {
            return;
        }

        Mode = ScoreInputMode.NoteEntry;
        NotifyStateChanged();
    }

    /// <summary>Returns to selection mode.</summary>
    public void ExitNoteEntry()
    {
        if (Mode == ScoreInputMode.Selection)
        {
            return;
        }

        Mode = ScoreInputMode.Selection;
        NotifyStateChanged();
    }

    /// <summary>Restores the previous score and musical cursor snapshot.</summary>
    public void Undo()
    {
        if (!_history.CanUndo)
        {
            return;
        }

        _history.Undo();
        _redoStates.Push(CaptureState());
        if (_undoStates.TryPop(out EditorStateSnapshot state))
        {
            RestoreState(state);
        }

        NotifyStateChanged();
    }

    /// <summary>Restores the next score and musical cursor snapshot.</summary>
    public void Redo()
    {
        if (!_history.CanRedo)
        {
            return;
        }

        _history.Redo();
        _undoStates.Push(CaptureState());
        if (_redoStates.TryPop(out EditorStateSnapshot state))
        {
            RestoreState(state);
        }

        NotifyStateChanged();
    }

    private void WriteNote(Step step)
    {
        if (Mode != ScoreInputMode.NoteEntry)
        {
            return;
        }

        Pitch pitch = NearestPitch(step);
        (int measureIndex, Fraction localPosition) = EnsureCursorMeasure();
        EditContext context = new(Cursor.StaffIndex, measureIndex, Cursor.VoiceNumber);
        MusicEvent target = FindEventAt(context, localPosition);
        Duration? duration = target is Rest ? CurrentDuration : null;
        IScoreCommand command = new InsertNoteCommand(target.Id, pitch, duration);
        Apply(command, context);
        _lastEventId = target.Id;
        _lastContext = context;
        _lastNoteIndex = target is Chord chord ? chord.Notes.Length : 0;
        _lastPitch = pitch;
        Cursor = Cursor with { Position = Cursor.Position + CurrentDuration.Length };
        NotifyStateChanged();
    }

    private Pitch NearestPitch(Step step)
    {
        if (_lastPitch is not Pitch previous)
        {
            return new Pitch(step, 0, 4);
        }

        int previousDiatonicIndex = checked(previous.Octave * 7 + (int)previous.Step);
        Pitch closest = new(step, 0, previous.Octave);
        int closestDistance = int.MaxValue;
        for (int octave = previous.Octave - 1; octave <= previous.Octave + 1; octave++)
        {
            Pitch candidate = new(step, 0, octave);
            int candidateDistance = Math.Abs(checked(octave * 7 + (int)step) - previousDiatonicIndex);
            if (candidateDistance < closestDistance)
            {
                closest = candidate;
                closestDistance = candidateDistance;
            }
        }

        return closest;
    }

    private (int MeasureIndex, Fraction LocalPosition) EnsureCursorMeasure()
    {
        while (true)
        {
            Fraction measureStart = Fraction.Zero;
            for (int measureIndex = 0; measureIndex < CurrentScore.Measures.Length; measureIndex++)
            {
                Measure measure = CurrentScore.Measures[measureIndex];
                Fraction measureEnd = measureStart + measure.TimeSignature.Length;
                if (Cursor.Position < measureEnd)
                {
                    return (measureIndex, Cursor.Position - measureStart);
                }

                measureStart = measureEnd;
            }

            if (Cursor.Position != measureStart)
            {
                throw new InvalidOperationException("The input cursor is outside the score timeline.");
            }

            int finalMeasureIndex = CurrentScore.Measures.Length - 1;
            EditContext context = new(Cursor.StaffIndex, finalMeasureIndex, Cursor.VoiceNumber);
            Apply(new AppendMeasureCommand(), context);
        }
    }

    private MusicEvent FindEventAt(EditContext context, Fraction localPosition)
    {
        StaffMeasureKey key = new(context.StaffIndex, context.MeasureIndex);
        if (!CurrentScore.Content.TryGetValue(key, out StaffMeasure? staffMeasure))
        {
            throw new InvalidOperationException($"Staff {context.StaffIndex} has no content in measure {context.MeasureIndex + 1}.");
        }

        foreach (Voice voice in staffMeasure.Voices)
        {
            if (voice.Number != context.VoiceNumber)
            {
                continue;
            }

            foreach (MusicEvent musicEvent in voice.Events)
            {
                if (musicEvent.Onset == localPosition)
                {
                    return musicEvent;
                }
            }

            throw new InvalidOperationException("The input cursor must be aligned to the start of a musical event.");
        }

        throw new InvalidOperationException($"Voice {context.VoiceNumber} does not exist on staff {context.StaffIndex}.");
    }

    private void WriteRest()
    {
        if (Mode != ScoreInputMode.NoteEntry)
        {
            return;
        }

        (int measureIndex, Fraction localPosition) = EnsureCursorMeasure();
        EditContext context = new(Cursor.StaffIndex, measureIndex, Cursor.VoiceNumber);
        MusicEvent target = FindEventAt(context, localPosition);
        if (target is not Rest rest)
        {
            return;
        }

        if (rest.Duration != CurrentDuration)
        {
            Apply(new ChangeDurationCommand(rest.Id, CurrentDuration), context);
        }

        Cursor = Cursor with { Position = Cursor.Position + CurrentDuration.Length };
        NotifyStateChanged();
    }

    private void SetDuration(NoteValue value)
    {
        if (Mode != ScoreInputMode.NoteEntry)
        {
            return;
        }

        CurrentDuration = new Duration(value, CurrentDuration.Dots);
        NotifyStateChanged();
    }

    private void ToggleDot()
    {
        if (Mode != ScoreInputMode.NoteEntry)
        {
            return;
        }

        CurrentDuration = new Duration(CurrentDuration.Value, CurrentDuration.Dots == 0 ? 1 : 0);
        NotifyStateChanged();
    }

    private void TransposeLastPitch(int semitones, int diatonicSteps, int alterationDelta)
    {
        if (Mode != ScoreInputMode.NoteEntry || _lastEventId is not EventId eventId ||
            _lastContext is not EditContext context || _lastPitch is not Pitch pitch)
        {
            return;
        }

        Pitch changed = alterationDelta != 0
            ? new Pitch(pitch.Step, checked(pitch.Alter + alterationDelta), pitch.Octave)
            : pitch.Transpose(new Interval(diatonicSteps, semitones));
        Apply(new ChangePitchCommand(eventId, _lastNoteIndex, changed), context);
        _lastPitch = changed;
        NotifyStateChanged();
    }

    private void ToggleTie()
    {
        if (Mode != ScoreInputMode.NoteEntry || _lastEventId is not EventId eventId ||
            _lastContext is not EditContext context)
        {
            return;
        }

        Chord chord = CurrentScore.Content[new StaffMeasureKey(context.StaffIndex, context.MeasureIndex)]
            .Voices.First(voice => voice.Number == context.VoiceNumber)
            .Events.OfType<Chord>().First(musicEvent => musicEvent.Id == eventId);
        bool tiedToNext = !chord.Notes[_lastNoteIndex].TiedToNext;
        Apply(new ChangeTieCommand(eventId, _lastNoteIndex, tiedToNext), context);
        NotifyStateChanged();
    }

    private void Apply(IScoreCommand command, EditContext context)
    {
        Score next = command.Apply(CurrentScore, context);
        _undoStates.Push(CaptureState());
        _redoStates.Clear();
        _history.Push(next, command.Description, Selection.Empty);
    }

    private EditorStateSnapshot CaptureState() => new(
        Cursor,
        _lastEventId,
        _lastContext,
        _lastPitch,
        _lastNoteIndex);

    private void RestoreState(EditorStateSnapshot state)
    {
        Cursor = state.Cursor;
        _lastEventId = state.LastEventId;
        _lastContext = state.LastContext;
        _lastPitch = state.LastPitch;
        _lastNoteIndex = state.LastNoteIndex;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private readonly record struct EditorStateSnapshot(
        ScoreInputCursor Cursor,
        EventId? LastEventId,
        EditContext? LastContext,
        Pitch? LastPitch,
        int LastNoteIndex);
}
