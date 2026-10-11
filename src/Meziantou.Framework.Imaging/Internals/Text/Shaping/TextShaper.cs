using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Shapes a run of text: turns its characters into the glyphs of a font and their positions, with the OpenType Layout
/// tables of the font.
/// </summary>
/// <remarks>
/// <para>A run has one font, one script, one language and one direction. The steps are:</para>
/// <list type="number">
/// <item><description>
/// <b>Characters.</b> The characters that the font does not have are decomposed when it has their parts, marks are put
/// in canonical order, and a character and its marks are composed when the font has the composed character: the text
/// is in the form the font was made for, whatever form it was typed in.
/// </description></item>
/// <item><description><b>Glyphs.</b> Each character gets its glyph from the character map, variation sequences included.</description></item>
/// <item><description>
/// <b>Substitution.</b> The lookups of the features of the GSUB table that apply to the run, in the order of the
/// lookup list.
/// </description></item>
/// <item><description>
/// <b>Positioning.</b> The advances of the glyphs, then the lookups of the GPOS table; the legacy kerning table when
/// the font has no kerning feature. The advance of a mark is removed.
/// </description></item>
/// </list>
/// <para>
/// The features applied to every run are the ones the OpenType specification asks a client to apply by default for
/// horizontal text: <c>ccmp</c>, <c>locl</c>, <c>rlig</c>, <c>calt</c>, <c>clig</c>, <c>liga</c>, <c>rclm</c>, the
/// direction features (<c>ltra</c>, <c>ltrm</c>, <c>rtla</c>, <c>rtlm</c>), the fraction features around a fraction
/// slash, and for positioning <c>abvm</c>, <c>blwm</c>, <c>mark</c>, <c>mkmk</c>, <c>curs</c>, <c>dist</c>,
/// <c>kern</c>.
/// </para>
/// </remarks>
internal static class TextShaper
{
    // The bits of the mask of a glyph: the features of every glyph, then the features of the parts of a fraction.
    private const uint GlobalMask = 0x01;
    private const uint FractionMask = 0x02;
    private const uint NumeratorMask = 0x04;
    private const uint DenominatorMask = 0x08;

    private const int FractionSlash = 0x2044;
    private const int ZeroWidthNonJoiner = 0x200C;
    private const int ZeroWidthJoiner = 0x200D;

    private static readonly uint Frac = ShapingFeature.ToTag("frac");
    private static readonly uint Numr = ShapingFeature.ToTag("numr");
    private static readonly uint Dnom = ShapingFeature.ToTag("dnom");
    private static readonly uint Kern = ShapingFeature.ToTag("kern");

    private static readonly uint[] LeftToRightFeatures = [ShapingFeature.ToTag("ltra"), ShapingFeature.ToTag("ltrm")];
    private static readonly uint[] RightToLeftFeatures = [ShapingFeature.ToTag("rtla"), ShapingFeature.ToTag("rtlm")];

    private static readonly uint[] DefaultFeatures =
    [
        ShapingFeature.ToTag("abvm"), ShapingFeature.ToTag("blwm"), ShapingFeature.ToTag("ccmp"), ShapingFeature.ToTag("locl"),
        ShapingFeature.ToTag("mark"), ShapingFeature.ToTag("mkmk"), ShapingFeature.ToTag("rlig"),
        ShapingFeature.ToTag("calt"), ShapingFeature.ToTag("clig"), ShapingFeature.ToTag("curs"), ShapingFeature.ToTag("dist"),
        Kern, ShapingFeature.ToTag("liga"), ShapingFeature.ToTag("rclm"),
    ];

    /// <summary>Adds the characters of a text to a buffer, each with the index of its first UTF-16 code unit as cluster.</summary>
    public static void AddText(GlyphBuffer buffer, ReadOnlySpan<char> text, int clusterOffset = 0)
    {
        var index = 0;
        while (index < text.Length)
        {
            Rune.DecodeFromUtf16(text[index..], out var rune, out var consumed);
            buffer.Add(rune.Value, clusterOffset + index);
            index += consumed;
        }
    }

