using System.Globalization;

namespace Meziantou.Framework;

/// <summary>Provides the properties of a code point defined by the Unicode Character Database.</summary>
/// <remarks>
/// <para>
/// Every method takes a code point as an <see cref="int"/> and never allocates: the values are read from tables
/// indexed by the code point. Only the methods that write to a buffer throw, when the buffer is too small. An integer
/// that is not a code point (negative, or greater than U+10FFFF) has the default value of the property, the one of a
/// code point the standard does not list.
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

    /// <summary>Determines whether a code point has the ID_Start property: whether it can start an identifier.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when the code point is a letter or a letter number that can start an identifier.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr31/" />
    public static bool IsIdStart(int codePoint)
    {
        return (UnicodeTables.GeneralTuples[UnicodeTables.GetGeneral(codePoint) * 2] & 0x40) != 0;
    }

    /// <summary>Determines whether a code point has the ID_Continue property: whether it can follow the first code point of an identifier.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when the code point can be part of an identifier after its first code point, like a letter, a digit or a combining mark.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr31/" />
    public static bool IsIdContinue(int codePoint)
    {
        return (UnicodeTables.GeneralTuples[UnicodeTables.GetGeneral(codePoint) * 2] & 0x80) != 0;
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

    /// <summary>Gets the Vertical_Orientation property of a code point: how the character is set in a line of vertical text.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>
    /// The orientation: upright as in a horizontal line, rotated by a quarter turn clockwise, or one of the two with
    /// another glyph than in a horizontal line. A value that is not a code point is rotated, the default of the
    /// property.
    /// </returns>
    /// <seealso href="https://www.unicode.org/reports/tr50/" />
    public static UnicodeVerticalOrientation GetVerticalOrientation(int codePoint)
    {
        return (UnicodeVerticalOrientation)UnicodeTables.GetVerticalOrientation(codePoint);
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
    public static int ToSimpleLowercase(int codePoint) => codePoint + UnicodeTableReader.ReadInt32(UnicodeTables.CaseTuples, UnicodeTables.GetCase(codePoint) * 16);

    /// <summary>Gets the Simple_Uppercase_Mapping of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The uppercase code point, or <paramref name="codePoint"/> when it has no mapping.</returns>
    /// <remarks>The mapping is the one-to-one, culture-independent mapping of the Unicode Character Database.</remarks>
    public static int ToSimpleUppercase(int codePoint) => codePoint + UnicodeTableReader.ReadInt32(UnicodeTables.CaseTuples, (UnicodeTables.GetCase(codePoint) * 16) + 4);

    /// <summary>Gets the Simple_Titlecase_Mapping of a code point: the form of the code point at the start of a word.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>
    /// The titlecase code point, or <paramref name="codePoint"/> when it has no mapping. It is the
    /// <see cref="ToSimpleUppercase(int)"/> mapping, except for a few code points such as the Latin digraphs
    /// (<c>ǆ</c> → <c>ǅ</c>) and the Georgian Mkhedruli letters.
    /// </returns>
    /// <remarks>The mapping is the one-to-one, culture-independent mapping of the Unicode Character Database.</remarks>
    public static int ToSimpleTitlecase(int codePoint) => codePoint + UnicodeTableReader.ReadInt32(UnicodeTables.CaseTuples, (UnicodeTables.GetCase(codePoint) * 16) + 8);

    /// <summary>Gets the Simple_Case_Folding of a code point: the mappings of <c>CaseFolding.txt</c> whose status is <c>C</c> or <c>S</c>.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The folded code point, or <paramref name="codePoint"/> when it has no mapping.</returns>
    /// <remarks>
    /// Two strings that differ only by case have the same simple case folding when their characters fold one to one;
    /// the full case folding (status <c>F</c>), which can map a code point to several ones (U+00DF to <c>ss</c>), is not
    /// applied. The Turkic mappings (status <c>T</c>) are not applied either.
    /// </remarks>
    public static int ToSimpleCaseFold(int codePoint) => codePoint + UnicodeTableReader.ReadInt32(UnicodeTables.CaseTuples, (UnicodeTables.GetCase(codePoint) * 16) + 12);

    /// <summary>Determines whether a code point has a binary property.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> when the code point has the property; <see langword="false"/> otherwise, and for an integer that is not a code point.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="property"/> is not a value of <see cref="UnicodeBinaryProperty"/>.</exception>
    public static bool HasBinaryProperty(int codePoint, UnicodeBinaryProperty property)
    {
        var (first, count) = GetBinaryPropertyRangeIndexes(property);
        var ranges = UnicodeBinaryPropertyTables.Ranges;
        var low = first;
        var high = first + count - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) / 2);
            var offset = middle * 6;
            if (codePoint < UnicodeTableReader.ReadUInt24(ranges, offset))
            {
                high = middle - 1;
            }
            else if (codePoint > UnicodeTableReader.ReadUInt24(ranges, offset + 3))
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the code points that have a binary property.</summary>
    /// <param name="property">The property.</param>
    /// <returns>The ranges of the code points that have the property, in increasing order. The ranges neither overlap nor touch.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="property"/> is not a value of <see cref="UnicodeBinaryProperty"/>.</exception>
    /// <remarks>Each call returns a new array.</remarks>
    public static UnicodeRange[] GetBinaryPropertyRanges(UnicodeBinaryProperty property)
    {
        var (first, count) = GetBinaryPropertyRangeIndexes(property);
        var ranges = UnicodeBinaryPropertyTables.Ranges;
        var result = new UnicodeRange[count];
        for (var i = 0; i < count; i++)
        {
            var offset = (first + i) * 6;
            result[i] = new UnicodeRange(UnicodeTableReader.ReadUInt24(ranges, offset), UnicodeTableReader.ReadUInt24(ranges, offset + 3));
        }

        return result;
    }

    private static (int First, int Count) GetBinaryPropertyRangeIndexes(UnicodeBinaryProperty property)
    {
        if ((uint)property >= UnicodeBinaryPropertyTables.PropertyCount)
            throw new ArgumentOutOfRangeException(nameof(property), property, "The value is not a binary property");

        var offsets = UnicodeBinaryPropertyTables.RangeOffsets;
        var first = UnicodeTableReader.ReadUInt24(offsets, (int)property * 3);
        var next = UnicodeTableReader.ReadUInt24(offsets, ((int)property + 1) * 3);
        return (first, next - first);
    }

    /// <summary>Gets the greatest number of code points a full case mapping writes for a code point.</summary>
    /// <seealso cref="GetLowercaseMapping(int, Span{int})"/>
    /// <seealso cref="GetUppercaseMapping(int, Span{int})"/>
    /// <seealso cref="GetTitlecaseMapping(int, Span{int})"/>
    public static int MaxCaseMappingLength => 3;

    /// <summary>Gets the full Lowercase_Mapping of a code point, which can be several code points (<c>İ</c>, U+0130, → U+0069 U+0307).</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="destination">The buffer that receives the code points of the mapping.</param>
    /// <returns>The number of code points written. It is 1, <paramref name="codePoint"/> itself, when the code point has no mapping.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small. <see cref="MaxCaseMappingLength"/> elements are always enough.</exception>
    /// <remarks>
    /// The mapping is the default case conversion of the Unicode Standard (section 3.13) without its conditional
    /// mappings: the unconditional mappings of SpecialCasing.txt, else the simple mapping. The mappings that depend on
    /// the language (Turkish, Lithuanian) or on the context (the final sigma) are not applied.
    /// </remarks>
    public static int GetLowercaseMapping(int codePoint, Span<int> destination) => GetCaseMapping(codePoint, destination, CaseMappingKind.Lowercase);

    /// <summary>Gets the full Uppercase_Mapping of a code point, which can be several code points (<c>ß</c> → <c>SS</c>).</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="destination">The buffer that receives the code points of the mapping.</param>
    /// <returns>The number of code points written. It is 1, <paramref name="codePoint"/> itself, when the code point has no mapping.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small. <see cref="MaxCaseMappingLength"/> elements are always enough.</exception>
    /// <remarks>
    /// The mapping is the default case conversion of the Unicode Standard (section 3.13) without its conditional
    /// mappings: the unconditional mappings of SpecialCasing.txt, else the simple mapping. The mappings that depend on
    /// the language (Turkish, Lithuanian) or on the context (the final sigma) are not applied.
    /// </remarks>
    public static int GetUppercaseMapping(int codePoint, Span<int> destination) => GetCaseMapping(codePoint, destination, CaseMappingKind.Uppercase);

    /// <summary>Gets the full Titlecase_Mapping of a code point, which can be several code points (<c>ﬁ</c> → <c>Fi</c>).</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="destination">The buffer that receives the code points of the mapping.</param>
    /// <returns>The number of code points written. It is 1, <paramref name="codePoint"/> itself, when the code point has no mapping.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small. <see cref="MaxCaseMappingLength"/> elements are always enough.</exception>
    /// <remarks>
    /// The mapping is the default case conversion of the Unicode Standard (section 3.13) without its conditional
    /// mappings: the unconditional mappings of SpecialCasing.txt, else the simple mapping. The mappings that depend on
    /// the language (Turkish, Lithuanian) or on the context (the final sigma) are not applied.
    /// </remarks>
    public static int GetTitlecaseMapping(int codePoint, Span<int> destination) => GetCaseMapping(codePoint, destination, CaseMappingKind.Titlecase);

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

    private static int GetCaseMapping(int codePoint, Span<int> destination, CaseMappingKind kind)
    {
        // 6-byte entries: the code point, the offset of its mappings in the pool, then the lengths of the mappings.
        var records = UnicodeTables.SpecialCasingMappings;
        var offset = UnicodeTables.MayHaveSpecialCasing(codePoint) ? UnicodeTableReader.FindRecord(records, 6, codePoint) : -1;
        if (offset < 0)
        {
            if (destination.IsEmpty)
                throw new ArgumentException("The destination is too small", nameof(destination));

            destination[0] = kind switch
            {
                CaseMappingKind.Lowercase => ToSimpleLowercase(codePoint),
                CaseMappingKind.Uppercase => ToSimpleUppercase(codePoint),
                _ => ToSimpleTitlecase(codePoint),
            };
            return 1;
        }

        var index = records[offset + 3] | (records[offset + 4] << 8);
        var lengths = records[offset + 5];
        var lowercaseLength = lengths & 0x3;
        var titlecaseLength = (lengths >> 2) & 0x3;
        var (start, length) = kind switch
        {
            CaseMappingKind.Lowercase => (index, lowercaseLength),
            CaseMappingKind.Titlecase => (index + lowercaseLength, titlecaseLength),
            _ => (index + lowercaseLength + titlecaseLength, (lengths >> 4) & 0x3),
        };

        if (destination.Length < length)
            throw new ArgumentException("The destination is too small", nameof(destination));

        var pool = UnicodeTables.SpecialCasingPool;
        for (var i = 0; i < length; i++)
        {
            destination[i] = UnicodeTableReader.ReadUInt24(pool, (start + i) * 3);
        }

        return length;
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

    private enum CaseMappingKind
    {
        Lowercase,
        Uppercase,
        Titlecase,
    }
}
