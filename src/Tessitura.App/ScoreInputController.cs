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

/// <summary>Describes the single selected event shown in the property inspector.</summary>
public sealed record ScoreEventProperties(
    EventId EventId,
    string Kind,
    Duration Duration,
    ImmutableArray<Note> Notes,
    bool HasSelectedNote,
    int StaffIndex,
    int MeasureIndex,
    int MeasureNumber,
    int VoiceNumber,
    Fraction Position);

/// <summary>Applies keyboard note-entry actions to an immutable score history.</summary>
public sealed class ScoreInputController
{
    private readonly History _history;
    private readonly Stack<EditorStateSnapshot> _undoStates = new();
    private readonly Stack<EditorStateSnapshot> _redoStates = new();
    private ClipboardFragment? _clipboard;
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

    /// <summary>Gets the selection associated with the current score snapshot.</summary>
    public Selection CurrentSelection => _history.CurrentSelection;

    /// <summary>Gets the current input mode.</summary>
    public ScoreInputMode Mode { get; private set; }

    /// <summary>Gets the musical input cursor.</summary>
    public ScoreInputCursor Cursor { get; private set; }

    /// <summary>Gets the duration used by the next note or rest action.</summary>
    public Duration CurrentDuration { get; private set; } = new(NoteValue.Quarter, 0);

    /// <summary>Gets the pitch of the most recently entered note, if any.</summary>
    public Pitch? LastEnteredPitch => _lastPitch;

    /// <summary>Gets the selected event's editable properties, or null for no single event.</summary>
    public ScoreEventProperties? SelectedEventProperties
    {
        get
        {
            if (CurrentSelection.Items.Length != 1)
            {
                return null;
            }

            SelectionItem item = CurrentSelection.Items[0];
            EventLocation location = FindEventLocation(item.EventId);
            ImmutableArray<Note> notes = location.Event is Chord chord
                ? item.NoteIndex is int noteIndex
                    ? [chord.Notes[noteIndex]]
                    : chord.Notes
                : ImmutableArray<Note>.Empty;
            string kind = location.Event switch
            {
                Chord { Notes.Length: 1 } => "Nota",
                Chord => "Acorde",
                Rest => "Silencio",
                _ => "Evento",
            };
            bool hasSelectedNote = location.Event is Chord selectedChord &&
                (item.NoteIndex is not null || selectedChord.Notes.Length == 1);
            return new ScoreEventProperties(location.Event.Id, kind, location.Event.Duration, notes,
                hasSelectedNote, location.Context.StaffIndex, location.Context.MeasureIndex,
                CurrentScore.Measures[location.Context.MeasureIndex].Number,
                location.Context.VoiceNumber, location.Position.Position);
        }
    }

