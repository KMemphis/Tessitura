using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Adds a written pitch to a chord or replaces a rest with a note.</summary>
public sealed record InsertNoteCommand(EventId EventId, Pitch Pitch, Duration? Duration = null) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Insert note";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context) => ScoreCommandEditor.UpdateEvent(
        score,
        context,
        EventId,
        musicEvent => musicEvent switch
        {
            Chord chord => chord with { Notes = chord.Notes.Add(new Note(Pitch)) },
            Rest rest => new Chord(rest.Id, rest.Onset, Duration ?? rest.Duration, [new Note(Pitch)], StemDirection.Auto),
            _ => throw new InvalidOperationException("The target event cannot contain a note."),
        });
}

/// <summary>Adds a new measure after the score's current final measure.</summary>
public sealed record AppendMeasureCommand : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Append measure";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score.Measures.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("A measure cannot be appended to an empty score.");
        }

        Measure previousMeasure = score.Measures[^1];
        Measure nextMeasure = previousMeasure with { Number = previousMeasure.Number + 1 };
        ImmutableArray<Measure> measures = score.Measures.Add(nextMeasure);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = score.Content.ToBuilder();
        int staffCount = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            staffCount = checked(staffCount + instrument.Staves.Length);
        }

        NoteValue unit = (NoteValue)nextMeasure.TimeSignature.Denominator;
        if (!Enum.IsDefined(unit))
        {
            throw new InvalidOperationException(
                $"Cannot append a measure with denominator {nextMeasure.TimeSignature.Denominator}.");
        }

        for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
        {
            StaffMeasureKey previousKey = new(staffIndex, score.Measures.Length - 1);
            if (!score.Content.TryGetValue(previousKey, out StaffMeasure? previousContent))
            {
                throw new InvalidOperationException($"Staff {staffIndex} is missing content in the final measure.");
            }

            ImmutableArray<Voice>.Builder voices = ImmutableArray.CreateBuilder<Voice>(previousContent.Voices.Length);
            foreach (Voice previousVoice in previousContent.Voices)
            {
                ImmutableArray<MusicEvent>.Builder rests =
                    ImmutableArray.CreateBuilder<MusicEvent>(nextMeasure.TimeSignature.Numerator);
                Fraction onset = Fraction.Zero;
                for (int beat = 0; beat < nextMeasure.TimeSignature.Numerator; beat++)
                {
                    rests.Add(new Rest(
                        new EventId(Guid.NewGuid()),
                        onset,
                        new Duration(unit, 0)));
                    onset += new Fraction(1, nextMeasure.TimeSignature.Denominator);
                }

                voices.Add(new Voice(previousVoice.Number, rests.MoveToImmutable()));
            }

            content.Add(
                new StaffMeasureKey(staffIndex, measures.Length - 1),
                new StaffMeasure(voices.MoveToImmutable()));
        }

        return score with { Measures = measures, Content = content.ToImmutable() };
    }
}

/// <summary>Removes a note from a chord, replacing its last note with a rest.</summary>
public sealed record DeleteNoteCommand(EventId EventId, int NoteIndex) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Delete note";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context) => ScoreCommandEditor.UpdateEvent(
        score,
        context,
        EventId,
        musicEvent =>
        {
            if (musicEvent is not Chord chord)
            {
                throw new InvalidOperationException("The target event is not a chord.");
            }

            ScoreCommandEditor.ValidateNoteIndex(chord, NoteIndex);
            if (chord.Notes.Length == 1)
            {
                return new Rest(chord.Id, chord.Onset, chord.Duration);
            }

            return chord with { Notes = chord.Notes.RemoveAt(NoteIndex) };
        });
}

/// <summary>Changes the written pitch of one note in a chord.</summary>
public sealed record ChangePitchCommand(EventId EventId, int NoteIndex, Pitch Pitch) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Change pitch";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context) => ScoreCommandEditor.UpdateNote(
        score,
        context,
        EventId,
        NoteIndex,
        note => note with { Pitch = Pitch });
}

/// <summary>Changes the chromatic alteration of one written note.</summary>
public sealed record ChangeAlterationCommand(EventId EventId, int NoteIndex, int Alteration) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Change alteration";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context) => ScoreCommandEditor.UpdateNote(
        score,
        context,
        EventId,
        NoteIndex,
        note => note with { Pitch = new Pitch(note.Pitch.Step, Alteration, note.Pitch.Octave) });
}

