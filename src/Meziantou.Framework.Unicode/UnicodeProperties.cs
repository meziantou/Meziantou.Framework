using System.Globalization;

namespace Meziantou.Framework;

/// <summary>Provides the properties of a code point defined by the Unicode Character Database.</summary>
/// <remarks>
/// <para>
/// Every method takes a code point as an <see cref="int"/>, never throws and never allocates: the values are read
/// from tables indexed by the code point. An integer that is not a code point (negative, or greater than U+10FFFF)
/// has the default value of the property, the one of a code point the standard does not list.
/// </para>
/// <para>
/// The values are those of one version of the Unicode Standard, <see cref="UnicodeVersion"/>, whatever the version
/// the runtime implements: the results are the same on every platform and every framework version.
/// </para>
/// </remarks>
/// <seealso href="https://www.unicode.org/reports/tr44/" />
public static class UnicodeProperties
{
    /// <summary>Gets the version of the Unicode Standard the properties come from.</summary>
    public static Version UnicodeVersion { get; } = Version.Parse(UnicodeTables.UnicodeVersion);

    /// <summary>Gets the General_Category of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The category; <see cref="UnicodeCategory.OtherNotAssigned"/> for an unassigned code point.</returns>
    public static UnicodeCategory GetGeneralCategory(int codePoint)
    {
        return (UnicodeCategory)(UnicodeTables.GeneralTuples[UnicodeTables.GetGeneral(codePoint) * 2] & 0x1F);
    }

    /// <summary>Determines whether a code point has the Default_Ignorable_Code_Point property.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when the code point should have no visible glyph by default, like a variation selector or a zero-width joiner.</returns>
    public static bool IsDefaultIgnorable(int codePoint)
    {
        return (UnicodeTables.GeneralTuples[UnicodeTables.GetGeneral(codePoint) * 2] & 0x20) != 0;
    }

    /// <summary>Gets the Canonical_Combining_Class of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The combining class; 0 for a starter.</returns>
    public static byte GetCanonicalCombiningClass(int codePoint) => UnicodeNormalizationTables.NormalizationTuples[UnicodeNormalizationTables.GetNormalization(codePoint) * 2];

    /// <summary>Gets the Bidi_Class of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>
    /// The bidirectional class. An unassigned code point has the default class of its range (for instance
    /// <see cref="UnicodeBidirectionalCategory.RightToLeft"/> in the Hebrew block), as the Unicode Bidirectional
    /// Algorithm requires.
    /// </returns>
    /// <seealso href="https://www.unicode.org/reports/tr9/" />
    public static UnicodeBidirectionalCategory GetBidiClass(int codePoint)
    {
        return (UnicodeBidirectionalCategory)(UnicodeTables.GetBidi(codePoint) & 0x1F);
    }

    /// <summary>Determines whether a code point has the Bidi_Mirrored property.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when the glyph of the code point is mirrored in right-to-left text.</returns>
    public static bool IsBidiMirrored(int codePoint) => (UnicodeTables.GetBidi(codePoint) & 0x80) != 0;

    /// <summary>Gets the Bidi_Mirroring_Glyph of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="mirrored">The code point whose glyph is the mirror image, when the method returns <see langword="true"/>; otherwise <paramref name="codePoint"/>.</param>
    /// <returns><see langword="true"/> when another code point has the mirrored glyph. Some mirrored code points have none.</returns>
    public static bool TryGetBidiMirroringGlyph(int codePoint, out int mirrored)
    {
        if (IsBidiMirrored(codePoint) && TryFindPair(UnicodeTables.BidiMirroringPairs, codePoint, out mirrored))
            return true;

        mirrored = codePoint;
        return false;
    }

    /// <summary>Gets the Bidi_Paired_Bracket_Type of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>Whether the code point is an opening bracket, a closing bracket or neither.</returns>
    public static UnicodeBidiPairedBracketType GetBidiPairedBracketType(int codePoint)
    {
        return (UnicodeBidiPairedBracketType)((UnicodeTables.GetBidi(codePoint) >> 5) & 0x3);
    }

    /// <summary>Gets the Bidi_Paired_Bracket of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="paired">The other bracket of the pair, when the method returns <see langword="true"/>; otherwise <paramref name="codePoint"/>.</param>
    /// <returns><see langword="true"/> when the code point is an opening or a closing bracket.</returns>
    public static bool TryGetBidiPairedBracket(int codePoint, out int paired)
    {
        if (GetBidiPairedBracketType(codePoint) is not UnicodeBidiPairedBracketType.None && TryFindPair(UnicodeTables.BidiBracketPairs, codePoint, out paired))
            return true;

        paired = codePoint;
        return false;
    }

    /// <summary>Gets the Line_Break property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The class as listed by the standard, before the resolution of the Unicode Line Breaking Algorithm (rule LB1).</returns>
    /// <seealso href="https://www.unicode.org/reports/tr14/" />
    public static UnicodeLineBreakClass GetLineBreakClass(int codePoint)
    {
        return (UnicodeLineBreakClass)UnicodeTables.LineBreakTuples[UnicodeTables.GetLineBreak(codePoint) * 2];
    }

