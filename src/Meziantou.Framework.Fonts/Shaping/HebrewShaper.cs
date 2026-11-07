namespace Meziantou.Framework.Fonts;

/// <summary>What Hebrew needs besides the features of the font.</summary>
/// <remarks>
/// Microsoft, "Developing OpenType Fonts for Hebrew Script": the font does the shaping (<c>ccmp</c>, <c>dlig</c>) and
/// positions the points (<c>mark</c>). A font made before OpenType Layout has no <c>mark</c> feature: it draws a
/// letter and its point with one glyph, the precomposed letter of the Alphabetic Presentation Forms block.
/// </remarks>
internal static class HebrewShaper
{
    private const int FirstPresentationForm = 0xFB1D;
    private const int LastPresentationForm = 0xFB4E;
    private const int Dagesh = 0x05BC;
    private const int ShinWithShinDot = 0xFB2A;
    private const int ShinWithSinDot = 0xFB2B;

    /// <summary>
    /// Composes a letter and a point into a precomposed letter. Unicode excludes these letters from composition, so
    /// that a normalized text never has them: they are only for the fonts that cannot position a point.
    /// </summary>
    public static bool TryCompose(int first, int second, out int composite)
    {
        if (second is < 0x05B0 or > 0x05C2)
        {
            composite = 0;
            return false;
        }

        // The decompositions of the precomposed letters are the pairs.
        for (var candidate = FirstPresentationForm; candidate <= LastPresentationForm; candidate++)
        {
            if (UnicodeNormalization.TryDecomposeCanonical(candidate, out var letter, out var point) && letter == first && point == second)
            {
                composite = candidate;
                return true;
            }
        }

        // The dot of a shin comes before its dagesh for shaping (ShapingCombiningClass), and after it in the
        // decompositions: the shin that already has its dot takes the dagesh.
        if (second == Dagesh && first is ShinWithShinDot or ShinWithSinDot)
        {
            composite = first + 2;
            return true;
        }

        composite = 0;
        return false;
    }

    /// <summary>
    /// Puts a meteg between the two parts of a vowel written with two points: after a patah or a qamats and a sheva or
    /// a hiriq, the meteg (or another mark below) is drawn between them.
    /// </summary>
    /// <param name="marks">The marks of a letter, in the order of shaping.</param>
    /// <returns><see langword="true"/> when marks were moved.</returns>
    public static bool ReorderMarks(Span<GlyphInfo> marks)
    {
        for (var i = 2; i < marks.Length; i++)
        {
            if (marks[i - 2].CombiningClass is ShapingCombiningClass.Patah or ShapingCombiningClass.Qamats
                && marks[i - 1].CombiningClass is ShapingCombiningClass.Sheva or ShapingCombiningClass.Hiriq
                && marks[i].CombiningClass is ShapingCombiningClass.Meteg or ShapingCombiningClass.Below)
            {
                (marks[i - 1], marks[i]) = (marks[i], marks[i - 1]);
                return true;
            }
        }

        return false;
    }
}
