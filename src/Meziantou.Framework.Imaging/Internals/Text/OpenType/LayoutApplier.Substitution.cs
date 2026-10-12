using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>The substitution lookups: OpenType specification, "GSUB - Glyph Substitution Table", lookup types 1 to 4 and 8.</content>
internal sealed partial class LayoutApplier
{
    /// <summary>Type 1: one glyph becomes another glyph.</summary>
    private bool ApplySingleSubstitution(int subtable, ref int index)
    {
        var data = _data.Span;
        ref var info = ref _buffer.Info[index];
        var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), info.Glyph);
        if (coverage < 0)
            return false;

        int glyph;
        switch (FontBinary.UInt16(data, subtable))
        {
            // The same difference for every glyph, modulo 65536.
            case 1:
                glyph = (info.Glyph + FontBinary.Int16(data, subtable + 4)) & 0xFFFF;
                break;

            // One glyph per covered glyph.
            case 2:
                if (coverage >= FontBinary.UInt16(data, subtable + 4))
                    return false;

                glyph = FontBinary.UInt16(data, subtable + 6 + (coverage * 2));
                break;

            default:
                return false;
        }

        SetGlyph(ref info, glyph, history: 0, impliedClass: 0);
        index++;
        return true;
    }

    /// <summary>Type 2: one glyph becomes a sequence of glyphs.</summary>
    private bool ApplyMultipleSubstitution(int subtable, ref int index)
    {
        var data = _data.Span;
        var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), _buffer.Info[index].Glyph);
        if (FontBinary.UInt16(data, subtable) != 1 || coverage < 0 || coverage >= FontBinary.UInt16(data, subtable + 4))
            return false;

        var sequence = subtable + FontBinary.UInt16(data, subtable + 6 + (coverage * 2));
        var count = FontBinary.UInt16(data, sequence);
        switch (count)
        {
            // The specification asks for at least one glyph; fonts use an empty sequence to remove a glyph.
            case 0:
                _buffer.Delete(index);
                return true;

            case 1:
                SetGlyph(ref _buffer.Info[index], FontBinary.UInt16(data, sequence + 2), history: 0, impliedClass: 0);
                index++;
                return true;
        }

        // The parts of a ligature that is taken apart are bases; the glyphs of the sequence remember their rank, so
        // that a mark is only attached to the first of them.
        var impliedClass = _buffer.Info[index].IsLigature ? GlyphInfo.BaseGlyph : 0;

        // The glyphs of a glyph that is a ligature, or that follows a component of one, keep what ties them to it;
        // the others are numbered in their sequence.
        var isTiedToLigature = _buffer.Info[index].LigatureId != 0;
        _buffer.Resize(index, 1, count);
        for (var i = 0; i < count; i++)
        {
            ref var info = ref _buffer.Info[index + i];
            SetGlyph(ref info, FontBinary.UInt16(data, sequence + 2 + (i * 2)), GlyphInfo.Multiplied, impliedClass);
            if (!isTiedToLigature)
            {
                info.Props &= unchecked((byte)~GlyphInfo.LigatureWithComponents);
                info.LigatureComponent = (byte)Math.Min(i + 1, byte.MaxValue);
            }
        }

        index += count;
        return true;
    }

    /// <summary>Type 3: one glyph becomes one of several alternates, chosen by the value of the feature.</summary>
    private bool ApplyAlternateSubstitution(int subtable, ref int index)
    {
        var data = _data.Span;
        ref var info = ref _buffer.Info[index];
        var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), info.Glyph);
        if (FontBinary.UInt16(data, subtable) != 1 || coverage < 0 || coverage >= FontBinary.UInt16(data, subtable + 4))
            return false;

        // The value of the feature at the glyph counts the alternates from 1.
        var value = (int)((info.Mask & _mask) >> BitOperations.TrailingZeroCount(_mask));
        var alternates = subtable + FontBinary.UInt16(data, subtable + 6 + (coverage * 2));
        if (value < 1 || value > FontBinary.UInt16(data, alternates))
            return false;

        SetGlyph(ref info, FontBinary.UInt16(data, alternates + (value * 2)), history: 0, impliedClass: 0);
        index++;
        return true;
    }

    /// <summary>Type 4: a sequence of glyphs becomes one glyph.</summary>
    private bool ApplyLigatureSubstitution(int subtable, ref int index)
    {
        var data = _data.Span;
        var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), _buffer.Info[index].Glyph);
        if (FontBinary.UInt16(data, subtable) != 1 || coverage < 0 || coverage >= FontBinary.UInt16(data, subtable + 4))
            return false;

        // The ligatures that start with the glyph, by order of preference.
        var set = subtable + FontBinary.UInt16(data, subtable + 6 + (coverage * 2));
        var count = FontBinary.UInt16(data, set);
        Span<int> positions = stackalloc int[MaxSequenceLength];
        for (var i = 0; i < count; i++)
        {
            var ligature = set + FontBinary.UInt16(data, set + 2 + (i * 2));
            var componentCount = FontBinary.UInt16(data, ligature + 2);
            if (componentCount is 0 or > MaxSequenceLength)
                continue;

            positions[0] = index;
            if (!MatchForward(index, componentCount - 1, LayoutMatchKind.Glyph, ligature + 4, 0, isContext: false, positions[1..componentCount], out _))
                continue;

            Ligate(positions[..componentCount], FontBinary.UInt16(data, ligature), ref index);
            return true;
        }

        return false;
    }

    /// <summary>Replaces the glyphs at the given positions by a ligature.</summary>
    private void Ligate(ReadOnlySpan<int> positions, int ligature, ref int index)
    {
        var first = positions[0];
        if (positions.Length == 1)
        {
            SetGlyph(ref _buffer.Info[first], ligature, history: 0, impliedClass: 0);
            index = first + 1;
            return;
        }

        var info = _buffer.Info;

        // A base with its marks stays a base, and marks together stay a mark: marks that follow can still be attached
        // to the result as to a single glyph. Anything else is a ligature, with one component per glyph it replaces,
        // or more when some of them were ligatures already.
        var hasOnlyMarksAfterFirst = true;
        for (var i = 1; i < positions.Length; i++)
        {
            hasOnlyMarksAfterFirst &= info[positions[i]].IsMark;
        }

        var isLigature = !(hasOnlyMarksAfterFirst && (info[first].IsMark || info[first].IsBase));
        var id = isLigature ? _buffer.AllocateLigatureId() : (byte)0;

        // The marks the lookup skipped between two components stay in the buffer, and now follow a component of the
        // ligature. A mark that already followed a component of a ligature that is itself a component keeps its rank
        // inside it.
        var componentsBefore = 0;
        var last = positions[^1];
        for (var i = 0; i < positions.Length; i++)
        {
            ref readonly var component = ref info[positions[i]];
            var componentId = component.LigatureId;
            var componentSize = GetComponentCount(component);
            var end = i + 1 < positions.Length ? positions[i + 1] : info.Length;
            for (var j = positions[i] + 1; isLigature && j < end; j++)
            {
                ref var mark = ref info[j];
                if (i + 1 == positions.Length)
                {
                    // After the last component: only the marks that were its own follow it into the ligature.
                    if (componentId == 0 || mark.LigatureId != componentId || mark.LigatureComponent == 0 || IsLigatureItself(mark))
                        break;
                }

                var rank = mark.LigatureId == componentId && componentId != 0 && mark.LigatureComponent != 0 && !IsLigatureItself(mark) ? Math.Min(mark.LigatureComponent, componentSize) : componentSize;
                mark.LigatureId = id;
                mark.LigatureComponent = (byte)Math.Min(componentsBefore + rank, byte.MaxValue);
            }

            componentsBefore += componentSize;
        }

        _buffer.MergeClusters(first, last + 1);
        ref var target = ref info[first];
        SetGlyph(ref target, ligature, GlyphInfo.Ligated, isLigature ? GlyphInfo.LigatureGlyph : 0);
        if (isLigature)
        {
            target.Props |= GlyphInfo.LigatureWithComponents;
            target.LigatureId = id;
            target.LigatureComponent = (byte)Math.Min(componentsBefore, byte.MaxValue);

            // The glyph of a mark that starts a ligature is no longer the glyph of a mark.
            if (target.Category == UnicodeCategory.NonSpacingMark)
            {
                target.Category = UnicodeCategory.OtherLetter;
            }
        }

        // Remove the other components, from the last one so that the positions stay valid.
        for (var i = positions.Length - 1; i >= 1; i--)
        {
            _buffer.Resize(positions[i], 1, 0);
        }

        index = last - (positions.Length - 1) + 1;
    }

    /// <summary>Gets the number of components a glyph stands for: those of a ligature, or one.</summary>
    private static int GetComponentCount(in GlyphInfo info) => IsLigatureItself(info) ? Math.Max((int)info.LigatureComponent, 1) : 1;

    /// <summary>Determines whether a glyph is a ligature formed by a lookup, whose component field is a count, not a rank.</summary>
    private static bool IsLigatureItself(in GlyphInfo info) => info.LigatureId != 0 && (info.Props & GlyphInfo.LigatureWithComponents) != 0;

    /// <summary>
    /// Type 8: one glyph becomes another one when the glyphs before and after it are the expected ones. The lookup is
    /// applied from the end of the text, so that it sees the glyphs after as it left them.
    /// </summary>
    private bool ApplyReverseChainedSubstitution(int subtable, ref int index)
    {
        // Not inside another lookup: the specification excludes it.
        if (_nesting != 0)
            return false;

        var data = _data.Span;
        ref var info = ref _buffer.Info[index];
        var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), info.Glyph);
        if (FontBinary.UInt16(data, subtable) != 1 || coverage < 0)
            return false;

        var backtrackCount = FontBinary.UInt16(data, subtable + 4);
        var lookahead = subtable + 6 + (backtrackCount * 2);
        var lookaheadCount = FontBinary.UInt16(data, lookahead);
        var substitutes = lookahead + 2 + (lookaheadCount * 2);
        if (coverage >= FontBinary.UInt16(data, substitutes)
            || !MatchBackward(index, backtrackCount, LayoutMatchKind.Coverage, subtable + 6, subtable)
            || !MatchForward(index, lookaheadCount, LayoutMatchKind.Coverage, lookahead + 2, subtable, isContext: true, default, out _))
        {
            return false;
        }

        SetGlyph(ref info, FontBinary.UInt16(data, substitutes + 2 + (coverage * 2)), history: 0, impliedClass: 0);
        return true;
    }
}
