using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Playback.Performance;

/// <summary>Turns a score and its performance markings into the notes to play.</summary>
public static class Interpreter
{
    /// <summary>Interprets a score.</summary>
    /// <param name="score">The score snapshot.</param>
    /// <param name="hints">The dynamics, articulations and tempo marks; none by default.</param>
    /// <param name="settings">The translation tables; the defaults otherwise.</param>
    /// <returns>The sorted notes and the tempo map.</returns>
    public static Interpretation Interpret(Score score, PerformanceHints? hints = null, InterpretationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(score);
        hints ??= PerformanceHints.FromScore(score);
        settings ??= new InterpretationSettings();
        ImmutableArray<MeasureVisit> visits = RepeatUnfolder.Unfold(score);
        Dictionary<EventId, Dynamic> dynamicAt = [];
        foreach (DynamicMark mark in hints.Dynamics)
        {
            dynamicAt[mark.Event] = mark.Level;
        }

        Dictionary<EventId, List<Articulation>> articulationsAt = [];
        foreach (ArticulationMark mark in hints.Articulations)
        {
            if (!articulationsAt.TryGetValue(mark.Event, out List<Articulation>? list))
            {
                list = [];
                articulationsAt[mark.Event] = list;
            }

            list.Add(mark.Kind);
        }

        List<PerformedNote> notes = [];
        List<(int Staff, Fraction Start, Fraction End, int Shift)> octaves = OctaveSpans(score, visits);
        int staffBase = 0;
        for (int instrument = 0; instrument < score.Instruments.Length; instrument++)
        {
            int staffCount = score.Instruments[instrument].Staves.Length;
            Dictionary<Fraction, Dynamic> levels = CollectDynamics(score, visits, staffBase, staffCount, dynamicAt);
            List<Fraction> levelPositions = [.. levels.Keys.Order()];
            for (int staff = staffBase; staff < staffBase + staffCount; staff++)
            {
                for (int voiceNumber = 1; voiceNumber <= 4; voiceNumber++)
                {
                    CollectVoice(score, visits, instrument, staff, voiceNumber, settings,
                        levels, levelPositions, articulationsAt, notes, octaves);
                }
            }

            staffBase += staffCount;
        }

        notes.Sort((a, b) =>
        {
            int byStart = a.Start.CompareTo(b.Start);
            if (byStart != 0)
            {
                return byStart;
            }

            int byInstrument = a.Instrument.CompareTo(b.Instrument);
            return byInstrument != 0 ? byInstrument : a.Midi.CompareTo(b.Midi);
        });
        TempoMark[] tempos = ExpandTempos(score, visits, hints.Tempos);
        return new Interpretation([.. notes], new TempoMap(settings.DefaultTempo, tempos));
    }

    // An 8va line makes the notes from its first event through its last event sound an octave higher (8vb, lower).
    private static List<(int Staff, Fraction Start, Fraction End, int Shift)> OctaveSpans(
        Score score, ImmutableArray<MeasureVisit> visits)
    {
        List<(int, Fraction, Fraction, int)> spans = [];
        if (!score.SpannerList.Any(s => s.Kind is SpannerKind.OctaveUp or SpannerKind.OctaveDown))
        {
            return spans;
        }

        Dictionary<EventId, EventLocation> where = [];
        foreach (KeyValuePair<StaffMeasureKey, StaffMeasure> entry in score.Content)
        {
            if (entry.Key.MeasureIndex < 0 || entry.Key.MeasureIndex >= score.Measures.Length)
            {
                continue;
            }

            foreach (Voice voice in entry.Value.Voices)
            {
                foreach ((MusicEvent musicEvent, Fraction onset, Fraction length) in voice.Events.Flatten())
                {
                    where.TryAdd(musicEvent.Id, new EventLocation(entry.Key.StaffIndex,
                        entry.Key.MeasureIndex, onset, onset + length));
                }
            }
        }

        foreach (Spanner spanner in score.SpannerList)
        {
            if (spanner.Kind is SpannerKind.OctaveUp or SpannerKind.OctaveDown &&
                where.TryGetValue(spanner.Start, out EventLocation first) &&
                where.TryGetValue(spanner.End, out EventLocation last) &&
                first.Staff == last.Staff &&
                (first.MeasureIndex < last.MeasureIndex ||
                 (first.MeasureIndex == last.MeasureIndex && first.Start <= last.End)))
            {
                int shift = spanner.Kind == SpannerKind.OctaveUp ? 12 : -12;
                foreach (MeasureVisit visit in visits)
                {
                    if (visit.MeasureIndex < first.MeasureIndex || visit.MeasureIndex > last.MeasureIndex)
                    {
                        continue;
                    }

                    Fraction localStart = visit.MeasureIndex == first.MeasureIndex ? first.Start : Fraction.Zero;
                    Fraction localEnd = visit.MeasureIndex == last.MeasureIndex
                        ? last.End : score.Measures[visit.MeasureIndex].TimeSignature.Length;
                    if (localStart < localEnd)
                    {
                        spans.Add((first.Staff, visit.PlaybackPosition + localStart,
                            visit.PlaybackPosition + localEnd, shift));
                    }
                }
            }
        }

        return spans;
    }