    /// <summary>Shapes the characters of a buffer.</summary>
    /// <param name="buffer">The characters of the run; it receives the glyphs and their positions.</param>
    /// <param name="face">The font.</param>
    /// <param name="script">The script of the run.</param>
    /// <param name="language">The OpenType tag of the language of the run, or 0.</param>
    /// <param name="isRightToLeft"><see langword="true"/> when the run is laid out from right to left.</param>
    /// <param name="features">The features to turn on or off, besides the default ones.</param>
    /// <exception cref="InvalidImageContentException">A layout table of the font is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The glyphs exceed <see cref="ImageResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void Shape(GlyphBuffer buffer, FontFace face, UnicodeScript script, uint language, bool isRightToLeft, ReadOnlySpan<ShapingFeature> features)
    {
        buffer.StartShaping();
        SetCharacterProperties(buffer);
        FormClusters(buffer);
        Normalize(buffer, face);
        MapGlyphs(buffer, face);

        Span<uint> scripts = stackalloc uint[OpenTypeScript.MaxTagCount];
        scripts = scripts[..OpenTypeScript.GetTags(script, scripts)];

        var applier = new LayoutApplier(buffer, face);
        applier.SetGlyphClasses();
        var plan = new FeaturePlan(features);
        var hasFractions = false;
        if (face.Gsub is { } gsub)
        {
            var lookups = CollectLookups(gsub, face.GsubData.Span, scripts, language, isRightToLeft, plan, out hasFractions, out _);
            SetMasks(buffer, plan, hasFractions);
            applier.Begin(gsub, face.GsubData, isPositioning: false, isRightToLeft);
            foreach (var lookup in lookups)
            {
                applier.ApplyLookup(lookup.Index, lookup.Mask);
            }
        }
        else
        {
            SetMasks(buffer, plan, hasFractions);
        }

        HideIgnorables(buffer, face);

        // The positions: the advance of each glyph, then what the font adds to it.
        buffer.CreatePositions();
        var info = buffer.Info;
        var positions = buffer.Positions;
        for (var i = 0; i < info.Length; i++)
        {
            positions[i].XAdvance = info[i].ShaperData != 0 ? GetSpaceAdvance(face, info[i].ShaperData) : face.GetAdvanceWidth(info[i].Glyph);
        }

        var hasKerningFeature = false;
        var hasPositioning = false;
        if (face.Gpos is { } gpos)
        {
            var lookups = CollectLookups(gpos, face.GposData.Span, scripts, language, isRightToLeft, plan, out _, out hasKerningFeature);
            hasPositioning = lookups.Count > 0;
            applier.Begin(gpos, face.GposData, isPositioning: true, isRightToLeft);
            foreach (var lookup in lookups)
            {
                applier.ApplyLookup(lookup.Index, lookup.Mask);
            }
        }

        if (!hasKerningFeature && face.HasLegacyKerning && (!plan.TryGetMask(Kern, out var kerningMask) || kerningMask != 0))
        {
            ApplyLegacyKerning(buffer, face);
        }

        // A mark does not move the pen. Without positioning lookups, a mark that has an advance is drawn before the pen
        // it would have moved, over the glyph it follows.
        for (var i = 0; i < info.Length; i++)
        {
            if (info[i].IsMark)
            {
                if (!hasPositioning && !isRightToLeft)
                {
                    positions[i].XOffset -= positions[i].XAdvance;
                }

                positions[i].XAdvance = 0;
            }

            if (info[i].IsIgnorable)
            {
                positions[i] = default;
            }
        }

        LayoutApplier.FinishPositions(buffer.Positions, isRightToLeft);
    }

    /// <summary>Gives each character its general category, its combining class and what it is for shaping.</summary>
    private static void SetCharacterProperties(GlyphBuffer buffer)
    {
        foreach (ref var info in buffer.Info)
        {
            SetCharacterProperties(ref info, info.CodePoint);
        }
    }

