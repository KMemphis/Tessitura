using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Names the ensembles offered by the new-score wizard.</summary>
public enum ScoreTemplate
{
    /// <summary>Piano on a treble and a bass staff.</summary>
    Piano,
    /// <summary>Two violins, viola and cello.</summary>
    StringQuartet,
    /// <summary>Soprano, alto, tenor and bass on four staves.</summary>
    ChoirSatb,
}

/// <summary>Holds the choices made in the new-score wizard.</summary>
/// <param name="Template">The ensemble.</param>
/// <param name="Title">The score title.</param>
/// <param name="Composer">The composer credit.</param>
/// <param name="TimeSignature">The opening meter.</param>
/// <param name="KeySignature">The opening key signature.</param>
/// <param name="MeasureCount">How many empty measures to create.</param>
public sealed record NewScoreOptions(
    ScoreTemplate Template,
    string Title,
    string Composer,
    TimeSignature TimeSignature,
    KeySignature KeySignature,
    int MeasureCount = 8)
{
    /// <summary>Creates the wizard defaults for a template.</summary>
    /// <param name="template">The chosen ensemble.</param>
    /// <returns>Options with an untitled score in 4/4 and C major.</returns>
    public static NewScoreOptions CreateDefault(ScoreTemplate template) =>
        new(template, "Sin título", "", new TimeSignature(4, 4), new KeySignature(0));
}

/// <summary>Builds an empty score, filled with rests, from wizard options.</summary>
public static class NewScoreFactory
{
    /// <summary>Creates the rests that cover one whole measure, split at the beats.</summary>
    /// <param name="timeSignature">The measure's meter.</param>
    /// <returns>The rests, starting at onset zero.</returns>
    public static ImmutableArray<MusicEvent> CreateMeasureRests(TimeSignature timeSignature) =>
        RhythmicScoreNormalizer.CreateFullMeasureRests(timeSignature);

    /// <summary>Creates the score for the options.</summary>
    /// <param name="options">The wizard choices.</param>
    /// <returns>A valid score whose every staff measure is covered by rests.</returns>
    public static Score Create(NewScoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MeasureCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        ImmutableArray<Instrument> instruments = options.Template switch
        {
            ScoreTemplate.Piano =>
            [
                new Instrument("Piano", [new Staff("Mano derecha"), new Staff("Mano izquierda", Clef.Bass)]),
            ],
            ScoreTemplate.StringQuartet =>
            [
                new Instrument("Violín I", [new Staff("Violín I")]),
                new Instrument("Violín II", [new Staff("Violín II")]),
                new Instrument("Viola", [new Staff("Viola", Clef.Alto)]),
                new Instrument("Violonchelo", [new Staff("Violonchelo", Clef.Bass)]),
            ],
            ScoreTemplate.ChoirSatb =>
            [
                new Instrument("Soprano", [new Staff("Soprano")]),
                new Instrument("Alto", [new Staff("Alto")]),
                new Instrument("Tenor", [new Staff("Tenor")]),
                new Instrument("Bajo", [new Staff("Bajo", Clef.Bass)]),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        int staffCount = 0;
        foreach (Instrument instrument in instruments)
        {
            staffCount += instrument.Staves.Length;
        }

        ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>(options.MeasureCount);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int measureIndex = 0; measureIndex < options.MeasureCount; measureIndex++)
        {
            measures.Add(new Measure(measureIndex + 1, options.TimeSignature, options.KeySignature));
            for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
            {
                content[new StaffMeasureKey(staffIndex, measureIndex)] = new StaffMeasure(
                    [new Voice(1, RhythmicScoreNormalizer.CreateFullMeasureRests(options.TimeSignature))]);
            }
        }

        return new Score(new ScoreMetadata(options.Title, options.Composer), instruments,
            measures.MoveToImmutable(), content.ToImmutable());
    }
}
