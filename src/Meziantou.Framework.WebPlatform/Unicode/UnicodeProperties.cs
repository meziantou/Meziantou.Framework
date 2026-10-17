using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Unicode;

internal enum IdnaStatus : byte
{
    Valid = 0,
    Ignored = 1,
    Mapped = 2,
    Deviation = 3,
    Disallowed = 4,
}

/// <summary>Unicode properties backed by the generated tables (independent of the OS and ICU).</summary>
internal static class UnicodeProperties
{
    /// <summary>Returns the index of the range containing <paramref name="codePoint"/> in a sorted array of range starts.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindRange(ReadOnlySpan<int> starts, int codePoint)
    {
        var index = starts.BinarySearch(codePoint);
        return index >= 0 ? index : ~index - 1;
    }

    public static IdnaStatus GetIdnaStatus(int codePoint, out ReadOnlySpan<char> mapping)
    {
        var index = FindRange(IdnaMappingTable.RangeStarts, codePoint);
        var status = (IdnaStatus)IdnaMappingTable.RangeStatuses[index];
        var length = IdnaMappingTable.RangeMappingLengths[index];
        mapping = length == 0 ? default : IdnaMappingTable.MappingData.AsSpan(IdnaMappingTable.RangeMappingOffsets[index], length);
        return status;
    }

    public static int GetCanonicalCombiningClass(int codePoint)
    {
        if (codePoint < 0x300)
            return 0;

        var starts = NormalizationData.CombiningClassRangeStarts;
        var index = FindRange(starts, codePoint);
        if (index < 0 || codePoint > NormalizationData.CombiningClassRangeEnds[index])
            return 0;

        return NormalizationData.CombiningClassValues[index];
    }

    public static bool IsMark(int codePoint)
    {
        var index = FindRange(MarkData.RangeStarts, codePoint);
        return index >= 0 && codePoint <= MarkData.RangeEnds[index];
    }

    /// <summary>Whether the code point has the ID_Start property (https://www.unicode.org/reports/tr31/).</summary>
    public static bool IsIdStart(int codePoint)
    {
        var index = FindRange(IDStartData.RangeStarts, codePoint);
        return index >= 0 && codePoint <= IDStartData.RangeEnds[index];
    }

    /// <summary>Whether the code point has the ID_Continue property (https://www.unicode.org/reports/tr31/).</summary>
    public static bool IsIdContinue(int codePoint)
    {
        var index = FindRange(IDContinueData.RangeStarts, codePoint);
        return index >= 0 && codePoint <= IDContinueData.RangeEnds[index];
    }

    public static JoiningType GetJoiningType(int codePoint) => (JoiningType)JoiningTypeData.RangeValues[FindRange(JoiningTypeData.RangeStarts, codePoint)];

    /// <summary>The Vertical_Orientation of a code point (https://www.unicode.org/reports/tr50/): its orientation in vertical text.</summary>
    public static VerticalOrientation GetVerticalOrientation(int codePoint)
    {
        // The characters before U+00A7 are all rotated
        if (codePoint < 0xA7)
            return VerticalOrientation.R;

        return (VerticalOrientation)VerticalOrientationData.RangeValues[FindRange(VerticalOrientationData.RangeStarts, codePoint)];
    }

    /// <summary>Whether the code point has the Default_Ignorable_Code_Point property (https://www.unicode.org/reports/tr44/#Default_Ignorable_Code_Point): it is invisible (it has no advance).</summary>
    public static bool IsDefaultIgnorable(int codePoint)
    {
        if (codePoint < 0xAD)
            return false;

        var index = FindRange(DefaultIgnorableData.RangeStarts, codePoint);
        return index >= 0 && codePoint <= DefaultIgnorableData.RangeEnds[index];
    }

    /// <summary>Whether a code point has the Script property Han (https://www.unicode.org/reports/tr24/).</summary>
    public static bool IsHan(int codePoint)
    {
        if (codePoint < 0x2E80)
            return false;

        var index = FindRange(HanScriptData.RangeStarts, codePoint);
        return index >= 0 && codePoint <= HanScriptData.RangeEnds[index];
    }

