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
        int staffBase = 0;
        for (int instrument = 0; instrument < score.Instruments.Length; instrument++)
        {
            int staffCount = score.Instruments[instrument].Staves.Length;
            Dictionary<Fraction, Dynamic> levels = CollectDynamics(score, staffBase, staffCount, dynamicAt);
            List<Fraction> levelPositions = [.. levels.Keys.Order()];
            for (int staff = staffBase; staff < staffBase + staffCount; staff++)
            {
                for (int voiceNumber = 1; voiceNumber <= 4; voiceNumber++)
                {
                    CollectVoice(score, instrument, staff, voiceNumber, settings, levels, levelPositions, articulationsAt, notes);
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
        return new Interpretation([.. notes], new TempoMap(settings.DefaultTempo, hints.Tempos));
    }

    private static Dictionary<Fraction, Dynamic> CollectDynamics(Score score, int staffBase, int staffCount,
        Dictionary<EventId, Dynamic> dynamicAt)
    {
        Dictionary<Fraction, Dynamic> levels = [];
        if (dynamicAt.Count == 0)
        {
            return levels;
        }

        Fraction measureStart = Fraction.Zero;
        for (int measure = 0; measure < score.Measures.Length; measure++)
        {
            for (int staff = staffBase; staff < staffBase + staffCount; staff++)
            {
                if (!score.Content.TryGetValue(new StaffMeasureKey(staff, measure), out StaffMeasure? content))
                {
                    continue;
                }

                foreach (Voice voice in content.Voices)
                {
                    foreach ((MusicEvent musicEvent, Fraction leafOnset, _) in voice.Events.Flatten())
                    {
                        if (dynamicAt.TryGetValue(musicEvent.Id, out Dynamic level))
                        {
                            levels[measureStart + leafOnset] = level;
                        }
                    }
                }
            }

            measureStart += score.Measures[measure].TimeSignature.Length;
        }

        return levels;
    }

    private static void CollectVoice(Score score, int instrument, int staff, int voiceNumber,
        InterpretationSettings settings, Dictionary<Fraction, Dynamic> levels, List<Fraction> levelPositions,
        Dictionary<EventId, List<Articulation>> articulationsAt, List<PerformedNote> notes)
    {
        // A note tied to the next is one sounding note; its articulation comes from the first event.
        Dictionary<int, int> open = [];
        List<(PerformedNote Note, IReadOnlyList<Articulation> Articulations)> pending = [];
        Fraction measureStart = Fraction.Zero;
        for (int measure = 0; measure < score.Measures.Length; measure++)
        {
            Voice? voice = score.Content.TryGetValue(new StaffMeasureKey(staff, measure), out StaffMeasure? content)
                ? content.Voices.FirstOrDefault(v => v.Number == voiceNumber) : null;
            if (voice is not null)
            {
                foreach ((MusicEvent musicEvent, Fraction leafOnset, Fraction leafLength) in voice.Events.Flatten())
                {
                    Fraction start = measureStart + leafOnset;
                    Dictionary<int, int> next = [];
                    if (musicEvent is Chord chord)
                    {
                        IReadOnlyList<Articulation> marks = articulationsAt.TryGetValue(chord.Id, out List<Articulation>? list)
                            ? list : Array.Empty<Articulation>();
                        Dynamic level = LevelAt(start, settings, levels, levelPositions);
                        foreach (Note written in chord.Notes)
                        {
                            int midi = written.Pitch.MidiNumber;
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

            measureStart += score.Measures[measure].TimeSignature.Length;
        }

        foreach ((PerformedNote note, IReadOnlyList<Articulation> marks) in pending)
        {
            Fraction gate = Gate(settings, marks);
            notes.Add(note with { SoundingLength = note.NotatedLength * gate });
        }
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
}
