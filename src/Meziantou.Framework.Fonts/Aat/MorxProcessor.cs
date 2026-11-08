namespace Meziantou.Framework.Fonts;

/// <summary>
/// Applies the subtables of a <c>morx</c> table to the glyphs of a run: Apple TrueType Reference Manual, "The 'morx'
/// table".
/// </summary>
/// <remarks>
/// <para>
/// A glyph a subtable deletes keeps its place, as the deleted glyph 0xFFFF, until every chain is applied: the
/// subtables after it see it as the class "deleted glyph". It leaves the buffer at the end, and its character joins the
/// cluster of a neighbor.
/// </para>
/// <para>
/// A glyph a subtable replaces keeps the properties of its character: a character without visible form is hidden
/// once the glyphs are positioned, whatever glyph the font gave it, as the reference shaper does.
/// </para>
/// <para>
/// A state machine visits the glyphs once each, and once more each time an entry says not to advance. A font can say
/// that without end: every visit is charged to the operation budget of the buffer.
/// </para>
/// </remarks>
internal static class MorxProcessor
{
    private const int Rearrangement = 0;
    private const int Contextual = 1;
    private const int Ligature = 2;
    private const int Noncontextual = 4;
    private const int Insertion = 5;

    private const int DontAdvance = 0x4000;
    private const int NoIndex = 0xFFFF;

    /// <summary>The components a ligature subtable remembers: the oldest are forgotten beyond it.</summary>
    private const int ComponentStackSize = 64;

    /// <summary>Applies the subtables of the chains whose flags are on.</summary>
    /// <param name="buffer">The glyphs, in the order of the characters.</param>
    /// <param name="face">The font.</param>
    /// <param name="morx">The table.</param>
    /// <param name="data">The data of the table.</param>
    /// <param name="chainFlags">The flags of each chain for the features of the run.</param>
    /// <param name="isRightToLeft"><see langword="true"/> when the glyphs are drawn from right to left: the order they are drawn in is the reverse of the order of the buffer.</param>
    /// <param name="isVertical"><see langword="true"/> for a vertical run.</param>
    /// <exception cref="InvalidFontException">A subtable is malformed, or asks for more work than a text can need.</exception>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void Apply(GlyphBuffer buffer, FontFace face, MorxTable morx, ReadOnlySpan<byte> data, ReadOnlySpan<uint> chainFlags, bool isRightToLeft, bool isVertical)
    {
        var hasDeletions = false;
        for (var c = 0; c < morx.Chains.Length; c++)
        {
            var flags = chainFlags[c];
            foreach (var subtable in morx.Chains[c].Subtables)
            {
                if ((subtable.Flags & flags) == 0)
                    continue;

                if (!subtable.IsAnyOrientation && subtable.IsVertical != isVertical)
                    continue;

                buffer.StartPass(1);

                // The glyph array of the specification is in the order the glyphs are drawn, which is the order of the
                // buffer for a left-to-right run; a subtable says whether it reads it that way or in the order of the
                // characters, then forward or backward.
                var reverse = subtable.IsDescending ^ (isRightToLeft && !subtable.IsLogicalOrder);
                if (reverse)
                {
                    buffer.Info.Reverse();
                }

                var content = FontBinary.Slice(data, subtable.Offset, subtable.Length);
                switch (subtable.Type)
                {
                    case Rearrangement:
                        ApplyRearrangement(buffer, content, face.GlyphCount);
                        break;

                    case Contextual:
                        ApplyContextual(buffer, content, face.GlyphCount);
                        break;

                    case Ligature:
                        hasDeletions |= ApplyLigature(buffer, content, face.GlyphCount);
                        break;

                    case Noncontextual:
                        ApplyNoncontextual(buffer, content, face.GlyphCount);
                        break;

                    case Insertion:
                        ApplyInsertion(buffer, content, face.GlyphCount);
                        break;
                }

                if (reverse)
                {
                    buffer.Info.Reverse();
                }

                // A contextual or non-contextual substitution can delete a glyph too, by replacing it with 0xFFFF.
                hasDeletions = hasDeletions || HasDeletedGlyphs(buffer.Info);
            }
        }

        if (hasDeletions)
        {
            RemoveDeletedGlyphs(buffer);
        }
    }

