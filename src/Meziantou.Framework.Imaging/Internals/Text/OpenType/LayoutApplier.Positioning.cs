using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>The positioning lookups: OpenType specification, "GPOS - Glyph Positioning Table", lookup types 1 to 6.</content>
internal sealed partial class LayoutApplier
{
    // The fields of a value record, in the order of their bits: four values, then four offsets to device tables. A
    // device table adjusts a value at a pixel size (hinting), which this library does not do, or - in a variable font -
    // names the deltas of the value in the item variation store of the GDEF table.
    private const int XPlacement = 0x0001;
    private const int YPlacement = 0x0002;
    private const int XAdvance = 0x0004;
    private const int YAdvance = 0x0008;
    private const int XPlacementDevice = 0x0010;
    private const int YPlacementDevice = 0x0020;
    private const int XAdvanceDevice = 0x0040;
    private const int YAdvanceDevice = 0x0080;
    private const int VariationIndexFormat = 0x8000;

    private static int GetValueRecordLength(int format) => BitOperations.PopCount((uint)(format & 0xFF)) * 2;

    /// <summary>Adds a value record to the position of a glyph.</summary>
    /// <param name="data">The table.</param>
    /// <param name="parent">The table the offsets of the record are from: the subtable, or the pair set of the first format of pairs.</param>
    /// <param name="record">The position of the record.</param>
    /// <param name="format">The fields of the record.</param>
    /// <param name="position">The position of the glyph.</param>
    private void ApplyValue(ReadOnlySpan<byte> data, int parent, int record, int format, ref GlyphPosition position)
    {
        if (_hasVariations && (format & (XPlacementDevice | YPlacementDevice | XAdvanceDevice | YAdvanceDevice)) != 0)
        {
            var device = record + (BitOperations.PopCount((uint)(format & 0x0F)) * 2);
            if ((format & XPlacementDevice) != 0)
            {
                position.XOffset += GetVariationDelta(data, parent, FontBinary.UInt16(data, device));
                device += 2;
            }

            if ((format & YPlacementDevice) != 0)
            {
                position.YOffset += GetVariationDelta(data, parent, FontBinary.UInt16(data, device));
                device += 2;
            }

            if ((format & XAdvanceDevice) != 0)
            {
                if (!_isVertical)
                {
                    position.XAdvance += GetVariationDelta(data, parent, FontBinary.UInt16(data, device));
                }

                device += 2;
            }

            if ((format & YAdvanceDevice) != 0 && _isVertical)
            {
                position.YAdvance -= GetVariationDelta(data, parent, FontBinary.UInt16(data, device));
            }
        }

        if ((format & XPlacement) != 0)
        {
            position.XOffset += FontBinary.Int16(data, record);
            record += 2;
        }

        if ((format & YPlacement) != 0)
        {
            position.YOffset += FontBinary.Int16(data, record);
            record += 2;
        }

        // The advance of the direction of the text: the third field in a horizontal line, the fourth one in a
        // vertical line, where the pen moves down as the advance grows and the y axis of the font goes up.
        if ((format & XAdvance) != 0)
        {
            if (!_isVertical)
            {
                position.XAdvance += FontBinary.Int16(data, record);
            }

            record += 2;
        }

        if ((format & YAdvance) != 0 && _isVertical)
        {
            position.YAdvance -= FontBinary.Int16(data, record);
        }
    }

    /// <summary>
    /// Gets the delta of a value for the instance of a variable font, rounded to a font unit: the device table of the
    /// value is then a variation index, the subtable and the item of its deltas in the store of the GDEF table.
    /// </summary>
    private int GetVariationDelta(ReadOnlySpan<byte> data, int parent, int offset)
    {
        if (offset == 0)
            return 0;

        var device = parent + offset;
        return FontBinary.UInt16(data, device + 4) == VariationIndexFormat
            ? _face.GetLayoutDelta(FontBinary.UInt16(data, device), FontBinary.UInt16(data, device + 2))
            : 0;
    }

