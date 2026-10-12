namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The order of the marks of a character for shaping. It is the canonical order of Unicode, except for the marks whose
/// canonical order is not the one they are written and drawn in.
/// </summary>
/// <remarks>
/// <para>
/// The canonical combining classes 10 to 26 are the Hebrew points and 27 to 35 the Arabic vowel marks, one class per
/// mark: their numbers are an accident of the encoding, and the canonical order they give is not the one fonts are
/// made for.
/// </para>
/// <para>
/// <b>Hebrew.</b> The marks that modify the consonant come first (the dots of shin and sin, the dagesh, the rafe), then
/// the holam, the vowels below the letter, and the meteg, which is written after the vowel. This is the order the
/// lookups of Hebrew fonts expect; the canonical order puts the dagesh after the vowels and the dots of shin after
/// both.
/// </para>
/// <para>
/// <b>Arabic.</b> The shadda comes before the vowel marks, which are drawn above or below it: Unicode Technical
/// Report #53, "Unicode Arabic Mark Rendering".
/// </para>
/// <para>
/// <b>Thai.</b> The vowels below the consonant (class 103) are the marks closest to it: they come before the phinthu
/// (class 9), which is drawn under them. Microsoft, "Developing OpenType Fonts for Thai Script": the classes of
/// marks below the base. The marks of Lao are in that order already.
/// </para>
/// <para>
/// <b>Telugu.</b> The length marks (classes 84 and 91) are the second part of a vowel sign: they stay next to its
/// first part, before a nukta (class 7) or a halant (class 9), which the canonical order puts between the two.
/// </para>
/// <para>
/// <b>Tibetan.</b> The vowel sign u (class 132) is drawn under the stack, closer to it than the vowel signs above
/// (class 130): it comes before them, after the sign aa (class 129).
/// </para>
/// </remarks>
internal static class ShapingCombiningClass
{
    /// <summary>The first class of the marks <see cref="ArabicShaper"/> moves to the front of the marks of a letter.</summary>
    public const int FrontBelow = 25;

    /// <summary>The second class of the marks <see cref="ArabicShaper"/> moves to the front of the marks of a letter.</summary>
    public const int FrontAbove = 26;

    private const int ThaiVowelBelow = 103;
    private const int TeluguLengthMark = 84;
    private const int TeluguAiLengthMark = 91;
    private const int TibetanVowelAbove = 130;
    private const int TibetanVowelBelow = 132;

    public const int Shadda = 27;
    public const int Patah = 20;
    public const int Qamats = 21;
    public const int Sheva = 22;
    public const int Hiriq = 23;
    public const int Meteg = 25;
    public const int Below = 220;
    public const int Above = 230;

    // The canonical classes of the Hebrew points, in the order of shaping: shin dot, sin dot, dagesh, rafe, holam,
    // hataf segol, hataf patah, hataf qamats, tsere, segol, patah, qamats, sheva, hiriq, qubuts, meteg, varika. The
    // first one gets the class 10.
    private static ReadOnlySpan<byte> HebrewOrder => [24, 25, 21, 23, 19, 11, 12, 13, 15, 16, 17, 18, 10, 14, 20, 22, 26];

    // The canonical classes of the Arabic marks, in the order of shaping: shadda, then fathatan, dammatan, kasratan,
    // fatha, damma, kasra, sukun, superscript alef. The first one gets the class 27.
    private static ReadOnlySpan<byte> ArabicOrder => [33, 27, 28, 29, 30, 31, 32, 34, 35];

    /// <summary>Gets the class that orders a mark for shaping, from its canonical combining class.</summary>
    public static byte Get(int codePoint, byte combiningClass)
    {
        // Marks whose class is the one of all the viramas or the nuktas, and that are written at another place.
        switch (codePoint)
        {
            // The sakot of Tai Tham comes after the tone marks, and the padma of Tibetan after the vowel signs.
            case 0x1A60 or 0x0FC6:
                return 254;

            // The tsa -phru of Tibetan comes before the vowel sign u.
            case 0x0F39:
                return 127;
        }

        if (combiningClass is >= 10 and <= 26)
            return (byte)(10 + HebrewOrder.IndexOf(combiningClass));

        if (combiningClass is >= 27 and <= 35)
            return (byte)(27 + ArabicOrder.IndexOf(combiningClass));

        return combiningClass switch
        {
            ThaiVowelBelow => 3,
            TeluguLengthMark => 4,
            TeluguAiLengthMark => 5,
            TibetanVowelAbove => 132,
            TibetanVowelBelow => 131,
            _ => combiningClass,
        };
    }
}