    /// <summary>"Indic rearrangement": moves the first one or two, and the last one or two, glyphs of a marked range.</summary>
    private static void ApplyRearrangement(GlyphBuffer buffer, ReadOnlySpan<byte> table, int glyphCount)
    {
        const int MarkFirst = 0x8000;
        const int MarkLast = 0x2000;

        var states = new AatStateTable(table, 4, glyphCount);
        var state = AatStateTable.StartOfText;
        int start = 0, end = 0;
        for (var i = 0; ;)
        {
            buffer.Charge(1);
            var length = buffer.Length;
            var glyphClass = i < length ? states.GetClass(buffer.Info[i].Glyph) : AatStateTable.EndOfText;
            var entry = states.GetEntry(state, glyphClass);
            var flags = FontBinary.UInt16(entry, 2);
            if ((flags & MarkFirst) != 0)
            {
                start = Math.Min(i, length);
            }

            if ((flags & MarkLast) != 0)
            {
                end = Math.Min(i + 1, length);
            }

            var verb = flags & 0x0F;
            if (verb != 0 && start < end)
            {
                Rearrange(buffer, start, end, verb, Math.Min(Math.Max(end, i + 1), length));
            }

            state = FontBinary.UInt16(entry, 0);
            if (i >= length)
                break;

            if ((flags & DontAdvance) == 0)
            {
                i++;
            }
        }
    }

    /// <summary>Applies a verb of rearrangement to a range: A and B are the first glyphs, C and D the last ones, x what is between.</summary>
    /// <param name="buffer">The glyphs.</param>
    /// <param name="start">The first glyph of the range.</param>
    /// <param name="end">The glyph after the range.</param>
    /// <param name="verb">The verb.</param>
    /// <param name="clusterEnd">
    /// The glyph after the glyphs whose clusters are merged: the reference shaper ties the glyphs from the start of the
    /// range to the current glyph, which the range may not reach.
    /// </param>
    private static void Rearrange(GlyphBuffer buffer, int start, int end, int verb, int clusterEnd)
    {
        // The glyphs taken at the start and at the end, and whether each group is reversed.
        var (left, right, reverseLeft, reverseRight) = verb switch
        {
            1 => (1, 0, false, false), // Ax => xA
            2 => (0, 1, false, false), // xD => Dx
            3 => (1, 1, false, false), // AxD => DxA
            4 => (2, 0, false, false), // ABx => xAB
            5 => (2, 0, true, false), // ABx => xBA
            6 => (0, 2, false, false), // xCD => CDx
            7 => (0, 2, false, true), // xCD => DCx
            8 => (1, 2, false, false), // AxCD => CDxA
            9 => (1, 2, false, true), // AxCD => DCxA
            10 => (2, 1, false, false), // ABxD => DxAB
            11 => (2, 1, true, false), // ABxD => DxBA
            12 => (2, 2, false, false), // ABxCD => CDxAB
            13 => (2, 2, true, false), // ABxCD => CDxBA
            14 => (2, 2, false, true), // ABxCD => DCxAB
            _ => (2, 2, true, true), // ABxCD => DCxBA
        };

        var count = end - start;
        if (left + right > count)
            return;

        buffer.MergeClusters(start, clusterEnd);
        var info = buffer.Info.Slice(start, count);
        Span<GlyphInfo> first = stackalloc GlyphInfo[2];
        Span<GlyphInfo> last = stackalloc GlyphInfo[2];
        info[..left].CopyTo(first);
        info[(count - right)..].CopyTo(last);
        if (reverseLeft)
        {
            first[..left].Reverse();
        }

        if (reverseRight)
        {
            last[..right].Reverse();
        }

        // The middle moves to after the last glyphs, then the first glyphs go to the end.
        info.Slice(left, count - left - right).CopyTo(info[right..]);
        last[..right].CopyTo(info);
        first[..left].CopyTo(info[(count - left)..]);
    }

