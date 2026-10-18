using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The positioning of the fonts without GPOS (HarfBuzz's hb-ot-shape-fallback.cc, MIT license): the marks are placed around
/// their base from their combining class and the extents of the glyphs, the spaces that the font does not support get the
/// advance of their type, and the pairs of the <c>kern</c> table are applied.
/// </summary>
internal static class FallbackPositioning
{
    // The canonical combining classes
    private const int AttachedBelowLeft = 200;
    private const int AttachedBelow = 202;
    private const int AttachedAbove = 214;
    private const int AttachedAboveRight = 216;
    private const int BelowLeft = 218;
    private const int Below = 220;
    private const int BelowRight = 222;
    private const int AboveLeft = 228;
    private const int Above = 230;
    private const int AboveRight = 232;
    private const int DoubleBelow = 233;
    private const int DoubleAbove = 234;

    /// <summary>
    /// The combining class of a mark for its fallback position (HarfBuzz's <c>recategorize_combining_class</c>): the
    /// fixed-position classes of Hebrew, Arabic, Syriac, Thai, Lao and Tibetan are mapped to the positional classes.
    /// </summary>
    private static int RecategorizeCombiningClass(int codePoint, int combiningClass)
    {
        if (combiningClass >= 200)
            return combiningClass;

        // Thai and Lao need some per-character work
        if ((codePoint & ~0xFF) == 0x0E00)
        {
            if (combiningClass == 0)
            {
                switch (codePoint)
                {
                    case 0x0E31 or 0x0E34 or 0x0E35 or 0x0E36 or 0x0E37 or 0x0E47 or 0x0E4C or 0x0E4D or 0x0E4E:
                        combiningClass = AboveRight;
                        break;
                    case 0x0EB1 or 0x0EB4 or 0x0EB5 or 0x0EB6 or 0x0EB7 or 0x0EBB or 0x0ECC or 0x0ECD:
                        combiningClass = Above;
                        break;
                    case 0x0EBC:
                        combiningClass = Below;
                        break;
                }
            }
            else if (codePoint == 0x0E3A)
            {
                // The Thai virama is below-right
                combiningClass = BelowRight;
            }
        }

        return combiningClass switch
        {
            // Hebrew: sheva, hataf segol, hataf patah, hataf qamats, hiriq, tsere, segol, patah, qamats, qubuts, meteg
            22 or 15 or 16 or 17 or 23 or 18 or 19 or 20 or 21 or 24 or 25 => Below,
            13 => AttachedAbove, // rafe
            10 => AboveRight, // shin dot
            11 or 14 => AboveLeft, // sin dot, holam

            // Hebrew point varika
            26 => Above,

            // Arabic and Syriac: fathatan, dammatan, fatha, damma, shadda, sukun, superscript alef, superscript alaph
            28 or 29 or 31 or 32 or 27 or 34 or 35 or 36 => Above,

            // kasratan, kasra
            30 or 33 => Below,

            // Thai: sara u / sara uu, mai
            3 => BelowRight,
            107 => AboveRight,

            // Lao: sign u / sign uu, mai
            118 => Below,
            122 => Above,

            // Tibetan: sign aa, sign i, sign u
            129 => Below,
            132 => Above,
            131 => Below,
            _ => combiningClass,
        };
    }

    /// <summary>Maps the combining classes of the non-spacing marks to their positional classes (before the substitutions).</summary>
    public static void RecategorizeMarks(ShapingBuffer buffer)
    {
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (info[i].GeneralCategory == UnicodeCategory.NonSpacingMark)
            {
                info[i].SetModifiedCombiningClass(RecategorizeCombiningClass(info[i].Codepoint, info[i].ModifiedCombiningClass));
            }
        }
    }

    private static void ZeroMarkAdvances(ShapingBuffer buffer, int start, int end, bool adjustOffsets)
    {
        var info = buffer.Info;
        var positions = buffer.Positions;
        for (var i = start; i < end; i++)
        {
            if (info[i].GeneralCategory == UnicodeCategory.NonSpacingMark)
            {
                if (adjustOffsets)
                {
                    positions[i].XOffset -= positions[i].XAdvance;
                    positions[i].YOffset -= positions[i].YAdvance;
                }

                positions[i].XAdvance = 0;
                positions[i].YAdvance = 0;
            }
        }
    }

    /// <summary>The extents of a glyph (HarfBuzz's <c>hb_glyph_extents_t</c>: the y bearing is the top, the height is negative).</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Extents
    {
        public double XBearing;
        public double YBearing;
        public double Width;
        public double Height;
    }

    private static void PositionMark(ShapingBuffer buffer, in ShapingFont font, ref Extents baseExtents, int i, int combiningClass)
    {
        if (!font.TryGetGlyphExtents(buffer.Info[i].Codepoint, out var markXBearing, out var markYBearing, out var markWidth, out var markHeight))
            return;

        var gap = font.UnitsPerEm / 16d;
        ref var position = ref buffer.Positions[i];
        position.XOffset = 0;
        position.YOffset = 0;

        // The horizontal position (the left and right marks are not positioned)
        switch (combiningClass)
        {
            case DoubleBelow or DoubleAbove when buffer.Direction == ShapingDirection.LeftToRight:
                position.XOffset = baseExtents.XBearing + baseExtents.Width - (markWidth / 2) - markXBearing;
                break;
            case DoubleBelow or DoubleAbove when buffer.Direction == ShapingDirection.RightToLeft:
                position.XOffset = baseExtents.XBearing - (markWidth / 2) - markXBearing;
                break;
            case AttachedBelowLeft or BelowLeft or AboveLeft:
                // Left aligned
                position.XOffset = baseExtents.XBearing - markXBearing;
                break;
            case AttachedAboveRight or BelowRight or AboveRight:
                // Right aligned
                position.XOffset = baseExtents.XBearing + baseExtents.Width - markWidth - markXBearing;
                break;
            default:
                // Centered
                position.XOffset = baseExtents.XBearing + ((baseExtents.Width - markWidth) / 2) - markXBearing;
                break;
        }

        // The vertical position
        switch (combiningClass)
        {
            case DoubleBelow or BelowLeft or Below or BelowRight or AttachedBelowLeft or AttachedBelow:
                if (combiningClass is not (AttachedBelowLeft or AttachedBelow))
                {
                    // Add a gap
                    baseExtents.Height -= gap;
                }

                position.YOffset = baseExtents.YBearing + baseExtents.Height - markYBearing;

                // Never shift up the "below" marks
                if ((gap > 0) == (position.YOffset > 0))
                {
                    baseExtents.Height -= position.YOffset;
                    position.YOffset = 0;
                }

                baseExtents.Height += markHeight;
                break;

            case DoubleAbove or AboveLeft or Above or AboveRight or AttachedAbove or AttachedAboveRight:
                if (combiningClass is not (AttachedAbove or AttachedAboveRight))
                {
                    // Add a gap
                    baseExtents.YBearing += gap;
                    baseExtents.Height -= gap;
                }

                position.YOffset = baseExtents.YBearing - markYBearing - markHeight;

                // Do not shift down the "above" marks too much
                if ((gap > 0) != (position.YOffset > 0))
                {
                    var correction = -position.YOffset / 2;
                    baseExtents.YBearing += correction;
                    baseExtents.Height -= correction;
                    position.YOffset += correction;
                }

                baseExtents.YBearing -= markHeight;
                baseExtents.Height += markHeight;
                break;
        }
    }

    private static void PositionAroundBase(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer, int baseIndex, int end, bool adjustOffsets)
    {
        buffer.UnsafeToBreak(baseIndex, end);
        var info = buffer.Info;
        var positions = buffer.Positions;
        if (!font.TryGetGlyphExtents(info[baseIndex].Codepoint, out _, out var yBearing, out _, out var height))
        {
            // Without extents, the marks are only zero width
            ZeroMarkAdvances(buffer, baseIndex + 1, end, adjustOffsets);
            return;
        }

        // The horizontal advance is used for the horizontal positioning (also for the glyphs without ink)
        var baseExtents = new Extents { XBearing = 0, YBearing = yBearing + positions[baseIndex].YOffset, Width = font.GetHorizontalAdvance(info[baseIndex].Codepoint), Height = height };
        var ligatureId = info[baseIndex].LigatureId;
        var componentCount = info[baseIndex].LigatureComponentCount;
        double xOffset = 0, yOffset = 0;
        if (!buffer.IsBackward)
        {
            xOffset = -positions[baseIndex].XAdvance;
            yOffset = -positions[baseIndex].YAdvance;
        }

        var componentExtents = baseExtents;
        var lastComponent = -1;
        var lastCombiningClass = 255;
        var clusterExtents = baseExtents;
        for (var i = baseIndex + 1; i < end; i++)
        {
            if (info[i].ModifiedCombiningClass != 0)
            {
                if (componentCount > 1)
                {
                    var thisLigatureId = info[i].LigatureId;
                    var thisComponent = info[i].LigatureComponent - 1;

                    // Attach to the last component when the mark does not belong to the ligature
                    if (ligatureId == 0 || ligatureId != thisLigatureId || thisComponent >= componentCount)
                    {
                        thisComponent = componentCount - 1;
                    }

                    if (lastComponent != thisComponent)
                    {
                        lastComponent = thisComponent;
                        lastCombiningClass = 255;
                        componentExtents = baseExtents;
                        var leftToRight = plan.Direction != ShapingDirection.RightToLeft;
                        componentExtents.XBearing += (leftToRight ? thisComponent : componentCount - 1 - thisComponent) * componentExtents.Width / componentCount;
                        componentExtents.Width /= componentCount;
                    }
                }

                var thisCombiningClass = info[i].ModifiedCombiningClass;
                if (lastCombiningClass != thisCombiningClass)
                {
                    lastCombiningClass = thisCombiningClass;
                    clusterExtents = componentExtents;
                }

                PositionMark(buffer, font, ref clusterExtents, i, thisCombiningClass);
                positions[i].XAdvance = 0;
                positions[i].YAdvance = 0;
                positions[i].XOffset += xOffset;
                positions[i].YOffset += yOffset;
            }
            else if (!buffer.IsBackward)
            {
                xOffset -= positions[i].XAdvance;
                yOffset -= positions[i].YAdvance;
            }
            else
            {
                xOffset += positions[i].XAdvance;
                yOffset += positions[i].YAdvance;
            }
        }
    }

    private static void PositionCluster(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer, int start, int end, bool adjustOffsets)
    {
        if (end - start < 2)
            return;

        // The base glyph, then its marks
        var info = buffer.Info;
        for (var i = start; i < end; i++)
        {
            if (info[i].IsUnicodeMark)
                continue;

            int j;
            for (j = i + 1; j < end; j++)
            {
                if (info[j].IsHidden || info[j].IsDefaultIgnorable)
                    continue;

                if (!info[j].IsUnicodeMark)
                    break;
            }

            PositionAroundBase(plan, font, buffer, i, j, adjustOffsets);
            i = j - 1;
        }
    }

    /// <summary>Positions the marks around their base (HarfBuzz's <c>_hb_ot_shape_fallback_mark_position</c>).</summary>
    public static void PositionMarks(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer, bool adjustOffsets)
    {
        var start = 0;
        var info = buffer.Info;
        for (var i = 1; i < buffer.Length; i++)
        {
            if (!info[i].IsUnicodeMark && !info[i].IsHidden && !info[i].IsDefaultIgnorable)
            {
                PositionCluster(plan, font, buffer, start, i, adjustOffsets);
                start = i;
            }
        }

        PositionCluster(plan, font, buffer, start, buffer.Length, adjustOffsets);
    }

    /// <summary>Sets the advances of the spaces that the font does not support (HarfBuzz's <c>_hb_ot_shape_fallback_spaces</c>).</summary>
    public static void PositionSpaces(in ShapingFont font, ShapingBuffer buffer)
    {
        var info = buffer.Info;
        var positions = buffer.Positions;
        var horizontal = buffer.IsHorizontal;
        double em = font.UnitsPerEm;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (!info[i].IsUnicodeSpace || info[i].IsLigated)
                continue;

            var type = info[i].SpaceFallback;
            double? advance = type switch
            {
                SpaceFallback.Em or SpaceFallback.Em2 or SpaceFallback.Em3 or SpaceFallback.Em4 or SpaceFallback.Em5 or SpaceFallback.Em6 or SpaceFallback.Em16 => em / (int)type,
                SpaceFallback.FourEm18 => em * 4 / 18,
                SpaceFallback.Figure => GetFirstAdvance(font, "0123456789", horizontal),
                SpaceFallback.Punctuation => GetFirstAdvance(font, ".,", horizontal),
                SpaceFallback.Narrow => (horizontal ? positions[i].XAdvance : -positions[i].YAdvance) / 2,
                _ => null,
            };

            if (advance is { } value)
            {
                if (horizontal)
                {
                    positions[i].XAdvance = value;
                }
                else
                {
                    positions[i].YAdvance = -value;
                }
            }
        }

        static double? GetFirstAdvance(in ShapingFont font, string characters, bool horizontal)
        {
            foreach (var character in characters)
            {
                if (font.TryGetNominalGlyph(character, out var glyph))
                    return horizontal ? font.GetHorizontalAdvance(glyph) : font.GetVerticalAdvance(glyph);
            }

            return null;
        }
    }

    /// <summary>
    /// Applies the kerning of the <c>kern</c> table (https://learn.microsoft.com/typography/opentype/spec/kern) to the pairs of
    /// glyphs that have the mask of the <c>kern</c> feature, in visual order, skipping the marks (HarfBuzz's <c>hb_kern_machine_t</c>):
    /// half of the kerning goes to the advance of the first glyph, the other half to the advance and the offset of the second one.
    /// </summary>
    public static void ApplyKernTable(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        var mask = plan.KernMask;
        if (mask == 0 || !buffer.IsHorizontal)
            return;

        var reverse = buffer.IsBackward;
        if (reverse)
        {
            buffer.Reverse();
        }

        var info = buffer.Info;
        var positions = buffer.Positions;
        for (var i = 0; i < buffer.Length; i++)
        {
            if ((info[i].Mask & mask) == 0)
                continue;

            // The next glyph that is not a mark nor a default ignorable character
            var j = i + 1;
            while (j < buffer.Length && (info[j].IsMark || info[j].IsDefaultIgnorable))
            {
                j++;
            }

            if (j >= buffer.Length || (info[j].Mask & mask) == 0)
                continue;

            double kerning = font.Font.GetKernTableValue((ushort)info[i].Codepoint, (ushort)info[j].Codepoint);
            if (kerning != 0)
            {
                var first = Math.Floor(kerning / 2);
                var second = kerning - first;
                positions[i].XAdvance += first;
                positions[j].XAdvance += second;
                positions[j].XOffset += second;
                buffer.UnsafeToBreak(i, j + 1);
            }

            i = j - 1;
        }

        if (reverse)
        {
            buffer.Reverse();
        }
    }
}