    private static void SetCharacterProperties(ref GlyphInfo info, int codePoint)
    {
        info.CodePoint = codePoint;
        info.Glyph = codePoint;
        info.Category = UnicodeProperties.GetGeneralCategory(codePoint);
        info.CombiningClass = UnicodeProperties.GetCanonicalCombiningClass(codePoint);
        var flags = 0;

        // The fillers of Hangul are default ignorable for Unicode and are letters for a font: they take part in the
        // shaping of a syllable and are shown as the font draws them.
        if (UnicodeProperties.IsDefaultIgnorable(codePoint) && codePoint is not (0x115F or 0x1160 or 0x3164 or 0xFFA0))
        {
            flags |= GlyphInfo.DefaultIgnorable;
        }

        flags |= codePoint switch
        {
            ZeroWidthNonJoiner => GlyphInfo.ZeroWidthNonJoiner,
            ZeroWidthJoiner => GlyphInfo.ZeroWidthJoiner,
            _ => 0,
        };
        info.Flags = (byte)flags;
        info.ShaperData = 0;
    }

    /// <summary>
    /// Gives a character and what continues it one cluster: its marks, the modifier of an emoji, the parts of an emoji
    /// joined by zero width joiners, the second of two regional indicators, the tags of a tag sequence.
    /// </summary>
    private static void FormClusters(GlyphBuffer buffer)
    {
        var info = buffer.Info;
        var regionalIndicators = 0;
        for (var i = 1; i < info.Length; i++)
        {
            var codePoint = info[i].CodePoint;
            var previous = info[i - 1].CodePoint;
            regionalIndicators = previous is >= 0x1F1E6 and <= 0x1F1FF ? regionalIndicators + 1 : 0;
            var continues = info[i].Category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                || codePoint is ZeroWidthJoiner or (>= 0x1F3FB and <= 0x1F3FF) or (>= 0xE0020 and <= 0xE007F)
                || (previous == ZeroWidthJoiner && UnicodeEmoji.IsExtendedPictographic(new Rune(codePoint)))
                || (codePoint is >= 0x1F1E6 and <= 0x1F1FF && (regionalIndicators & 1) == 1);
            if (continues)
            {
                info[i].Cluster = info[i - 1].Cluster;
            }
        }
    }

