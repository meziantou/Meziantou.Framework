namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The shaping model of Hangul: syllable blocks made of letters (jamo).</summary>
/// <remarks>
/// <para>
/// Microsoft, "Developing OpenType Fonts for Korean Hangul Script"; the Unicode Standard, section 3.12 "Conjoining
/// Jamo Behavior" (the arithmetic of the syllables) and section 18.6 "Hangul" (the tone marks).
/// </para>
/// <para>
/// <b>Syllables.</b> A syllable block is a leading consonant (L), a vowel (V) and an optional trailing consonant (T).
/// The blocks of modern Korean are also encoded as one character each, and a font draws them with one glyph: a
/// sequence of letters that makes such a block is composed, when the font has its glyph. The other blocks (the ones
/// of Old Hangul, or of a font without the glyph) are drawn with the glyphs of their letters, which the font shapes
/// with a feature per position: <c>ljmo</c>, <c>vjmo</c>, <c>tjmo</c>. A syllable character followed by a trailing
/// consonant is the same block as its letters followed by it.
/// </para>
/// <para>
/// <b>Tone marks.</b> The two tone marks of Middle Korean are written after the syllable and drawn on its left: a
/// mark that has a width is moved before the syllable; a mark without width is left where it is, for the font to
/// place. A tone mark without a syllable is shown with a dotted circle.
/// </para>
/// </remarks>
internal static class HangulShaper
{
    /// <summary>The value of <see cref="GlyphInfo.ShaperData"/> of a leading consonant of a block drawn with letters.</summary>
    public const byte Leading = 0x30;

    /// <summary>The value of <see cref="GlyphInfo.ShaperData"/> of a vowel of a block drawn with letters.</summary>
    public const byte Vowel = 0x31;

    /// <summary>The value of <see cref="GlyphInfo.ShaperData"/> of a trailing consonant of a block drawn with letters.</summary>
    public const byte Trailing = 0x32;

    // The arithmetic of the Unicode Standard, section 3.12.
    private const int SyllableBase = 0xAC00;
    private const int LeadingBase = 0x1100;
    private const int VowelBase = 0x1161;
    private const int TrailingBase = 0x11A7;
    private const int LeadingCount = 19;
    private const int VowelCount = 21;
    private const int TrailingCount = 28;
    private const int SyllableCount = LeadingCount * VowelCount * TrailingCount;
    private const int DottedCircle = 0x25CC;

    /// <summary>The features of the model: one per position in a block, applied with the features every script has.</summary>
    public static ShaperPlan Plan { get; } = new(
        [
            new(ShapingFeature.ToTag("ljmo"), 1, ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("vjmo"), 1, ShaperFeatureFlags.PerGlyph),
            new(ShapingFeature.ToTag("tjmo"), 1, ShaperFeatureFlags.PerGlyph),
        ],
        finalStage: 1);

