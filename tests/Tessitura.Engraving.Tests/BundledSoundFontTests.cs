using MeltySynth;
using Tessitura.App;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class BundledSoundFontTests
{
    [Fact]
    public void IncludedSoundFontCanBeLoadedAndReused()
    {
        string cache = Path.Combine(Path.GetTempPath(), "tessitura-soundfont-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string first = BundledSoundFont.EnsureAvailable(AppContext.BaseDirectory, cache);
            string second = BundledSoundFont.EnsureAvailable(AppContext.BaseDirectory, cache);

            Assert.Equal(first, second);
            Assert.True(new FileInfo(first).Length > 1_000_000);
            SoundFont font = new(first);
            Synthesizer synth = new(font, new SynthesizerSettings(44100));
            synth.NoteOn(0, 60, 100);
            float[] left = new float[1024];
            float[] right = new float[1024];
            synth.Render(left, right);
            Assert.Contains(left, sample => Math.Abs(sample) > 0.00001f);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void MissingBundleReportsItsPath()
    {
        string baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => BundledSoundFont.EnsureAvailable(baseDirectory, cache));
        Assert.Contains("default.sf2.br", error.FileName);
    }
}