    /// <summary>"Contextual glyph substitution": replaces the current glyph and the marked one through lookup tables.</summary>
    private static void ApplyContextual(GlyphBuffer buffer, ReadOnlySpan<byte> table, int glyphCount)
    {
        const int SetMark = 0x8000;

        var states = new AatStateTable(table, 8, glyphCount);
        var substitutions = FontBinary.Offset32(table, AatStateTable.HeaderLength);
        var state = AatStateTable.StartOfText;

        // The mark is on the first glyph until a glyph is marked, as in CoreText, for which the fonts are made; at the
        // end of the glyphs, an entry acts only once a glyph was marked.
        var mark = 0;
        var isMarkSet = false;
        for (var i = 0; ;)
        {
            buffer.Charge(1);
            var length = buffer.Length;
            var info = buffer.Info;
            var isEnd = i >= length;
            var glyphClass = isEnd ? AatStateTable.EndOfText : states.GetClass(info[i].Glyph);
            var entry = states.GetEntry(state, glyphClass);
            var flags = FontBinary.UInt16(entry, 2);
            var markIndex = FontBinary.UInt16(entry, 4);
            var currentIndex = FontBinary.UInt16(entry, 6);
            if (!isEnd || isMarkSet)
            {
                if (markIndex != NoIndex && mark < length)
                {
                    Substitute(table, substitutions, markIndex, ref info[mark], glyphCount);
                    buffer.MarkUnsafeToBreak(mark, i + 1);
                }

                // At the end of the glyphs, the current glyph is the last one.
                var current = Math.Min(i, length - 1);
                if (currentIndex != NoIndex && current >= 0)
                {
                    Substitute(table, substitutions, currentIndex, ref info[current], glyphCount);
                }
            }

            if ((flags & SetMark) != 0 && !isEnd)
            {
                mark = i;
                isMarkSet = true;
            }

            state = FontBinary.UInt16(entry, 0);
            if (isEnd)
                break;

            if ((flags & DontAdvance) == 0)
            {
                i++;
            }
        }
    }

    /// <summary>Replaces a glyph through one of the lookup tables of a contextual subtable.</summary>
    private static void Substitute(ReadOnlySpan<byte> table, int substitutions, int index, ref GlyphInfo info, int glyphCount)
    {
        var offset = FontBinary.Offset32(table, FontBinary.AddOffset(substitutions, index * 4));
        var lookup = FontBinary.Slice(table, (long)substitutions + offset, table.Length - ((long)substitutions + offset));

        // "A value of 0 returned from a table lookup [means] make no substitution."
        if (AatLookup.TryGetValue(lookup, info.Glyph, glyphCount, out var glyph) && glyph != 0)
        {
            info.Glyph = glyph;
        }
    }

    /// <summary>"Non-contextual glyph substitution": replaces every glyph the lookup table has.</summary>
    private static void ApplyNoncontextual(GlyphBuffer buffer, ReadOnlySpan<byte> table, int glyphCount)
    {
        AatLookup.Validate(table, glyphCount);
        buffer.Charge(buffer.Length);
        foreach (ref var info in buffer.Info)
        {
            if (AatLookup.TryGetValue(table, info.Glyph, glyphCount, out var glyph) && glyph != 0)
            {
                info.Glyph = glyph;
            }
        }
    }

    /// <summary>"Ligature subtables": the glyphs pushed on a stack are replaced by ligatures from the components they make.</summary>
    /// <returns><see langword="true"/> when glyphs were deleted.</returns>
    private static bool ApplyLigature(GlyphBuffer buffer, ReadOnlySpan<byte> table, int glyphCount)
    {
        const int SetComponent = 0x8000;
        const int PerformAction = 0x2000;

        var states = new AatStateTable(table, 6, glyphCount);
        var actions = FontBinary.Offset32(table, AatStateTable.HeaderLength);
        var components = FontBinary.Offset32(table, AatStateTable.HeaderLength + 4);
        var ligatures = FontBinary.Offset32(table, AatStateTable.HeaderLength + 8);
        Span<int> stack = stackalloc int[ComponentStackSize];
        var stackCount = 0;
        var stackTop = 0;
        var hasDeletions = false;
        var state = AatStateTable.StartOfText;
        for (var i = 0; ;)
        {
            buffer.Charge(1);
            var length = buffer.Length;
            var glyphClass = i < length ? states.GetClass(buffer.Info[i].Glyph) : AatStateTable.EndOfText;
            var entry = states.GetEntry(state, glyphClass);
            var flags = FontBinary.UInt16(entry, 2);
            if ((flags & SetComponent) != 0 && i < length)
            {
                // A glyph visited again without advancing is pushed once.
                if (stackCount == 0 || stack[(stackTop + ComponentStackSize - 1) % ComponentStackSize] != i)
                {
                    stack[stackTop] = i;
                    stackTop = (stackTop + 1) % ComponentStackSize;
                    stackCount = Math.Min(stackCount + 1, ComponentStackSize);
                }
            }

            if ((flags & PerformAction) != 0)
            {
                hasDeletions |= PerformLigatureAction(buffer, table, actions, components, ligatures, FontBinary.UInt16(entry, 4), stack, ref stackTop, ref stackCount);
            }

            state = FontBinary.UInt16(entry, 0);
            if (i >= length)
                break;

            if ((flags & DontAdvance) == 0)
            {
                i++;
            }
        }

        return hasDeletions;
    }

