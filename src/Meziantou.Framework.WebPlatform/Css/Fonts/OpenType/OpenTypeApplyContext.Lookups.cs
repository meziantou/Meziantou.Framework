namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The lookup types of GSUB (https://learn.microsoft.com/typography/opentype/spec/gsub) and GPOS
/// (https://learn.microsoft.com/typography/opentype/spec/gpos), ported from HarfBuzz (src/OT/Layout/GSUB, src/OT/Layout/GPOS
/// and hb-ot-layout-gsubgpos.hh, MIT license).
/// </summary>
internal sealed partial class OpenTypeApplyContext
{
    /// <summary>
    /// Applies a lookup to the glyphs of the buffer (HarfBuzz's <c>apply_string</c>): forward, writing to the output for GSUB
    /// and in place for GPOS, or backward in place for the reverse chained substitutions.
    /// </summary>
    public bool ApplyLookup(OpenTypeLookup lookup)
    {
        var buffer = Buffer;
        if (buffer.Length == 0 || LookupMask == 0)
            return false;

        SetLookupProperties(lookup.Properties);
        var result = false;
        if (!IsReverse(lookup))
        {
            if (!IsPositioning)
            {
                buffer.ClearOutput();
            }

            buffer.Index = 0;
            var digest = lookup.Digest;
            while (buffer.Successful)
            {
                // Skip quickly the glyphs the lookup does not apply to
                var info = buffer.Info;
                var j = buffer.Index;
                while (j < buffer.Length && !(digest.MayHave(info[j].Codepoint) && (info[j].Mask & LookupMask) != 0 && CheckGlyphProperty(info[j], LookupProperties)))
                {
                    j++;
                }

                if (j > buffer.Index && !buffer.NextGlyphs(j - buffer.Index))
                    break;

                if (buffer.Index >= buffer.Length)
                    break;

                if (ApplyLookupToCurrentGlyph(lookup))
                {
                    result = true;
                }
                else
                {
                    buffer.NextGlyph();
                }
            }

            if (!IsPositioning)
            {
                buffer.Sync();
            }
        }
        else
        {
            buffer.Index = buffer.Length - 1;
            while (buffer.Index >= 0)
            {
                ref var current = ref buffer.Current;
                if (lookup.Digest.MayHave(current.Codepoint) && (current.Mask & LookupMask) != 0 && CheckGlyphProperty(current, LookupProperties))
                {
                    result |= ApplyLookupToCurrentGlyph(lookup);
                }

                // The reverse lookups do not advance the position
                buffer.Index--;
            }
        }

        return result;
    }

    private bool IsReverse(OpenTypeLookup lookup) => !IsPositioning && lookup.Type == OpenTypeLookup.ReverseChainedSingleSubstitution;

    /// <summary>Applies the first subtable of a lookup that applies to the current glyph.</summary>
    private bool ApplyLookupToCurrentGlyph(OpenTypeLookup lookup)
    {
        foreach (var subtable in lookup.SubtableOffsets)
        {
            var applied = IsPositioning ? ApplyPositioning(lookup.Type, subtable) : ApplySubstitution(lookup.Type, subtable);
            if (applied)
                return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // GSUB
    // ----------------------------------------------------------------------------------------------------------------

    private bool ApplySubstitution(int type, int subtable)
    {
        var table = Table.Data;
        var format = FontBinary.U16(table, subtable);
        switch (type)
        {
            case OpenTypeLookup.SingleSubstitution:
                return ApplySingleSubstitution(table, subtable, format);
            case OpenTypeLookup.MultipleSubstitution when format == 1:
                return ApplyMultipleSubstitution(table, subtable);
            case OpenTypeLookup.AlternateSubstitution when format == 1:
                return ApplyAlternateSubstitution(table, subtable);
            case OpenTypeLookup.LigatureSubstitution when format == 1:
                return ApplyLigatureSubstitution(table, subtable);
            case OpenTypeLookup.ContextSubstitution:
                return ApplyContext(table, subtable, format);
            case OpenTypeLookup.ChainedContextSubstitution:
                return ApplyChainedContext(table, subtable, format);
            case OpenTypeLookup.ReverseChainedSingleSubstitution when format == 1:
                return ApplyReverseChainedSingleSubstitution(table, subtable);
            default:
                return false;
        }
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gsub#lookuptype-1-single-substitution-subtable</summary>
    private bool ApplySingleSubstitution(ReadOnlySpan<byte> table, int subtable, int format)
    {
        var glyph = Buffer.Current.Codepoint;
        var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), glyph);
        if (index < 0)
            return false;

        int substitute;
        if (format == 1)
        {
            // A delta added to the glyph (modulo 65536)
            substitute = (ushort)(glyph + FontBinary.I16(table, subtable + 4));
        }
        else if (format == 2)
        {
            if (index >= FontBinary.U16(table, subtable + 4))
                return false;

            substitute = FontBinary.U16(table, subtable + 6 + (index * 2));
        }
        else
        {
            return false;
        }

        ReplaceGlyph(substitute);
        return true;
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gsub#lookuptype-2-multiple-substitution-subtable</summary>
    private bool ApplyMultipleSubstitution(ReadOnlySpan<byte> table, int subtable)
    {
        var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), Buffer.Current.Codepoint);
        if (index < 0 || index >= FontBinary.U16(table, subtable + 4))
            return false;

        var sequence = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6 + (index * 2));
        if (sequence == 0)
            return false;

        var count = FontBinary.U16(table, sequence);

        // In place, not considered as a multiplied substitution
        if (count == 1)
        {
            ReplaceGlyph(FontBinary.U16(table, sequence + 2));
            return true;
        }

        // The specification disallows this, but Uniscribe allows it
        if (count == 0)
        {
            Buffer.DeleteGlyph();
            return true;
        }

        var classGuess = Buffer.Current.IsLigature ? GlyphProperties.BaseGlyph : GlyphProperties.None;
        var ligatureId = Buffer.Current.LigatureId;
        for (var i = 0; i < count; i++)
        {
            // A glyph attached to a ligature keeps its ligature properties
            if (ligatureId == 0)
            {
                Buffer.Current.SetLigaturePropertiesForComponent(i);
            }

            OutputGlyphForComponent(FontBinary.U16(table, sequence + 2 + (i * 2)), classGuess);
        }

        Buffer.SkipGlyph();
        return true;
    }

    /// <summary>
    /// https://learn.microsoft.com/typography/opentype/spec/gsub#lookuptype-3-alternate-substitution-subtable: the alternate
    /// is the value of the feature (1 is the first one); the <c>rand</c> feature picks one randomly.
    /// </summary>
    private bool ApplyAlternateSubstitution(ReadOnlySpan<byte> table, int subtable)
    {
        var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), Buffer.Current.Codepoint);
        if (index < 0 || index >= FontBinary.U16(table, subtable + 4))
            return false;

        var alternateSet = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6 + (index * 2));
        var count = FontBinary.U16(table, alternateSet);
        if (alternateSet == 0 || count == 0)
            return false;

        var shift = System.Numerics.BitOperations.TrailingZeroCount(LookupMask);
        var alternate = (int)((LookupMask & Buffer.Current.Mask) >> shift);
        if (alternate == ShapePlan.MaxFeatureValue && Random)
        {
            Buffer.UnsafeToBreak(0, Buffer.Length);
            alternate = (int)(NextRandom() % count) + 1;
        }

        if (alternate > count || alternate == 0)
            return false;

        ReplaceGlyph(FontBinary.U16(table, alternateSet + 2 + ((alternate - 1) * 2)));
        return true;
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gsub#lookuptype-4-ligature-substitution-subtable</summary>
    private bool ApplyLigatureSubstitution(ReadOnlySpan<byte> table, int subtable)
    {
        var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), Buffer.Current.Codepoint);
        if (index < 0 || index >= FontBinary.U16(table, subtable + 4))
            return false;

        var ligatureSet = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6 + (index * 2));
        if (ligatureSet == 0)
            return false;

        var count = FontBinary.U16(table, ligatureSet);
        for (var i = 0; i < count; i++)
        {
            var ligature = OpenTypeLayoutTable.Offset16(table, ligatureSet, ligatureSet + 2 + (i * 2));
            if (ligature != 0 && ApplyLigature(table, ligature))
                return true;
        }

        return false;
    }

    private bool ApplyLigature(ReadOnlySpan<byte> table, int ligature)
    {
        var ligatureGlyph = FontBinary.U16(table, ligature);
        var componentCount = FontBinary.U16(table, ligature + 2);
        if (componentCount == 0)
            return false;

        // In place, not considered as a ligature
        if (componentCount == 1)
        {
            ReplaceGlyph(ligatureGlyph);
            return true;
        }

        if (componentCount > MaxContextLength)
            return false;

        if (!MatchInput(componentCount, ligature + 4, GlyphMatchKind.Glyph, 0, out var matchEnd, out var totalComponentCount))
        {
            return false;
        }

        LigateInput(componentCount, matchEnd, ligatureGlyph, totalComponentCount);
        return true;
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gsub#lookuptype-8-reverse-chaining-contextual-single-substitution-subtable</summary>
    private bool ApplyReverseChainedSingleSubstitution(ReadOnlySpan<byte> table, int subtable)
    {
        var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), Buffer.Current.Codepoint);
        if (index < 0)
            return false;

        // No chaining to this type
        if (NestingLevelLeft != MaxNestingLevel)
            return false;

        var backtrackCount = FontBinary.U16(table, subtable + 4);
        var lookahead = subtable + 6 + (backtrackCount * 2);
        var lookaheadCount = FontBinary.U16(table, lookahead);
        var substitute = lookahead + 2 + (lookaheadCount * 2);
        if (index >= FontBinary.U16(table, substitute))
            return false;

        if (MatchBacktrack(backtrackCount, subtable + 6, GlyphMatchKind.Coverage, subtable, out var startIndex)
            && MatchLookahead(lookaheadCount, lookahead + 2, GlyphMatchKind.Coverage, subtable, Buffer.Index + 1, out var endIndex))
        {
            Buffer.UnsafeToBreakFromOutput(startIndex, endIndex);
            ReplaceGlyphInPlace(FontBinary.U16(table, substitute + 2 + (index * 2)));

            // The position is not decreased: the main loop does it
            return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Contextual lookups (https://learn.microsoft.com/typography/opentype/spec/chapter2#sequence-context-format-1-simple-glyph-contexts)
    // ----------------------------------------------------------------------------------------------------------------

    private bool ApplyContext(ReadOnlySpan<byte> table, int subtable, int format)
    {
        var glyph = Buffer.Current.Codepoint;
        switch (format)
        {
            case 1:
            {
                // Rule sets by the coverage index of the first glyph, the rules match glyphs
                var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), glyph);
                if (index < 0 || index >= FontBinary.U16(table, subtable + 4))
                    return false;

                return ApplyRuleSet(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6 + (index * 2)), GlyphMatchKind.Glyph, 0);
            }

            case 2:
            {
                // Rule sets by the class of the first glyph, the rules match classes
                if (Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), glyph) < 0)
                    return false;

                var classDefinition = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 4);
                var glyphClass = Table.GetCachedClass(table, classDefinition, glyph);
                if (glyphClass >= FontBinary.U16(table, subtable + 6))
                    return false;

                return ApplyRuleSet(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 8 + (glyphClass * 2)), GlyphMatchKind.Class, classDefinition);
            }

            case 3:
            {
                // A single rule of coverages
                var glyphCount = FontBinary.U16(table, subtable + 2);
                var lookupCount = FontBinary.U16(table, subtable + 4);
                if (glyphCount == 0 || Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6), glyph) < 0)
                    return false;

                return ApplyContextRule(glyphCount, subtable + 8, lookupCount, subtable + 6 + (glyphCount * 2), GlyphMatchKind.Coverage, subtable);
            }

            default:
                return false;
        }
    }

    private bool ApplyRuleSet(ReadOnlySpan<byte> table, int ruleSet, GlyphMatchKind kind, int matchData)
    {
        if (ruleSet == 0)
            return false;

        var count = FontBinary.U16(table, ruleSet);
        for (var i = 0; i < count; i++)
        {
            var rule = OpenTypeLayoutTable.Offset16(table, ruleSet, ruleSet + 2 + (i * 2));
            if (rule == 0)
                continue;

            var glyphCount = FontBinary.U16(table, rule);
            var lookupCount = FontBinary.U16(table, rule + 2);
            if (glyphCount == 0)
                continue;

            if (ApplyContextRule(glyphCount, rule + 4, lookupCount, rule + 4 + ((glyphCount - 1) * 2), kind, matchData))
                return true;
        }

        return false;
    }

    /// <summary>Applies a rule of a context lookup (HarfBuzz's <c>context_apply_lookup</c>).</summary>
    private bool ApplyContextRule(int inputCount, int input, int lookupCount, int lookupRecords, GlyphMatchKind kind, int matchData)
    {
        if (inputCount > MaxContextLength)
            return false;

        if (MatchInput(inputCount, input, kind, matchData, out var matchEnd, out _))
        {
            Buffer.UnsafeToBreak(Buffer.Index, matchEnd);
            ApplyLookup(inputCount, lookupCount, lookupRecords, matchEnd);
            return true;
        }

        return false;
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/chapter2#chained-sequence-context-format-1-simple-glyph-contexts</summary>
    private bool ApplyChainedContext(ReadOnlySpan<byte> table, int subtable, int format)
    {
        var glyph = Buffer.Current.Codepoint;
        switch (format)
        {
            case 1:
            {
                var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), glyph);
                if (index < 0 || index >= FontBinary.U16(table, subtable + 4))
                    return false;

                return ApplyChainedRuleSet(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6 + (index * 2)), GlyphMatchKind.Glyph, 0, 0, 0);
            }

            case 2:
            {
                if (Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), glyph) < 0)
                    return false;

                var backtrackClasses = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 4);
                var inputClasses = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6);
                var lookaheadClasses = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 8);
                var glyphClass = Table.GetCachedClass(table, inputClasses, glyph);
                if (glyphClass >= FontBinary.U16(table, subtable + 10))
                    return false;

                return ApplyChainedRuleSet(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 12 + (glyphClass * 2)), GlyphMatchKind.Class, backtrackClasses, inputClasses, lookaheadClasses);
            }

            case 3:
            {
                var backtrackCount = FontBinary.U16(table, subtable + 2);
                var input = subtable + 4 + (backtrackCount * 2);
                var inputCount = FontBinary.U16(table, input);
                var lookahead = input + 2 + (inputCount * 2);
                var lookaheadCount = FontBinary.U16(table, lookahead);
                var lookups = lookahead + 2 + (lookaheadCount * 2);
                if (inputCount == 0 || Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, input + 2), glyph) < 0)
                    return false;

                return ApplyChainedRule(
                    backtrackCount, subtable + 4, GlyphMatchKind.Coverage, subtable,
                    inputCount, input + 4, GlyphMatchKind.Coverage, subtable,
                    lookaheadCount, lookahead + 2, GlyphMatchKind.Coverage, subtable,
                    FontBinary.U16(table, lookups), lookups + 2);
            }

            default:
                return false;
        }
    }

    private bool ApplyChainedRuleSet(ReadOnlySpan<byte> table, int ruleSet, GlyphMatchKind kind, int backtrackData, int inputData, int lookaheadData)
    {
        if (ruleSet == 0)
            return false;

        var count = FontBinary.U16(table, ruleSet);
        for (var i = 0; i < count; i++)
        {
            var rule = OpenTypeLayoutTable.Offset16(table, ruleSet, ruleSet + 2 + (i * 2));
            if (rule == 0)
                continue;

            var backtrackCount = FontBinary.U16(table, rule);
            var input = rule + 2 + (backtrackCount * 2);
            var inputCount = FontBinary.U16(table, input);
            if (inputCount == 0)
                continue;

            var lookahead = input + 2 + ((inputCount - 1) * 2);
            var lookaheadCount = FontBinary.U16(table, lookahead);
            var lookups = lookahead + 2 + (lookaheadCount * 2);
            if (ApplyChainedRule(
                backtrackCount, rule + 2, kind, backtrackData,
                inputCount, input + 2, kind, inputData,
                lookaheadCount, lookahead + 2, kind, lookaheadData,
                FontBinary.U16(table, lookups), lookups + 2))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Applies a rule of a chained context lookup (HarfBuzz's <c>chain_context_apply_lookup</c>).</summary>
    private bool ApplyChainedRule(
        int backtrackCount, int backtrack, GlyphMatchKind backtrackKind, int backtrackData,
        int inputCount, int input, GlyphMatchKind inputKind, int inputData,
        int lookaheadCount, int lookahead, GlyphMatchKind lookaheadKind, int lookaheadData,
        int lookupCount, int lookupRecords)
    {
        if (inputCount > MaxContextLength)
            return false;

        var buffer = Buffer;
        if (!MatchInput(inputCount, input, inputKind, inputData, out var matchEnd, out _)
            || !MatchLookahead(lookaheadCount, lookahead, lookaheadKind, lookaheadData, matchEnd, out var endIndex)
            || !MatchBacktrack(backtrackCount, backtrack, backtrackKind, backtrackData, out var startIndex))
        {
            return false;
        }

        buffer.UnsafeToBreakFromOutput(startIndex, endIndex);
        ApplyLookup(inputCount, lookupCount, lookupRecords, matchEnd);
        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // GPOS
    // ----------------------------------------------------------------------------------------------------------------

    private bool ApplyPositioning(int type, int subtable)
    {
        var table = Table.Data;
        var format = FontBinary.U16(table, subtable);
        switch (type)
        {
            case OpenTypeLookup.SingleAdjustment:
                return ApplySingleAdjustment(table, subtable, format);
            case OpenTypeLookup.PairAdjustment:
                return ApplyPairAdjustment(table, subtable, format);
            case OpenTypeLookup.CursiveAttachment when format == 1:
                return ApplyCursiveAttachment(table, subtable);
            case OpenTypeLookup.MarkToBaseAttachment when format == 1:
                return ApplyMarkToBase(table, subtable);
            case OpenTypeLookup.MarkToLigatureAttachment when format == 1:
                return ApplyMarkToLigature(table, subtable);
            case OpenTypeLookup.MarkToMarkAttachment when format == 1:
                return ApplyMarkToMark(table, subtable);
            case OpenTypeLookup.ContextPositioning:
                return ApplyContext(table, subtable, format);
            case OpenTypeLookup.ChainedContextPositioning:
                return ApplyChainedContext(table, subtable, format);
            default:
                return false;
        }
    }

    /// <summary>The size of a value record of a value format.</summary>
    private static int GetValueRecordSize(int valueFormat) => System.Numerics.BitOperations.PopCount((uint)(valueFormat & 0xFF)) * 2;

    /// <summary>
    /// Adds a value record to the position of a glyph (https://learn.microsoft.com/typography/opentype/spec/gpos#value-record):
    /// the placements, the advance of the direction, and the variation deltas of the device tables (relative to
    /// <paramref name="baseOffset"/>). Returns whether a value is not zero.
    /// </summary>
    private bool ApplyValue(ReadOnlySpan<byte> table, int baseOffset, int record, int valueFormat, ref GlyphPosition position)
    {
        if (valueFormat == 0)
            return false;

        var horizontal = Direction != ShapingDirection.TopToBottom;
        var applied = false;
        if ((valueFormat & 0x1) != 0)
        {
            var value = FontBinary.I16(table, record);
            position.XOffset += value;
            applied |= value != 0;
            record += 2;
        }

        if ((valueFormat & 0x2) != 0)
        {
            var value = FontBinary.I16(table, record);
            position.YOffset += value;
            applied |= value != 0;
            record += 2;
        }

        if ((valueFormat & 0x4) != 0)
        {
            var value = FontBinary.I16(table, record);
            if (horizontal)
            {
                position.XAdvance += value;
            }

            applied |= value != 0;
            record += 2;
        }

        // The vertical advances grow downward, the font coordinates upward
        if ((valueFormat & 0x8) != 0)
        {
            var value = FontBinary.I16(table, record);
            if (!horizontal)
            {
                position.YAdvance -= value;
            }

            applied |= value != 0;
            record += 2;
        }

        if ((valueFormat & 0xF0) == 0)
            return applied;

        var gdef = Gdef;
        for (var bit = 0x10; bit <= 0x80; bit <<= 1)
        {
            if ((valueFormat & bit) == 0)
                continue;

            var device = FontBinary.U16(table, record);
            record += 2;
            if (device == 0)
                continue;

            applied = true;
            if (gdef is null)
                continue;

            var delta = gdef.GetDeviceDelta(table, baseOffset, device, Instance);
            switch (bit)
            {
                case 0x10:
                    position.XOffset += delta;
                    break;
                case 0x20:
                    position.YOffset += delta;
                    break;
                case 0x40 when horizontal:
                    position.XAdvance += delta;
                    break;
                case 0x80 when !horizontal:
                    position.YAdvance -= delta;
                    break;
            }
        }

        return applied;
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gpos#lookup-type-1-single-adjustment-positioning-subtable</summary>
    private bool ApplySingleAdjustment(ReadOnlySpan<byte> table, int subtable, int format)
    {
        var buffer = Buffer;
        var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), buffer.Current.Codepoint);
        if (index < 0)
            return false;

        var valueFormat = FontBinary.U16(table, subtable + 4);
        if (format == 1)
        {
            ApplyValue(table, subtable, subtable + 6, valueFormat, ref buffer.CurrentPosition);
        }
        else if (format == 2)
        {
            if (index >= FontBinary.U16(table, subtable + 6))
                return false;

            ApplyValue(table, subtable, subtable + 8 + (index * GetValueRecordSize(valueFormat)), valueFormat, ref buffer.CurrentPosition);
        }
        else
        {
            return false;
        }

        buffer.Index++;
        return true;
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gpos#lookup-type-2-pair-adjustment-positioning-subtable</summary>
    private bool ApplyPairAdjustment(ReadOnlySpan<byte> table, int subtable, int format)
    {
        var buffer = Buffer;
        var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), buffer.Current.Codepoint);
        if (index < 0 || format is not (1 or 2))
            return false;

        ref var iterator = ref IterInput;
        ResetFast(ref iterator, buffer.Index);
        if (!Next(ref iterator, out _))
            return false;

        var second = iterator.Index;
        if (!FindPairRecord(table, subtable, format, index, buffer.Current.Codepoint, buffer.Info[second].Codepoint, out var baseOffset, out var record))
            return false;

        var valueFormat1 = FontBinary.U16(table, subtable + 4);
        var valueFormat2 = FontBinary.U16(table, subtable + 6);
        var size1 = GetValueRecordSize(valueFormat1);
        var size2 = GetValueRecordSize(valueFormat2);
        var appliedFirst = size1 != 0 && ApplyValue(table, baseOffset, record, valueFormat1, ref buffer.CurrentPosition);
        var appliedSecond = size2 != 0 && ApplyValue(table, baseOffset, record + size1, valueFormat2, ref buffer.Positions[second]);
        if (appliedFirst || appliedSecond)
        {
            buffer.UnsafeToBreak(buffer.Index, second + 1);
        }

        // When the second glyph has a value, the next pair starts after it
        if (size2 != 0)
        {
            second++;
            buffer.UnsafeToBreak(buffer.Index, second + 1);
        }

        buffer.Index = second;
        return true;
    }

    /// <summary>
    /// Finds the value records of a pair of glyphs in a pair adjustment subtable whose coverage contains the first glyph (at
    /// <paramref name="coverageIndex"/>): the pair value record of format 1 (relative to its pair set), or the record of the
    /// classes of format 2 (relative to the subtable). Returns false when the subtable has no record for the pair.
    /// </summary>
    private bool FindPairRecord(ReadOnlySpan<byte> table, int subtable, int format, int coverageIndex, int firstGlyph, int secondGlyph, out int baseOffset, out int record)
    {
        var size1 = GetValueRecordSize(FontBinary.U16(table, subtable + 4));
        var size2 = GetValueRecordSize(FontBinary.U16(table, subtable + 6));
        if (format == 1)
        {
            // The pair value records of the first glyph, sorted by second glyph; the device tables are relative to the pair set
            baseOffset = 0;
            record = -1;
            if (coverageIndex >= FontBinary.U16(table, subtable + 8))
                return false;

            var pairSet = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 10 + (coverageIndex * 2));
            if (pairSet == 0)
                return false;

            baseOffset = pairSet;
            var recordSize = 2 + size1 + size2;
            int low = 0, high = Math.Min(FontBinary.U16(table, pairSet), (table.Length - pairSet - 2) / recordSize) - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var candidate = pairSet + 2 + (middle * recordSize);
                var glyph = FontBinary.U16(table, candidate);
                if (glyph < secondGlyph)
                {
                    low = middle + 1;
                }
                else if (glyph > secondGlyph)
                {
                    high = middle - 1;
                }
                else
                {
                    record = candidate + 2;
                    return true;
                }
            }

            return false;
        }

        // The values of the classes of the two glyphs
        baseOffset = subtable;
        record = -1;
        var class1 = Table.GetCachedClass(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 8), firstGlyph);
        var class2 = Table.GetCachedClass(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 10), secondGlyph);
        var class1Count = FontBinary.U16(table, subtable + 12);
        var class2Count = FontBinary.U16(table, subtable + 14);
        if (class1 >= class1Count || class2 >= class2Count)
            return false;

        record = subtable + 16 + (((class1 * class2Count) + class2) * (size1 + size2));
        return true;
    }

    /// <summary>
    /// Whether the lookups of GPOS are only pair adjustments that do not change the second glyph (the kerning of most fonts)
    /// and that do not skip the base glyphs nor the ligatures: for a text without marks nor default ignorable characters,
    /// their result is the sum, for each pair of consecutive glyphs, of the values of the first glyph (<see cref="ApplyPairKerning"/>).
    /// </summary>
    internal static bool IsPairKerningOnly(OpenTypeLayoutTable table, ReadOnlySpan<ShapePlanLookup> lookups)
    {
        var data = table.Data;
        foreach (var entry in lookups)
        {
            if (entry.PerSyllable || table.GetLookup(entry.Index) is not { Type: OpenTypeLookup.PairAdjustment } lookup || (lookup.Flags & (LookupFlags.IgnoreBaseGlyphs | LookupFlags.IgnoreLigatures)) != LookupFlags.None)
                return false;

            foreach (var subtable in lookup.SubtableOffsets)
            {
                if (FontBinary.U16(data, subtable) is not (1 or 2) || FontBinary.U16(data, subtable + 6) != 0)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies the pair adjustment lookups of a plan whose GPOS lookups are only pair kerning (<see cref="IsPairKerningOnly"/>)
    /// to a buffer without marks nor default ignorable characters, whose glyphs all have the same lookup masks: the result is
    /// the one of <see cref="ApplyLookup(OpenTypeLookup)"/> for each lookup (each glyph is the first glyph of a pair with the next one), with
    /// the cache of the advances of the pairs of the plan.
    /// </summary>
    internal void ApplyPairKerning(ShapePlan plan, ReadOnlySpan<ShapePlanLookup> lookups, PairKerningCache? cache)
    {
        var buffer = Buffer;
        var info = buffer.Info;
        var positions = buffer.Positions;
        var horizontal = Direction != ShapingDirection.TopToBottom;
        for (var i = 0; i + 1 < buffer.Length; i++)
        {
            var first = info[i].Codepoint;
            var second = info[i + 1].Codepoint;
            if (cache is not null && cache.TryGet(first, second, out var advance))
            {
                if (double.IsNaN(advance))
                    continue;

                if (horizontal)
                {
                    positions[i].XAdvance += advance;
                }
                else
                {
                    positions[i].YAdvance += advance;
                }
            }
            else
            {
                var position = default(GlyphPosition);
                var applied = ComputePairKerning(plan, lookups, first, second, ref position);
                if (cache is not null && position.XOffset == 0 && position.YOffset == 0 && (horizontal ? position.YAdvance : position.XAdvance) == 0)
                {
                    cache.Set(first, second, !applied ? double.NaN : horizontal ? position.XAdvance : position.YAdvance);
                }

                if (!applied)
                    continue;

                positions[i].XAdvance += position.XAdvance;
                positions[i].YAdvance += position.YAdvance;
                positions[i].XOffset += position.XOffset;
                positions[i].YOffset += position.YOffset;
            }

            buffer.UnsafeToBreak(i, i + 2);
        }
    }

    /// <summary>
    /// The adjustment of the first glyph of a pair by the lookups that apply to the text (for each lookup, the first subtable
    /// that has a record for the pair): returns whether a value was applied.
    /// </summary>
    private bool ComputePairKerning(ShapePlan plan, ReadOnlySpan<ShapePlanLookup> lookups, int first, int second, ref GlyphPosition position)
    {
        var table = Table.Data;
        var applied = false;
        foreach (var entry in lookups)
        {
            // The glyphs have the masks of the global features
            if ((entry.Mask & plan.GlobalMask) == 0 || Table.GetLookup(entry.Index) is not { } lookup || !lookup.Digest.MayHave(first))
                continue;

            foreach (var subtable in lookup.SubtableOffsets)
            {
                var format = FontBinary.U16(table, subtable);
                var index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), first);
                if (index < 0 || !FindPairRecord(table, subtable, format, index, first, second, out var baseOffset, out var record))
                    continue;

                applied |= ApplyValue(table, baseOffset, record, FontBinary.U16(table, subtable + 4), ref position);
                break;
            }
        }

        return applied;
    }

    /// <summary>
    /// The coordinates of an anchor table in font units (https://learn.microsoft.com/typography/opentype/spec/gpos#anchor-tables):
    /// the coordinates of the formats 1 and 2 (the contour point of format 2 needs the hinted outline: it is not used, as in
    /// HarfBuzz without ppem), with the variation deltas of the device tables of format 3.
    /// </summary>
    private void GetAnchor(ReadOnlySpan<byte> table, int anchor, out double x, out double y)
    {
        x = FontBinary.I16(table, anchor + 2);
        y = FontBinary.I16(table, anchor + 4);
        if (FontBinary.U16(table, anchor) == 3 && Gdef is { } gdef)
        {
            x += gdef.GetDeviceDelta(table, anchor, FontBinary.U16(table, anchor + 6), Instance);
            y += gdef.GetDeviceDelta(table, anchor, FontBinary.U16(table, anchor + 8), Instance);
        }
    }

    /// <summary>The anchor of an anchor matrix (a base array, a ligature attach or a mark-2 array), or 0 when it is null.</summary>
    private static int GetMatrixAnchor(ReadOnlySpan<byte> table, int matrix, int row, int column, int columns)
    {
        if (matrix == 0 || row >= FontBinary.U16(table, matrix) || column >= columns)
            return 0;

        return OpenTypeLayoutTable.Offset16(table, matrix, matrix + 2 + (((row * columns) + column) * 2));
    }

    /// <summary>
    /// Attaches the current mark to a glyph with the anchors of a mark array (HarfBuzz's <c>MarkArray::apply</c>): the
    /// offset of the mark is the difference of the anchors, plus the cross-stream offset of the cursive chain of the glyph.
    /// </summary>
    private bool ApplyMarkArray(ReadOnlySpan<byte> table, int markArray, int markIndex, int matrix, int row, int classCount, int glyphPosition)
    {
        var buffer = Buffer;
        if (markIndex >= FontBinary.U16(table, markArray))
            return false;

        var markRecord = markArray + 2 + (markIndex * 4);
        var markClass = FontBinary.U16(table, markRecord);
        var markAnchor = OpenTypeLayoutTable.Offset16(table, markArray, markRecord + 2);
        var glyphAnchor = GetMatrixAnchor(table, matrix, row, markClass, classCount);

        // Without anchor for this glyph and this class, the next subtables have a chance
        if (glyphAnchor == 0 || markAnchor == 0)
            return false;

        buffer.UnsafeToBreak(glyphPosition, buffer.Index + 1);
        GetAnchor(table, markAnchor, out var markX, out var markY);
        GetAnchor(table, glyphAnchor, out var baseX, out var baseY);
        var crossOffset = ResolveCrossOffset(buffer.Positions, buffer.Length, glyphPosition, buffer.IsHorizontal);

        ref var mark = ref buffer.CurrentPosition;
        var chain = glyphPosition - buffer.Index;
        if (chain is >= short.MinValue and <= short.MaxValue)
        {
            mark.AttachChain = (short)chain;
            mark.AttachType = AttachmentType.Mark;
            mark.XOffset = baseX - markX;
            mark.YOffset = baseY - markY;
            if (buffer.IsHorizontal)
            {
                mark.YOffset += crossOffset;
            }
            else
            {
                mark.XOffset += crossOffset;
            }

            buffer.ScratchFlags |= ShapingScratchFlags.HasGposAttachment;
        }

        buffer.Index++;
        return true;
    }

    /// <summary>The cross-stream offset of a glyph and of the glyphs of its cursive chain.</summary>
    private static double ResolveCrossOffset(GlyphPosition[] positions, int length, int glyph, bool horizontal)
    {
        var offset = horizontal ? positions[glyph].YOffset : positions[glyph].XOffset;
        var nesting = 0;
        while (positions[glyph].AttachType == AttachmentType.Cursive && nesting++ < MaxNestingLevel)
        {
            var chain = positions[glyph].AttachChain;
            if (chain == 0)
                break;

            var parent = glyph + chain;
            if ((uint)parent >= (uint)length)
                break;

            glyph = parent;
            offset += horizontal ? positions[glyph].YOffset : positions[glyph].XOffset;
        }

        return offset;
    }

    /// <summary>
    /// Whether a glyph can be the base of a mark: only the first glyph of a multiple substitution (HarfBuzz's
    /// <c>MarkBasePosFormat1::accept</c>), unless a mark separates the glyphs of the sequence.
    /// </summary>
    private static bool AcceptsMarks(GlyphInfo[] info, int index)
    {
        return !info[index].IsMultiplied
            || info[index].LigatureComponent == 0
            || index == 0
            || info[index - 1].IsMark
            || !info[index - 1].IsMultiplied
            || info[index].LigatureId != info[index - 1].LigatureId
            || info[index].LigatureComponent != info[index - 1].LigatureComponent + 1;
    }

    /// <summary>Searches backward for the base glyph of the current mark (ignoring the marks), with the cache of <see cref="LastBase"/>; returns -1 when there is none.</summary>
    private int FindBase(ReadOnlySpan<byte> table, int baseCoverage)
    {
        var buffer = Buffer;
        ref var iterator = ref IterInput;
        iterator.LookupProperties = (uint)LookupFlags.IgnoreMarks;
        if (LastBaseUntil > buffer.Index)
        {
            LastBaseUntil = 0;
            LastBase = -1;
        }

        var info = buffer.Info;
        for (var j = buffer.Index; j > LastBaseUntil; j--)
        {
            var match = Match(iterator, info[j - 1]);
            if (match == SkippyMatch.Match && !AcceptsMarks(info, j - 1) && Table.GetCachedCoverageIndex(table, baseCoverage, info[j - 1].Codepoint) < 0)
            {
                match = SkippyMatch.Skip;
            }

            if (match == SkippyMatch.Match)
            {
                LastBase = j - 1;
                break;
            }
        }

        LastBaseUntil = buffer.Index;
        return LastBase;
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gpos#lookup-type-4-mark-to-base-attachment-positioning-subtable</summary>
    private bool ApplyMarkToBase(ReadOnlySpan<byte> table, int subtable)
    {
        var buffer = Buffer;
        var markIndex = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), buffer.Current.Codepoint);
        if (markIndex < 0)
            return false;

        var baseCoverage = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 4);
        var baseIndex = FindBase(table, baseCoverage);
        if (baseIndex < 0)
        {
            return false;
        }

        var coverageIndex = Table.GetCachedCoverageIndex(table, baseCoverage, buffer.Info[baseIndex].Codepoint);
        if (coverageIndex < 0)
        {
            return false;
        }

        var classCount = FontBinary.U16(table, subtable + 6);
        var markArray = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 8);
        var baseArray = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 10);
        return ApplyMarkArray(table, markArray, markIndex, baseArray, coverageIndex, classCount, baseIndex);
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gpos#lookup-type-5-mark-to-ligature-attachment-positioning-subtable</summary>
    private bool ApplyMarkToLigature(ReadOnlySpan<byte> table, int subtable)
    {
        var buffer = Buffer;
        var markIndex = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), buffer.Current.Codepoint);
        if (markIndex < 0)
            return false;

        var ligatureCoverage = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 4);
        var ligatureIndex = FindBase(table, ligatureCoverage);
        if (ligatureIndex < 0)
        {
            return false;
        }

        var coverageIndex = Table.GetCachedCoverageIndex(table, ligatureCoverage, buffer.Info[ligatureIndex].Codepoint);
        var ligatureArray = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 10);
        if (coverageIndex < 0 || coverageIndex >= FontBinary.U16(table, ligatureArray))
        {
            return false;
        }

        var ligatureAttach = OpenTypeLayoutTable.Offset16(table, ligatureArray, ligatureArray + 2 + (coverageIndex * 2));
        var componentCount = ligatureAttach == 0 ? 0 : FontBinary.U16(table, ligatureAttach);
        if (componentCount == 0)
        {
            return false;
        }

        // The mark attaches to its component when it belongs to the ligature, else to the last component
        var ligatureId = buffer.Info[ligatureIndex].LigatureId;
        var markId = buffer.Current.LigatureId;
        var markComponent = buffer.Current.LigatureComponent;
        var component = ligatureId != 0 && ligatureId == markId && markComponent > 0 ? Math.Min(componentCount, markComponent) - 1 : componentCount - 1;

        var classCount = FontBinary.U16(table, subtable + 6);
        var markArray = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 8);
        return ApplyMarkArray(table, markArray, markIndex, ligatureAttach, component, classCount, ligatureIndex);
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gpos#lookup-type-6-mark-to-mark-attachment-positioning-subtable</summary>
    private bool ApplyMarkToMark(ReadOnlySpan<byte> table, int subtable)
    {
        var buffer = Buffer;
        var mark1Index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2), buffer.Current.Codepoint);
        if (mark1Index < 0)
            return false;

        // The previous mark, until a glyph that is not a mark
        ref var iterator = ref IterInput;
        ResetFast(ref iterator, buffer.Index);
        iterator.LookupProperties = LookupProperties & ~(uint)LookupFlags.IgnoreFlags;
        if (!Prev(ref iterator, out _))
        {
            return false;
        }

        var info = buffer.Info;
        var j = iterator.Index;
        if (!info[j].IsMark)
        {
            return false;
        }

        var id1 = buffer.Current.LigatureId;
        var id2 = info[j].LigatureId;
        var component1 = buffer.Current.LigatureComponent;
        var component2 = info[j].LigatureComponent;

        // The marks of the same base, of the same component of a ligature, or one of them is a ligature
        var good = id1 == id2 ? id1 == 0 || component1 == component2 : (id1 > 0 && component1 == 0) || (id2 > 0 && component2 == 0);
        if (!good)
        {
            return false;
        }

        var mark2Index = Table.GetCachedCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, subtable, subtable + 4), info[j].Codepoint);
        if (mark2Index < 0)
        {
            return false;
        }

        var classCount = FontBinary.U16(table, subtable + 6);
        var mark1Array = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 8);
        var mark2Array = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 10);
        return ApplyMarkArray(table, mark1Array, mark1Index, mark2Array, mark2Index, classCount, j);
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/gpos#lookup-type-3-cursive-attachment-positioning-subtable</summary>
    private bool ApplyCursiveAttachment(ReadOnlySpan<byte> table, int subtable)
    {
        var buffer = Buffer;
        var coverage = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 2);
        var recordCount = FontBinary.U16(table, subtable + 4);
        var thisIndex = Table.GetCachedCoverageIndex(table, coverage, buffer.Current.Codepoint);
        if (thisIndex < 0 || thisIndex >= recordCount)
            return false;

        var entryAnchor = OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6 + (thisIndex * 4));
        if (entryAnchor == 0)
            return false;

        ref var iterator = ref IterInput;
        ResetFast(ref iterator, buffer.Index);
        if (!Prev(ref iterator, out _))
        {
            return false;
        }

        var previousIndex = Table.GetCachedCoverageIndex(table, coverage, buffer.Info[iterator.Index].Codepoint);
        var exitAnchor = previousIndex >= 0 && previousIndex < recordCount ? OpenTypeLayoutTable.Offset16(table, subtable, subtable + 6 + (previousIndex * 4) + 2) : 0;
        if (exitAnchor == 0)
        {
            return false;
        }

        var i = iterator.Index;
        var j = buffer.Index;
        buffer.UnsafeToBreak(i, j + 1);
        GetAnchor(table, exitAnchor, out var exitX, out var exitY);
        GetAnchor(table, entryAnchor, out var entryX, out var entryY);
        var positions = buffer.Positions;

        // The main-direction adjustment
        switch (Direction)
        {
            case ShapingDirection.LeftToRight:
            {
                positions[i].XAdvance = exitX + positions[i].XOffset;
                var d = entryX + positions[j].XOffset;
                positions[j].XAdvance -= d;
                positions[j].XOffset -= d;
                break;
            }

            case ShapingDirection.RightToLeft:
            {
                var d = exitX + positions[i].XOffset;
                positions[i].XAdvance -= d;
                positions[i].XOffset -= d;
                positions[j].XAdvance = entryX + positions[j].XOffset;
                break;
            }

            case ShapingDirection.TopToBottom:
            {
                positions[i].YAdvance = exitY + positions[i].YOffset;
                var d = entryY + positions[j].YOffset;
                positions[j].YAdvance -= d;
                positions[j].YOffset -= d;
                break;
            }
        }

        // The cross-direction adjustment: the child is attached to its parent (the root stays on the baseline); with the
        // RightToLeft flag, the last glyph is the root
        var child = i;
        var parent = j;
        var xOffset = entryX - exitX;
        var yOffset = entryY - exitY;
        if ((LookupProperties & (uint)LookupFlags.RightToLeft) == 0)
        {
            (child, parent) = (parent, child);
            xOffset = -xOffset;
            yOffset = -yOffset;
        }

        // When the child was attached to another glyph, its chain is reversed so that its tree attaches to the new parent
        ReverseCursiveMinorOffset(positions, buffer.Length, child, buffer.IsHorizontal, parent, 0);
        var chain = parent - child;
        if (chain is >= short.MinValue and <= short.MaxValue)
        {
            positions[child].AttachChain = (short)chain;
            positions[child].AttachType = AttachmentType.Cursive;
            buffer.ScratchFlags |= ShapingScratchFlags.HasGposAttachment;
            if (buffer.IsHorizontal)
            {
                positions[child].YOffset = yOffset;
            }
            else
            {
                positions[child].XOffset = xOffset;
            }

            // When the parent was attached to the child, separate them
            if (positions[parent].AttachChain == -positions[child].AttachChain)
            {
                positions[parent].AttachChain = 0;
                if (buffer.IsHorizontal)
                {
                    positions[parent].YOffset = 0;
                }
                else
                {
                    positions[parent].XOffset = 0;
                }
            }
        }

        buffer.Index++;
        return true;
    }

    private static void ReverseCursiveMinorOffset(GlyphPosition[] positions, int length, int i, bool horizontal, int newParent, int nesting)
    {
        if (nesting > MaxNestingLevel)
            return;

        var chain = positions[i].AttachChain;
        var type = positions[i].AttachType;
        if (chain == 0 || type != AttachmentType.Cursive)
            return;

        positions[i].AttachChain = 0;
        var j = i + chain;
        if ((uint)j >= (uint)length || j == newParent)
            return;

        ReverseCursiveMinorOffset(positions, length, j, horizontal, newParent, nesting + 1);
        if (horizontal)
        {
            positions[j].YOffset = -positions[i].YOffset;
        }
        else
        {
            positions[j].XOffset = -positions[i].XOffset;
        }

        positions[j].AttachChain = (short)-chain;
        positions[j].AttachType = type;
    }

    /// <summary>
    /// Accumulates the offsets of the attached glyphs (marks and cursive chains) after the positioning (HarfBuzz's
    /// <c>GPOS::position_finish_offsets</c>): the offset of a glyph includes the offset of the glyph it attaches to and the
    /// advances between them.
    /// </summary>
    public static void FinishOffsets(ShapingBuffer buffer)
    {
        if ((buffer.ScratchFlags & ShapingScratchFlags.HasGposAttachment) == ShapingScratchFlags.None)
            return;

        var positions = buffer.Positions;
        var length = buffer.Length;
        if (!buffer.IsBackward)
        {
            for (var i = 0; i < length; i++)
            {
                if (positions[i].AttachChain != 0)
                {
                    PropagateAttachmentOffsets(positions, length, i, buffer.Direction, MaxNestingLevel);
                }
            }
        }
        else
        {
            for (var i = length - 1; i >= 0; i--)
            {
                if (positions[i].AttachChain != 0)
                {
                    PropagateAttachmentOffsets(positions, length, i, buffer.Direction, MaxNestingLevel);
                }
            }
        }
    }

    private static void PropagateAttachmentOffsets(GlyphPosition[] positions, int length, int i, ShapingDirection direction, int nesting)
    {
        var chain = positions[i].AttachChain;
        var type = positions[i].AttachType;
        positions[i].AttachChain = 0;
        var j = i + chain;
        if ((uint)j >= (uint)length || nesting == 0)
            return;

        if (positions[j].AttachChain != 0)
        {
            PropagateAttachmentOffsets(positions, length, j, direction, nesting - 1);
        }

        var horizontal = direction != ShapingDirection.TopToBottom;
        if (type == AttachmentType.Cursive)
        {
            if (horizontal)
            {
                positions[i].YOffset += positions[j].YOffset;
            }
            else
            {
                positions[i].XOffset += positions[j].XOffset;
            }

            return;
        }

        if (horizontal)
        {
            positions[i].XOffset += positions[j].XOffset;
        }
        else
        {
            positions[i].YOffset += positions[j].YOffset;
        }

        // i is the mark, j the glyph it attaches to (before it in OpenType)
        var forward = direction != ShapingDirection.RightToLeft;
        if (j < i)
        {
            if (forward)
            {
                for (var k = j; k < i; k++)
                {
                    positions[i].XOffset -= positions[k].XAdvance;
                    positions[i].YOffset -= positions[k].YAdvance;
                }
            }
            else
            {
                for (var k = j + 1; k < i + 1; k++)
                {
                    positions[i].XOffset += positions[k].XAdvance;
                    positions[i].YOffset += positions[k].YAdvance;
                }
            }
        }
        else if (forward)
        {
            for (var k = i; k < j; k++)
            {
                positions[i].XOffset += positions[k].XAdvance;
                positions[i].YOffset += positions[k].YAdvance;
            }
        }
        else
        {
            for (var k = i + 1; k < j + 1; k++)
            {
                positions[i].XOffset -= positions[k].XAdvance;
                positions[i].YOffset -= positions[k].YAdvance;
            }
        }
    }
}
