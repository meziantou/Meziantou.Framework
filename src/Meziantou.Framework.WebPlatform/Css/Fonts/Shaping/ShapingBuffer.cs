using System.Buffers;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The direction of a <see cref="ShapingBuffer"/> (HarfBuzz's <c>hb_direction_t</c>).</summary>
internal enum ShapingDirection : byte
{
    LeftToRight,
    RightToLeft,

    /// <summary>Vertical text, from top to bottom (the upright glyphs of vertical text).</summary>
    TopToBottom,
}

/// <summary>The flags of a <see cref="ShapingBuffer"/> set while it is shaped (HarfBuzz's <c>HB_BUFFER_SCRATCH_FLAG_*</c>).</summary>
[Flags]
internal enum ShapingScratchFlags
{
    None = 0,
    HasDefaultIgnorables = 0x1,
    HasSpaceFallback = 0x2,
    HasGposAttachment = 0x4,
    HasContinuations = 0x8,
    HasCombiningGraphemeJoiner = 0x10,
    HasFractionSlash = 0x20,
    HasVariationSelectorFallback = 0x40,

    /// <summary>A syllable of a syllabic script is broken (a dotted circle is inserted in it).</summary>
    HasBrokenSyllable = 0x80,

    /// <summary>The flags reserved for the shapers of the scripts.</summary>
    Shaper0 = 0x01000000,
    Shaper1 = 0x02000000,
    Shaper2 = 0x04000000,
    Shaper3 = 0x08000000,
}

/// <summary>
/// The buffer of the text shaping (HarfBuzz's <c>hb_buffer_t</c>, https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-buffer.cc,
/// MIT license): the characters of a run, then its glyphs (<see cref="GlyphInfo"/>) and their positions (<see cref="GlyphPosition"/>).
/// The GSUB lookups read the glyphs from <see cref="Index"/> and write them to the output (<see cref="OutLength"/>), which
/// becomes the content with <see cref="Sync"/>. The clusters are kept monotonic (HarfBuzz's cluster level 0: the clusters
/// are merged when glyphs are reordered or combined).
/// </summary>
/// <remarks>The arrays are rented from the shared array pool and returned by <see cref="Dispose"/>.</remarks>
internal sealed class ShapingBuffer : IDisposable
{
    /// <summary>The maximum number of characters of the context before and after the text.</summary>
    public const int ContextLength = 5;

    private const int MaxLengthFactor = 64;
    private const int MaxLengthMinimum = 16384;
    private const int MaxOpsFactor = 1024;
    private const int MaxOpsMinimum = 16384;

    private GlyphInfo[] _info = [];
    private GlyphInfo[] _spare = [];
    private GlyphPosition[] _positions = [];
    private bool _separateOutput;
    private int _maxLength;
    private uint _serial;

    private readonly int[] _preContext = new int[ContextLength];
    private readonly int[] _postContext = new int[ContextLength];

    private OpenTypeApplyContext? _applyContext;

    /// <summary>Returns the context of the application of the lookups of a table to the buffer (reused).</summary>
    public OpenTypeApplyContext GetApplyContext(OpenTypeLayoutTable table, OpenTypeGdef? gdef, FontVariationInstance? instance)
    {
        if (_applyContext is null)
        {
            _applyContext = new OpenTypeApplyContext(table, gdef, this, instance);
        }
        else
        {
            _applyContext.Reset(table, gdef, instance);
        }

        return _applyContext;
    }

    /// <summary>The capacity of the buffer (the number of glyphs it holds without growing).</summary>
    public int Capacity => _info.Length;

    /// <summary>The number of glyphs.</summary>
    public int Length { get; set; }

    /// <summary>The position of the glyph being processed.</summary>
    public int Index { get; set; }

    /// <summary>The number of glyphs of the output.</summary>
    public int OutLength { get; private set; }

    /// <summary>Whether the lookups write to the output (GSUB), else they modify the glyphs in place (GPOS).</summary>
    public bool HaveOutput { get; private set; }

    /// <summary>Whether the operations did not overflow (a malformed font can make a lookup grow the buffer too much).</summary>
    public bool Successful { get; set; } = true;

    /// <summary>The remaining number of operations (a protection against the malformed fonts).</summary>
    public int MaxOps { get; set; }

    /// <summary>The direction of the run.</summary>
    public ShapingDirection Direction { get; set; }

    /// <summary>The script of the run.</summary>
    public UnicodeScript Script { get; set; }

    /// <summary>The flags set while the buffer is shaped.</summary>
    public ShapingScratchFlags ScratchFlags { get; set; }

    /// <summary>The state of the random number generator of the <c>rand</c> feature.</summary>
    public uint RandomState { get; set; } = 1;

    /// <summary>The glyphs of the buffer (to skip the lookups that apply to none of them).</summary>
    public GlyphDigest Digest;

    /// <summary>The glyphs (or characters).</summary>
    public GlyphInfo[] Info => _info;

    /// <summary>The output glyphs: the glyphs themselves until the output grows faster than the input.</summary>
    public GlyphInfo[] OutInfo => _separateOutput ? _spare : _info;

    /// <summary>The positions of the glyphs.</summary>
    public GlyphPosition[] Positions => _positions;

    /// <summary>The characters before the text, the closest first (they change the shaping, e.g. the Arabic joining).</summary>
    public ReadOnlySpan<int> PreContext => _preContext.AsSpan(0, PreContextLength);

    /// <summary>The characters after the text, the closest first.</summary>
    public ReadOnlySpan<int> PostContext => _postContext.AsSpan(0, PostContextLength);

    public int PreContextLength { get; private set; }

    public int PostContextLength { get; private set; }

    /// <summary>Whether the direction is right-to-left (backward).</summary>
    public bool IsBackward => Direction == ShapingDirection.RightToLeft;

    /// <summary>Whether the direction is horizontal.</summary>
    public bool IsHorizontal => Direction != ShapingDirection.TopToBottom;

    /// <summary>The number of glyphs before the current one (in the output when there is one).</summary>
    public int BacktrackLength => HaveOutput ? OutLength : Index;

    /// <summary>The number of glyphs from the current one.</summary>
    public int LookaheadLength => Length - Index;

    /// <summary>The current glyph.</summary>
    public ref GlyphInfo Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _info[Index];
    }

    /// <summary>The position of the current glyph.</summary>
    public ref GlyphPosition CurrentPosition => ref _positions[Index];

    /// <summary>The last glyph of the output.</summary>
    public ref GlyphInfo Previous => ref OutInfo[OutLength - 1];

    /// <summary>Resets the buffer for a text of <paramref name="capacity"/> characters.</summary>
    public void Reset(int capacity)
    {
        Length = 0;
        Index = 0;
        OutLength = 0;
        HaveOutput = false;
        _separateOutput = false;
        Successful = true;
        ScratchFlags = ShapingScratchFlags.None;
        RandomState = 1;
        _serial = 0;
        Digest = default;
        PreContextLength = 0;
        PostContextLength = 0;
        _maxLength = Math.Max(capacity * MaxLengthFactor, MaxLengthMinimum);
        MaxOps = (int)Math.Min(Math.Max((long)capacity * MaxOpsFactor, MaxOpsMinimum), int.MaxValue);
        EnsureCapacity(Math.Max(capacity, 16));
    }

    /// <summary>Adds a character.</summary>
    public void Add(int codepoint, int cluster)
    {
        if (!Ensure(Length + 1))
            return;

        _info[Length] = new GlyphInfo { Codepoint = codepoint, Cluster = cluster };
        Length++;
    }

    /// <summary>Sets the characters before the text (<paramref name="before"/>, the closest last) and after it.</summary>
    public void SetContext(ReadOnlySpan<int> before, ReadOnlySpan<int> after)
    {
        PreContextLength = Math.Min(before.Length, ContextLength);
        for (var i = 0; i < PreContextLength; i++)
        {
            _preContext[i] = before[before.Length - 1 - i];
        }

        PostContextLength = Math.Min(after.Length, ContextLength);
        after[..PostContextLength].CopyTo(_postContext);
    }

    /// <summary>The next serial number (for the ligature ids).</summary>
    public uint NextSerial() => ++_serial;

    /// <summary>Allocates an id for a new ligature (1 to 7, HarfBuzz's <c>_hb_allocate_lig_id</c>).</summary>
    public int AllocateLigatureId()
    {
        var id = (int)(NextSerial() & 7);
        return id == 0 ? AllocateLigatureId() : id;
    }

    /// <summary>Makes sure that the buffer can hold <paramref name="size"/> glyphs; false when the buffer would be too large.</summary>
    public bool Ensure(int size)
    {
        if (size <= _info.Length)
            return true;

        if (size > _maxLength || !Successful)
        {
            Successful = false;
            return false;
        }

        EnsureCapacity(size);
        return true;
    }

    private void EnsureCapacity(int size)
    {
        if (size <= _info.Length)
            return;

        var newSize = Math.Max(size, _info.Length * 2);
        var info = ArrayPool<GlyphInfo>.Shared.Rent(newSize);
        var spare = ArrayPool<GlyphInfo>.Shared.Rent(newSize);
        var positions = ArrayPool<GlyphPosition>.Shared.Rent(newSize);
        _info.AsSpan().CopyTo(info);
        _spare.AsSpan().CopyTo(spare);
        _positions.AsSpan().CopyTo(positions);
        ReturnArrays();
        _info = info;
        _spare = spare;
        _positions = positions;
    }

    private void ReturnArrays()
    {
        if (_info.Length > 0)
        {
            ArrayPool<GlyphInfo>.Shared.Return(_info);
            ArrayPool<GlyphInfo>.Shared.Return(_spare);
            ArrayPool<GlyphPosition>.Shared.Return(_positions);
        }
    }

    public void Dispose()
    {
        ReturnArrays();
        _info = [];
        _spare = [];
        _positions = [];
    }

    /// <summary>Starts a pass that writes the glyphs to the output.</summary>
    public void ClearOutput()
    {
        HaveOutput = true;
        Index = 0;
        OutLength = 0;
        _separateOutput = false;
    }

    /// <summary>Starts the positioning: the positions are zero.</summary>
    public void ClearPositions()
    {
        HaveOutput = false;
        OutLength = 0;
        _separateOutput = false;
        _positions.AsSpan(0, Length).Clear();
    }

    /// <summary>Ends a pass that wrote to the output: the output becomes the content of the buffer.</summary>
    public bool Sync()
    {
        var result = false;
        if (Successful && NextGlyphs(Length - Index))
        {
            if (_separateOutput)
            {
                (_info, _spare) = (_spare, _info);
            }

            Length = OutLength;
            result = true;
        }

        HaveOutput = false;
        OutLength = 0;
        _separateOutput = false;
        Index = 0;
        return result;
    }

    /// <summary>Makes room in the output for <paramref name="numberOut"/> glyphs replacing <paramref name="numberIn"/> input glyphs (the output then needs its own array).</summary>
    private bool MakeRoomFor(int numberIn, int numberOut)
    {
        if (!Ensure(OutLength + numberOut))
            return false;

        if (!_separateOutput && OutLength + numberOut > Index + numberIn)
        {
            _info.AsSpan(0, OutLength).CopyTo(_spare);
            _separateOutput = true;
        }

        return true;
    }

    /// <summary>Moves the input glyphs from the current one <paramref name="count"/> positions forward (to rewind the output into the input).</summary>
    private bool ShiftForward(int count)
    {
        if (!Ensure(Length + count))
            return false;

        MaxOps -= Length - Index;
        if (MaxOps < 0)
        {
            Successful = false;
            return false;
        }

        Array.Copy(_info, Index, _info, Index + count, Length - Index);
        if (Index + count > Length)
        {
            _info.AsSpan(Length, Index + count - Length).Clear();
        }

        Length += count;
        Index += count;
        return true;
    }

    /// <summary>Moves the current position to the output position <paramref name="i"/>, copying the glyphs between the output and the input.</summary>
    public bool MoveTo(int i)
    {
        if (!HaveOutput)
        {
            Index = i;
            return true;
        }

        if (!Successful)
            return false;

        if (OutLength < i)
        {
            var count = i - OutLength;
            if (!MakeRoomFor(count, count))
                return false;

            MaxOps -= count;
            if (MaxOps < 0)
            {
                Successful = false;
                return false;
            }

            Array.Copy(_info, Index, OutInfo, OutLength, count);
            Index += count;
            OutLength += count;
        }
        else if (OutLength > i)
        {
            // Rewinding
            var count = OutLength - i;
            if (Index < count && !ShiftForward(count - Index))
                return false;

            MaxOps -= count;
            if (MaxOps < 0)
            {
                Successful = false;
                return false;
            }

            Index -= count;
            OutLength -= count;
            Array.Copy(OutInfo, OutLength, _info, Index, count);
        }

        return true;
    }

    /// <summary>Replaces <paramref name="numberIn"/> glyphs from the current one by <paramref name="glyphs"/> in the output (their clusters are merged).</summary>
    public bool ReplaceGlyphs(int numberIn, ReadOnlySpan<int> glyphs)
    {
        if (!MakeRoomFor(numberIn, glyphs.Length))
            return false;

        MergeClusters(Index, Index + numberIn);
        var original = Index < Length ? _info[Index] : OutInfo[OutLength - 1];
        var output = OutInfo;
        for (var i = 0; i < glyphs.Length; i++)
        {
            output[OutLength + i] = original;
            output[OutLength + i].Codepoint = glyphs[i];
        }

        Index += numberIn;
        OutLength += glyphs.Length;
        return true;
    }

    /// <summary>Replaces the current glyph in the output.</summary>
    public bool ReplaceGlyph(int glyph) => ReplaceGlyphs(1, [glyph]);

    /// <summary>Writes a copy of the current glyph with another glyph id to the output (without consuming the input).</summary>
    public bool OutputGlyph(int glyph) => ReplaceGlyphs(0, [glyph]);

    /// <summary>Writes a glyph to the output.</summary>
    public bool OutputInfo(in GlyphInfo info)
    {
        if (!MakeRoomFor(0, 1))
            return false;

        OutInfo[OutLength] = info;
        OutLength++;
        return true;
    }

    /// <summary>Copies the current glyph to the output without consuming it.</summary>
    public bool CopyGlyph()
    {
        var copy = _info[Index];
        return OutputInfo(copy);
    }

    /// <summary>Copies the current glyph to the output (when there is one) and moves to the next glyph.</summary>
    public bool NextGlyph()
    {
        if (HaveOutput)
        {
            if (_separateOutput || OutLength != Index)
            {
                if (!Ensure(OutLength + 1))
                    return false;

                OutInfo[OutLength] = _info[Index];
            }

            OutLength++;
        }

        Index++;
        return true;
    }

    /// <summary>Copies <paramref name="count"/> glyphs to the output (when there is one) and moves after them.</summary>
    public bool NextGlyphs(int count)
    {
        if (HaveOutput)
        {
            if (_separateOutput || OutLength != Index)
            {
                if (!Ensure(OutLength + count))
                    return false;

                Array.Copy(_info, Index, OutInfo, OutLength, count);
            }

            OutLength += count;
        }

        Index += count;
        return true;
    }

    /// <summary>Removes the last glyph of the output.</summary>
    public void RemoveLastOutput() => OutLength--;

    /// <summary>Moves to the next glyph without copying the current one to the output.</summary>
    public void SkipGlyph() => Index++;

    /// <summary>Removes the current glyph (its cluster is merged into a neighbor) and moves to the next glyph.</summary>
    public void DeleteGlyph()
    {
        var cluster = _info[Index].Cluster;
        if ((Index + 1 < Length && cluster == _info[Index + 1].Cluster) || (OutLength > 0 && cluster == OutInfo[OutLength - 1].Cluster))
        {
            // The cluster survives
        }
        else if (OutLength > 0)
        {
            // Merge the cluster backward
            var output = OutInfo;
            if (cluster < output[OutLength - 1].Cluster)
            {
                var mask = _info[Index].Mask;
                var oldCluster = output[OutLength - 1].Cluster;
                for (var i = OutLength; i > 0 && output[i - 1].Cluster == oldCluster; i--)
                {
                    SetCluster(ref output[i - 1], cluster, mask);
                }
            }
        }
        else if (Index + 1 < Length)
        {
            // Merge the cluster forward
            MergeClusters(Index, Index + 2);
        }

        SkipGlyph();
    }

    /// <summary>Removes the glyphs that match a predicate, in place (with the positions), merging their clusters.</summary>
    public void DeleteGlyphsInPlace(Func<GlyphInfo, bool> filter)
    {
        var j = 0;
        var info = _info;
        for (var i = 0; i < Length; i++)
        {
            if (filter(info[i]))
            {
                var cluster = info[i].Cluster;
                if (i + 1 < Length && cluster == info[i + 1].Cluster)
                    continue;

                if (j > 0)
                {
                    if (cluster < info[j - 1].Cluster)
                    {
                        var mask = info[i].Mask;
                        var oldCluster = info[j - 1].Cluster;
                        for (var k = j; k > 0 && info[k - 1].Cluster == oldCluster; k--)
                        {
                            SetCluster(ref info[k - 1], cluster, mask);
                        }
                    }

                    continue;
                }

                if (i + 1 < Length)
                {
                    MergeClusters(i, i + 2);
                }

                continue;
            }

            if (j != i)
            {
                info[j] = info[i];
                _positions[j] = _positions[i];
            }

            j++;
        }

        Length = j;
    }

    /// <summary>Sets the value of a mask on the glyphs whose cluster is in a range (the whole buffer by default).</summary>
    public void SetMasks(uint value, uint mask, int clusterStart = 0, int clusterEnd = int.MaxValue)
    {
        if (mask == 0)
            return;

        var notMask = ~mask;
        value &= mask;
        for (var i = 0; i < Length; i++)
        {
            ref var info = ref _info[i];
            if (clusterStart <= info.Cluster && info.Cluster < clusterEnd)
            {
                info.Mask = (info.Mask & notMask) | value;
            }
        }
    }

    /// <summary>Adds a mask to all the glyphs.</summary>
    public void AddMasks(uint mask)
    {
        for (var i = 0; i < Length; i++)
        {
            _info[i].Mask |= mask;
        }
    }

    /// <summary>Merges the clusters of the glyphs from <paramref name="start"/> to <paramref name="end"/> (input positions): they get the smallest cluster.</summary>
    public void MergeClusters(int start, int end)
    {
        if (end - start < 2)
            return;

        MaxOps -= end - start;
        if (MaxOps < 0)
        {
            Successful = false;
        }

        var info = _info;
        var cluster = info[start].Cluster;
        for (var i = start + 1; i < end; i++)
        {
            cluster = Math.Min(cluster, info[i].Cluster);
        }

        // Extend the end and the start to the whole clusters
        if (cluster != info[end - 1].Cluster)
        {
            while (end < Length && info[end - 1].Cluster == info[end].Cluster)
            {
                end++;
            }
        }

        if (cluster != info[start].Cluster)
        {
            while (Index < start && info[start - 1].Cluster == info[start].Cluster)
            {
                start--;
            }
        }

        // When the start of the buffer is reached, continue in the output
        if (Index == start && info[start].Cluster != cluster)
        {
            var output = OutInfo;
            for (var i = OutLength; i > 0 && output[i - 1].Cluster == info[start].Cluster; i--)
            {
                SetCluster(ref output[i - 1], cluster);
            }
        }

        for (var i = start; i < end; i++)
        {
            SetCluster(ref info[i], cluster);
        }
    }

    /// <summary>Merges the clusters of the output glyphs from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public void MergeOutClusters(int start, int end)
    {
        if (end - start < 2)
            return;

        MaxOps -= end - start;
        if (MaxOps < 0)
        {
            Successful = false;
        }

        var output = OutInfo;
        var cluster = output[start].Cluster;
        for (var i = start + 1; i < end; i++)
        {
            cluster = Math.Min(cluster, output[i].Cluster);
        }

        while (start > 0 && output[start - 1].Cluster == output[start].Cluster)
        {
            start--;
        }

        while (end < OutLength && output[end - 1].Cluster == output[end].Cluster)
        {
            end++;
        }

        // When the end of the output is reached, continue in the input
        if (end == OutLength)
        {
            for (var i = Index; i < Length && _info[i].Cluster == output[end - 1].Cluster; i++)
            {
                SetCluster(ref _info[i], cluster);
            }
        }

        for (var i = start; i < end; i++)
        {
            SetCluster(ref output[i], cluster);
        }
    }

    private static void SetCluster(ref GlyphInfo info, int cluster, uint mask = 0)
    {
        if (info.Cluster != cluster)
        {
            info.Mask = (info.Mask & ~GlyphFlags.Defined) | (mask & GlyphFlags.Defined);
        }

        info.Cluster = cluster;
    }

    /// <summary>Marks the glyphs from <paramref name="start"/> to <paramref name="end"/> (input positions) as unsafe to break between them.</summary>
    public void UnsafeToBreak(int start, int end) => SetGlyphFlags(GlyphFlags.UnsafeToBreak | GlyphFlags.UnsafeToConcat, start, end, interior: true, fromOutput: false);

    /// <summary>Marks the glyphs from the output position <paramref name="start"/> to the input position <paramref name="end"/> as unsafe to break between them.</summary>
    public void UnsafeToBreakFromOutput(int start, int end) => SetGlyphFlags(GlyphFlags.UnsafeToBreak | GlyphFlags.UnsafeToConcat, start, end, interior: true, fromOutput: true);

    /// <summary>
    /// Marks a place where a tatweel could be inserted (HarfBuzz's <c>safe_to_insert_tatweel</c>): as the tatweels are not
    /// inserted, it is unsafe to break.
    /// </summary>
    public void SafeToInsertTatweel(int start, int end) => UnsafeToBreak(start, end);

    private void SetGlyphFlags(uint mask, int start, int end, bool interior, bool fromOutput)
    {
        if (end != int.MaxValue && end - start > 255)
            return;

        end = Math.Min(end, Length);
        if (interior && !fromOutput && end - start < 2)
            return;

        if (!fromOutput || !HaveOutput)
        {
            if (!interior)
            {
                for (var i = start; i < end; i++)
                {
                    _info[i].Mask |= mask;
                }
            }
            else
            {
                var cluster = FindMinCluster(_info, start, end, int.MaxValue);
                SetGlyphFlags(_info, start, end, cluster, mask);
            }
        }
        else
        {
            var output = OutInfo;
            if (!interior)
            {
                for (var i = start; i < OutLength; i++)
                {
                    output[i].Mask |= mask;
                }

                for (var i = Index; i < end; i++)
                {
                    _info[i].Mask |= mask;
                }
            }
            else
            {
                var cluster = FindMinCluster(_info, Index, end, int.MaxValue);
                cluster = FindMinCluster(output, start, OutLength, cluster);
                SetGlyphFlags(output, start, OutLength, cluster, mask);
                SetGlyphFlags(_info, Index, end, cluster, mask);
            }
        }
    }

    private void SetGlyphFlags(GlyphInfo[] infos, int start, int end, int cluster, uint mask)
    {
        if (start >= end)
            return;

        MaxOps -= end - start;
        if (MaxOps < 0)
        {
            Successful = false;
        }

        var first = infos[start].Cluster;
        var last = infos[end - 1].Cluster;
        if (cluster != first && cluster != last)
        {
            for (var i = start; i < end; i++)
            {
                if (cluster != infos[i].Cluster)
                {
                    infos[i].Mask |= mask;
                }
            }

            return;
        }

        // The clusters are monotonic
        if (cluster == first)
        {
            for (var i = end; start < i && infos[i - 1].Cluster != first; i--)
            {
                infos[i - 1].Mask |= mask;
            }
        }
        else
        {
            for (var i = start; i < end && infos[i].Cluster != last; i++)
            {
                infos[i].Mask |= mask;
            }
        }
    }

    private static int FindMinCluster(GlyphInfo[] infos, int start, int end, int cluster) => start >= end ? cluster : Math.Min(cluster, Math.Min(infos[start].Cluster, infos[end - 1].Cluster));

    /// <summary>Reverses the glyphs (and their positions) from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public void Reverse(int start, int end)
    {
        _info.AsSpan(start, end - start).Reverse();
        _positions.AsSpan(start, end - start).Reverse();
    }

    /// <summary>Reverses all the glyphs.</summary>
    public void Reverse() => Reverse(0, Length);

    /// <summary>Sorts the glyphs from <paramref name="start"/> to <paramref name="end"/> with a stable insertion sort (HarfBuzz's <c>hb_buffer_t::sort</c>), merging the clusters of the moved glyphs.</summary>
    public void Sort(int start, int end, Comparison<GlyphInfo> comparison)
    {
        var info = _info;
        for (var i = start + 1; i < end; i++)
        {
            var j = i;
            while (j > start && comparison(info[j - 1], info[i]) > 0)
            {
                j--;
            }

            if (i == j)
                continue;

            // Move the item i to the position j
            MergeClusters(j, i + 1);
            var item = info[i];
            Array.Copy(info, j, info, j + 1, i - j);
            info[j] = item;
        }
    }

    /// <summary>The end of the syllable that starts at <paramref name="start"/> (HarfBuzz's <c>next_syllable</c>): the glyphs of a syllable have the same <see cref="GlyphInfo.Syllable"/>.</summary>
    public int NextSyllable(int start)
    {
        var info = _info;
        var syllable = info[start].Syllable;
        while (++start < Length && info[start].Syllable == syllable)
        {
        }

        return start;
    }

    /// <summary>Recomputes the digest of the glyphs.</summary>
    public void UpdateDigest()
    {
        Digest = default;
        for (var i = 0; i < Length; i++)
        {
            Digest.Add(_info[i].Codepoint);
        }
    }

    /// <summary>The next random number of the <c>rand</c> feature (minstd_rand).</summary>
    public uint NextRandom()
    {
        RandomState = (uint)((ulong)RandomState * 48271 % 2147483647);
        return RandomState;
    }
}