    private static Dictionary<Fraction, Dynamic> CollectDynamics(Score score,
        ImmutableArray<MeasureVisit> visits, int staffBase, int staffCount,
        Dictionary<EventId, Dynamic> dynamicAt)
    {
        Dictionary<Fraction, Dynamic> levels = [];
        if (dynamicAt.Count == 0)
        {
            return levels;
        }

        foreach (MeasureVisit visit in visits)
        {
            for (int staff = staffBase; staff < staffBase + staffCount; staff++)
            {
                if (!score.Content.TryGetValue(new StaffMeasureKey(staff, visit.MeasureIndex), out StaffMeasure? content))
                {
                    continue;
                }

                foreach (Voice voice in content.Voices)
                {
                    foreach ((MusicEvent musicEvent, Fraction leafOnset, _) in voice.Events.Flatten())
                    {
                        if (dynamicAt.TryGetValue(musicEvent.Id, out Dynamic level))
                        {
                            levels[visit.PlaybackPosition + leafOnset] = level;
                        }
                    }
                }
            }
        }

        return levels;
    }

    private static void CollectVoice(Score score, ImmutableArray<MeasureVisit> visits,
        int instrument, int staff, int voiceNumber,
        InterpretationSettings settings, Dictionary<Fraction, Dynamic> levels, List<Fraction> levelPositions,
        Dictionary<EventId, List<Articulation>> articulationsAt, List<PerformedNote> notes,
        List<(int Staff, Fraction Start, Fraction End, int Shift)> octaves)
    {
        // A note tied to the next is one sounding note; its articulation comes from the first event.
        Dictionary<int, int> open = [];
        List<(PerformedNote Note, IReadOnlyList<Articulation> Articulations)> pending = [];
        foreach (MeasureVisit visit in visits)
        {
            if (visit.StartsSegment)
            {
                open.Clear();
            }

            Voice? voice = score.Content.TryGetValue(new StaffMeasureKey(staff, visit.MeasureIndex), out StaffMeasure? content)
                ? content.Voices.FirstOrDefault(v => v.Number == voiceNumber) : null;
            if (voice is not null)
            {
                foreach ((MusicEvent musicEvent, Fraction leafOnset, Fraction leafLength) in voice.Events.Flatten())
                {
                    Fraction start = visit.PlaybackPosition + leafOnset;
                    Dictionary<int, int> next = [];
                    if (musicEvent is Chord chord)
                    {
                        IReadOnlyList<Articulation> marks = articulationsAt.TryGetValue(chord.Id, out List<Articulation>? list)
                            ? list : Array.Empty<Articulation>();
                        Dynamic level = LevelAt(start, settings, levels, levelPositions);
                        foreach (Note written in chord.Notes)
                        {
                            int midi = written.Pitch.MidiNumber;
                            foreach ((int octaveStaff, Fraction octaveStart, Fraction octaveEnd, int shift) in octaves)
                            {
                                if (octaveStaff == staff && start >= octaveStart && start < octaveEnd)
                                {
                                    midi += shift;
                                }
                            }

                            int index;
                            if (open.TryGetValue(midi, out int existing) &&
                                pending[existing].Note.Start + pending[existing].Note.NotatedLength == start)
                            {
                                index = existing;
                                pending[index] = (pending[index].Note with
                                {
                                    NotatedLength = pending[index].Note.NotatedLength + leafLength,
                                }, pending[index].Articulations);
                            }
                            else
                            {
                                pending.Add((new PerformedNote(instrument, staff, voiceNumber, midi, start,
                                    leafLength, leafLength, Velocity(settings, level, marks)), marks));
                                index = pending.Count - 1;
                            }

                            if (written.TiedToNext)
                            {
                                next[midi] = index;
                            }
                        }
                    }

                    open = next;
                }
            }
        }

        foreach ((PerformedNote note, IReadOnlyList<Articulation> marks) in pending)
        {
            Fraction gate = Gate(settings, marks);
            notes.Add(note with { SoundingLength = note.NotatedLength * gate });
        }
    }

