using MeltySynth;
using Xunit;

namespace Tessitura.IO.Tests;

public sealed class AudioProbeTests
{
    [Fact]
    public void MeltySynthRendersNonSilentNoteFromTestSoundFont()
    {
        string fontPath = Path.Combine(AppContext.BaseDirectory, "sf_spec_test.sf2");
        Synthesizer synthesizer = new(fontPath, 48000);
        synthesizer.NoteOn(0, 60, 100);

        float[] left = new float[4800];
        float[] right = new float[4800];
        synthesizer.Render(left, right);

        Assert.Contains(left, sample => Math.Abs(sample) > 0.0001f);
        Assert.Contains(right, sample => Math.Abs(sample) > 0.0001f);
    }
}