    /// <summary>Pops the components of a ligature, adds their entries of the component table, and stores the ligatures the actions say.</summary>
    /// <remarks>
    /// An action that pops more components than the stack has, or whose indexes are outside the component table or
    /// the ligature list, changes nothing: CoreText leaves the glyphs as they are, and so does the reference shaper.
    /// </remarks>
    private static bool PerformLigatureAction(GlyphBuffer buffer, ReadOnlySpan<byte> table, int actions, int components, int ligatures, int actionIndex, Span<int> stack, ref int stackTop, ref int stackCount)
    {
        const uint Last = 0x80000000;
        const uint Store = 0x40000000;

        var info = buffer.Info;
        Span<(int Position, int Glyph)> changes = stackalloc (int, int)[ComponentStackSize];
        var changeCount = 0;
        long index = 0;
        long action = actions + ((long)actionIndex * 4);
        var isComplete = false;
        while (stackCount > 0)
        {
            buffer.Charge(1);
            stackTop = (stackTop + ComponentStackSize - 1) % ComponentStackSize;
            stackCount--;
            var position = stack[stackTop];
            if (!TryReadUInt32(table, action, out var value))
                return false;

            action += 4;

            // "A 30-bit value which is sign-extended to 32-bits and added to the glyph ID, resulting in an index into
            // the component table."
            var offset = (int)(value << 2) >> 2;
            if (!TryReadUInt16(table, components + (((long)info[position].Glyph + offset) * 2), out var component))
                return false;

            index += component;
            var glyph = AatStateTable.Deleted;
            if ((value & (Last | Store)) != 0)
            {
                if (!TryReadUInt16(table, ligatures + (index * 2), out glyph))
                    return false;

                index = 0;
            }

            changes[changeCount++] = (position, glyph);
            if ((value & Last) != 0)
            {
                isComplete = true;
                break;
            }
        }

        if (!isComplete)
            return false;

        var first = int.MaxValue;
        var lastPosition = -1;
        var hasDeletions = false;
        for (var i = changeCount - 1; i >= 0; i--)
        {
            var (position, glyph) = changes[i];
            first = Math.Min(first, position);
            lastPosition = Math.Max(lastPosition, position);
            info[position].Glyph = glyph;
            if (glyph == AatStateTable.Deleted)
            {
                hasDeletions = true;
                continue;
            }


            // The ligature is a component again, for the ligatures it is part of.
            stack[stackTop] = position;
            stackTop = (stackTop + 1) % ComponentStackSize;
            stackCount = Math.Min(stackCount + 1, ComponentStackSize);
        }

        buffer.MergeClusters(first, lastPosition + 1);
        return hasDeletions;

        static bool TryReadUInt16(ReadOnlySpan<byte> data, long position, out int value)
        {
            value = 0;
            if (position < 0 || position + 2 > data.Length)
                return false;

            value = FontBinary.UInt16(data, (int)position);
            return true;
        }

        static bool TryReadUInt32(ReadOnlySpan<byte> data, long position, out uint value)
        {
            value = 0;
            if (position < 0 || position + 4 > data.Length)
                return false;

            value = FontBinary.UInt32(data, (int)position);
            return true;
        }
    }