    /// <summary>Creates the registered actions for note entry and its keyboard shortcuts.</summary>
    /// <returns>The actions that should be added to the application's action registry.</returns>
    public ImmutableArray<ActionDefinition> CreateActions()
    {
        ImmutableArray<ActionDefinition>.Builder actions = ImmutableArray.CreateBuilder<ActionDefinition>();
        actions.Add(new ActionDefinition("score.note-entry", "Modo de entrada de notas", "N", EnterNoteEntry));
        actions.Add(new ActionDefinition("score.cursor.staff-up", "Cursor al pentagrama superior", "Alt+Up",
            () => MoveCursorStaff(-1)));
        actions.Add(new ActionDefinition("score.cursor.staff-down", "Cursor al pentagrama inferior", "Alt+Down",
            () => MoveCursorStaff(1)));
        actions.Add(new ActionDefinition("score.cursor.start", "Cursor al inicio de la partitura", "Ctrl+Home",
            MoveCursorToStart));
        (ArticulationKind Kind, string Name, string Shortcut)[] marks =
        [
            (ArticulationKind.Staccato, "staccato", "Alt+S"), (ArticulationKind.Staccatissimo, "staccatissimo", "Alt+Shift+S"),
            (ArticulationKind.Tenuto, "tenuto", "Alt+T"), (ArticulationKind.Accent, "acento", "Alt+A"),
            (ArticulationKind.Marcato, "marcato", "Alt+M"), (ArticulationKind.Fermata, "calderón", "Alt+F"),
            (ArticulationKind.Trill, "trino", "Alt+R"), (ArticulationKind.Mordent, "mordente", "Alt+Shift+M"),
            (ArticulationKind.Turn, "grupeto", "Alt+G"),
        ];
        foreach ((SpannerKind kind, string name, string shortcut) in new (SpannerKind, string, string)[]
        {
            (SpannerKind.Crescendo, "crescendo", "Alt+Shift+C"), (SpannerKind.Diminuendo, "diminuendo", "Alt+Shift+D"),
            (SpannerKind.OctaveUp, "8va", "Alt+Shift+O"), (SpannerKind.OctaveDown, "8vb", "Alt+Shift+B"), (SpannerKind.Pedal, "pedal", "Alt+Shift+P"),
        })
        {
            actions.Add(new ActionDefinition($"spanner.{kind.ToString().ToLowerInvariant()}", $"Añadir {name} sobre la selección", shortcut,
                () => SpanSelection(kind)));
        }

        actions.Add(new ActionDefinition("spanner.slur", "Ligadura de expresión sobre la selección", "S", () => SlurSelection()));
        foreach ((ArticulationKind kind, string name, string shortcut) in marks)
        {
            actions.Add(new ActionDefinition($"articulation.{kind.ToString().ToLowerInvariant()}", $"Alternar {name}", shortcut,
                () => ToggleSelectedArticulation(kind)));
        }

        actions.Add(new ActionDefinition("edit.copy", "Copiar", "Ctrl+C", () => CopySelection()));
        actions.Add(new ActionDefinition("edit.paste", "Pegar", "Ctrl+V", () => PasteClipboard()));
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

    /// <summary>Selects an event, extends a musical range, or adds it to the selection list.</summary>
    /// <param name="eventId">The stable score-event identifier.</param>
    /// <param name="extendRange">Whether to retain the current anchor and extend to the event.</param>
    /// <param name="additive">Whether to toggle the event in a multi-element selection.</param>
    public void SelectEvent(EventId eventId, bool extendRange = false, bool additive = false)
    {
        EventLocation target = FindEventLocation(eventId);
        SelectionItem targetItem = MakeSelectionItem(target.Event);

        if (extendRange && !CurrentSelection.Items.IsDefaultOrEmpty)
        {
            MusicalSelectionPoint anchor = CurrentSelection.Range?.Anchor ??
                FindEventLocation(CurrentSelection.Items[0].EventId).Position;
            SelectionRange range = new(anchor, target.Position);
            _history.SetSelection(BuildRangeSelection(range));
        }
        else if (additive)
        {
            ImmutableArray<SelectionItem>.Builder items =
                ImmutableArray.CreateBuilder<SelectionItem>(CurrentSelection.Items.Length + 1);
            bool removed = false;
            foreach (SelectionItem item in CurrentSelection.Items)
            {
                if (!removed && item == targetItem)
                {
                    removed = true;
                    continue;
                }

                items.Add(item);
            }

            if (!removed)
            {
                items.Add(targetItem);
            }

            _history.SetSelection(new Selection(items.ToImmutable()));
        }
        else
        {
            _history.SetSelection(new Selection([targetItem]));
        }

        NotifyStateChanged();
    }

    /// <summary>Checks whether an identifier belongs to a current score event.</summary>
    /// <param name="eventId">The event identifier to check.</param>
    /// <returns>Whether the current score contains the event.</returns>
    public bool ContainsEvent(EventId eventId)
    {
        foreach (StaffMeasure staffMeasure in CurrentScore.Content.Values)
        {
            foreach (Voice voice in staffMeasure.Voices)
            {
                foreach (MusicEvent musicEvent in voice.Events)
                {
                    if (musicEvent.Id == eventId)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Changes the duration of the single selected score event.</summary>
    /// <param name="duration">The written duration.</param>
    /// <returns>Whether a selected event was changed.</returns>
    public bool ChangeSelectedDuration(Duration duration) =>
        ApplySelectedEvent(properties => new ChangeDurationCommand(properties.EventId, duration));

    /// <summary>Changes the dot count of the single selected score event.</summary>
    /// <param name="dotCount">The number of augmentation dots.</param>
    /// <returns>Whether a selected event was changed.</returns>
    public bool ChangeSelectedDotCount(int dotCount) =>
        ApplySelectedEvent(properties => new ChangeDotCountCommand(properties.EventId, dotCount));

    /// <summary>Moves the input cursor to another staff, keeping its position and voice.</summary>
    /// <param name="delta">Negative moves up, positive moves down.</param>
    /// <returns>Whether the cursor changed staff.</returns>
    public bool MoveCursorStaff(int delta)
    {
        int staffCount = CountStaves(CurrentScore);
        int target = Math.Clamp(Cursor.StaffIndex + delta, 0, staffCount - 1);
        if (target == Cursor.StaffIndex ||
            !CurrentScore.Content.TryGetValue(new StaffMeasureKey(target, 0), out StaffMeasure? staffMeasure) ||
            !staffMeasure.Voices.Any(voice => voice.Number == Cursor.VoiceNumber))
        {
            return false;
        }

        Cursor = Cursor with { StaffIndex = target };
        _lastPitch = null;
        NotifyStateChanged();
        return true;
    }

    /// <summary>Places the cursor at a position without touching the history (used by the playback head).</summary>
    /// <param name="position">The absolute position in whole-note units, not negative.</param>
    public void SetCursorPosition(Fraction position)
    {
        if (position < Fraction.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (position != Cursor.Position)
        {
            Cursor = Cursor with { Position = position };
            NotifyStateChanged();
        }
    }

    /// <summary>Moves the input cursor to the beginning of the score on its staff.</summary>
    public void MoveCursorToStart()
    {
        Cursor = Cursor with { Position = Fraction.Zero };
        _lastPitch = null;
        NotifyStateChanged();
    }

    /// <summary>Writes the given sounding pitches as one chord at the cursor and advances by the current duration.</summary>
    /// <param name="midiNumbers">The MIDI note numbers played together.</param>
    /// <returns>Whether anything was written; only in note-entry mode.</returns>
    public bool EnterChord(IReadOnlyList<int> midiNumbers)
    {
        ArgumentNullException.ThrowIfNull(midiNumbers);
        if (Mode != ScoreInputMode.NoteEntry || midiNumbers.Count == 0)
        {
            return false;
        }

        (int measureIndex, Fraction localPosition) = EnsureCursorMeasure();
        bool sharps = CurrentScore.Measures[measureIndex].KeySignature.Fifths >= 0;
        EditContext context = new(Cursor.StaffIndex, measureIndex, Cursor.VoiceNumber);
        MusicEvent target = FindEventAt(context, localPosition);
        bool first = true;
        Pitch last = default;
        foreach (int midi in midiNumbers.Order().Distinct())
        {
            Pitch pitch = SpellMidi(midi, sharps);
            Duration? duration = first && target is Rest ? CurrentDuration : null;
            Apply(new InsertNoteCommand(target.Id, pitch, duration), context);
            first = false;
            last = pitch;
        }

        _lastEventId = target.Id;
        _lastContext = context;
        _lastNoteIndex = midiNumbers.Distinct().Count() - 1;
        _lastPitch = last;
        Cursor = Cursor with { Position = Cursor.Position + CurrentDuration.Length };
        NotifyStateChanged();
        return true;
    }

    private static Pitch SpellMidi(int midi, bool sharps)
    {
        (Step Step, int Alter)[] sharpNames =
            [(Step.C, 0), (Step.C, 1), (Step.D, 0), (Step.D, 1), (Step.E, 0), (Step.F, 0), (Step.F, 1), (Step.G, 0), (Step.G, 1), (Step.A, 0), (Step.A, 1), (Step.B, 0)];
        (Step Step, int Alter)[] flatNames =
            [(Step.C, 0), (Step.D, -1), (Step.D, 0), (Step.E, -1), (Step.E, 0), (Step.F, 0), (Step.G, -1), (Step.G, 0), (Step.A, -1), (Step.A, 0), (Step.B, -1), (Step.B, 0)];
        (Step step, int alter) = (sharps ? sharpNames : flatNames)[midi % 12];
        return new Pitch(step, alter, midi / 12 - 1);
    }

    /// <summary>Gets whether the internal clipboard holds a copied fragment.</summary>
    public bool CanPaste => _clipboard is not null;

    /// <summary>Copies the selected events into the internal clipboard.</summary>
    /// <returns>Whether the selection contained events to copy.</returns>
    public bool CopySelection()
    {
        ClipboardFragment? fragment = ClipboardFragment.Copy(CurrentScore, CurrentSelection);
        if (fragment is null)
        {
            return false;
        }

        _clipboard = fragment;
        NotifyStateChanged();
        return true;
    }

    /// <summary>
    /// Pastes the clipboard at the first selected event (its staff, voice and position), or at the
    /// input cursor when nothing is selected or note entry is active.
    /// </summary>
    /// <returns>Whether the fragment could be pasted.</returns>
    public bool PasteClipboard()
    {
        if (_clipboard is not ClipboardFragment fragment)
        {
            return false;
        }

        EditContext context = new(Cursor.StaffIndex, 0, Cursor.VoiceNumber);
        Fraction position = Cursor.Position;
        if (Mode != ScoreInputMode.NoteEntry && !CurrentSelection.Items.IsDefaultOrEmpty)
        {
            EventLocation? first = null;
            foreach (SelectionItem item in CurrentSelection.Items)
            {
                EventLocation candidate = FindEventLocation(item.EventId);
                if (first is null || candidate.Position.Position < first.Value.Position.Position ||
                    (candidate.Position.Position == first.Value.Position.Position &&
                     candidate.Position.StaffIndex < first.Value.Position.StaffIndex))
                {
                    first = candidate;
                }
            }

            context = first!.Value.Context;
            position = first.Value.Position.Position;
        }

        try
        {
            Apply(new PasteCommand(fragment, position), context);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            return false;
        }

        NotifyStateChanged();
        return true;
    }

    /// <summary>Attaches what the user typed in a popover (dynamic, tempo, chord symbol or text) to the selected event.</summary>
    /// <param name="kind">Which popover the text came from.</param>
    /// <param name="text">The typed text, such as "mf", "q=120" or "Cmaj7".</param>
    /// <returns>Whether one event was selected and the text was understood.</returns>
    public bool SubmitText(TextEntryKind kind, string text)
    {
        ScoreEventProperties? selected = SelectedEventProperties;
        if (selected is not ScoreEventProperties properties ||
            !TextEntryParser.TryParse(text, kind, properties.EventId, out Attachment? attachment) || attachment is null)
        {
            return false;
        }

        return ApplySelectedEvent(_ => new AddAttachmentCommand(attachment));
    }

    /// <summary>Slurs from the first to the last selected event of the same staff.</summary>
    /// <returns>Whether at least two events of one staff were selected.</returns>
    public bool SlurSelection() => SpanSelection(SpannerKind.Slur);

    /// <summary>Adds a spanner of a kind from the first to the last selected event of the same staff.</summary>
    /// <param name="kind">The kind of line.</param>
    /// <returns>Whether at least two events of one staff were selected.</returns>
    public bool SpanSelection(SpannerKind kind)
    {
        List<EventLocation> locations = [];
        foreach (SelectionItem item in CurrentSelection.Items)
        {
            EventLocation location = FindEventLocation(item.EventId);
            if (locations.Count == 0 || location.Position.StaffIndex == locations[0].Position.StaffIndex)
            {
                locations.Add(location);
            }
        }

        if (locations.Count < 2)
        {
            return false;
        }

        locations.Sort((a, b) => a.Position.Position.CompareTo(b.Position.Position));
        EventLocation first = locations[0];
        EventLocation last = locations[^1];
        if (first.Event.Id == last.Event.Id)
        {
            return false;
        }

        Apply(new AddSpannerCommand(new Spanner(first.Event.Id, last.Event.Id, kind)), first.Context, CurrentSelection);
        NotifyStateChanged();
        return true;
    }

    /// <summary>Adds an articulation to the selected event, or removes it if already present.</summary>
    /// <param name="kind">The articulation or ornament.</param>
    /// <returns>Whether a single event was selected.</returns>
    public bool ToggleSelectedArticulation(ArticulationKind kind) =>
        ApplySelectedEvent(properties => new ToggleArticulationCommand(properties.EventId, kind));

    /// <summary>Changes the initial clef on the selected event's staff.</summary>
    /// <param name="clef">The new staff clef.</param>
    /// <returns>Whether a single score event was selected.</returns>
    public bool ChangeSelectedClef(Clef clef) =>
        ApplySelectedEvent(_ => new ChangeClefCommand(clef));

    /// <summary>Changes the key signature from the selected event's measure onward.</summary>
    /// <param name="keySignature">The new key signature.</param>
    /// <returns>Whether a single score event was selected.</returns>
    public bool ChangeSelectedKeySignature(KeySignature keySignature) =>
        ApplySelectedEvent(_ => new ChangeKeySignatureCommand(keySignature));

    /// <summary>Changes the meter in the selected event's measure.</summary>
    /// <param name="timeSignature">The new time signature.</param>
    /// <returns>Whether a single score event was selected.</returns>
    public bool ChangeSelectedTimeSignature(TimeSignature timeSignature) =>
        ApplySelectedEvent(_ => new ChangeTimeSignatureCommand(timeSignature));

    /// <summary>Changes the written accidental on the selected note.</summary>
    /// <param name="alteration">The chromatic alteration in semitones.</param>
    /// <returns>Whether a note was selected and changed.</returns>
    public bool ChangeSelectedAlteration(int alteration)
    {
        if (CurrentSelection.Items.Length != 1)
        {
            return false;
        }

        SelectionItem item = CurrentSelection.Items[0];
        EventLocation location = FindEventLocation(item.EventId);
        if (location.Event is not Chord chord)
        {
            return false;
        }

        int noteIndex = item.NoteIndex ?? (chord.Notes.Length == 1 ? 0 : -1);
        if (noteIndex < 0)
        {
            return false;
        }

        Apply(new ChangeAlterationCommand(item.EventId, noteIndex, alteration),
            location.Context, CurrentSelection);
        NotifyStateChanged();
        return true;
    }

    /// <summary>Toggles the tie on the selected note.</summary>
    /// <returns>Whether a note was selected and changed.</returns>
    public bool ToggleSelectedTie()
    {
        if (CurrentSelection.Items.Length != 1)
        {
            return false;
        }

        SelectionItem item = CurrentSelection.Items[0];
        EventLocation location = FindEventLocation(item.EventId);
        if (location.Event is not Chord chord)
        {
            return false;
        }

        int noteIndex = item.NoteIndex ?? (chord.Notes.Length == 1 ? 0 : -1);
        if (noteIndex < 0)
        {
            return false;
        }

        bool tiedToNext = !chord.Notes[noteIndex].TiedToNext;
        Apply(new ChangeTieCommand(item.EventId, noteIndex, tiedToNext),
            location.Context, CurrentSelection);
        NotifyStateChanged();
        return true;
    }

    private void WriteNote(Step step)
    {
        if (Mode != ScoreInputMode.NoteEntry)
        {
            return;
        }

        (int measureIndex, Fraction localPosition) = EnsureCursorMeasure();
        Pitch pitch = NearestPitch(step);
        pitch = new Pitch(pitch.Step, ResolveInputAlteration(measureIndex, localPosition, pitch), pitch.Octave);
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

    private int ResolveInputAlteration(int measureIndex, Fraction localPosition, Pitch pitch)
    {
        int alteration = CurrentScore.Measures[measureIndex].KeySignature.GetAlter(pitch.Step);
        if (!CurrentScore.Content.TryGetValue(new StaffMeasureKey(Cursor.StaffIndex, measureIndex),
            out StaffMeasure? staffMeasure))
        {
            return alteration;
        }

        foreach (Voice voice in staffMeasure.Voices)
        {
            foreach (MusicEvent musicEvent in voice.Events)
            {
                if (musicEvent.Onset >= localPosition)
                {
                    break;
                }

                if (musicEvent is not Chord chord)
                {
                    continue;
                }

                foreach (Note note in chord.Notes)
                {
                    if (note.Pitch.Step == pitch.Step && note.Pitch.Octave == pitch.Octave)
                    {
                        alteration = note.Pitch.Alter;
                    }
                }
            }
        }

        return alteration;
    }

    private Clef GetCursorClef()
    {
        int remaining = Cursor.StaffIndex;
        foreach (Instrument instrument in CurrentScore.Instruments)
        {
            if (remaining < instrument.Staves.Length)
            {
                return instrument.Staves[remaining].InitialClef;
            }

            remaining -= instrument.Staves.Length;
        }

        return Clef.Treble;
    }

    private Pitch NearestPitch(Step step)
    {
        if (_lastPitch is not Pitch previous)
        {
            // Start near the middle of the staff's range: bass-clef staves sit an octave lower.
            return new Pitch(step, 0, GetCursorClef() == Clef.Bass ? 3 : 4);
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

    private Selection BuildRangeSelection(SelectionRange range)
    {
        int firstStaff = Math.Min(range.Anchor.StaffIndex, range.Target.StaffIndex);
        int lastStaff = Math.Max(range.Anchor.StaffIndex, range.Target.StaffIndex);
        Fraction firstPosition = range.Anchor.Position < range.Target.Position
            ? range.Anchor.Position
            : range.Target.Position;
        Fraction lastPosition = range.Anchor.Position > range.Target.Position
            ? range.Anchor.Position
            : range.Target.Position;
        ImmutableArray<SelectionItem>.Builder items = ImmutableArray.CreateBuilder<SelectionItem>();

        for (int staffIndex = firstStaff; staffIndex <= lastStaff; staffIndex++)
        {
            Fraction measureStart = Fraction.Zero;
            for (int measureIndex = 0; measureIndex < CurrentScore.Measures.Length; measureIndex++)
            {
                Measure measure = CurrentScore.Measures[measureIndex];
                if (CurrentScore.Content.TryGetValue(new StaffMeasureKey(staffIndex, measureIndex),
                    out StaffMeasure? staffMeasure))
                {
                    foreach (Voice voice in staffMeasure.Voices)
                    {
                        foreach (MusicEvent musicEvent in voice.Events)
                        {
                            Fraction position = measureStart + musicEvent.Onset;
                            if (position >= firstPosition && position <= lastPosition)
                            {
                                items.Add(MakeSelectionItem(musicEvent));
                            }
                        }
                    }
                }

                measureStart += measure.TimeSignature.Length;
            }
        }

        return new Selection(items.ToImmutable(), range);
    }

    private EventLocation FindEventLocation(EventId eventId)
    {
        for (int staffIndex = 0; staffIndex < CountStaves(CurrentScore); staffIndex++)
        {
            Fraction measureStart = Fraction.Zero;
            for (int measureIndex = 0; measureIndex < CurrentScore.Measures.Length; measureIndex++)
            {
                if (CurrentScore.Content.TryGetValue(new StaffMeasureKey(staffIndex, measureIndex),
                    out StaffMeasure? staffMeasure))
                {
                    foreach (Voice voice in staffMeasure.Voices)
                    {
                        foreach (MusicEvent musicEvent in voice.Events)
                        {
                            if (musicEvent.Id == eventId)
                            {
                                return new EventLocation(
                                    musicEvent,
                                    new MusicalSelectionPoint(staffIndex, measureStart + musicEvent.Onset),
                                    new EditContext(staffIndex, measureIndex, voice.Number));
                            }
                        }
                    }
                }

                measureStart += CurrentScore.Measures[measureIndex].TimeSignature.Length;
            }
        }

        throw new KeyNotFoundException($"Score event '{eventId.Value}' was not found.");
    }

    private static SelectionItem MakeSelectionItem(MusicEvent musicEvent) =>
        musicEvent is Chord { Notes.Length: 1 } ? new SelectionItem(musicEvent.Id, 0) :
        new SelectionItem(musicEvent.Id);

    private static int CountStaves(Score score)
    {
        int count = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            count = checked(count + instrument.Staves.Length);
        }

        return count;
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

    private bool ApplySelectedEvent(Func<ScoreEventProperties, IScoreCommand> createCommand)
    {
        ScoreEventProperties? properties = SelectedEventProperties;
        if (properties is not ScoreEventProperties selected)
        {
            return false;
        }

        EventLocation location = FindEventLocation(selected.EventId);
        Apply(createCommand(selected), location.Context, CurrentSelection);
        NotifyStateChanged();
        return true;
    }

    private void Apply(IScoreCommand command, EditContext context, Selection? selection = null)
    {
        Score next = command.Apply(CurrentScore, context);
        _undoStates.Push(CaptureState());
        _redoStates.Clear();
        _history.Push(next, command.Description, selection ?? Selection.Empty);
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

    private readonly record struct EventLocation(
        MusicEvent Event,
        MusicalSelectionPoint Position,
        EditContext Context);
}