    /// <summary>Gets the East_Asian_Width property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The width class.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr11/" />
    public static UnicodeEastAsianWidth GetEastAsianWidth(int codePoint)
    {
        return (UnicodeEastAsianWidth)(UnicodeTables.LineBreakTuples[(UnicodeTables.GetLineBreak(codePoint) * 2) + 1] & 0x7);
    }

    /// <summary>Gets the Grapheme_Cluster_Break property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The class used by the grapheme cluster boundary rules.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr29/" />
    public static UnicodeGraphemeClusterBreak GetGraphemeClusterBreak(int codePoint)
    {
        return (UnicodeGraphemeClusterBreak)(UnicodeTables.SegmentationTuples[UnicodeTables.GetSegmentation(codePoint) * 2] & 0x1F);
    }

    /// <summary>Gets the Indic_Conjunct_Break property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The class used by the grapheme cluster rule that keeps Indic conjuncts together (GB9c).</returns>
    public static UnicodeIndicConjunctBreak GetIndicConjunctBreak(int codePoint)
    {
        return (UnicodeIndicConjunctBreak)((UnicodeTables.SegmentationTuples[UnicodeTables.GetSegmentation(codePoint) * 2] >> 5) & 0x3);
    }

    /// <summary>Gets the Word_Break property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The class used by the word boundary rules.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr29/" />
    public static UnicodeWordBreak GetWordBreak(int codePoint)
    {
        return (UnicodeWordBreak)UnicodeTables.SegmentationTuples[(UnicodeTables.GetSegmentation(codePoint) * 2) + 1];
    }

    /// <summary>Gets the Joining_Type property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>How the code point joins its neighbors in a cursive script such as Arabic.</returns>
    public static UnicodeJoiningType GetJoiningType(int codePoint)
    {
        return (UnicodeJoiningType)UnicodeTables.JoiningTuples[UnicodeTables.GetJoining(codePoint) * 2];
    }

    /// <summary>Gets the Joining_Group property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The group of letters that share the joining behavior and the basic shapes of the code point.</returns>
    public static UnicodeJoiningGroup GetJoiningGroup(int codePoint)
    {
        return (UnicodeJoiningGroup)UnicodeTables.JoiningTuples[(UnicodeTables.GetJoining(codePoint) * 2) + 1];
    }

    /// <summary>Gets the Indic_Syllabic_Category property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The role of the code point in the syllables of the Brahmi-derived scripts.</returns>
    public static UnicodeIndicSyllabicCategory GetIndicSyllabicCategory(int codePoint)
    {
        return (UnicodeIndicSyllabicCategory)UnicodeTables.IndicTuples[UnicodeTables.GetIndic(codePoint) * 2];
    }

    /// <summary>Gets the Indic_Positional_Category property of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>Where a dependent vowel or another mark is placed relative to its base.</returns>
    public static UnicodeIndicPositionalCategory GetIndicPositionalCategory(int codePoint)
    {
        return (UnicodeIndicPositionalCategory)UnicodeTables.IndicTuples[(UnicodeTables.GetIndic(codePoint) * 2) + 1];
    }

    /// <summary>Gets the Simple_Lowercase_Mapping of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The lowercase code point, or <paramref name="codePoint"/> when it has no mapping.</returns>
    /// <remarks>The mapping is the one-to-one, culture-independent mapping of the Unicode Character Database.</remarks>
    public static int ToSimpleLowercase(int codePoint) => codePoint + UnicodeTableReader.ReadInt32(UnicodeTables.CaseTuples, UnicodeTables.GetCase(codePoint) * 8);

    /// <summary>Gets the Simple_Uppercase_Mapping of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The uppercase code point, or <paramref name="codePoint"/> when it has no mapping.</returns>
    /// <remarks>The mapping is the one-to-one, culture-independent mapping of the Unicode Character Database.</remarks>
    public static int ToSimpleUppercase(int codePoint) => codePoint + UnicodeTableReader.ReadInt32(UnicodeTables.CaseTuples, (UnicodeTables.GetCase(codePoint) * 8) + 4);

    internal static EmojiProperties GetEmojiProperties(int codePoint)
    {
        return (EmojiProperties)UnicodeTables.GeneralTuples[(UnicodeTables.GetGeneral(codePoint) * 2) + 1];
    }

    /// <summary>The flags that the line breaking rules read with the class: see <c>UnicodeTables.GetLineBreak</c>.</summary>
    internal static int GetLineBreakTuple(int codePoint)
    {
        var index = UnicodeTables.GetLineBreak(codePoint) * 2;
        var tuples = UnicodeTables.LineBreakTuples;
        return tuples[index] | (tuples[index + 1] << 8);
    }

    /// <summary>The grapheme and word properties of a code point: see <c>UnicodeTables.GetSegmentation</c>.</summary>
    internal static int GetSegmentationTuple(int codePoint)
    {
        var index = UnicodeTables.GetSegmentation(codePoint) * 2;
        var tuples = UnicodeTables.SegmentationTuples;
        return tuples[index] | (tuples[index + 1] << 8);
    }

    private static bool TryFindPair(ReadOnlySpan<byte> pairs, int codePoint, out int value)
    {
        // 6-byte entries: the code point, then the value.
        var offset = UnicodeTableReader.FindRecord(pairs, 6, codePoint);
        if (offset < 0)
        {
            value = codePoint;
            return false;
        }

        value = UnicodeTableReader.ReadUInt24(pairs, offset + 3);
        return true;
    }
}
