using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The OpenType shaping of a run of one font, one script and one direction (HarfBuzz's <c>hb_ot_shape_internal</c>,
/// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shape.cc, MIT license): the Unicode properties and the clusters of
/// the characters, the normalization against the font, the masks of the features (the shaper of the script sets its own), the
/// GSUB lookups, the default advances, the GPOS lookups or the fallback positioning (the <c>kern</c> table, the marks), and
/// the hiding of the default ignorable characters.
/// </summary>
/// <remarks>
/// The glyphs of the result are in logical order for the direction of the run (<see cref="ShapingBuffer.Direction"/> when the
/// buffer was created): the glyphs of a right-to-left run are drawn from its right edge. The positions are in font units.
/// The bidi mirroring is done by the caller (the inline layout mirrors the characters of the right-to-left runs): the
/// <c>rtlm</c> feature applies to the characters that have no mirrored character.
/// </remarks>
internal static class OpenTypeShapingEngine
{
    /// <summary>Shapes the characters of a buffer with a plan and a font.</summary>
    public static void Shape(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        var targetDirection = buffer.Direction;
        SetUnicodeProperties(buffer, plan.GlobalMask);
        FormClusters(buffer);
        EnsureNativeDirection(buffer);
        plan.Shaper.PreprocessText(plan, buffer, font);

        // Substitute
        RotateCharacters(plan, buffer, font, targetDirection);
        ShapingNormalizer.Normalize(plan, buffer, font);
        SetupMasks(plan, buffer, font);
        if (plan.FallbackMarkPositioning)
        {
            FallbackPositioning.RecategorizeMarks(buffer);
        }

        MapGlyphs(plan, buffer);
        plan.Substitute(font, buffer);

        // Position
        Position(plan, buffer, font);

        // The output is in the logical order of the target direction: the buffer is reversed when the characters were
        // shaped in the other direction (e.g. digits in a right-to-left run of a left-to-right script)
        if (buffer.Direction != targetDirection)
        {
            buffer.Reverse();
            buffer.Direction = targetDirection;
        }

        plan.Shaper.PostprocessGlyphs(plan, buffer, font);
        PropagateFlags(buffer);
    }

    /// <summary>
    /// Sets the Unicode properties of a character (HarfBuzz's <c>_hb_glyph_info_set_unicode_props</c>): its general category,
    /// whether it is a default ignorable character (the joiners, the Mongolian variation selectors, the tags, the combining
    /// grapheme joiner and the line feeds), and the modified combining class of a mark.
    /// </summary>
    public static void SetUnicodeProperties(ref GlyphInfo info, ShapingBuffer buffer)
    {
        var u = info.Codepoint;
        var category = UnicodeProperties.GetGeneralCategory(u);
        var properties = (UnicodeGlyphProperties)category;
        if (u == '\n')
        {
            // A segment break is not rendered (as a default ignorable character)
            buffer.ScratchFlags |= ShapingScratchFlags.HasDefaultIgnorables;
            properties |= UnicodeGlyphProperties.Ignorable;
        }
        else if (u >= 0x80)
        {
            if (ShapingUnicode.IsDefaultIgnorable(u))
            {
                buffer.ScratchFlags |= ShapingScratchFlags.HasDefaultIgnorables;
                properties |= UnicodeGlyphProperties.Ignorable;
                if (u == 0x200C)
                {
                    properties |= UnicodeGlyphProperties.FormatZwnj;
                }
                else if (u == 0x200D)
                {
                    properties |= UnicodeGlyphProperties.FormatZwj;
                }
                else if (u is (>= 0x180B and <= 0x180D) or 0x180F or (>= 0xE0020 and <= 0xE007F))
                {
                    // The Mongolian free variation selectors and the tags are hidden, but not skipped by GSUB
                    properties |= UnicodeGlyphProperties.Hidden;
                }
                else if (u == 0x034F)
                {
                    // The combining grapheme joiner is not skipped by GSUB either
                    buffer.ScratchFlags |= ShapingScratchFlags.HasCombiningGraphemeJoiner;
                    properties |= UnicodeGlyphProperties.Hidden;
                }
            }

            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                buffer.ScratchFlags |= ShapingScratchFlags.HasContinuations;
                properties |= UnicodeGlyphProperties.Continuation | (UnicodeGlyphProperties)(ShapingUnicode.GetModifiedCombiningClass(u) << 8);
            }
        }

        info.UnicodeProperties = properties;
    }

    /// <summary>
    /// Sets the Unicode properties and the global mask of the characters, and marks the continuations of the grapheme
    /// clusters (HarfBuzz's <c>hb_set_unicode_props</c>): the marks, the emoji modifiers, the second regional indicator of a
    /// pair, the ZWJ and the pictograph after it, the halfwidth katakana voiced sound marks and the tags.
    /// </summary>
    private static void SetUnicodeProperties(ShapingBuffer buffer, uint globalMask)
    {
        var info = buffer.Info;
        var count = buffer.Length;
        for (var i = 0; i < count; i++)
        {
            info[i].Mask = globalMask;
            SetUnicodeProperties(ref info[i], buffer);
            var u = info[i].Codepoint;
            if (u < 0x80)
                continue;

            var category = info[i].GeneralCategory;
            if (category is UnicodeCategory.LowercaseLetter or UnicodeCategory.UppercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter or UnicodeCategory.SpaceSeparator)
                continue;

            if (category == UnicodeCategory.ModifierSymbol && u is >= 0x1F3FB and <= 0x1F3FF)
            {
                SetContinuation(ref info[i], buffer);
            }
            else if (i > 0 && IsRegionalIndicator(u))
            {
                if (IsRegionalIndicator(info[i - 1].Codepoint) && !info[i - 1].IsContinuation)
                {
                    SetContinuation(ref info[i], buffer);
                }
            }
            else if (info[i].IsZwj)
            {
                // A ZWJ after a space starts the cluster of the word after it, as in Chrome, which shapes the words between the
                // spaces separately: the soft wrap opportunity after the space is between the two clusters (CSS Text: a joiner
                // after a space starts a typographic character unit of its own, UAX #14 LB10; WPT
                // css/css-text/white-space/white-space-vs-joiners-002.html)
                if (i == 0 || info[i - 1].Codepoint != ' ')
                {
                    SetContinuation(ref info[i], buffer);
                }

                if (i + 1 < count && UnicodeEmoji.IsExtendedPictographic(info[i + 1].Codepoint))
                {
                    i++;
                    info[i].Mask = globalMask;
                    SetUnicodeProperties(ref info[i], buffer);
                    SetContinuation(ref info[i], buffer);
                }
            }
            else if (u is (>= 0xFF9E and <= 0xFF9F) or (>= 0xE0020 and <= 0xE007F))
            {
                SetContinuation(ref info[i], buffer);
            }
            else if (u == 0x2044)
            {
                buffer.ScratchFlags |= ShapingScratchFlags.HasFractionSlash;
            }
        }

        static bool IsRegionalIndicator(int u) => u is >= 0x1F1E6 and <= 0x1F1FF;

        static void SetContinuation(ref GlyphInfo info, ShapingBuffer buffer)
        {
            buffer.ScratchFlags |= ShapingScratchFlags.HasContinuations;
            info.UnicodeProperties |= UnicodeGlyphProperties.Continuation;
        }
    }

    /// <summary>Merges the clusters of the grapheme clusters (HarfBuzz's <c>hb_form_clusters</c>).</summary>
    private static void FormClusters(ShapingBuffer buffer)
    {
        if ((buffer.ScratchFlags & ShapingScratchFlags.HasContinuations) == ShapingScratchFlags.None)
            return;

        var info = buffer.Info;
        var start = 0;
        for (var i = 1; i <= buffer.Length; i++)
        {
            if (i == buffer.Length || !info[i].IsContinuation)
            {
                buffer.MergeClusters(start, i);
                start = i;
            }
        }
    }

    /// <summary>
    /// Shapes the run in the native direction of its script (HarfBuzz's <c>hb_ensure_native_direction</c>): a run of a
    /// right-to-left script set left-to-right (e.g. with <c>unicode-bidi: bidi-override</c>) is reversed by grapheme and shaped
    /// right-to-left, unless it only has digits; and conversely.
    /// </summary>
    private static void EnsureNativeDirection(ShapingBuffer buffer)
    {
        var direction = buffer.Direction;
        if (direction == ShapingDirection.TopToBottom)
            return;

        var native = IsRightToLeftScript(buffer.Script) ? ShapingDirection.RightToLeft : IsLeftToRightScript(buffer.Script) ? ShapingDirection.LeftToRight : direction;

        // The numbers of a right-to-left script (and the regional indicators) are left-to-right
        if (native == ShapingDirection.RightToLeft && direction == ShapingDirection.LeftToRight)
        {
            bool foundNumber = false, foundLetter = false;
            var info = buffer.Info;
            for (var i = 0; i < buffer.Length; i++)
            {
                var category = info[i].GeneralCategory;
                if (category <= UnicodeCategory.OtherLetter)
                {
                    foundLetter = true;
                    break;
                }

                if (category == UnicodeCategory.DecimalDigitNumber || info[i].Codepoint is >= 0x1F1E6 and <= 0x1F1FF)
                {
                    foundNumber = true;
                }
            }

            if (foundNumber && !foundLetter)
            {
                native = ShapingDirection.LeftToRight;
            }
        }

        if (direction != native)
        {
            // Reverse the grapheme clusters (each one keeps its order)
            var info = buffer.Info;
            var start = 0;
            for (var i = 1; i <= buffer.Length; i++)
            {
                if (i == buffer.Length || !info[i].IsContinuation)
                {
                    buffer.Reverse(start, i);
                    start = i;
                }
            }

            buffer.Reverse();
            buffer.Direction = native;
        }
    }

    /// <summary>Whether the horizontal direction of a script is right-to-left (HarfBuzz's <c>hb_script_get_horizontal_direction</c>).</summary>
    internal static bool IsRightToLeftScript(UnicodeScript script) => script is UnicodeScript.Arabic or UnicodeScript.Hebrew or UnicodeScript.Syriac or UnicodeScript.Thaana
        or UnicodeScript.Cypriot or UnicodeScript.Kharoshthi or UnicodeScript.Phoenician or UnicodeScript.Nko or UnicodeScript.Lydian or UnicodeScript.Avestan
        or UnicodeScript.ImperialAramaic or UnicodeScript.InscriptionalPahlavi or UnicodeScript.InscriptionalParthian or UnicodeScript.OldSouthArabian
        or UnicodeScript.OldTurkic or UnicodeScript.Samaritan or UnicodeScript.Mandaic or UnicodeScript.MeroiticCursive or UnicodeScript.MeroiticHieroglyphs
        or UnicodeScript.Manichaean or UnicodeScript.MendeKikakui or UnicodeScript.Nabataean or UnicodeScript.OldNorthArabian or UnicodeScript.Palmyrene
        or UnicodeScript.PsalterPahlavi or UnicodeScript.Hatran or UnicodeScript.Adlam or UnicodeScript.HanifiRohingya or UnicodeScript.OldSogdian
        or UnicodeScript.Sogdian or UnicodeScript.Elymaic or UnicodeScript.Chorasmian or UnicodeScript.Yezidi or UnicodeScript.OldUyghur
        or UnicodeScript.Garay or UnicodeScript.Sidetic;

    /// <summary>
    /// Whether a script is left-to-right: the scripts Common, Inherited and Unknown, and the scripts written in both
    /// directions (Old Hungarian, Old Italic, Runic, Tifinagh) take the direction of the run.
    /// </summary>
    private static bool IsLeftToRightScript(UnicodeScript script) => script is not (UnicodeScript.Unknown or UnicodeScript.Common or UnicodeScript.Inherited
        or UnicodeScript.OldHungarian or UnicodeScript.OldItalic or UnicodeScript.Runic or UnicodeScript.Tifinagh) && !IsRightToLeftScript(script);

    /// <summary>
    /// The mirroring and the vertical forms (HarfBuzz's <c>hb_ot_rotate_chars</c>): in a right-to-left run, the characters that
    /// were not mirrored by the caller get the mask of <c>rtlm</c>; in vertical text, the punctuation is replaced by its
    /// vertical presentation form when the font has no <c>vert</c> feature.
    /// </summary>
    private static void RotateCharacters(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font, ShapingDirection targetDirection)
    {
        var info = buffer.Info;
        if (targetDirection == ShapingDirection.RightToLeft && plan.RtlmMask != 0)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var u = info[i].Codepoint;
                if (!(UnicodeProperties.IsBidiMirrored(u) && CharacterProperties.GetBidiMirroringGlyph(u) >= 0))
                {
                    info[i].Mask |= plan.RtlmMask;
                }
            }
        }

        if (targetDirection == ShapingDirection.TopToBottom && !plan.HasVert)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var vertical = ShapingUnicode.GetVerticalCharacter(info[i].Codepoint);
                if (vertical != info[i].Codepoint && font.HasGlyph(vertical))
                {
                    info[i].Codepoint = vertical;
                }
            }
        }
    }

    /// <summary>Sets the masks of the automatic fractions and of the shaper (HarfBuzz's <c>hb_ot_shape_setup_masks</c>).</summary>
    private static void SetupMasks(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        // https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shape.cc: the digits around a fraction slash get the
        // masks of numr, frac and dnom
        if ((buffer.ScratchFlags & ShapingScratchFlags.HasFractionSlash) != ShapingScratchFlags.None && plan.HasFrac)
        {
            uint preMask, postMask;
            if (!buffer.IsBackward)
            {
                preMask = plan.NumrMask | plan.FracMask;
                postMask = plan.FracMask | plan.DnomMask;
            }
            else
            {
                preMask = plan.FracMask | plan.DnomMask;
                postMask = plan.NumrMask | plan.FracMask;
            }

            var info = buffer.Info;
            var count = buffer.Length;
            for (var i = 0; i < count; i++)
            {
                if (info[i].Codepoint != 0x2044)
                    continue;

                int start = i, end = i + 1;
                while (start > 0 && info[start - 1].GeneralCategory == UnicodeCategory.DecimalDigitNumber)
                {
                    start--;
                }

                while (end < count && info[end].GeneralCategory == UnicodeCategory.DecimalDigitNumber)
                {
                    end++;
                }

                if (start == i || end == i + 1)
                    continue;

                buffer.UnsafeToBreak(start, end);
                for (var j = start; j < i; j++)
                {
                    info[j].Mask |= preMask;
                }

                info[i].Mask |= plan.FracMask;
                for (var j = i + 1; j < end; j++)
                {
                    info[j].Mask |= postMask;
                }

                i = end - 1;
            }
        }

        plan.Shaper.SetupMasks(plan, buffer, font);
    }

    /// <summary>
    /// Replaces the characters by their glyphs (set by the normalization) and sets the glyph properties of GDEF, or synthesizes
    /// them from the Unicode properties when the font has no glyph classes (a non-spacing mark is a mark).
    /// </summary>
    private static void MapGlyphs(ShapePlan plan, ShapingBuffer buffer)
    {
        var info = buffer.Info;
        var gdef = plan.Gdef;
        var hasClasses = !plan.FallbackGlyphClasses;
        buffer.Digest = default;
        for (var i = 0; i < buffer.Length; i++)
        {
            ref var glyph = ref info[i];
            glyph.Codepoint = glyph.GlyphIndex;
            glyph.LigatureProperties = 0;
            if (hasClasses)
            {
                glyph.GlyphProperties = gdef!.GetGlyphProperties(glyph.Codepoint);
            }
            else
            {
                // The default ignorable characters are never marks (e.g. the Mongolian variation selectors)
                glyph.GlyphProperties = glyph.GeneralCategory != UnicodeCategory.NonSpacingMark || glyph.IsDefaultIgnorable ? GlyphProperties.BaseGlyph : GlyphProperties.Mark;
            }

            buffer.Digest.Add(glyph.Codepoint);
        }
    }

    /// <summary>The positioning (HarfBuzz's <c>hb_ot_position</c>).</summary>
    private static void Position(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        buffer.ClearPositions();
        var info = buffer.Info;
        var positions = buffer.Positions;
        var horizontal = buffer.IsHorizontal;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (horizontal)
            {
                positions[i].XAdvance = font.GetHorizontalAdvance(info[i].Codepoint);
            }
            else
            {
                positions[i].YAdvance = -font.GetVerticalAdvance(info[i].Codepoint);
            }
        }

        if ((buffer.ScratchFlags & ShapingScratchFlags.HasSpaceFallback) != ShapingScratchFlags.None)
        {
            FallbackPositioning.PositionSpaces(font, buffer);
        }

        // Without GPOS, a zeroed mark is moved over the previous glyph (in the forward direction)
        var adjustOffsetsWhenZeroing = plan.AdjustMarkPositioningWhenZeroing && !buffer.IsBackward;
        if (plan.ZeroMarks && plan.Shaper.ZeroWidthMarks == ZeroWidthMarks.ByGdefEarly)
        {
            ZeroMarkWidthsByGdef(buffer, adjustOffsetsWhenZeroing);
        }

        if (plan.ApplyGpos)
        {
            plan.Position(font, buffer);
        }

        if (plan.ApplyKern)
        {
            FallbackPositioning.ApplyKernTable(plan, font, buffer);
        }

        if (plan.ZeroMarks && plan.Shaper.ZeroWidthMarks == ZeroWidthMarks.ByGdefLate)
        {
            ZeroMarkWidthsByGdef(buffer, adjustOffsetsWhenZeroing);
        }

        // The default ignorable characters have no advance
        if ((buffer.ScratchFlags & ShapingScratchFlags.HasDefaultIgnorables) != ShapingScratchFlags.None)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (info[i].IsDefaultIgnorable)
                {
                    positions[i].XAdvance = 0;
                    positions[i].YAdvance = 0;
                    if (horizontal)
                    {
                        positions[i].XOffset = 0;
                    }
                    else
                    {
                        positions[i].YOffset = 0;
                    }
                }
            }
        }

        OpenTypeApplyContext.FinishOffsets(buffer);
        if (plan.FallbackMarkPositioning)
        {
            FallbackPositioning.PositionMarks(plan, font, buffer, adjustOffsetsWhenZeroing);
        }
    }

    private static void ZeroMarkWidthsByGdef(ShapingBuffer buffer, bool adjustOffsets)
    {
        var info = buffer.Info;
        var positions = buffer.Positions;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (info[i].IsMark)
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

    /// <summary>Gives the glyph flags of a cluster to all its glyphs (HarfBuzz's <c>hb_propagate_flags</c>).</summary>
    private static void PropagateFlags(ShapingBuffer buffer)
    {
        var info = buffer.Info;
        var start = 0;
        while (start < buffer.Length)
        {
            var end = start + 1;
            while (end < buffer.Length && info[end].Cluster == info[start].Cluster)
            {
                end++;
            }

            if (end - start > 1)
            {
                var flags = 0u;
                for (var i = start; i < end; i++)
                {
                    flags |= info[i].Mask & GlyphFlags.Defined;
                }

                for (var i = start; i < end; i++)
                {
                    info[i].Mask = (info[i].Mask & ~GlyphFlags.Defined) | flags;
                }
            }

            start = end;
        }
    }
}
