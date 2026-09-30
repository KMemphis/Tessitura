using System.Collections.Immutable;
using Tessitura.Playback.Performance;

namespace Tessitura.Playback.Audio;

/// <summary>Names what a sequenced event asks of the synthesizer.</summary>
public enum SequencedEventKind : byte
{
    /// <summary>Selects a General MIDI program on a channel.</summary>
    ProgramChange,
    /// <summary>Releases a key.</summary>
    NoteOff,
    /// <summary>Presses a key.</summary>
    NoteOn,
}

/// <summary>One synthesizer command at an exact sample.</summary>
/// <param name="Sample">The sample index from the start of the piece.</param>
/// <param name="Kind">The command.</param>
/// <param name="Channel">The MIDI channel from 0 to 15.</param>
/// <param name="Data1">The key, or the program for a program change.</param>
/// <param name="Data2">The velocity.</param>
public readonly record struct SequencedEvent(long Sample, SequencedEventKind Kind, byte Channel, byte Data1, byte Data2);

/// <summary>An immutable, sorted list of synthesizer commands measured in samples.</summary>
public sealed class SequenceData
{
    private SequenceData(ImmutableArray<SequencedEvent> events, int sampleRate, long lengthSamples)
    {
        Events = events;
        SampleRate = sampleRate;
        LengthSamples = lengthSamples;
    }

    /// <summary>Gets an empty sequence.</summary>
    public static SequenceData Empty { get; } = new([], 48000, 0);

    /// <summary>Gets the commands, ordered by sample; at one sample program changes precede releases, which precede presses.</summary>
    public ImmutableArray<SequencedEvent> Events { get; }

    /// <summary>Gets the sample rate the sample indexes refer to.</summary>
    public int SampleRate { get; }

    /// <summary>Gets the sample of the last command.</summary>
    public long LengthSamples { get; }

    /// <summary>Converts an interpretation to sample-timed commands.</summary>
    /// <param name="interpretation">The notes and tempo map to play.</param>
    /// <param name="sampleRate">The output sample rate in Hz.</param>
    /// <param name="programs">The General MIDI program of each instrument; instruments beyond the list use program 0.</param>
    /// <returns>The sequence.</returns>
    public static SequenceData Build(Interpretation interpretation, int sampleRate, IReadOnlyList<int>? programs = null)
    {
        ArgumentNullException.ThrowIfNull(interpretation);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        List<SequencedEvent> events = [];
        SortedSet<int> instruments = [];
        foreach (PerformedNote note in interpretation.Notes)
        {
            instruments.Add(note.Instrument);
            byte channel = ChannelOf(note.Instrument);
            long on = ToSample(interpretation, note.Start, sampleRate);
            long off = Math.Max(on + 1, ToSample(interpretation, note.Start + note.SoundingLength, sampleRate));
            events.Add(new SequencedEvent(on, SequencedEventKind.NoteOn, channel, (byte)Math.Clamp(note.Midi, 0, 127), (byte)note.Velocity));
            events.Add(new SequencedEvent(off, SequencedEventKind.NoteOff, channel, (byte)Math.Clamp(note.Midi, 0, 127), 0));
        }

        foreach (int instrument in instruments)
        {
            int program = programs is not null && instrument < programs.Count ? programs[instrument] : 0;
            events.Add(new SequencedEvent(0, SequencedEventKind.ProgramChange, ChannelOf(instrument), (byte)Math.Clamp(program, 0, 127), 0));
        }

        events.Sort(static (a, b) =>
        {
            int bySample = a.Sample.CompareTo(b.Sample);
            if (bySample != 0)
            {
                return bySample;
            }

            int byKind = a.Kind.CompareTo(b.Kind);
            return byKind != 0 ? byKind : a.Data1.CompareTo(b.Data1);
        });
        long length = events.Count == 0 ? 0 : events[^1].Sample;
        return new SequenceData([.. events], sampleRate, length);
    }

    /// <summary>Gets the MIDI channel of an instrument, skipping the percussion channel 10.</summary>
    /// <param name="instrument">The zero-based instrument index.</param>
    /// <returns>A channel from 0 to 15 other than 9.</returns>
    public static byte ChannelOf(int instrument)
    {
        int channel = instrument % 15;
        return (byte)(channel >= 9 ? channel + 1 : channel);
    }

    /// <summary>Finds the index of the first command at or after a sample.</summary>
    /// <param name="sample">The sample index.</param>
    /// <returns>An index from 0 to the number of commands.</returns>
    public int FirstAtOrAfter(long sample)
    {
        int low = 0;
        int high = Events.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (Events[middle].Sample < sample)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static long ToSample(Interpretation interpretation, Tessitura.Core.Fraction position, int sampleRate) =>
        (long)Math.Round(interpretation.Tempo.SecondsAt(position) * sampleRate);
}