    /// <summary>The Script property of a code point (https://www.unicode.org/reports/tr24/).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UnicodeScript GetScript(int codePoint) => (UnicodeScript)Lookup(ScriptData.Index, ScriptData.Blocks, ScriptData.Shift, codePoint);

    /// <summary>
    /// The Script_Extensions property of a code point (https://www.unicode.org/reports/tr24/#Script_Extensions): the scripts
    /// that use it, or an empty span when it is its Script property.
    /// </summary>
    public static ReadOnlySpan<byte> GetScriptExtensions(int codePoint)
    {
        var starts = ScriptExtensionsData.RangeStarts;
        if (codePoint < starts[0])
            return default;

        var index = FindRange(starts, codePoint);
        if (codePoint > ScriptExtensionsData.RangeEnds[index])
            return default;

        var set = ScriptExtensionsData.RangeSets[index];
        var offsets = ScriptExtensionsData.SetOffsets;
        return ScriptExtensionsData.SetValues[offsets[set]..offsets[set + 1]];
    }

    /// <summary>Reads a two-stage lookup table generated by <c>tools/Meziantou.Framework.WebPlatform.SpecGen</c> (<c>WriteTrie</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Lookup(ReadOnlySpan<ushort> index, ReadOnlySpan<byte> blocks, int shift, int codePoint)
    {
        Debug.Assert((uint)codePoint <= 0x10FFFF);
        return blocks[(index[codePoint >> shift] << shift) | (codePoint & ((1 << shift) - 1))];
    }

    /// <summary>The Line_Break property (https://www.unicode.org/reports/tr14/#Properties), before the resolution of rule LB1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LineBreakClass GetLineBreakClass(int codePoint) => (LineBreakClass)Lookup(LineBreakData.Index, LineBreakData.Blocks, LineBreakData.Shift, codePoint);

    /// <summary>The East_Asian_Width property (https://www.unicode.org/reports/tr11/).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EastAsianWidth GetEastAsianWidth(int codePoint) => (EastAsianWidth)Lookup(EastAsianWidthData.Index, EastAsianWidthData.Blocks, EastAsianWidthData.Shift, codePoint);

    /// <summary>Whether the East_Asian_Width is Fullwidth, Wide or Halfwidth (<c>$EastAsian</c> of UAX #14).</summary>
    public static bool IsEastAsianFullwidthWideOrHalfwidth(int codePoint) => GetEastAsianWidth(codePoint) is EastAsianWidth.F or EastAsianWidth.W or EastAsianWidth.H;

    /// <summary>The General_Category property, from the Unicode version of the other tables (unlike <see cref="CharUnicodeInfo"/>, which depends on the runtime).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UnicodeCategory GetGeneralCategory(int codePoint) => (UnicodeCategory)Lookup(GeneralCategoryData.Index, GeneralCategoryData.Blocks, GeneralCategoryData.Shift, codePoint);

    /// <summary>The packed segmentation properties of a code point (see <see cref="SegmentationData"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte GetSegmentationProperties(int codePoint) => Lookup(SegmentationData.Index, SegmentationData.Blocks, SegmentationData.Shift, codePoint);

    /// <summary>The Grapheme_Cluster_Break property (https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Break_Property_Values).</summary>
    public static GraphemeClusterBreak GetGraphemeClusterBreak(int codePoint) => (GraphemeClusterBreak)(GetSegmentationProperties(codePoint) & 0xF);

    /// <summary>The Indic_Conjunct_Break property (https://www.unicode.org/reports/tr44/#Indic_Conjunct_Break).</summary>
    public static IndicConjunctBreak GetIndicConjunctBreak(int codePoint) => (IndicConjunctBreak)((GetSegmentationProperties(codePoint) >> 4) & 0x3);

    /// <summary>The Extended_Pictographic property (https://www.unicode.org/reports/tr51/#Emoji_Properties).</summary>
    public static bool IsExtendedPictographic(int codePoint) => (GetSegmentationProperties(codePoint) & 0x40) != 0;