/// <summary>Changes whether one written note is tied to its following matching note.</summary>
public sealed record ChangeTieCommand(EventId EventId, int NoteIndex, bool TiedToNext) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Change tie";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context) => ScoreCommandEditor.UpdateNote(
        score,
        context,
        EventId,
        NoteIndex,
        note => note with { TiedToNext = TiedToNext });
}

/// <summary>Changes the notated duration of a chord or rest.</summary>
public sealed record ChangeDurationCommand(EventId EventId, Duration Duration) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Change duration";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context) => ScoreCommandEditor.UpdateEvent(
        score,
        context,
        EventId,
        musicEvent => musicEvent switch
        {
            Chord chord => chord with { Duration = Duration },
            Rest rest => rest with { Duration = Duration },
            _ => throw new InvalidOperationException("The target event has no editable duration."),
        });
}

/// <summary>Changes the number of augmentation dots on a chord or rest.</summary>
public sealed record ChangeDotCountCommand(EventId EventId, int DotCount) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Change dot count";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context) => ScoreCommandEditor.UpdateEvent(
        score,
        context,
        EventId,
        musicEvent => musicEvent switch
        {
            Chord chord => chord with { Duration = new Duration(chord.Duration.Value, DotCount) },
            Rest rest => rest with { Duration = new Duration(rest.Duration.Value, DotCount) },
            _ => throw new InvalidOperationException("The target event has no editable duration."),
        });
}

internal static class ScoreCommandEditor
{
    public static Score UpdateEvent(
        Score score,
        EditContext context,
        EventId eventId,
        Func<MusicEvent, MusicEvent> update)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(update);

        StaffMeasureKey key = new(context.StaffIndex, context.MeasureIndex);
        if (!score.Content.TryGetValue(key, out StaffMeasure? staffMeasure))
        {
            throw new KeyNotFoundException($"Staff measure ({key.StaffIndex}, {key.MeasureIndex}) does not exist.");
        }

        int voiceIndex = FindVoiceIndex(staffMeasure, context.VoiceNumber);
        Voice voice = staffMeasure.Voices[voiceIndex];
        int eventIndex = FindEventIndex(voice, eventId);
        MusicEvent updatedEvent = update(voice.Events[eventIndex]);
        ImmutableArray<MusicEvent> updatedEvents = voice.Events.SetItem(eventIndex, updatedEvent);
        ImmutableArray<Voice> updatedVoices = staffMeasure.Voices.SetItem(voiceIndex, voice with { Events = updatedEvents });
        StaffMeasure updatedMeasure = staffMeasure with { Voices = updatedVoices };
        Score updatedScore = score with { Content = score.Content.SetItem(key, updatedMeasure) };
        return RhythmicScoreNormalizer.NormalizeVoice(updatedScore, context);
    }

    public static Score UpdateNote(
        Score score,
        EditContext context,
        EventId eventId,
        int noteIndex,
        Func<Note, Note> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return UpdateEvent(score, context, eventId, musicEvent =>
        {
            if (musicEvent is not Chord chord)
            {
                throw new InvalidOperationException("The target event is not a chord.");
            }

            ValidateNoteIndex(chord, noteIndex);
            return chord with { Notes = chord.Notes.SetItem(noteIndex, update(chord.Notes[noteIndex])) };
        });
    }

    public static void ValidateNoteIndex(Chord chord, int noteIndex)
    {
        if (chord.Notes.IsDefaultOrEmpty || noteIndex < 0 || noteIndex >= chord.Notes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(noteIndex));
        }
    }

    private static int FindVoiceIndex(StaffMeasure staffMeasure, int voiceNumber)
    {
        for (int index = 0; index < staffMeasure.Voices.Length; index++)
        {
            if (staffMeasure.Voices[index].Number == voiceNumber)
            {
                return index;
            }
        }

        throw new KeyNotFoundException($"Voice {voiceNumber} does not exist in the selected staff measure.");
    }

    private static int FindEventIndex(Voice voice, EventId eventId)
    {
        int match = -1;
        for (int index = 0; index < voice.Events.Length; index++)
        {
            if (voice.Events[index].Id != eventId)
            {
                continue;
            }

            if (match >= 0)
            {
                throw new InvalidOperationException($"Event ID '{eventId.Value}' occurs more than once in the voice.");
            }

            match = index;
        }

        return match >= 0
            ? match
            : throw new KeyNotFoundException($"Event ID '{eventId.Value}' does not exist in the selected voice.");
    }
}
