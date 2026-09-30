using Tessitura.Core;

namespace Tessitura.Engraving;

internal static class StaffPitchPosition
{
    public static int Get(Pitch pitch, Clef clef)
    {
        int treblePosition = (pitch.Octave - 4) * 7 + (int)pitch.Step - (int)Step.E;
        int bottomLinePosition = clef switch
        {
            Clef.Treble => 0,
            Clef.Bass => -12,
            Clef.Alto => -6,
            Clef.Tenor => -8,
            _ => throw new ArgumentOutOfRangeException(nameof(clef)),
        };
        return treblePosition - bottomLinePosition;
    }

    public static double GetY(Pitch pitch, Clef clef, double staffTop) =>
        staffTop + 4 - Get(pitch, clef) * 0.5;
}
