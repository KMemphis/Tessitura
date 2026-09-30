using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Tessitura.Core;
using MidiNote = Melanchall.DryWetMidi.Interaction.Note;
using ScoreNote = Tessitura.Core.Note;
using Chord = Tessitura.Core.Chord;
using TimeSignature = Tessitura.Core.TimeSignature;

namespace Tessitura.IO.Midi;

/// <summary>Exports a score as a Standard MIDI File of type 1: one tempo track plus one track per instrument.</summary>
public static class MidiExporter
{
    /// <summary>The time division written to the file, fine enough for a dotted 128th note.</summary>
    public const short TicksPerQuarterNote = 960;

    /// <summary>The default velocity of exported notes; dynamics arrive with the interpretation model (F3.5).</summary>
    public const int DefaultVelocity = 80;

    /// <summary>Builds the MIDI file of a score.</summary>
    /// <param name="score">The score to export.</param>
    /// <param name="tempo">The constant tempo in quarter notes per minute; the model has no tempo map yet.</param>
    /// <returns>A type-1 MIDI file.</returns>
    public static MidiFile ToMidiFile(Score score, int tempo = 120)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (tempo is < 20 or > 400)
        {
            throw new ArgumentOutOfRangeException(nameof(tempo));
        }

        TrackChunk conductor = new();
        conductor.Events.Add(new SequenceTrackNameEvent(score.Metadata.Title));
        conductor.Events.Add(new SetTempoEvent(60_000_000L / tempo));
        List<TrackChunk> tracks = [conductor];
        long ticksPerWhole = 4L * TicksPerQuarterNote;

        long measureStart = 0;
        TimeSignature? lastTime = null;
        KeySignature? lastKey = null;
        using (var manager = conductor.ManageTimedEvents())
        {
            foreach (Measure measure in score.Measures)
            {
                if (lastTime != measure.TimeSignature)
                {
                    manager.Objects.Add(new TimedEvent(
                        new TimeSignatureEvent((byte)measure.TimeSignature.Numerator, (byte)measure.TimeSignature.Denominator), measureStart));
                    lastTime = measure.TimeSignature;
                }

                if (lastKey != measure.KeySignature)
                {
                    manager.Objects.Add(new TimedEvent(new KeySignatureEvent((sbyte)measure.KeySignature.Fifths, 0), measureStart));
                    lastKey = measure.KeySignature;
                }

                measureStart += ToTicks(measure.TimeSignature.Length, ticksPerWhole);
            }
        }

        int staffBase = 0;
        int channel = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            TrackChunk track = new();
            track.Events.Add(new SequenceTrackNameEvent(instrument.Name));
            List<MidiNote> notes = CollectNotes(score, staffBase, instrument.Staves.Length, ticksPerWhole, (FourBitNumber)channel);
            using (var manager = track.ManageNotes())
            {
                manager.Objects.Add(notes);
            }

            tracks.Add(track);
            staffBase += instrument.Staves.Length;
            channel = (channel + 1) % 16;
            if (channel == 9)
            {
                channel = 10; // channel 10 is reserved for percussion in General MIDI
            }
        }

        return new MidiFile(tracks) { TimeDivision = new TicksPerQuarterNoteTimeDivision(TicksPerQuarterNote) };
    }

    /// <summary>Writes a score to a MIDI file, replacing the target atomically.</summary>
    /// <param name="path">The destination .mid path.</param>
    /// <param name="score">The score to export.</param>
    /// <param name="tempo">The constant tempo in quarter notes per minute.</param>
    public static void Save(string path, Score score, int tempo = 120)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        MidiFile file = ToMidiFile(score, tempo);
        string fullPath = Path.GetFullPath(path);
        string temporary = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            file.Write(temporary, overwriteFile: true);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static List<MidiNote> CollectNotes(Score score, int staffBase, int staffCount, long ticksPerWhole, FourBitNumber channel)
    {
        List<MidiNote> notes = [];
        for (int staff = staffBase; staff < staffBase + staffCount; staff++)
        {
            Dictionary<int, Dictionary<int, MidiNote>> open = [];
            long measureStart = 0;
            for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
            {
                Measure measure = score.Measures[measureIndex];
                if (score.Content.TryGetValue(new StaffMeasureKey(staff, measureIndex), out StaffMeasure? content))
                {
                    foreach (Voice voice in content.Voices)
                    {
                        if (!open.TryGetValue(voice.Number, out Dictionary<int, MidiNote>? tied))
                        {
                            tied = [];
                            open[voice.Number] = tied;
                        }

                        foreach ((MusicEvent musicEvent, Fraction leafOnset, Fraction leafLength) in voice.Events.Flatten())
                        {
                            long start = measureStart + ToTicks(leafOnset, ticksPerWhole);
                            long length = ToTicks(leafLength, ticksPerWhole);
                            Dictionary<int, MidiNote> next = [];
                            if (musicEvent is Chord chord)
                            {
                                foreach (ScoreNote written in chord.Notes)
                                {
                                    int midi = written.Pitch.MidiNumber;
                                    if (midi is < 0 or > 127)
                                    {
                                        continue;
                                    }

                                    MidiNote sounding;
                                    if (tied.TryGetValue(midi, out MidiNote? continued) && continued.Time + continued.Length == start)
                                    {
                                        continued.Length += length;
                                        sounding = continued;
                                    }
                                    else
                                    {
                                        sounding = new MidiNote((SevenBitNumber)midi, length, start)
                                        {
                                            Channel = channel,
                                            Velocity = (SevenBitNumber)DefaultVelocity,
                                        };
                                        notes.Add(sounding);
                                    }

                                    if (written.TiedToNext)
                                    {
                                        next[midi] = sounding;
                                    }
                                }
                            }

                            tied.Clear();
                            foreach ((int midi, MidiNote sounding) in next)
                            {
                                tied[midi] = sounding;
                            }
                        }
                    }
                }

                measureStart += ToTicks(measure.TimeSignature.Length, ticksPerWhole);
            }
        }

        return notes;
    }

    private static long ToTicks(Fraction wholeNotes, long ticksPerWhole)
    {
        Fraction ticks = wholeNotes * new Fraction(ticksPerWhole, 1);
        return ticks.Num / ticks.Den;
    }
}