    /// <summary>
    /// Whether a code point is an Emoji Presentation Participating Code Point (https://drafts.csswg.org/css-fonts-4/#emoji-presentation-participating-code-points):
    /// an emoji character (the Emoji property, https://www.unicode.org/reports/tr51/#Emoji_Properties), which can be followed by
    /// a presentation selector (as Chrome).
    /// </summary>
    public static bool IsEmojiPresentationParticipating(int codePoint)
    {
        var index = FindRange(EmojiPresentationData.EmojiRangeStarts, codePoint);
        return index >= 0 && codePoint <= EmojiPresentationData.EmojiRangeEnds[index];
    }

    /// <summary>The Emoji_Presentation property (https://www.unicode.org/reports/tr51/#Emoji_Properties): the code point is displayed as an emoji by default.</summary>
    public static bool IsEmojiPresentation(int codePoint)
    {
        var index = FindRange(EmojiPresentationData.PresentationRangeStarts, codePoint);
        return index >= 0 && codePoint <= EmojiPresentationData.PresentationRangeEnds[index];
    }

    /// <summary>Whether a code point is a variation selector (https://www.unicode.org/versions/latest/core-spec/chapter-23/#G19053): VS1 to VS256.</summary>
    public static bool IsVariationSelector(int codePoint) => codePoint is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF) or (>= 0x180B and <= 0x180D) or 0x180F;

    /// <summary>Whether the Script property of the code point is Hangul.</summary>
    public static bool IsHangul(int codePoint) => (GetSegmentationProperties(codePoint) & 0x80) != 0;

    /// <summary>The packed word break properties of a code point (see <see cref="WordBreakData"/>): Word_Break (bits 0-4) and Extended_Pictographic (bit 5).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte GetWordBreakProperties(int codePoint) => Lookup(WordBreakData.Index, WordBreakData.Blocks, WordBreakData.Shift, codePoint);

    /// <summary>The Word_Break property (https://www.unicode.org/reports/tr29/#Word_Boundaries).</summary>
    public static WordBreak GetWordBreak(int codePoint) => (WordBreak)(GetWordBreakProperties(codePoint) & 0x1F);

    /// <summary>The Bidi_Class property (https://www.unicode.org/reports/tr9/#Bidirectional_Character_Types).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BidiClass GetBidiClass(int codePoint) => (BidiClass)(Lookup(BidiData.Index, BidiData.Blocks, BidiData.Shift, codePoint) & 0x1F);

    /// <summary>The Bidi_Paired_Bracket_Type property (https://www.unicode.org/reports/tr9/#Paired_Brackets).</summary>
    public static BidiPairedBracketType GetBidiPairedBracketType(int codePoint) => (BidiPairedBracketType)((Lookup(BidiData.Index, BidiData.Blocks, BidiData.Shift, codePoint) >> 5) & 0x3);

    /// <summary>The Bidi_Paired_Bracket property: the opening or closing bracket paired with the code point, or the code point itself when its Bidi_Paired_Bracket_Type is None.</summary>
    public static int GetBidiPairedBracket(int codePoint)
    {
        var index = BidiBracketData.CodePoints.BinarySearch(codePoint);
        return index >= 0 ? BidiBracketData.PairedBrackets[index] : codePoint;
    }

    /// <summary>The Bidi_Mirrored property: whether the glyph of the code point is mirrored in right-to-left text (https://www.unicode.org/reports/tr9/#Mirroring).</summary>
    public static bool IsBidiMirrored(int codePoint) => (Lookup(BidiData.Index, BidiData.Blocks, BidiData.Shift, codePoint) & 0x80) != 0;

    /// <summary>
    /// The Bidi_Mirroring_Glyph property (rule L4, https://www.unicode.org/reports/tr9/#L4): the code point whose glyph is the mirror
    /// image of the glyph of <paramref name="codePoint"/>, or -1 when there is none (some Bidi_Mirrored characters must be
    /// mirrored by the font, e.g. with the OpenType <c>rtlm</c> feature).
    /// </summary>
    public static int GetBidiMirroringGlyph(int codePoint)
    {
        var index = BidiMirroringData.CodePoints.BinarySearch(codePoint);
        return index >= 0 ? BidiMirroringData.Glyphs[index] : -1;
    }
}
