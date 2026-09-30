using Tessitura.Core;

namespace Tessitura.Engraving;

internal static class KeySignaturePositions
{
    // Staff positions in half spaces above the bottom line, treble clef.
    // Behind Bars, Accidentals and Key Signatures > Key Signatures: F C G D A E B / B E A D G C F.
    private static readonly int[] TrebleSharps = [8, 5, 9, 6, 3, 7, 4];
    private static readonly int[] TrebleFlats = [4, 7, 3, 6, 2, 5, 1];
    private static readonly int[] TenorSharps = [2, 6, 3, 7, 4, 8, 5];

    public static int Get(int fifths, int index, Clef clef)
    {
        bool sharps = fifths > 0;
        int treble = sharps ? TrebleSharps[index] : TrebleFlats[index];
        return clef switch
        {
            Clef.Treble => treble,
            // Behind Bars: the bass signature sits a third lower; the last flat wraps up an octave to stay on the staff.
            Clef.Bass => treble - 2 < 0 ? treble - 2 + 7 : treble - 2,
            Clef.Alto => treble - 1,
            Clef.Tenor => sharps ? TenorSharps[index] : treble + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(clef)),
        };
    }
}
