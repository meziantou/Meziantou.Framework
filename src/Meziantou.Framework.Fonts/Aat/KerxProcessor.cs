namespace Meziantou.Framework.Fonts;

/// <summary>
/// Applies the subtables of a <c>kerx</c> table to the positions of a run: Apple TrueType Reference Manual, "The 'kerx'
/// table".
/// </summary>
/// <remarks>
/// The subtables see the glyphs in the order they are drawn, from the left (or from the top), as the specification
/// describes the glyph array; the adjustments are added in that order, and an attachment of format 4 is resolved when
/// it is made, from the advances the glyphs have at that point.
/// </remarks>
internal static class KerxProcessor
{
    private const int DontAdvance = 0x4000;
    private const int NoIndex = 0xFFFF;

    /// <summary>The glyphs the kerning stack of format 1 holds.</summary>
    private const int KerningStackSize = 8;

    /// <summary>Applies the subtables of the orientation of a run.</summary>
    /// <param name="buffer">The glyphs and their positions, in the order they are drawn.</param>
    /// <param name="face">The font.</param>
    /// <param name="isVertical"><see langword="true"/> for a vertical run.</param>
    /// <exception cref="InvalidFontException">A subtable is malformed, or asks for more work than a text can need.</exception>
    public static void Apply(GlyphBuffer buffer, FontFace face, bool isVertical)
    {
        var kerx = face.Kerx!;
        var data = face.KerxData;
        foreach (var subtable in kerx.Subtables)
        {
            if (subtable.IsVertical != isVertical || subtable.TupleCount != 0 || (subtable.Coverage & 0x20000000) != 0)
                continue;

            buffer.StartPass(1);
            var table = data.Slice(subtable.Offset, subtable.Length);
            var reverse = subtable.IsBackward && subtable.Format is 1 or 4;
            if (reverse)
            {
                buffer.Reverse();
            }

            // A cross-stream adjustment moves the glyph and every glyph after it, in the order they are drawn: it is
            // a shift of the line, which the glyphs of the subtable add up.
            var shifts = subtable.IsCrossStream && subtable.Format != 4 ? new int[buffer.Length] : null;
            switch (subtable.Format)
            {
                case 0 or 2 or 6:
                    ApplyPairs(buffer, face, table, subtable, isVertical, shifts);
                    break;

                case 1:
                    ApplyContextual(buffer, face, table, isVertical, shifts);
                    break;

                case 4:
                    ApplyAttachments(buffer, face, table, isVertical, reverse);
                    break;
            }

            if (shifts is not null)
            {
                ShiftLine(buffer.Positions, shifts, isVertical, reverse);
            }

            if (reverse)
            {
                buffer.Reverse();
            }
        }
    }

    /// <summary>Adds the cross-stream shifts of a subtable to the glyphs they apply to and to the glyphs after them.</summary>
    private static void ShiftLine(Span<GlyphPosition> positions, int[] shifts, bool isVertical, bool isReversed)
    {
        var shift = 0;
        for (var k = 0; k < positions.Length; k++)
        {
            var i = isReversed ? positions.Length - 1 - k : k;
            shift += shifts[i];
            if (isVertical)
            {
                positions[i].XOffset += shift;
            }
            else
            {
                positions[i].YOffset += shift;
            }
        }
    }

