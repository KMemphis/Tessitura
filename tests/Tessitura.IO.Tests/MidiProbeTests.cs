using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Xunit;

namespace Tessitura.IO.Tests;

public sealed class MidiProbeTests
{
    [Fact]
    public void NoteOnMessageCarriesPitchVelocityAndChannel()
    {
        NoteOnEvent note = new((SevenBitNumber)60, (SevenBitNumber)100)
        {
            Channel = (FourBitNumber)2,
        };

        Assert.Equal(60, (int)note.NoteNumber);
        Assert.Equal(100, (int)note.Velocity);
        Assert.Equal(2, (int)note.Channel);
    }
}