    /// <summary>Type 1: a glyph is moved or its advance is changed.</summary>
    private bool ApplySinglePositioning(int subtable, ref int index)
    {
        var data = _data.Span;
        var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), _buffer.Info[index].Glyph);
        if (coverage < 0)
            return false;

        var format = FontBinary.UInt16(data, subtable + 4);
        switch (FontBinary.UInt16(data, subtable))
        {
            // The same value for every glyph.
            case 1:
                ApplyValue(data, subtable, subtable + 6, format, ref _buffer.Positions[index]);
                break;

            // One value per covered glyph.
            case 2:
                if (coverage >= FontBinary.UInt16(data, subtable + 6))
                    return false;

                ApplyValue(data, subtable, subtable + 8 + (coverage * GetValueRecordLength(format)), format, ref _buffer.Positions[index]);
                break;

            default:
                return false;
        }

        index++;
        return true;
    }

    /// <summary>Type 2: two glyphs that follow each other are moved, which is how fonts kern.</summary>
    private bool ApplyPairPositioning(int subtable, ref int index)
    {
        var data = _data.Span;
        var info = _buffer.Info;
        var coverage = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), info[index].Glyph);
        if (coverage < 0)
            return false;

        var second = NextGlyph(index);
        if (second >= info.Length)
            return false;

        var format1 = FontBinary.UInt16(data, subtable + 4);
        var format2 = FontBinary.UInt16(data, subtable + 6);
        var length1 = GetValueRecordLength(format1);
        var length2 = GetValueRecordLength(format2);
        int record;
        var parent = subtable;
        switch (FontBinary.UInt16(data, subtable))
        {
            // The pairs of each first glyph, sorted by second glyph.
            case 1:
                {
                    if (coverage >= FontBinary.UInt16(data, subtable + 8))
                        return false;

                    var set = subtable + FontBinary.UInt16(data, subtable + 10 + (coverage * 2));
                    parent = set;
                    var recordLength = 2 + length1 + length2;
                    var low = 0;
                    var high = FontBinary.UInt16(data, set) - 1;
                    record = -1;
                    while (low <= high)
                    {
                        var middle = (low + high) >>> 1;
                        var candidate = set + 2 + (middle * recordLength);
                        var glyph = FontBinary.UInt16(data, candidate);
                        if (glyph == info[second].Glyph)
                        {
                            record = candidate + 2;
                            break;
                        }

                        if (glyph < info[second].Glyph)
                        {
                            low = middle + 1;
                        }
                        else
                        {
                            high = middle - 1;
                        }
                    }

                    if (record < 0)
                        return false;

                    break;
                }

            // A value for each class of first glyph and each class of second glyph.
            case 2:
                {
                    var class1 = LayoutCommon.GetClass(data, subtable + FontBinary.UInt16(data, subtable + 8), info[index].Glyph);
                    var class2 = LayoutCommon.GetClass(data, subtable + FontBinary.UInt16(data, subtable + 10), info[second].Glyph);
                    var class2Count = FontBinary.UInt16(data, subtable + 14);
                    if (class1 >= FontBinary.UInt16(data, subtable + 12) || class2 >= class2Count)
                        return false;

                    record = subtable + 16 + (((class1 * class2Count) + class2) * (length1 + length2));
                    break;
                }

            default:
                return false;
        }

        ApplyValue(data, parent, record, format1, ref _buffer.Positions[index]);
        ApplyValue(data, parent, record + length1, format2, ref _buffer.Positions[second]);

        // The second glyph is the first one of the next pair, unless the lookup moved it.
        index = format2 != 0 ? second + 1 : second;
        return true;
    }

    /// <summary>
    /// Type 3: the exit point of a glyph is joined to the entry point of the next one, which is how cursive scripts
    /// connect and how a line of text climbs or descends.
    /// </summary>
    private bool ApplyCursiveAttachment(int subtable, ref int index)
    {
        var data = _data.Span;
        var info = _buffer.Info;
        if (FontBinary.UInt16(data, subtable) != 1)
            return false;

        var coverage = subtable + FontBinary.UInt16(data, subtable + 2);
        var count = FontBinary.UInt16(data, subtable + 4);
        var current = LayoutCommon.GetCoverage(data, coverage, info[index].Glyph);
        if (current < 0 || current >= count)
            return false;

        var entry = FontBinary.UInt16(data, subtable + 6 + (current * 4));
        if (entry == 0)
            return false;

        var previous = PreviousGlyph(index, _flag);
        if (previous < 0)
            return false;

        var previousCoverage = LayoutCommon.GetCoverage(data, coverage, info[previous].Glyph);
        if (previousCoverage < 0 || previousCoverage >= count)
            return false;

        var exit = FontBinary.UInt16(data, subtable + 8 + (previousCoverage * 4));
        if (exit == 0)
            return false;

        ReadAnchor(data, subtable + entry, out var entryX, out var entryY);
        ReadAnchor(data, subtable + exit, out var exitX, out var exitY);
        var positions = _buffer.Positions;
        ref var first = ref positions[previous];
        ref var second = ref positions[index];

        // Along the line: the pen is at the exit point after the first glyph, and the second glyph is drawn with its
        // entry point at the pen. In a right-to-left run the glyphs are in visual order, the second one on the left.
        if (_isRightToLeft)
        {
            var shift = exitX + first.XOffset;
            first.XAdvance -= shift;
            first.XOffset -= shift;
            second.XAdvance = entryX + second.XOffset;
        }
        else
        {
            first.XAdvance = exitX + first.XOffset;
            var shift = entryX + second.XOffset;
            second.XAdvance -= shift;
            second.XOffset -= shift;
        }

        // Across the line: one of the two glyphs follows the other. The flag of the lookup says which one stays in
        // place: by default the first glyph, and the second one moves; with the right-to-left flag, the last glyph of
        // a connected sequence stays on the baseline and the ones before it move.
        var child = index;
        var parent = previous;
        var offset = exitY - entryY;
        if ((_flag & LayoutLookup.RightToLeft) != 0)
        {
            (child, parent) = (parent, child);
            offset = -offset;
        }

        // A glyph follows one other glyph. A child that already followed a glyph leads it from now on: the links of
        // its old chain are reversed, so that the glyphs that were connected stay connected, through the child, to
        // its new parent.
        ReverseCursiveChain(positions, child, parent);
        ref var childPosition = ref positions[child];
        childPosition.AttachmentType = GlyphPosition.CursiveAttachment;
        childPosition.AttachmentChain = parent - child;
        childPosition.YOffset = offset;

        // If the parent followed the child, the two are separated, so that the chain never loops.
        ref var parentPosition = ref positions[parent];
        if (parentPosition.AttachmentChain == child - parent)
        {
            parentPosition.AttachmentChain = 0;
            parentPosition.AttachmentType = GlyphPosition.NoAttachment;
            parentPosition.YOffset = 0;
        }

        index++;
        return true;
    }

    /// <summary>
    /// Reverses the cursive links from a glyph to the root of its chain: each glyph the chain went through follows
    /// the one that followed it. The walk stops at the glyph that becomes the parent of the first one.
    /// </summary>
    private static void ReverseCursiveChain(Span<GlyphPosition> positions, int index, int newParent)
    {
        ref var first = ref positions[index];
        if (first.AttachmentChain == 0 || first.AttachmentType != GlyphPosition.CursiveAttachment)
            return;

        // The link of each glyph is put on the glyph it led to, the other way around. A glyph is visited once: the
        // walk ends at a glyph without link, and the first one has none from now on.
        var current = index;
        var chain = first.AttachmentChain;
        var offset = first.YOffset;
        first.AttachmentChain = 0;
        for (var steps = 0; chain != 0 && steps < positions.Length; steps++)
        {
            var next = current + chain;
            if (next == newParent || (uint)next >= (uint)positions.Length)
                return;

            ref var position = ref positions[next];
            var nextChain = position.AttachmentType == GlyphPosition.CursiveAttachment ? position.AttachmentChain : 0;
            var nextOffset = position.YOffset;
            position.YOffset = -offset;
            position.AttachmentChain = -chain;
            position.AttachmentType = GlyphPosition.CursiveAttachment;
            current = next;
            chain = nextChain;
            offset = nextOffset;
        }
    }

    /// <summary>Type 4: a mark is placed on an anchor of the base glyph that precedes it.</summary>
    private bool ApplyMarkToBase(int subtable, ref int index)
    {
        var data = _data.Span;
        var info = _buffer.Info;
        var mark = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), info[index].Glyph);
        if (FontBinary.UInt16(data, subtable) != 1 || mark < 0)
            return false;

        // The base is the nearest glyph before the mark that is not a mark. Of the glyphs one glyph was replaced by,
        // the first one carries the marks: the others are passed over, unless the font makes one of them a base of
        // the mark (a letter a substitution put a spacing glyph before).
        var baseCoverage = subtable + FontBinary.UInt16(data, subtable + 4);
        var target = index;
        int baseIndex;
        do
        {
            target = PreviousGlyph(target, LayoutLookup.IgnoreMarks);
            if (target < 0)
                return false;

            baseIndex = LayoutCommon.GetCoverage(data, baseCoverage, info[target].Glyph);
        }
        while (baseIndex < 0 && (info[target].Props & GlyphInfo.Multiplied) != 0 && GetSequenceRank(info[target]) > 0 && !IsFirstOfItsSequence(target));

        if (baseIndex < 0)
            return false;

        // The base array: for each base, one anchor per class of mark.
        var classCount = FontBinary.UInt16(data, subtable + 6);
        var bases = subtable + FontBinary.UInt16(data, subtable + 10);
        if (baseIndex >= FontBinary.UInt16(data, bases))
            return false;

        return Attach(subtable + FontBinary.UInt16(data, subtable + 8), mark, classCount, bases, bases + 2 + (baseIndex * classCount * 2), target, ref index);
    }

    /// <summary>
    /// Determines whether a glyph of a multiplied sequence has no glyph of the same sequence before it that could carry
    /// the marks: a mark before it does not.
    /// </summary>
    private bool IsFirstOfItsSequence(int index)
    {
        // The glyph before is the one before it in the sequence when it has the rank just before: a sequence that
        // lost a glyph in between (to a ligature) is two sequences.
        var info = _buffer.Info;
        return index == 0 || info[index - 1].IsMark || (info[index - 1].Props & GlyphInfo.Multiplied) == 0
            || info[index - 1].LigatureId != info[index].LigatureId || GetSequenceRank(info[index - 1]) + 1 != GetSequenceRank(info[index]);
    }

    /// <summary>Gets the rank of a glyph in the sequence that replaced one glyph, from 0; the glyphs of a ligature that was replaced have no rank.</summary>
    private static int GetSequenceRank(in GlyphInfo info) => IsLigatureItself(info) ? 0 : Math.Max(info.LigatureComponent - 1, 0);

    /// <summary>Type 5: a mark is placed on an anchor of the component of the ligature it follows.</summary>
    private bool ApplyMarkToLigature(int subtable, ref int index)
    {
        var data = _data.Span;
        var info = _buffer.Info;
        var mark = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), info[index].Glyph);
        if (FontBinary.UInt16(data, subtable) != 1 || mark < 0)
            return false;

        // The ligature is the nearest glyph before the mark that is not a mark. A glyph a substitution put after
        // another one (a spacing vowel sign a font moves after the marks of its consonant) is passed over: the first
        // glyph of a sequence carries the marks.
        var target = index;
        do
        {
            target = PreviousGlyph(target, LayoutLookup.IgnoreMarks);
            if (target < 0)
                return false;
        }
        while ((info[target].Props & GlyphInfo.Multiplied) != 0 && GetSequenceRank(info[target]) > 0);

        var ligatureIndex = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 4), info[target].Glyph);
        var ligatures = subtable + FontBinary.UInt16(data, subtable + 10);
        if (ligatureIndex < 0 || ligatureIndex >= FontBinary.UInt16(data, ligatures))
            return false;

        // The ligature: one row of anchors per component.
        var ligature = ligatures + FontBinary.UInt16(data, ligatures + 2 + (ligatureIndex * 2));
        var componentCount = FontBinary.UInt16(data, ligature);
        if (componentCount == 0)
            return false;

        // The mark goes on the component it followed before the ligature was formed; a mark that has no such record
        // goes on the last component.
        ref readonly var markInfo = ref info[index];
        ref readonly var ligatureInfo = ref info[target];
        var component = componentCount - 1;
        if (ligatureInfo.LigatureId != 0 && ligatureInfo.LigatureId == markInfo.LigatureId && markInfo.LigatureComponent > 0)
        {
            component = Math.Min(componentCount, markInfo.LigatureComponent) - 1;
        }

        var classCount = FontBinary.UInt16(data, subtable + 6);
        return Attach(subtable + FontBinary.UInt16(data, subtable + 8), mark, classCount, ligature, ligature + 2 + (component * classCount * 2), target, ref index);
    }

    /// <summary>Type 6: a mark is placed on an anchor of the mark that precedes it.</summary>
    private bool ApplyMarkToMark(int subtable, ref int index)
    {
        var data = _data.Span;
        var info = _buffer.Info;
        var mark = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 2), info[index].Glyph);
        if (FontBinary.UInt16(data, subtable) != 1 || mark < 0)
            return false;

        // The glyph before, among those the lookup does not ignore, must be a mark.
        var target = PreviousGlyph(index, _flag & ~LayoutLookup.IgnoreBaseGlyphs & ~LayoutLookup.IgnoreLigatures & ~LayoutLookup.IgnoreMarks);
        if (target < 0 || !info[target].IsMark)
            return false;

        // Two marks stack when they are on the same glyph: the same component of the same ligature, or no ligature at
        // all, or when one of them is itself the ligature of marks.
        ref readonly var first = ref info[target];
        ref readonly var second = ref info[index];
        var sameComponent = first.LigatureId == second.LigatureId
            ? first.LigatureId == 0 || first.LigatureComponent == second.LigatureComponent
            : (first.LigatureId != 0 && IsLigatureItself(first)) || (second.LigatureId != 0 && IsLigatureItself(second));
        if (!sameComponent)
            return false;

        var targetIndex = LayoutCommon.GetCoverage(data, subtable + FontBinary.UInt16(data, subtable + 4), first.Glyph);
        var classCount = FontBinary.UInt16(data, subtable + 6);
        var marks = subtable + FontBinary.UInt16(data, subtable + 10);
        if (targetIndex < 0 || targetIndex >= FontBinary.UInt16(data, marks))
            return false;

        return Attach(subtable + FontBinary.UInt16(data, subtable + 8), mark, classCount, marks, marks + 2 + (targetIndex * classCount * 2), target, ref index);
    }

    /// <summary>Places a mark so that its anchor is on an anchor of another glyph.</summary>
    /// <param name="markArray">The mark array: for each mark, its class and its anchor.</param>
    /// <param name="markIndex">The coverage index of the mark.</param>
    /// <param name="classCount">The number of classes of marks.</param>
    /// <param name="anchorBase">The table the anchor offsets of the other glyph are from.</param>
    /// <param name="anchors">The row of anchor offsets of the other glyph, one per class.</param>
    /// <param name="target">The glyph the mark is attached to.</param>
    /// <param name="index">The mark; the glyph after it when it is attached.</param>
    private bool Attach(int markArray, int markIndex, int classCount, int anchorBase, int anchors, int target, ref int index)
    {
        var data = _data.Span;
        if (markIndex >= FontBinary.UInt16(data, markArray))
            return false;

        var markClass = FontBinary.UInt16(data, markArray + 2 + (markIndex * 4));
        if (markClass >= classCount)
            return false;

        // A glyph has no anchor for a class of marks it does not carry.
        var anchor = FontBinary.UInt16(data, anchors + (markClass * 2));
        if (anchor == 0)
            return false;

        ReadAnchor(data, markArray + FontBinary.UInt16(data, markArray + 4 + (markIndex * 4)), out var markX, out var markY);
        ReadAnchor(data, anchorBase + anchor, out var targetX, out var targetY);

        // Horizontally, the offset is from the origin of the other glyph: the advances between the two glyphs are
        // taken out when the positions are completed, and the mark follows the other glyph if a later lookup moves
        // it. Vertically, the mark is placed now, from where the other glyph is at this point of the lookups, the
        // glyphs it is connected to included: a lookup works with the positions the lookups before it made, and a
        // lookup that moves the other glyph afterward leaves the mark where it is.
        ref var position = ref _buffer.Positions[index];
        position.XOffset = targetX - markX;
        position.YOffset = targetY - markY + GetVerticalOffset(target);
        position.AttachmentType = GlyphPosition.MarkAttachment;
        position.AttachmentChain = target - index;
        index++;
        return true;
    }

    /// <summary>
    /// Gets how far a glyph is from the baseline at this point of the lookups: its offset, and the offsets of the
    /// glyphs a cursive attachment connects it to. The offset of a mark is from the baseline already.
    /// </summary>
    private int GetVerticalOffset(int index)
    {
        var positions = _buffer.Positions;
        var offset = positions[index].YOffset;
        for (var depth = 0; depth < MaxSequenceLength; depth++)
        {
            ref readonly var position = ref positions[index];
            if (position.AttachmentChain == 0 || position.AttachmentType != GlyphPosition.CursiveAttachment)
                break;

            index += position.AttachmentChain;
            if ((uint)index >= (uint)positions.Length)
                break;

            offset += positions[index].YOffset;
        }

        return offset;
    }

    /// <summary>
    /// Reads an anchor: a point of a glyph, in font units. The three formats start with the coordinates; the contour
    /// point of the format 2 refines them for a hinted glyph, which there is not, and the device tables of the format 3
    /// give the deltas of the coordinates in a variable font.
    /// </summary>
    private void ReadAnchor(ReadOnlySpan<byte> data, int anchor, out int x, out int y)
    {
        x = FontBinary.Int16(data, anchor + 2);
        y = FontBinary.Int16(data, anchor + 4);
        if (_hasVariations && FontBinary.UInt16(data, anchor) == 3)
        {
            x += GetVariationDelta(data, anchor, FontBinary.UInt16(data, anchor + 6));
            y += GetVariationDelta(data, anchor, FontBinary.UInt16(data, anchor + 8));
        }
    }

    /// <summary>
    /// Completes the positions after the lookups: the offset of an attached glyph, which the lookups give from the
    /// glyph it is attached to, becomes an offset from the pen.
    /// </summary>
    /// <remarks>
    /// A glyph attached to the exit of another one (cursive attachment) is moved vertically with it, glyph after glyph
    /// along the word. A mark is moved horizontally with the glyph that carries it; vertically it was placed when it
    /// was attached.
    /// </remarks>
    public static void FinishPositions(Span<GlyphPosition> positions, bool isRightToLeft)
    {
        var hasAttachments = false;
        foreach (ref readonly var position in positions)
        {
            hasAttachments |= position.AttachmentChain != 0;
        }

        if (!hasAttachments)
            return;

        for (var i = 0; i < positions.Length; i++)
        {
            Resolve(positions, i, depth: 0, isRightToLeft);
        }

        static void Resolve(Span<GlyphPosition> positions, int index, int depth, bool isRightToLeft)
        {
            ref var position = ref positions[index];
            var chain = position.AttachmentChain;
            if (chain == 0)
                return;

            // The chain is consumed first: a glyph is resolved once, whatever refers to it.
            var type = position.AttachmentType;
            position.AttachmentChain = 0;
            var parent = index + chain;
            if ((uint)parent >= (uint)positions.Length || depth > MaxSequenceLength)
                return;

            Resolve(positions, parent, depth + 1, isRightToLeft);
            if (type == GlyphPosition.CursiveAttachment)
            {
                position.YOffset += positions[parent].YOffset;
                return;
            }

            position.XOffset += positions[parent].XOffset;

            // The pen has moved from the origin of the parent to the mark: by the advances of the glyphs from the
            // parent to the mark in a left-to-right run, where the parent is before the mark; back by the advances of
            // the glyphs from the mark to the parent in a right-to-left run, where the glyphs are reversed afterward.
            if (isRightToLeft)
            {
                for (var i = parent + 1; i <= index; i++)
                {
                    position.XOffset += positions[i].XAdvance;
                    position.YOffset += positions[i].YAdvance;
                }
            }
            else
            {
                for (var i = parent; i < index; i++)
                {
                    position.XOffset -= positions[i].XAdvance;
                    position.YOffset -= positions[i].YAdvance;
                }
            }
        }
    }
}
