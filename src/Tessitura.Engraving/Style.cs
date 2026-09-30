using System.Text.Json.Serialization;
using Tessitura.Smufl;

namespace Tessitura.Engraving;

/// <summary>Contains adjustable engraving measurements in staff spaces.</summary>
public sealed record Style
{
    /// <summary>Gets the staff-line width from SMuFL engravingDefaults.staffLineThickness.</summary>
    public double StaffLineThickness { get; init; }

    /// <summary>Gets the stem width from SMuFL engravingDefaults.stemThickness.</summary>
    public double StemThickness { get; init; }

    /// <summary>Gets the beam width from SMuFL engravingDefaults.beamThickness.</summary>
    public double BeamThickness { get; init; }

    /// <summary>Gets the gap between beams from SMuFL engravingDefaults.beamSpacing.</summary>
    public double BeamSpacing { get; init; }

    /// <summary>Gets the ledger-line width from SMuFL engravingDefaults.legerLineThickness.</summary>
    public double LedgerLineThickness { get; init; }

    /// <summary>Gets the ledger-line overhang from SMuFL engravingDefaults.legerLineExtension.</summary>
    public double LedgerLineExtension { get; init; }

    /// <summary>Gets the normal stem length from Behind Bars, Ground Rules, Stems.</summary>
    public double StemLength { get; init; }

    /// <summary>Gets Tessitura's adjustable clearance for accidentals; see Behind Bars, Accidentals, Placing.</summary>
    public double MinimumAccidentalGap { get; init; }

    /// <summary>Gets Tessitura's adjustable clearance between rhythmic columns; see Behind Bars, Ground Rules, Spacing symbols.</summary>
    public double MinimumRhythmicGap { get; init; }

    /// <summary>Creates the initial style from a SMuFL font and Behind Bars rules.</summary>
    /// <param name="metadata">The selected SMuFL font metadata.</param>
    /// <returns>The editable initial style.</returns>
    public static Style CreateDefault(SmuflMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new Style
        {
            // SMuFL engravingDefaults: font-specific line widths and beam gap.
            StaffLineThickness = metadata.GetEngravingDefault("staffLineThickness"),
            StemThickness = metadata.GetEngravingDefault("stemThickness"),
            BeamThickness = metadata.GetEngravingDefault("beamThickness"),
            BeamSpacing = metadata.GetEngravingDefault("beamSpacing"),
            LedgerLineThickness = metadata.GetEngravingDefault("legerLineThickness"),
            LedgerLineExtension = metadata.GetEngravingDefault("legerLineExtension"),
            // Behind Bars, Ground Rules > Stems: a normal stem is 3.5 staff spaces.
            StemLength = 3.5,
            // Behind Bars, Accidentals > Placing: preserve visible clearance.
            // The numeric baseline is Tessitura's adjustable choice, not a quoted rule.
            MinimumAccidentalGap = 0.25,
            // Behind Bars, Ground Rules > Spacing symbols: separate adjacent symbols.
            // The numeric baseline is Tessitura's adjustable choice, not a quoted rule.
            MinimumRhythmicGap = 0.5,
        };
    }
}

/// <summary>Provides source-generated JSON metadata for engraving styles.</summary>
[JsonSerializable(typeof(Style))]
public partial class StyleJsonContext : JsonSerializerContext;