    /// <summary>Kerns the pairs of glyphs that follow each other: format 0 (sorted pairs), 2 and 6 (arrays of classes).</summary>
    private static void ApplyPairs(GlyphBuffer buffer, FontFace face, ReadOnlySpan<byte> table, KerxTable.Subtable subtable, bool isVertical, int[]? shifts)
    {
        var info = buffer.Info;
        var positions = buffer.Positions;
        buffer.Charge(info.Length);
        for (var i = 0; i + 1 < info.Length; i++)
        {
            // The glyphs of a pair are the visible ones: marks and invisible glyphs do not come between them.
            if (info[i].IsMark || info[i].IsIgnorable)
                continue;

            var next = i + 1;
            while (next < info.Length && (info[next].IsMark || info[next].IsIgnorable))
            {
                next++;
            }

            if (next >= info.Length)
                break;

            var value = subtable.Format switch
            {
                0 => GetFormat0Value(table, info[i].Glyph, info[next].Glyph),
                2 => GetFormat2Value(table, face.GlyphCount, info[i].Glyph, info[next].Glyph),
                _ => GetFormat6Value(table, face.GlyphCount, info[i].Glyph, info[next].Glyph),
            };

            if (value == 0)
                continue;

            if (shifts is not null)
            {
                shifts[next] += value;
            }
            else if (isVertical)
            {
                positions[i].YAdvance -= value;
            }
            else
            {
                // As for the legacy kerning table: half of the adjustment is taken from the advance of the first glyph,
                // the other half from the second one, which is moved back by it.
                var first = value >> 1;
                var second = value - first;
                positions[i].XAdvance += first;
                positions[next].XAdvance += second;
                positions[next].XOffset += second;
            }

            buffer.MarkUnsafeToBreak(i, next + 1);
        }
    }

