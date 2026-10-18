using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The Unicode properties of a <see cref="GlyphInfo"/> (HarfBuzz's <c>hb_unicode_props_flags_t</c>).</summary>
[Flags]
internal enum UnicodeGlyphProperties : ushort
{
    None = 0,

    /// <summary>The General_Category, as a <see cref="UnicodeCategory"/>.</summary>
    GeneralCategoryMask = 0x001F,

    /// <summary>A default ignorable character: hidden after the shaping, skipped by the lookups.</summary>
    Ignorable = 0x0020,

    /// <summary>A Mongolian free variation selector, a tag character or a combining grapheme joiner: hidden after the shaping, but not skipped by GSUB.</summary>
    Hidden = 0x0040,

    /// <summary>The character continues the grapheme cluster of the previous one (a mark, a ZWJ, an emoji modifier).</summary>
    Continuation = 0x0080,

    // The high byte: the modified combining class of the marks, the space type of the spaces, or the flags of the format characters

    /// <summary>A zero width joiner (with the General_Category Cf).</summary>
    FormatZwj = 0x0100,

    /// <summary>A zero width non-joiner (with the General_Category Cf).</summary>
    FormatZwnj = 0x0200,

    /// <summary>A variation selector that the font does not support for its base (with the General_Category Cf).</summary>
    FormatVariationSelector = 0x0400,
}

/// <summary>The flags of the glyphs set by the shaping in the low bits of <see cref="GlyphInfo.Mask"/> (HarfBuzz's <c>hb_glyph_flags_t</c>).</summary>
internal static class GlyphFlags
{
    /// <summary>Breaking the text before the cluster of the glyph and shaping the two parts separately would give another result.</summary>
    public const uint UnsafeToBreak = 0x1;

    /// <summary>Concatenating the results of the shaping of two texts split before the cluster of the glyph would give another result.</summary>
    public const uint UnsafeToConcat = 0x2;

    /// <summary>A tatweel can be inserted before the glyph (not produced: it makes the glyph unsafe to break).</summary>
    public const uint SafeToInsertTatweel = 0x4;

    /// <summary>All the flags.</summary>
    public const uint Defined = 0x7;
}

/// <summary>
/// A character, then a glyph, of a <see cref="ShapingBuffer"/> (HarfBuzz's <c>hb_glyph_info_t</c>, with its variables as
/// fields): <see cref="Codepoint"/> is the code point until the glyphs are mapped, then the glyph.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct GlyphInfo
{
    /// <summary>The code point, or the glyph after the mapping of the characters to glyphs.</summary>
    public int Codepoint;

    /// <summary>The masks of the features that apply to the glyph, and the <see cref="GlyphFlags"/> in the low bits.</summary>
    public uint Mask;

    /// <summary>The UTF-16 index of the first character of the cluster of the glyph in the shaped text.</summary>
    public int Cluster;

    /// <summary>The glyph of the character, set by the normalization (HarfBuzz's <c>normalizer_glyph_index</c>).</summary>
    public int GlyphIndex;

    /// <summary>The Unicode properties.</summary>
    public UnicodeGlyphProperties UnicodeProperties;

    /// <summary>The glyph properties.</summary>
    public GlyphProperties GlyphProperties;

    /// <summary>The ligature properties: the ligature id (bits 5-7), whether the glyph is a ligature (bit 4), the number of components of a ligature or the component of a mark (bits 0-3).</summary>
    public byte LigatureProperties;

    /// <summary>The syllable of the glyph (set by the shapers of the syllabic scripts; the lookups with per-syllable matching do not cross the syllables).</summary>
    public byte Syllable;

    /// <summary>A category of the character for the shaper of the script (e.g. the Indic category).</summary>
    public byte ShaperCategory;

    /// <summary>An auxiliary value of the shaper of the script (e.g. the Arabic joining action, the Indic position).</summary>
    public byte ShaperAuxiliary;

    private const byte IsLigatureBase = 0x10;

    /// <summary>The General_Category of the character.</summary>
    public readonly UnicodeCategory GeneralCategory => (UnicodeCategory)(UnicodeProperties & UnicodeGlyphProperties.GeneralCategoryMask);

    /// <summary>Sets the General_Category (clears the high byte of the Unicode properties).</summary>
    public void SetGeneralCategory(UnicodeCategory category) => UnicodeProperties = (UnicodeGlyphProperties)(((int)UnicodeProperties & (0xFF & ~(int)UnicodeGlyphProperties.GeneralCategoryMask)) | (int)category);

    /// <summary>Whether the character is a mark (General_Category M).</summary>
    public readonly bool IsUnicodeMark => GeneralCategory is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>The modified combining class of a mark (0 for the other characters).</summary>
    public readonly int ModifiedCombiningClass => IsUnicodeMark ? (int)UnicodeProperties >> 8 : 0;

    public void SetModifiedCombiningClass(int combiningClass)
    {
        if (IsUnicodeMark)
        {
            UnicodeProperties = (UnicodeGlyphProperties)((combiningClass << 8) | ((int)UnicodeProperties & 0xFF));
        }
    }

    /// <summary>Whether the character is a space separator (General_Category Zs).</summary>
    public readonly bool IsUnicodeSpace => GeneralCategory == UnicodeCategory.SpaceSeparator;

    /// <summary>The fallback type of a space (<see cref="SpaceFallback"/>), stored in the high byte of the Unicode properties.</summary>
    public readonly SpaceFallback SpaceFallback => IsUnicodeSpace ? (SpaceFallback)((int)UnicodeProperties >> 8) : SpaceFallback.NotSpace;

    public void SetSpaceFallback(SpaceFallback value)
    {
        if (IsUnicodeSpace)
        {
            UnicodeProperties = (UnicodeGlyphProperties)(((int)value << 8) | ((int)UnicodeProperties & 0xFF));
        }
    }

    /// <summary>Whether the character is a default ignorable character that was not substituted (it is hidden after the shaping).</summary>
    public readonly bool IsDefaultIgnorable => (UnicodeProperties & UnicodeGlyphProperties.Ignorable) != UnicodeGlyphProperties.None && !IsSubstituted;

    public void ClearDefaultIgnorable() => UnicodeProperties &= ~UnicodeGlyphProperties.Ignorable;

    public readonly bool IsHidden => (UnicodeProperties & UnicodeGlyphProperties.Hidden) != UnicodeGlyphProperties.None;

    public void Unhide() => UnicodeProperties &= ~UnicodeGlyphProperties.Hidden;

    public readonly bool IsContinuation => (UnicodeProperties & UnicodeGlyphProperties.Continuation) != UnicodeGlyphProperties.None;

    public readonly bool IsZwnj => GeneralCategory == UnicodeCategory.Format && (UnicodeProperties & UnicodeGlyphProperties.FormatZwnj) != UnicodeGlyphProperties.None;

    public readonly bool IsZwj => GeneralCategory == UnicodeCategory.Format && (UnicodeProperties & UnicodeGlyphProperties.FormatZwj) != UnicodeGlyphProperties.None;

    /// <summary>Whether the character is a variation selector that the font does not support for its base.</summary>
    public readonly bool IsVariationSelector => GeneralCategory == UnicodeCategory.Format && (UnicodeProperties & UnicodeGlyphProperties.FormatVariationSelector) != UnicodeGlyphProperties.None;

    public void SetVariationSelector(bool customize)
    {
        if (customize)
        {
            SetGeneralCategory(UnicodeCategory.Format);
            UnicodeProperties |= UnicodeGlyphProperties.FormatVariationSelector;
        }
        else
        {
            SetGeneralCategory(UnicodeCategory.NonSpacingMark);
        }
    }

    // The glyph properties

    public readonly bool IsBaseGlyph => (GlyphProperties & GlyphProperties.BaseGlyph) != GlyphProperties.None;

    public readonly bool IsLigature => (GlyphProperties & GlyphProperties.Ligature) != GlyphProperties.None;

    public readonly bool IsMark => (GlyphProperties & GlyphProperties.Mark) != GlyphProperties.None;

    public readonly bool IsSubstituted => (GlyphProperties & GlyphProperties.Substituted) != GlyphProperties.None;

    public readonly bool IsLigated => (GlyphProperties & GlyphProperties.Ligated) != GlyphProperties.None;

    public readonly bool IsMultiplied => (GlyphProperties & GlyphProperties.Multiplied) != GlyphProperties.None;

    public readonly bool IsLigatedAndDidNotMultiply => IsLigated && !IsMultiplied;

    public void ClearLigatedAndMultiplied() => GlyphProperties &= ~(GlyphProperties.Ligated | GlyphProperties.Multiplied);

    // The ligature properties (HarfBuzz's lig_props: see the comment of hb-ot-layout.hh)

    public readonly int LigatureId => LigatureProperties >> 5;

    /// <summary>The component of a ligature that the glyph belongs to (0 for a ligature glyph).</summary>
    public readonly int LigatureComponent => (LigatureProperties & IsLigatureBase) != 0 ? 0 : LigatureProperties & 0x0F;

    /// <summary>The number of components of a ligature glyph (1 for the other glyphs).</summary>
    public readonly int LigatureComponentCount => IsLigature && (LigatureProperties & IsLigatureBase) != 0 ? LigatureProperties & 0x0F : 1;

    /// <summary>The number of components the glyph adds to a ligature (0 for a piece of a multiple substitution after the first one).</summary>
    public readonly int LigatureComponentCountInLigation => IsMultiplied && LigatureComponent != 0 ? 0 : LigatureComponentCount;

    public void SetLigaturePropertiesForLigature(int ligatureId, int componentCount) => LigatureProperties = (byte)((ligatureId << 5) | IsLigatureBase | (componentCount & 0x0F));

    public void SetLigaturePropertiesForMark(int ligatureId, int component) => LigatureProperties = (byte)((ligatureId << 5) | (component & 0x0F));

    public void SetLigaturePropertiesForComponent(int component) => SetLigaturePropertiesForMark(0, component);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasMask(uint mask) => (Mask & mask) != 0;
}

/// <summary>The spaces whose advance is synthesized when the font does not support them (HarfBuzz's <c>hb_unicode_funcs_t::space_t</c>): the value is the divisor of the em size.</summary>
internal enum SpaceFallback : byte
{
    NotSpace = 0,
    Em = 1,
    Em2 = 2,
    Em3 = 3,
    Em4 = 4,
    Em5 = 5,
    Em6 = 6,
    Em16 = 16,
    FourEm18 = 17,
    Space = 18,
    Figure = 19,
    Punctuation = 20,
    Narrow = 21,
}

/// <summary>The kinds of GPOS attachments of a <see cref="GlyphPosition"/>.</summary>
internal enum AttachmentType : byte
{
    None = 0,
    Mark = 1,
    Cursive = 2,
}

/// <summary>
/// The position of a glyph of a <see cref="ShapingBuffer"/> in font units (HarfBuzz's <c>hb_glyph_position_t</c>): the advances
/// and the offsets, with the y axis upwards; the vertical advances are negative (downwards).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct GlyphPosition
{
    public double XAdvance;
    public double YAdvance;
    public double XOffset;
    public double YOffset;

    /// <summary>The glyph this glyph attaches to, relative to this glyph (0 when it is not attached).</summary>
    public short AttachChain;

    public AttachmentType AttachType;
}