    /// <summary>Puts the characters in the form the font has glyphs for.</summary>
    private static void Normalize(GlyphBuffer buffer, FontFace face)
    {
        // Decompose the characters the font does not have into parts it has.
        Span<int> parts = stackalloc int[UnicodeNormalization.MaxCanonicalDecompositionLength + 1];
        for (var i = 0; i < buffer.Length; i++)
        {
            var codePoint = buffer.Info[i].CodePoint;
            if (face.MapCodePoint(codePoint) != 0)
                continue;

            var count = Decompose(face, codePoint, parts, 0);
            if (count > 0)
            {
                buffer.Resize(i, 1, count);
                for (var j = 0; j < count; j++)
                {
                    SetCharacterProperties(ref buffer.Info[i + j], parts[j]);
                }

                i += count - 1;
                continue;
            }

            ReplaceMissingCharacter(ref buffer.Info[i], face);
        }

        // Put the marks of each character in canonical order: by combining class, the order of equal classes kept.
        var info = buffer.Info;
        for (var start = 0; start < info.Length; start++)
        {
            if (info[start].CombiningClass == 0)
                continue;

            var end = start + 1;
            while (end < info.Length && info[end].CombiningClass != 0)
            {
                end++;
            }

            var moved = false;
            for (var i = start + 1; i < end; i++)
            {
                var current = info[i];
                var j = i;
                while (j > start && info[j - 1].CombiningClass > current.CombiningClass)
                {
                    info[j] = info[j - 1];
                    j--;
                    moved = true;
                }

                info[j] = current;
            }

            if (moved)
            {
                buffer.MergeClusters(start, end);
            }

            start = end - 1;
        }

        // Compose a character and a mark when the font has the composed character and no other mark comes between.
        var starter = -1;
        var lastClass = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            ref var current = ref buffer.Info[i];
            var isMark = current.Category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            if (starter >= 0 && isMark && (i == starter + 1 || lastClass < current.CombiningClass)
                && UnicodeNormalization.TryComposeCanonical(buffer.Info[starter].CodePoint, current.CodePoint, out var composite)
                && face.MapCodePoint(composite) != 0)
            {
                buffer.MergeClusters(starter, i + 1);
                var cluster = buffer.Info[starter].Cluster;
                SetCharacterProperties(ref buffer.Info[starter], composite);
                buffer.Info[starter].Cluster = cluster;
                buffer.Resize(i, 1, 0);
                i--;
                continue;
            }

            if (current.CombiningClass == 0)
            {
                starter = i;
                lastClass = 0;
            }
            else
            {
                lastClass = current.CombiningClass;
            }
        }
    }

    /// <summary>Decomposes a character into characters the font has; returns their number, or 0 when the font lacks one of them.</summary>
    private static int Decompose(FontFace face, int codePoint, Span<int> parts, int depth)
    {
        if (depth > UnicodeNormalization.MaxCanonicalDecompositionLength || !UnicodeNormalization.TryDecomposeCanonical(codePoint, out var first, out var second))
            return 0;

        if (second >= 0 && face.MapCodePoint(second) == 0)
            return 0;

        // The first part can be decomposed again: it is, as far as needed to reach a character the font has.
        var count = face.MapCodePoint(first) != 0 ? 0 : Decompose(face, first, parts, depth + 1);
        if (count == 0)
        {
            if (face.MapCodePoint(first) == 0)
                return 0;

            parts[count++] = first;
        }

        if (second >= 0)
        {
            parts[count++] = second;
        }

        return count;
    }

    /// <summary>
    /// Replaces a character the font does not have by one that stands for it: the hyphen for the non-breaking hyphen,
    /// the space for the spaces of a fixed width, whose width is then set from the size of the font.
    /// </summary>
    private static void ReplaceMissingCharacter(ref GlyphInfo info, FontFace face)
    {
        if (info.CodePoint == 0x2011 && face.MapCodePoint(0x2010) != 0)
        {
            info.Glyph = 0x2010;
            return;
        }

        var width = GetSpaceKind(info.CodePoint);
        if (width != 0 && face.MapCodePoint(' ') != 0)
        {
            info.Glyph = ' ';
            info.ShaperData = (byte)width;
        }
    }

    // The widths of the spaces: a fraction of the em, or the width of another character.
    private const int SpaceOfSpace = 1;
    private const int SpaceEm = 2;
    private const int SpaceEm2 = 3;
    private const int SpaceEm3 = 4;
    private const int SpaceEm4 = 5;
    private const int SpaceEm6 = 6;
    private const int SpaceEm16 = 7;
    private const int SpaceFourEm18 = 8;
    private const int SpaceFigure = 9;
    private const int SpacePunctuation = 10;
    private const int SpaceNarrow = 11;
    private const int SpaceEm5 = 12;

    private static int GetSpaceKind(int codePoint)
    {
        return codePoint switch
        {
            0x00A0 => SpaceOfSpace,
            0x2000 or 0x2002 => SpaceEm2,
            0x2001 or 0x2003 or 0x3000 => SpaceEm,
            0x2004 => SpaceEm3,
            0x2005 => SpaceEm4,
            0x2006 => SpaceEm6,
            0x2007 => SpaceFigure,
            0x2008 => SpacePunctuation,
            0x2009 => SpaceEm5,
            0x200A => SpaceEm16,
            0x202F => SpaceNarrow,
            0x205F => SpaceFourEm18,
            _ => 0,
        };
    }

    private static int GetSpaceAdvance(FontFace face, int kind)
    {
        var em = face.UnitsPerEm;
        var space = face.GetAdvanceWidth(face.MapCodePoint(' '));
        switch (kind)
        {
            case SpaceEm:
                return em;

            case SpaceEm2:
                return (em + 1) / 2;

            case SpaceEm3:
                return (em + 1) / 3;

            case SpaceEm4:
                return (em + 2) / 4;

            case SpaceEm5:
                return (em + 2) / 5;

            case SpaceEm6:
                return (em + 3) / 6;

            case SpaceEm16:
                return (em + 8) / 16;

            case SpaceFourEm18:
                return (int)(((long)em * 4) / 18);

            case SpaceFigure:
                // The width of the digits, which are all as wide in the fonts that have this space.
                for (var digit = '0'; digit <= '9'; digit++)
                {
                    var glyph = face.MapCodePoint(digit);
                    if (glyph != 0)
                        return face.GetAdvanceWidth(glyph);
                }

                return space;

            case SpacePunctuation:
                {
                    var glyph = face.MapCodePoint('.');
                    glyph = glyph != 0 ? glyph : face.MapCodePoint(',');
                    return glyph != 0 ? face.GetAdvanceWidth(glyph) : space;
                }

            case SpaceNarrow:
                return space / 2;

            default:
                return space;
        }
    }

    /// <summary>Gives each character its glyph. A variation selector chooses the glyph of the character it follows when the font has one for the sequence.</summary>
    private static void MapGlyphs(GlyphBuffer buffer, FontFace face)
    {
        var info = buffer.Info;
        for (var i = 0; i < info.Length; i++)
        {
            var codePoint = info[i].Glyph;
            if (i + 1 < info.Length && IsVariationSelector(info[i + 1].CodePoint) && face.MapVariation(codePoint, info[i + 1].CodePoint, out var variation) == CmapVariation.Glyph)
            {
                info[i].Glyph = variation;
                info[i + 1].Glyph = face.MapCodePoint(info[i + 1].CodePoint);
                i++;
                continue;
            }

            info[i].Glyph = face.MapCodePoint(codePoint);
        }
    }

    private static bool IsVariationSelector(int codePoint) => codePoint is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF) or (>= 0x180B and <= 0x180D) or 0x180F;

    /// <summary>
    /// Gives each glyph the mask of the features that apply to it: all of them, and around a fraction slash the
    /// features that make the digits before it a numerator and the digits after it a denominator.
    /// </summary>
    private static void SetMasks(GlyphBuffer buffer, FeaturePlan plan, bool hasFractions)
    {
        var info = buffer.Info;
        plan.SetMasks(info);

        if (!hasFractions)
            return;

        for (var i = 0; i < info.Length; i++)
        {
            if (info[i].CodePoint != FractionSlash)
                continue;

            var start = i;
            while (start > 0 && info[start - 1].Category == UnicodeCategory.DecimalDigitNumber)
            {
                start--;
            }

            var end = i + 1;
            while (end < info.Length && info[end].Category == UnicodeCategory.DecimalDigitNumber)
            {
                end++;
            }

            // A fraction has digits on both sides of the slash.
            if (start == i || end == i + 1)
                continue;

            for (var j = start; j < i; j++)
            {
                info[j].Mask |= NumeratorMask | FractionMask;
            }

            info[i].Mask |= FractionMask;
            for (var j = i + 1; j < end; j++)
            {
                info[j].Mask |= FractionMask | DenominatorMask;
            }

            i = end - 1;
        }
    }

    /// <summary>
    /// Gets the lookups of a table to apply to a run, in the order of the lookup list: those of the feature the
    /// language system requires and of each feature that is on.
    /// </summary>
    private static List<PlannedLookup> CollectLookups(
        LayoutTable table,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<uint> scripts,
        uint language,
        bool isRightToLeft,
        FeaturePlan plan,
        out bool hasFractions,
        out bool hasKerning)
    {
        hasFractions = false;
        hasKerning = false;
        var lookups = new List<PlannedLookup>();
        var system = table.FindLanguageSystem(data, scripts, language);
        if (system == 0)
            return lookups;

        var required = LayoutTable.GetRequiredFeature(data, system);
        if (required != LayoutTable.NoFeature && table.GetFeatureTag(data, required) != 0)
        {
            AddLookups(table, data, required, GlobalMask, lookups);
        }

        // The fraction features need each other: a numerator without a fraction bar is not a fraction.
        var count = LayoutTable.GetFeatureCount(data, system);
        var hasFrac = false;
        var hasNumr = false;
        var hasDnom = false;
        for (var i = 0; i < count; i++)
        {
            var tag = table.GetFeatureTag(data, LayoutTable.GetFeatureIndex(data, system, i));
            hasFrac |= tag == Frac;
            hasNumr |= tag == Numr;
            hasDnom |= tag == Dnom;
        }

        hasFractions = hasFrac || (hasNumr && hasDnom);
        for (var i = 0; i < count; i++)
        {
            var feature = LayoutTable.GetFeatureIndex(data, system, i);
            var tag = table.GetFeatureTag(data, feature);
            if (tag == 0)
                continue;

            if (plan.TryGetMask(tag, out var mask))
            {
                // A feature the caller names: it applies where its value is not zero.
                if (mask != 0)
                {
                    hasKerning |= tag == Kern;
                    AddLookups(table, data, feature, mask, lookups);
                }
            }
            else if (tag == Frac || tag == Numr || tag == Dnom)
            {
                if (hasFractions)
                {
                    AddLookups(table, data, feature, tag == Frac ? FractionMask : tag == Numr ? NumeratorMask : DenominatorMask, lookups);
                }
            }
            else if (DefaultFeatures.AsSpan().Contains(tag) || (isRightToLeft ? RightToLeftFeatures : LeftToRightFeatures).AsSpan().Contains(tag))
            {
                hasKerning |= tag == Kern;
                AddLookups(table, data, feature, GlobalMask, lookups);
            }
        }

        // The lookups are applied in the order of the lookup list, each one once: a lookup of several features applies
        // where any of them does.
        lookups.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        var unique = 0;
        for (var i = 0; i < lookups.Count; i++)
        {
            if (unique > 0 && lookups[unique - 1].Index == lookups[i].Index)
            {
                lookups[unique - 1] = lookups[unique - 1] with { Mask = lookups[unique - 1].Mask | lookups[i].Mask };
            }
            else
            {
                lookups[unique++] = lookups[i];
            }
        }

        lookups.RemoveRange(unique, lookups.Count - unique);
        return lookups;
    }

    private static void AddLookups(LayoutTable table, ReadOnlySpan<byte> data, int feature, uint mask, List<PlannedLookup> lookups)
    {
        var count = table.GetFeatureLookupCount(data, feature);
        var lookupCount = table.GetLookupCount(data);
        for (var i = 0; i < count; i++)
        {
            // A feature can name a lookup the table does not have: it is ignored.
            var lookup = table.GetFeatureLookup(data, feature, i);
            if (lookup < lookupCount)
            {
                lookups.Add(new PlannedLookup(lookup, mask));
            }
        }
    }

    /// <summary>
    /// Makes the glyphs of the characters without visible form invisible: they become the glyph of the space, without
    /// advance, or are removed when the font has no space.
    /// </summary>
    private static void HideIgnorables(GlyphBuffer buffer, FontFace face)
    {
        var space = face.MapCodePoint(' ');
        for (var i = buffer.Length - 1; i >= 0; i--)
        {
            ref var info = ref buffer.Info[i];
            if (!info.IsIgnorable)
                continue;

            if (space != 0)
            {
                info.Glyph = space;
            }
            else
            {
                buffer.Delete(i);
            }
        }
    }

    /// <summary>Applies the pair adjustments of the legacy kerning table between each glyph and the next visible glyph that is not a mark.</summary>
    private static void ApplyLegacyKerning(GlyphBuffer buffer, FontFace face)
    {
        var info = buffer.Info;
        var positions = buffer.Positions;
        var previous = -1;
        for (var i = 0; i < info.Length; i++)
        {
            // The glyphs of a pair are the visible ones: marks and invisible glyphs do not come between them.
            if (info[i].IsMark || info[i].IsIgnorable)
                continue;

            if (previous >= 0)
            {
                // Half of the adjustment is taken from the advance of the first glyph, and the other half from the
                // second one, which is moved back by it: the two glyphs are as far apart as the font asks, and the
                // boundary between them stays between them.
                var adjustment = face.GetLegacyKerning(info[previous].Glyph, info[i].Glyph);
                var first = adjustment >> 1;
                var second = adjustment - first;
                positions[previous].XAdvance += first;
                positions[i].XAdvance += second;
                positions[i].XOffset += second;
            }

            previous = i;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PlannedLookup(int Index, uint Mask);

    /// <summary>
    /// The features the caller sets, and where the mask of a glyph holds the value of each one. A feature that is on
    /// for the whole run shares the bit of the features every glyph has; a feature with a value per range of
    /// characters, or with a value above 1 (the choice of an alternate), has bits of its own.
    /// </summary>
    private sealed class FeaturePlan
    {
        // The first bit after the ones of the default and fraction features.
        private const int FirstShift = 4;

        private readonly List<(uint Tag, uint Mask)> _masks = [];
        private readonly ShapingFeature[] _features;

        public FeaturePlan(ReadOnlySpan<ShapingFeature> features)
        {
            _features = features.ToArray();
            var shift = FirstShift;
            foreach (var feature in _features)
            {
                if (TryGetMask(feature.Tag, out _))
                    continue;

                // The largest value of the feature, and whether it is the same for every character.
                var isGlobal = true;
                var maximum = 0;
                var last = 0;
                foreach (var setting in _features)
                {
                    if (setting.Tag != feature.Tag)
                        continue;

                    isGlobal &= setting.IsGlobal;
                    maximum = Math.Max(maximum, setting.Value);
                    last = setting.Value;
                }

                var bits = 32 - BitOperations.LeadingZeroCount((uint)Math.Max(maximum, 1));
                if ((isGlobal && last <= 1) || shift + bits > 32)
                {
                    // On or off for the whole run. A feature that no bit is left for is on where its last value is.
                    _masks.Add((feature.Tag, last > 0 ? GlobalMask : 0));
                }
                else
                {
                    _masks.Add((feature.Tag, ((1u << bits) - 1) << shift));
                    shift += bits;
                }
            }
        }

        /// <summary>Gets the bits that hold the value of a feature the caller sets; 0 for a feature that is off.</summary>
        /// <returns><see langword="false"/> when the caller does not set the feature.</returns>
        public bool TryGetMask(uint tag, out uint mask)
        {
            foreach (var entry in _masks)
            {
                if (entry.Tag == tag)
                {
                    mask = entry.Mask;
                    return true;
                }
            }

            mask = 0;
            return false;
        }

        /// <summary>Gives each glyph the bit of the features of every glyph and the value of each feature at its character.</summary>
        public void SetMasks(Span<GlyphInfo> info)
        {
            foreach (ref var glyph in info)
            {
                glyph.Mask = GlobalMask;
            }

            // In the order they were given: a later setting replaces an earlier one where both apply. A default feature
            // that is only turned off for a range stays on elsewhere.
            foreach (var entry in _masks)
            {
                if (entry.Mask is 0 or GlobalMask)
                    continue;

                var shift = BitOperations.TrailingZeroCount(entry.Mask);
                var isDefault = DefaultFeatures.AsSpan().Contains(entry.Tag);
                foreach (ref var glyph in info)
                {
                    var value = isDefault ? 1u : 0u;
                    foreach (var setting in _features)
                    {
                        if (setting.Tag == entry.Tag && glyph.Cluster >= setting.Start && glyph.Cluster < setting.End)
                        {
                            value = (uint)Math.Max(setting.Value, 0);
                        }
                    }

                    glyph.Mask |= (value << shift) & entry.Mask;
                }
            }
        }
    }
}