    private static TempoMark[] ExpandTempos(Score score, ImmutableArray<MeasureVisit> visits,
        ImmutableArray<TempoMark> sourceTempos)
    {
        Fraction[] measureStarts = MeasureStarts(score);
        List<TempoMark> expanded = [];
        foreach (TempoMark tempo in sourceTempos)
        {
            if (tempo.Position < Fraction.Zero || !double.IsFinite(tempo.QuarterNotesPerMinute) ||
                tempo.QuarterNotesPerMinute <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceTempos), "Tempo marks must have a nonnegative position and positive finite tempo.");
            }
        }

        foreach (MeasureVisit visit in visits)
        {
            Fraction sourceStart = measureStarts[visit.MeasureIndex];
            Fraction sourceEnd = sourceStart + score.Measures[visit.MeasureIndex].TimeSignature.Length;
            foreach (TempoMark tempo in sourceTempos)
            {
                if (tempo.Position >= sourceStart && tempo.Position < sourceEnd)
                {
                    expanded.Add(tempo with
                    {
                        Position = visit.PlaybackPosition + tempo.Position - sourceStart,
                    });
                }
            }
        }

        return [.. expanded];
    }

    private static Fraction[] MeasureStarts(Score score)
    {
        Fraction[] starts = new Fraction[score.Measures.Length];
        Fraction position = Fraction.Zero;
        for (int index = 0; index < score.Measures.Length; index++)
        {
            starts[index] = position;
            position += score.Measures[index].TimeSignature.Length;
        }

        return starts;
    }

    private static Dynamic LevelAt(Fraction position, InterpretationSettings settings,
        Dictionary<Fraction, Dynamic> levels, List<Fraction> levelPositions)
    {
        Dynamic level = settings.DefaultDynamic;
        foreach (Fraction at in levelPositions)
        {
            if (at > position)
            {
                break;
            }

            level = levels[at];
        }

        return level;
    }

    private static int Velocity(InterpretationSettings settings, Dynamic level, IReadOnlyList<Articulation> marks)
    {
        int velocity = settings.VelocityOf(level);
        if (marks.Contains(Articulation.Marcato))
        {
            velocity += settings.MarcatoBoost;
        }
        else if (marks.Contains(Articulation.Accent))
        {
            velocity += settings.AccentBoost;
        }

        return Math.Clamp(velocity, 1, 127);
    }

    // The shortest gate wins when several articulations are combined, so staccato stays short under an accent.
    private static Fraction Gate(InterpretationSettings settings, IReadOnlyList<Articulation> marks)
    {
        Fraction gate = settings.NormalGate;
        bool marked = false;
        foreach (Articulation mark in marks)
        {
            Fraction? candidate = mark switch
            {
                Articulation.Staccato => settings.StaccatoGate,
                Articulation.Staccatissimo => settings.StaccatissimoGate,
                Articulation.Tenuto => settings.TenutoGate,
                Articulation.Marcato => settings.MarcatoGate,
                _ => null,
            };
            if (candidate is Fraction value && (!marked || value < gate))
            {
                gate = value;
                marked = true;
            }
        }

        return gate;
    }

    private readonly record struct EventLocation(int Staff, int MeasureIndex, Fraction Start, Fraction End);
}