    /// <summary>"Glyph insertion subtable": inserts glyphs of a list before or after the current glyph and the marked one.</summary>
    private static void ApplyInsertion(GlyphBuffer buffer, ReadOnlySpan<byte> table, int glyphCount)
    {
        const int SetMark = 0x8000;
        const int CurrentInsertBefore = 0x0800;
        const int MarkedInsertBefore = 0x0400;

        var states = new AatStateTable(table, 8, glyphCount);
        var glyphs = FontBinary.Offset32(table, AatStateTable.HeaderLength);
        var state = AatStateTable.StartOfText;

        // The mark is on the first glyph until a glyph is marked, and a mark is the place the glyph had before the
        // insertions of the entry that marks it: the behavior of CoreText, which the fonts are made for (and which the
        // reference shaper reproduces).
        var mark = 0;
        for (var i = 0; ;)
        {
            buffer.Charge(1);
            var isEnd = i >= buffer.Length;
            var glyphClass = isEnd ? AatStateTable.EndOfText : states.GetClass(buffer.Info[i].Glyph);
            var entry = states.GetEntry(state, glyphClass);
            var flags = FontBinary.UInt16(entry, 2);
            var currentInsert = FontBinary.UInt16(entry, 4);
            var markedInsert = FontBinary.UInt16(entry, 6);

            // The place of the current glyph, which moves when glyphs are inserted before it.
            var current = i;
            var markedCount = flags & 0x1F;
            if (markedInsert != NoIndex && markedCount > 0 && mark < buffer.Length)
            {
                var at = (flags & MarkedInsertBefore) != 0 ? mark : mark + 1;
                Insert(buffer, table, glyphs, markedInsert, markedCount, at, mark);
                current += at <= current ? markedCount : 0;
            }

            var next = current;
            var currentCount = (flags >> 5) & 0x1F;
            var isBefore = (flags & CurrentInsertBefore) != 0;
            var inserted = 0;
            if (currentInsert != NoIndex && currentCount > 0 && buffer.Length > 0)
            {
                var at = isBefore || isEnd ? current : current + 1;
                Insert(buffer, table, glyphs, currentInsert, currentCount, at, Math.Min(current, buffer.Length - 1));
                inserted = currentCount;
                if (isBefore)
                {
                    // "If you've made insertions immediately downstream of the current glyph, the next glyph processed
                    // would in fact be the first one inserted."
                    next = at;
                    current += currentCount;
                }

            }

            if ((flags & SetMark) != 0 && !isEnd)
            {
                mark = i;
            }

            state = FontBinary.UInt16(entry, 0);
            if (isEnd)
                break;

            // The inserted glyphs are not visited when the current glyph advances.
            i = (flags & DontAdvance) != 0 ? next : current + 1 + (isBefore ? 0 : inserted);
        }
    }

    /// <summary>Inserts glyphs of the insertion list, in the cluster of the glyph they are inserted at.</summary>
    private static void Insert(GlyphBuffer buffer, ReadOnlySpan<byte> table, int glyphs, int index, int count, int at, int target)
    {
        buffer.Charge(count);
        var start = FontBinary.AddOffset(glyphs, index * 2);
        FontBinary.EnsureRange(table, start, (long)count * 2);
        var template = target >= 0 && target < buffer.Length ? buffer.Info[target] : default;
        buffer.Resize(at, 0, count);
        var info = buffer.Info;
        for (var i = 0; i < count; i++)
        {
            ref var glyph = ref info[at + i];
            glyph = template;
            glyph.Glyph = FontBinary.UInt16(table, start + (i * 2));
            glyph.CodePoint = 0;
            glyph.Flags = (byte)(template.Flags & GlyphInfo.UnsafeToBreak);
            glyph.Props = GlyphInfo.BaseGlyph;
            glyph.Category = UnicodeCategory.OtherLetter;
        }
    }

    private static bool HasDeletedGlyphs(ReadOnlySpan<GlyphInfo> info)
    {
        foreach (ref readonly var glyph in info)
        {
            if (glyph.Glyph == AatStateTable.Deleted)
                return true;
        }

        return false;
    }

    /// <summary>Removes the deleted glyphs; the characters of each join the cluster of a neighbor.</summary>
    private static void RemoveDeletedGlyphs(GlyphBuffer buffer)
    {
        var info = buffer.Info;
        var write = 0;
        var pendingCluster = int.MaxValue;
        for (var read = 0; read < info.Length; read++)
        {
            if (info[read].Glyph == AatStateTable.Deleted)
            {
                // The characters of a deleted glyph belong to the cluster before it, or to the next one at the start.
                if (write == 0)
                {
                    pendingCluster = Math.Min(pendingCluster, info[read].Cluster);
                }

                continue;
            }

            info[write++] = info[read];
        }

        if (write == info.Length)
            return;

        if (write > 0 && pendingCluster < info[0].Cluster)
        {
            var cluster = info[0].Cluster;
            for (var i = 0; i < write && info[i].Cluster == cluster; i++)
            {
                info[i].Cluster = pendingCluster;
            }
        }

        buffer.Resize(write, info.Length - write, 0);
    }
}
