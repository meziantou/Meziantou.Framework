namespace Meziantou.Framework.Imaging.Internals;

/// <content>
/// The contextual lookups, which substitution and positioning share: OpenType specification, "Sequence Context" and
/// "Chained Sequence Context" formats 1 (glyphs), 2 (classes) and 3 (coverage tables).
/// </content>
internal sealed partial class LayoutApplier
{
    private bool ApplyContext(int subtable, ref int index)
    {
        var data = _data.Span;
        var glyph = _buffer.Info[index].Glyph;
        switch (FontBinary.UInt16(data, subtable))
        {
            case 1:
                {
                    var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), glyph);
                    if (coverage < 0 || coverage >= FontBinary.UInt16(data, subtable + 4))
                        return false;

                    return ApplyRuleSet(subtable, FontBinary.UInt16(data, subtable + 6 + (coverage * 2)), LayoutMatchKind.Glyph, 0, ref index);
                }

            case 2:
                {
                    if (LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), glyph) < 0)
                        return false;

                    var classesOffset = FontBinary.UInt16(data, subtable + 4);
                    var classes = classesOffset == 0 ? -1 : subtable + classesOffset;
                    var glyphClass = classes < 0 ? 0 : LayoutCommon.GetClass(data, classes, glyph);
                    if (glyphClass >= FontBinary.UInt16(data, subtable + 6))
                        return false;

                    return ApplyRuleSet(subtable, FontBinary.UInt16(data, subtable + 8 + (glyphClass * 2)), LayoutMatchKind.Class, classes, ref index);
                }

            case 3:
                {
                    // One coverage table per glyph of the sequence, then the lookups to apply.
                    var glyphCount = FontBinary.UInt16(data, subtable + 2);
                    if (glyphCount == 0 || LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 6), glyph) < 0)
                        return false;

                    return ApplySequence(
                        LayoutMatchKind.Coverage,
                        backtrackCount: 0,
                        backtrack: 0,
                        backtrackBase: subtable,
                        glyphCount,
                        input: subtable + 8,
                        inputBase: subtable,
                        lookaheadCount: 0,
                        lookahead: 0,
                        lookaheadBase: subtable,
                        FontBinary.UInt16(data, subtable + 4),
                        subtable + 6 + (glyphCount * 2),
                        ref index);
                }

            default:
                return false;
        }
    }

    /// <summary>Tries the rules of a rule set of a sequence context, in order.</summary>
    private bool ApplyRuleSet(int subtable, int ruleSetOffset, LayoutMatchKind kind, int classes, ref int index)
    {
        if (ruleSetOffset == 0)
            return false;

        var data = _data.Span;
        var ruleSet = subtable + ruleSetOffset;
        var count = FontBinary.UInt16(data, ruleSet);
        for (var i = 0; i < count; i++)
        {
            // A rule: the number of glyphs, the number of lookups, the glyphs after the first one, the lookups.
            var rule = ruleSet + FontBinary.UInt16(data, ruleSet + 2 + (i * 2));
            var glyphCount = FontBinary.UInt16(data, rule);
            if (glyphCount == 0)
                continue;

            if (ApplySequence(kind, 0, 0, classes, glyphCount, rule + 4, classes, 0, 0, classes, FontBinary.UInt16(data, rule + 2), rule + 4 + ((glyphCount - 1) * 2), ref index))
                return true;
        }

        return false;
    }

    private bool ApplyChainedContext(int subtable, ref int index)
    {
        var data = _data.Span;
        var glyph = _buffer.Info[index].Glyph;
        switch (FontBinary.UInt16(data, subtable))
        {
            case 1:
                {
                    var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), glyph);
                    if (coverage < 0 || coverage >= FontBinary.UInt16(data, subtable + 4))
                        return false;

                    return ApplyChainedRuleSet(subtable, FontBinary.UInt16(data, subtable + 6 + (coverage * 2)), LayoutMatchKind.Glyph, 0, 0, 0, ref index);
                }

            case 2:
                {
                    if (LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), glyph) < 0)
                        return false;

                    // Three class definitions: for the glyphs before, for the sequence, for the glyphs after. An offset of
                    // zero is a definition without glyph: every glyph has the class 0.
                    var backtrackClasses = GetClassDefinition(data, subtable, 4);
                    var inputClasses = GetClassDefinition(data, subtable, 6);
                    var lookaheadClasses = GetClassDefinition(data, subtable, 8);
                    var glyphClass = inputClasses < 0 ? 0 : LayoutCommon.GetClass(data, inputClasses, glyph);
                    if (glyphClass >= FontBinary.UInt16(data, subtable + 10))
                        return false;

                    return ApplyChainedRuleSet(subtable, FontBinary.UInt16(data, subtable + 12 + (glyphClass * 2)), LayoutMatchKind.Class, backtrackClasses, inputClasses, lookaheadClasses, ref index);
                }

            case 3:
                {
                    var backtrackCount = FontBinary.UInt16(data, subtable + 2);
                    var input = subtable + 4 + (backtrackCount * 2);
                    var inputCount = FontBinary.UInt16(data, input);
                    if (inputCount == 0 || LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, input + 2), glyph) < 0)
                        return false;

                    var lookahead = input + 2 + (inputCount * 2);
                    var lookaheadCount = FontBinary.UInt16(data, lookahead);
                    var lookups = lookahead + 2 + (lookaheadCount * 2);
                    return ApplySequence(
                        LayoutMatchKind.Coverage,
                        backtrackCount,
                        subtable + 4,
                        subtable,
                        inputCount,
                        input + 4,
                        subtable,
                        lookaheadCount,
                        lookahead + 2,
                        subtable,
                        FontBinary.UInt16(data, lookups),
                        lookups + 2,
                        ref index);
                }

            default:
                return false;
        }

        static int GetClassDefinition(ReadOnlySpan<byte> data, int subtable, int field)
        {
            var offset = FontBinary.UInt16(data, subtable + field);
            return offset == 0 ? -1 : subtable + offset;
        }
    }

    private bool ApplyChainedRuleSet(int subtable, int ruleSetOffset, LayoutMatchKind kind, int backtrackBase, int inputBase, int lookaheadBase, ref int index)
    {
        if (ruleSetOffset == 0)
            return false;

        var data = _data.Span;
        var ruleSet = subtable + ruleSetOffset;
        var count = FontBinary.UInt16(data, ruleSet);
        for (var i = 0; i < count; i++)
        {
            // A rule: the glyphs before (the nearest first), the glyphs of the sequence after its first one, the
            // glyphs after, then the lookups; each list starts with its length.
            var rule = ruleSet + FontBinary.UInt16(data, ruleSet + 2 + (i * 2));
            var backtrackCount = FontBinary.UInt16(data, rule);
            var input = rule + 2 + (backtrackCount * 2);
            var inputCount = FontBinary.UInt16(data, input);
            if (inputCount == 0)
                continue;

            var lookahead = input + 2 + ((inputCount - 1) * 2);
            var lookaheadCount = FontBinary.UInt16(data, lookahead);
            var lookups = lookahead + 2 + (lookaheadCount * 2);
            if (ApplySequence(kind, backtrackCount, rule + 2, backtrackBase, inputCount, input + 2, inputBase, lookaheadCount, lookahead + 2, lookaheadBase, FontBinary.UInt16(data, lookups), lookups + 2, ref index))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Matches a sequence at a glyph, with the glyphs that must precede and follow it, and applies the lookups of the
    /// rule to the glyphs of the sequence.
    /// </summary>
    /// <param name="kind">How the values describe the glyphs.</param>
    /// <param name="backtrackCount">The number of glyphs that must precede the sequence.</param>
    /// <param name="backtrack">The position of their values: the nearest glyph first.</param>
    /// <param name="backtrackBase">The class definition, or the table the coverage offsets are from.</param>
    /// <param name="inputCount">The number of glyphs of the sequence, with its first glyph, which already matched.</param>
    /// <param name="input">The position of the values of the glyphs after the first one.</param>
    /// <param name="inputBase">The class definition, or the table the coverage offsets are from.</param>
    /// <param name="lookaheadCount">The number of glyphs that must follow the sequence.</param>
    /// <param name="lookahead">The position of their values.</param>
    /// <param name="lookaheadBase">The class definition, or the table the coverage offsets are from.</param>
    /// <param name="lookupCount">The number of lookups to apply.</param>
    /// <param name="lookups">The position of the lookup records: an index in the sequence and a lookup.</param>
    /// <param name="index">The first glyph of the sequence; when the rule applies, the glyph after the sequence.</param>
    private bool ApplySequence(
        LayoutMatchKind kind,
        int backtrackCount,
        int backtrack,
        int backtrackBase,
        int inputCount,
        int input,
        int inputBase,
        int lookaheadCount,
        int lookahead,
        int lookaheadBase,
        int lookupCount,
        int lookups,
        ref int index)
    {
        if (inputCount > MaxSequenceLength)
            return false;

        Span<int> positions = stackalloc int[MaxSequenceLength];
        positions[0] = index;
        // The glyphs a rule replaces are a sequence a zero width non-joiner breaks, as it breaks a ligature; the glyphs
        // around them are seen through it.
        if (!MatchForward(index, inputCount - 1, kind, input, inputBase, isContext: false, positions[1..inputCount], out var last)
            || !MatchBackward(index, backtrackCount, kind, backtrack, backtrackBase)
            || !MatchForward(last, lookaheadCount, kind, lookahead, lookaheadBase, isContext: true, default, out _))
        {
            return false;
        }

        // Each lookup applies at one glyph of the sequence, which is the sequence as the previous lookups left it: a
        // lookup that removes glyphs (a ligature) shortens it, and a lookup that inserts glyphs lengthens it.
        var data = _data.Span;
        var count = inputCount;
        var end = last + 1;
        for (var i = 0; i < lookupCount; i++)
        {
            var sequenceIndex = FontBinary.UInt16(data, lookups + (i * 4));
            if (sequenceIndex >= count)
                continue;

            var position = positions[sequenceIndex];
            var length = _buffer.Length;
            ApplyNested(FontBinary.UInt16(data, lookups + (i * 4) + 2), position);
            var delta = _buffer.Length - length;
            if (delta == 0)
                continue;

            end = Math.Max(end + delta, position);
            var next = sequenceIndex + 1;
            if (delta > 0)
            {
                // The glyphs inserted after the glyph join the sequence, as far as it can be followed.
                var inserted = Math.Min(delta, MaxSequenceLength - count);
                positions[next..count].CopyTo(positions[(next + inserted)..]);
                for (var j = 0; j < inserted; j++)
                {
                    positions[next + j] = position + 1 + j;
                }

                count += inserted;
                next += inserted;
            }
            else
            {
                // The glyphs removed after the glyph leave the sequence.
                var removed = Math.Min(-delta, count - next);
                positions[(next + removed)..count].CopyTo(positions[next..]);
                count -= removed;
            }

            for (var j = next; j < count; j++)
            {
                positions[j] = Math.Max(positions[j] + delta, position);
            }
        }

        index = end;
        return true;
    }

    /// <summary>Matches values against the glyphs that follow a glyph, passing over the glyphs the lookup skips.</summary>
    private bool MatchForward(int start, int count, LayoutMatchKind kind, int values, int valuesBase, bool isContext, Span<int> positions, out int last)
    {
        var data = _data.Span;
        var info = _buffer.Info;
        var index = start;
        last = start;
        for (var i = 0; i < count; i++)
        {
            var value = FontBinary.UInt16(data, values + (i * 2));
            while (true)
            {
                index++;
                if (index >= info.Length)
                    return false;

                ref readonly var candidate = ref info[index];
                if (IsSkipped(candidate))
                    continue;

                if (Matches(data, kind, value, valuesBase, candidate.Glyph))
                    break;

                if (!IsTransparent(candidate, isContext))
                    return false;
            }

            if (!positions.IsEmpty)
            {
                positions[i] = index;
            }
        }

        last = index;
        return true;
    }

    /// <summary>Matches values against the glyphs that precede a glyph, the nearest first.</summary>
    private bool MatchBackward(int start, int count, LayoutMatchKind kind, int values, int valuesBase)
    {
        var data = _data.Span;
        var info = _buffer.Info;
        var index = start;
        for (var i = 0; i < count; i++)
        {
            var value = FontBinary.UInt16(data, values + (i * 2));
            while (true)
            {
                index--;
                if (index < 0)
                    return false;

                ref readonly var candidate = ref info[index];
                if (IsSkipped(candidate))
                    continue;

                if (Matches(data, kind, value, valuesBase, candidate.Glyph))
                    break;

                if (!IsTransparent(candidate, isContext: true))
                    return false;
            }
        }

        return true;
    }

    private static bool Matches(ReadOnlySpan<byte> data, LayoutMatchKind kind, int value, int valueBase, int glyph)
    {
        return kind switch
        {
            LayoutMatchKind.Glyph => glyph == value,
            LayoutMatchKind.Class => (valueBase < 0 ? 0 : LayoutCommon.GetClass(data, valueBase, glyph)) == value,
            _ => LayoutCommon.GetCoverage(data, valueBase + value, glyph) >= 0,
        };
    }
}
