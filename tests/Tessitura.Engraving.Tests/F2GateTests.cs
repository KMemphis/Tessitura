using System.Text;
using Avalonia.Input;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.IO.Tess;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;

namespace Tessitura.Engraving.Tests;

/// <summary>Puerta F2: write a complete SATB chorale without the mouse, save, reopen and export to PDF.</summary>
public sealed class F2GateTests
{
    // Sixteen quarter notes per voice: four 4/4 measures.
    private const string Soprano = "GABCBAGGABCDCBAG";
    private const string Alto = "EFGGGFEDEFGAGFED";
    private const string Tenor = "CCDEDCBBCDEFEDCB";
    private const string Bass = "CFGCGFGCBAGFEDCC";

    [Fact]
    public void ChoraleIsWrittenWithTheKeyboardSavedReopenedAndExportedToPdf()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tessitura-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Score blank = NewScoreFactory.Create(new NewScoreOptions(ScoreTemplate.ChoirSatb,
                "Coral de prueba", "Tessitura", new TimeSignature(4, 4), new KeySignature(0), MeasureCount: 4));
            ScoreInputController input = new(blank);
            using ScoreWindowShell shell = new(new ScoreCanvas(), input);
            ActionRegistry actions = ActionRegistry.LoadOrCreate(
                input.CreateActions().AddRange(shell.CreateActions()), Path.Combine(directory, "keys.json"));
            shell.AttachActionRegistry(actions);
            int mouseEvents = 0;

            Press(actions, Key.N);
            Press(actions, Key.D5);
            string[] voices = [Soprano, Alto, Tenor, Bass];
            for (int staff = 0; staff < voices.Length; staff++)
            {
                if (staff > 0)
                {
                    Press(actions, Key.Home, KeyModifiers.Control);
                    Press(actions, Key.Down, KeyModifiers.Alt);
                }

                foreach (char letter in voices[staff])
                {
                    Press(actions, Enum.Parse<Key>(letter.ToString()));
                }
            }

            Score written = input.CurrentScore;
            Assert.Equal(0, mouseEvents);
            Assert.Equal(4, written.Measures.Length);
            for (int staff = 0; staff < 4; staff++)
            {
                for (int measure = 0; measure < 4; measure++)
                {
                    StaffMeasure content = written.Content[new StaffMeasureKey(staff, measure)];
                    Assert.True(ScoreValidator.IsMeasureValid(written.Measures[measure], content));
                    Chord[] chords = [.. content.Voices[0].Events.OfType<Chord>()];
                    Assert.True(chords.Length == 4, $"staff {staff} measure {measure}: {chords.Length} chords, events {content.Voices[0].Events.Length}");
                    string expected = voices[staff].Substring(measure * 4, 4);
                    Assert.Equal(expected, string.Concat(chords.Select(c => c.Notes[0].Pitch.Step.ToString())));
                }
            }

            string tess = Path.Combine(directory, "coral.tess");
            Style style = Style.CreateDefault(LoadMetadata());
            TessFile.Save(tess, written, style);
            TessDocument reopened = TessFile.Open(tess);
            Assert.Equal(Dump(written), Dump(reopened.Score));

            string pdf = Path.Combine(directory, "coral.pdf");
            int pages = ScorePdfExport.Export(pdf, reopened.Score, reopened.Style, LoadMetadata(),
                Path.Combine(Root, "assets", "fonts", "Bravura.otf"),
                Path.Combine(Root, "assets", "fonts", "NotoSerif[wdth,wght].ttf"));
            byte[] bytes = File.ReadAllBytes(pdf);
            Assert.True(pages >= 1);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.True(bytes.Length > 5000);

            string? capture = Environment.GetEnvironmentVariable("TESSITURA_CAPTURE_DIR");
            if (capture is not null)
            {
                Directory.CreateDirectory(capture);
                File.Copy(pdf, Path.Combine(capture, "f2-gate-coral-satb.pdf"), true);
                File.Copy(tess, Path.Combine(capture, "f2-gate-coral-satb.tess"), true);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Press(ActionRegistry actions, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        Assert.True(actions.TryExecute(key, modifiers), $"{modifiers}+{key} is not bound");

    private static string Dump(Score score)
    {
        StringBuilder text = new();
        text.Append(score.Metadata).Append('|');
        foreach (Instrument instrument in score.Instruments)
        {
            text.Append(instrument.Name);
            foreach (Staff staff in instrument.Staves)
            {
                text.Append('/').Append(staff.Name).Append(staff.InitialClef);
            }
        }

        foreach (Measure measure in score.Measures)
        {
            text.Append('|').Append(measure.Number).Append(' ').Append(measure.TimeSignature)
                .Append(' ').Append(measure.KeySignature.Fifths);
        }

        foreach ((StaffMeasureKey key, StaffMeasure content) in score.Content
            .OrderBy(entry => entry.Key.StaffIndex).ThenBy(entry => entry.Key.MeasureIndex))
        {
            text.Append("\n").Append(key);
            foreach (Voice voice in content.Voices)
            {
                text.Append(" v").Append(voice.Number);
                foreach (MusicEvent e in voice.Events)
                {
                    text.Append(" [").Append(e.Id.Value).Append(' ').Append(e.Onset.Num).Append('/').Append(e.Onset.Den)
                        .Append(' ').Append(e.Duration);
                    if (e is Chord chord)
                    {
                        foreach (Note note in chord.Notes)
                        {
                            text.Append(' ').Append(note);
                        }
                    }

                    text.Append(']');
                }
            }
        }

        return text.ToString();
    }

    private static SmuflMetadata LoadMetadata() => SmuflMetadata.Load(
        Path.Combine(Root, "assets", "fonts", "Bravura.json"),
        Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));

    private static string Root
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tessitura.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Tessitura.sln was not found.");
        }
    }
}