    /// <summary>Composes the blocks the font has a glyph for, marks the letters of the others, and places the tone marks.</summary>
    /// <param name="buffer">The characters of the run, with their clusters.</param>
    /// <param name="face">The font.</param>
    /// <exception cref="ImageResourceLimitException">The glyphs exceed <see cref="ImageResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void ComposeSyllables(GlyphBuffer buffer, FontFace face)
    {
        // The syllable before the current character: [start, end).
        var start = 0;
        var end = 0;
        var i = 0;
        while (i < buffer.Length)
        {
            var codePoint = buffer.Info[i].CodePoint;
            if (codePoint is 0x302E or 0x302F)
            {
                var hasWidth = !(face.MapCodePoint(codePoint) is not 0 and var glyph && face.GetAdvanceWidth(glyph) == 0);
                if (start < end && end == i)
                {
                    if (hasWidth)
                    {
                        var mark = buffer.Info[i];
                        buffer.MergeClusters(start, i + 1);
                        mark.Cluster = buffer.Info[start].Cluster;
                        buffer.Info[start..i].CopyTo(buffer.Info[(start + 1)..]);
                        buffer.Info[start] = mark;
                    }
                }
                else if (face.MapCodePoint(DottedCircle) != 0)
                {
                    // The circle is the base of the mark: after a mark that has a width, as a syllable would be;
                    // before a mark without width. It stays with its mark: for the order of the marks around them,
                    // it is a mark of the same class.
                    var mark = buffer.Info[i];
                    buffer.Resize(i, 1, 2);
                    ref var circle = ref buffer.Info[hasWidth ? i + 1 : i];
                    buffer.Info[hasWidth ? i : i + 1] = mark;
                    TextShaper.SetCharacterProperties(ref circle, DottedCircle);
                    circle.Category = mark.Category;
                    circle.CombiningClass = mark.CombiningClass;
                    i++;
                }

                i++;
                start = end = i;
                continue;
            }

            start = i;
            if (IsLeading(codePoint) && i + 1 < buffer.Length && IsVowel(buffer.Info[i + 1].CodePoint))
            {
                var vowel = buffer.Info[i + 1].CodePoint;
                var trailing = i + 2 < buffer.Length && IsTrailing(buffer.Info[i + 2].CodePoint) ? buffer.Info[i + 2].CodePoint : 0;
                var length = trailing != 0 ? 3 : 2;
                if (IsModernLeading(codePoint) && IsModernVowel(vowel) && (trailing == 0 || IsModernTrailing(trailing)))
                {
                    var syllable = SyllableBase + ((((codePoint - LeadingBase) * VowelCount) + (vowel - VowelBase)) * TrailingCount) + (trailing != 0 ? trailing - TrailingBase : 0);
                    if (face.MapCodePoint(syllable) != 0)
                    {
                        Replace(buffer, i, length, syllable);
                        i++;
                        end = i;
                        continue;
                    }
                }

                MarkLetters(buffer, i, length);
                i += length;
                end = i;
                continue;
            }

            if (codePoint is >= SyllableBase and < SyllableBase + SyllableCount)
            {
                var index = codePoint - SyllableBase;
                var hasTrailing = index % TrailingCount != 0;
                var next = i + 1 < buffer.Length ? buffer.Info[i + 1].CodePoint : 0;
                var hasGlyph = face.MapCodePoint(codePoint) != 0;
                if (!hasTrailing && IsModernTrailing(next) && face.MapCodePoint(codePoint + (next - TrailingBase)) != 0)
                {
                    Replace(buffer, i, 2, codePoint + (next - TrailingBase));
                    i++;
                    end = i;
                    continue;
                }

                // The block is drawn with its letters when the font does not have its glyph, or when a trailing
                // consonant follows it that no syllable character includes.
                var isFollowed = !hasTrailing && IsTrailing(next);
                if (!hasGlyph || isFollowed)
                {
                    var leading = LeadingBase + (index / (VowelCount * TrailingCount));
                    var vowel = VowelBase + ((index / TrailingCount) % VowelCount);
                    var trailing = TrailingBase + (index % TrailingCount);
                    if (face.MapCodePoint(leading) != 0 && face.MapCodePoint(vowel) != 0 && (!hasTrailing || face.MapCodePoint(trailing) != 0))
                    {
                        var count = hasTrailing ? 3 : 2;
                        buffer.Resize(i, 1, count);
                        TextShaper.SetCharacterProperties(ref buffer.Info[i], leading);
                        TextShaper.SetCharacterProperties(ref buffer.Info[i + 1], vowel);
                        if (hasTrailing)
                        {
                            TextShaper.SetCharacterProperties(ref buffer.Info[i + 2], trailing);
                        }

                        // The trailing consonant that follows a block the font has is part of it; one that follows a
                        // block the font does not have is left alone.
                        if (hasGlyph && isFollowed)
                        {
                            count++;
                        }

                        MarkLetters(buffer, i, count);
                        i += count;
                        end = i;
                        continue;
                    }
                }

                if (hasGlyph)
                {
                    end = i + 1;
                }
            }

            i++;
        }
    }

    /// <summary>Gives the letters of the blocks drawn with letters the bit of the feature of their position.</summary>
    /// <param name="info">The glyphs of the run.</param>
    /// <param name="firstBit">The bit of the first feature in the masks of the glyphs.</param>
    public static void SetMasks(Span<GlyphInfo> info, int firstBit)
    {
        foreach (ref var glyph in info)
        {
            if (glyph.ShaperData is >= Leading and <= Trailing)
            {
                glyph.Mask |= 1u << (firstBit + glyph.ShaperData - Leading);
                glyph.ShaperData = 0;
            }
        }
    }

    private static void MarkLetters(GlyphBuffer buffer, int index, int count)
    {
        buffer.Info[index].ShaperData = Leading;
        buffer.Info[index + 1].ShaperData = Vowel;
        if (count > 2)
        {
            buffer.Info[index + 2].ShaperData = Trailing;
        }

        buffer.MergeClusters(index, index + count);
    }

    private static void Replace(GlyphBuffer buffer, int index, int count, int codePoint)
    {
        buffer.MergeClusters(index, index + count);
        buffer.Resize(index, count, 1);
        TextShaper.SetCharacterProperties(ref buffer.Info[index], codePoint);
    }

    private static bool IsLeading(int codePoint) => codePoint is (>= 0x1100 and <= 0x115F) or (>= 0xA960 and <= 0xA97C);

    private static bool IsVowel(int codePoint) => codePoint is (>= 0x1160 and <= 0x11A7) or (>= 0xD7B0 and <= 0xD7C6);

    private static bool IsTrailing(int codePoint) => codePoint is (>= 0x11A8 and <= 0x11FF) or (>= 0xD7CB and <= 0xD7FB);

    private static bool IsModernLeading(int codePoint) => codePoint is >= LeadingBase and < LeadingBase + LeadingCount;

    private static bool IsModernVowel(int codePoint) => codePoint is >= VowelBase and < VowelBase + VowelCount;

    private static bool IsModernTrailing(int codePoint) => codePoint is > TrailingBase and < TrailingBase + TrailingCount;
}