    private static int GetFormat0Value(ReadOnlySpan<byte> table, int left, int right)
    {
        const int Pairs = KerxTable.Subtable.HeaderLength + 16;

        var count = FontBinary.Offset32(table, KerxTable.Subtable.HeaderLength);
        FontBinary.EnsureRange(table, Pairs, (long)count * 6);
        var key = ((uint)left << 16) | (uint)right;
        int low = 0, high = count - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) >> 1);
            var position = Pairs + (middle * 6);
            var current = FontBinary.UInt32(table, position);
            if (current == key)
                return FontBinary.Int16(table, position + 4);

            if (current < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return 0;
    }

    private static int GetFormat2Value(ReadOnlySpan<byte> table, int glyphCount, int left, int right)
    {
        const int Header = KerxTable.Subtable.HeaderLength;

        // The values of the class tables are offsets in bytes: the left one of a row from the start of the array
        // ("pre-multiplied by the number of bytes in one row"), the right one of a value in the row ("pre-multiplied by
        // the number of bytes in a single kerning value"). The reference shaper reads them as indexes of values, which
        // the fonts of Apple systems contradict: their row 0, which has no kerning, would be read as other rows.
        var leftOffset = GetClass(table, FontBinary.Offset32(table, Header + 4), glyphCount, left);
        var rightOffset = GetClass(table, FontBinary.Offset32(table, Header + 8), glyphCount, right);
        if (leftOffset <= 0 && rightOffset <= 0)
            return 0;

        var position = (long)FontBinary.Offset32(table, Header + 12) + Math.Max(leftOffset, 0) + Math.Max(rightOffset, 0);
        return FontBinary.Int16(FontBinary.Slice(table, position, 2), 0);
    }

    private static int GetFormat6Value(ReadOnlySpan<byte> table, int glyphCount, int left, int right)
    {
        const int Header = KerxTable.Subtable.HeaderLength;

        var flags = FontBinary.UInt32(table, Header);
        var isLong = (flags & 1) != 0;
        var rowCount = FontBinary.UInt16(table, Header + 4);
        var columnCount = FontBinary.UInt16(table, Header + 6);
        var rows = FontBinary.Offset32(table, Header + 8);
        var columns = FontBinary.Offset32(table, Header + 12);
        var array = FontBinary.Offset32(table, Header + 16);
        var size = isLong ? 4 : 2;
        if (!TryGetValue(table, rows, glyphCount, left, size, out var row) || !TryGetValue(table, columns, glyphCount, right, size, out var column))
            return 0;

        var index = (long)row + column;
        if (index >= (long)rowCount * columnCount)
            return 0;

        var position = array + (index * size);
        return isLong ? FontBinary.Int32(FontBinary.Slice(table, position, 4), 0) : FontBinary.Int16(FontBinary.Slice(table, position, 2), 0);
    }

    /// <summary>Gets the value a class table gives to a glyph, or -1 when it has none.</summary>
    private static int GetClass(ReadOnlySpan<byte> table, int offset, int glyphCount, int glyph)
    {
        var lookup = FontBinary.Slice(table, offset, table.Length - (long)offset);
        return AatLookup.TryGetValue(lookup, glyph, glyphCount, out var value) ? value : -1;
    }

    private static bool TryGetValue(ReadOnlySpan<byte> table, int offset, int glyphCount, int glyph, int size, out long value)
    {
        var lookup = FontBinary.Slice(table, offset, table.Length - (long)offset);
        var result = AatLookup.TryGetValue(lookup, glyph, glyphCount, size, out var wide);
        value = (long)wide;
        return result;
    }

    /// <summary>Format 1: a state machine pushes glyphs on a stack, and a list of values kerns the glyphs it pops.</summary>
    private static void ApplyContextual(GlyphBuffer buffer, FontFace face, ReadOnlySpan<byte> table, bool isVertical, int[]? shifts)
    {
        const int Push = 0x8000;
        const int Reset = 0x2000;

        var states = new AatStateTable(table[KerxTable.Subtable.HeaderLength..], 6, face.GlyphCount);
        var values = FontBinary.Offset32(table, KerxTable.Subtable.HeaderLength + AatStateTable.HeaderLength);
        var stateTable = states.Data;
        Span<int> stack = stackalloc int[KerningStackSize];
        var stackCount = 0;
        var state = AatStateTable.StartOfText;
        var info = buffer.Info;
        var positions = buffer.Positions;
        for (var i = 0; ;)
        {
            buffer.Charge(1);
            var glyphClass = i < info.Length ? states.GetClass(info[i].Glyph) : AatStateTable.EndOfText;
            var entry = states.GetEntry(state, glyphClass);
            var flags = FontBinary.UInt16(entry, 2);
            var valueIndex = FontBinary.UInt16(entry, 4);
            if ((flags & Reset) != 0)
            {
                stackCount = 0;
            }

            if ((flags & Push) != 0 && i < info.Length)
            {
                if (stackCount == KerningStackSize)
                {
                    stack[..^1].CopyTo(stack);
                    stackCount--;
                }

                stack[stackCount++] = i;
            }

            if (valueIndex != NoIndex)
            {
                var position = FontBinary.AddOffset(values, valueIndex * 2);
                while (stackCount > 0)
                {
                    buffer.Charge(1);
                    var value = FontBinary.UInt16(stateTable, position);
                    position += 2;
                    if (value == NoIndex)
                        break;

                    var glyph = stack[--stackCount];
                    if (shifts is not null)
                    {
                        shifts[glyph] += (short)value;
                    }
                    else
                    {
                        Kern(positions, glyph, (short)value, isVertical);
                    }
                }
            }

            state = FontBinary.UInt16(entry, 0);
            if (i >= info.Length)
                break;

            if ((flags & DontAdvance) == 0)
            {
                i++;
            }
        }
    }

    /// <summary>Adds a value of format 1 to the place of a glyph: it moves the glyph and the glyphs after it.</summary>
    private static void Kern(Span<GlyphPosition> positions, int index, int value, bool isVertical)
    {
        ref var position = ref positions[index];
        if (isVertical)
        {
            position.YAdvance -= value;
            position.YOffset -= value;
        }
        else
        {
            position.XAdvance += value;
            position.XOffset += value;
        }
    }

    /// <summary>Format 4: a glyph is placed so that one of its points is on a point of the marked glyph.</summary>
    private static void ApplyAttachments(GlyphBuffer buffer, FontFace face, ReadOnlySpan<byte> table, bool isVertical, bool isReversed)
    {
        const int Mark = 0x8000;

        var states = new AatStateTable(table[KerxTable.Subtable.HeaderLength..], 6, face.GlyphCount);
        var flagsField = FontBinary.UInt32(table, KerxTable.Subtable.HeaderLength + AatStateTable.HeaderLength);
        var actionType = (int)(flagsField >> 30);
        var points = (int)(flagsField & 0x00FFFFFF);
        var stateTable = states.Data;
        var info = buffer.Info;
        var positions = buffer.Positions;
        var mark = -1;
        var state = AatStateTable.StartOfText;
        for (var i = 0; ;)
        {
            buffer.Charge(1);
            var glyphClass = i < info.Length ? states.GetClass(info[i].Glyph) : AatStateTable.EndOfText;
            var entry = states.GetEntry(state, glyphClass);
            var flags = FontBinary.UInt16(entry, 2);
            var actionIndex = FontBinary.UInt16(entry, 4);
            if (actionIndex != NoIndex && mark >= 0 && i < info.Length && TryGetPoints(face, stateTable, points, actionType, actionIndex, info[mark].Glyph, info[i].Glyph, out var dx, out var dy))
            {
                Attach(positions, mark, i, dx, dy, isVertical, isReversed);
                buffer.MarkUnsafeToBreak(Math.Min(mark, i), Math.Max(mark, i) + 1);
            }

            if ((flags & Mark) != 0 && i < info.Length)
            {
                mark = i;
            }

            state = FontBinary.UInt16(entry, 0);
            if (i >= info.Length)
                break;

            if ((flags & DontAdvance) == 0)
            {
                i++;
            }
        }
    }

    /// <summary>Gets the vector from the point of the marked glyph to the point of the current glyph that an action names.</summary>
    /// <returns><see langword="false"/> when the action names the control points of the outlines, which are not read, or an anchor the glyphs do not have.</returns>
    private static bool TryGetPoints(FontFace face, ReadOnlySpan<byte> table, int points, int actionType, int actionIndex, int markGlyph, int currentGlyph, out int dx, out int dy)
    {
        const int AnchorPoints = 1;
        const int Coordinates = 2;

        dx = dy = 0;
        if (actionType == Coordinates)
        {
            var action = FontBinary.AddOffset(points, actionIndex * 8);
            dx = FontBinary.Int16(table, action) - FontBinary.Int16(table, action + 4);
            dy = FontBinary.Int16(table, action + 2) - FontBinary.Int16(table, action + 6);
            return true;
        }

        if (actionType != AnchorPoints)
            return false;

        var anchors = FontBinary.AddOffset(points, actionIndex * 4);
        if (!TryGetAnchor(face, markGlyph, FontBinary.UInt16(table, anchors), out var markX, out var markY) || !TryGetAnchor(face, currentGlyph, FontBinary.UInt16(table, anchors + 2), out var currentX, out var currentY))
            return false;

        dx = markX - currentX;
        dy = markY - currentY;
        return true;
    }

    /// <summary>Places a glyph so that a vector from the origin of the marked glyph is a vector from its origin.</summary>
    private static void Attach(Span<GlyphPosition> positions, int mark, int current, int dx, int dy, bool isVertical, bool isReversed)
    {
        // The pen moves from the marked glyph to the current one by the advances between them, in the order they are
        // drawn.
        int penX = 0, penY = 0;
        var (from, to) = isReversed ? (current + 1, mark + 1) : (mark, current);
        for (var k = from; k < to; k++)
        {
            penX += positions[k].XAdvance;
            penY += positions[k].YAdvance;
        }

        if (isReversed)
        {
            penX = -penX;
            penY = -penY;
        }

        ref var position = ref positions[current];
        position.XOffset = positions[mark].XOffset + dx - (isVertical ? 0 : penX);
        position.YOffset = positions[mark].YOffset + dy - (isVertical ? penY : 0);
    }

    /// <summary>Gets an anchor point of a glyph from the <c>ankr</c> table.</summary>
    private static bool TryGetAnchor(FontFace face, int glyph, int index, out int x, out int y)
    {
        x = y = 0;
        var ankr = face.AnkrData;
        if (ankr.Length < 12)
            return false;

        var lookupOffset = FontBinary.Offset32(ankr, 4);
        var glyphData = FontBinary.Offset32(ankr, 8);
        if (!AatLookup.TryGetValue(FontBinary.Slice(ankr, lookupOffset, ankr.Length - (long)lookupOffset), glyph, face.GlyphCount, out var offset))
            return false;

        var data = FontBinary.AddOffset(glyphData, offset);
        var count = FontBinary.Offset32(ankr, data);
        if (index >= count)
            return false;

        x = FontBinary.Int16(ankr, data + 4 + (index * 4));
        y = FontBinary.Int16(ankr, data + 6 + (index * 4));
        return true;
    }
}
