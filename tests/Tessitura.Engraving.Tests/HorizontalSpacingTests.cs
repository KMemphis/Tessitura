using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Smufl;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class HorizontalSpacingTests
{
    [Fact]
    public void HalfNoteGetsAboutOneAndAHalfQuarterNoteWidth()
    {
        HorizontalSpacer spacer = new();
        Fraction quarter = new(1, 4);
        Fraction half = new(1, 2);

        double ratio = spacer.IdealWidth(half, quarter, 2) /
            spacer.IdealWidth(quarter, quarter, 2);

        Assert.InRange(ratio, 1.4, 1.7);
    }

    [Fact]
    public void AccidentalCannotTouchPreviousNote()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        SmuflBoundingBox notehead = metadata.GetBoundingBox("noteheadBlack");
        SmuflBoundingBox sharp = metadata.GetBoundingBox("accidentalSharp");
        double sharpWidth = sharp.NorthEast.X - sharp.SouthWest.X;
        SpacingColumn[] columns =
        [
            new(new Fraction(1, 4), -notehead.SouthWest.X, notehead.NorthEast.X),
            new(new Fraction(1, 4), sharpWidth + style.MinimumAccidentalGap,
                notehead.NorthEast.X),
        ];

        PositionedColumn[] positions = new HorizontalSpacer().Layout(columns, style, 0).ToArray();

        Assert.True(positions[0].X + columns[0].RightExtent + style.MinimumAccidentalGap
            <= positions[1].X - columns[1].LeftExtent);
    }

    [Fact]
    public void HeaderReservesNonoverlappingClefKeyAndMeter()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);

        SystemHeaderLayout header = new HorizontalSpacer().BuildHeader(
            metadata, "gClef", new KeySignature(2), new TimeSignature(4, 4), style);

        HeaderSymbol[] clef = header.Symbols.Where(item => item.Part == HeaderPart.Clef).ToArray();
        HeaderSymbol[] key = header.Symbols.Where(item => item.Part == HeaderPart.KeySignature).ToArray();
        HeaderSymbol[] top = header.Symbols.Where(item => item.Part == HeaderPart.MeterNumerator).ToArray();
        HeaderSymbol[] bottom = header.Symbols.Where(item => item.Part == HeaderPart.MeterDenominator).ToArray();
        Assert.Single(clef);
        Assert.Equal(2, key.Length);
        Assert.Single(top);
        Assert.Single(bottom);
        Assert.True(clef[0].X + clef[0].Width < key[0].X);
        Assert.True(key[1].X + key[1].Width < top[0].X);
        Assert.Equal(top[0].X, bottom[0].X);
        Assert.True(header.MusicStartX > top[0].X + top[0].Width);
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

            throw new DirectoryNotFoundException("Tessitura.sln was not found above the test directory.");
        }
    }
}
