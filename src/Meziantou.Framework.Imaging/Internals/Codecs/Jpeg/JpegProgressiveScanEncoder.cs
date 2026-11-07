using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The entropy coder of the scans of a progressive Huffman JPEG (ITU-T T.81 section G.1.2): DC first scans (the
/// differences of the DC coefficients shifted right by <c>Al</c>), DC refinement scans (one bit per block), AC first scans
/// (run lengths of zeros and the magnitudes divided by <c>2^Al</c>, with runs of blocks that have nothing more in the band)
/// and AC refinement scans (one more bit of every coefficient that is already nonzero, and the coefficients that become
/// nonzero).
/// </summary>
/// <remarks>
/// <para>
/// A scan is coded twice with the same code path: the first pass only counts the symbols, from which the Huffman tables of
/// the scan are built (<see cref="JpegHuffmanCodeBuilder"/>); the second one writes the codes. The typical tables of Annex K
/// cannot be used: they have no code for the end-of-band runs.
/// </para>
/// <para>
/// The luminance component uses the tables 0, the other components the tables 1.
/// </para>
/// <para>
/// The AC scans visit the coefficients of the band that matter through bit masks (<see cref="GetSignificantMask"/>): the
/// coefficients whose magnitude is not zero at bit <c>Al</c>, and for a refinement scan those already nonzero before it.
/// The bits of a block go through a <see cref="JpegBlockBitWriter"/>, which stuffs the block once it is coded.
/// </para>
/// </remarks>
internal sealed class JpegProgressiveScanEncoder
{
    /// <summary>
    /// The worst-case size of what coding one block appends: a pending end-of-band run (16 + 14 bits) with its correction
    /// bits (at most <see cref="MaxPendingCorrectionBits"/> + 63), 63 coefficients (at most 16 + 10 bits each), three ZRL
    /// codes and the correction bits of the block, doubled for byte stuffing, and the
    /// <see cref="JpegBlockBitWriter.SpeculativeBytes"/> of the bit writer.
    /// </summary>
    internal const int MaxBlockBytes = 1024;

    /// <summary>
    /// The worst-case size of what coding one block of a DC scan appends: a code and its additional bits (at most 16 + 11
    /// bits) after at most 7 pending bits, doubled for byte stuffing.
    /// </summary>
    internal const int MaxDcBlockBytes = 8;

    /// <summary>The longest run of blocks one code can end (EOB14 with 14 additional bits).</summary>
    private const int MaxEndOfBandRun = 0x7FFF;

    /// <summary>The number of correction bits from which a run of blocks is ended early, which bounds the bits kept for it.</summary>
    private const int MaxPendingCorrectionBits = 1000;

    // The progression of the library: the DC coefficients first without their lowest bit, then the low frequencies of the
    // luminance, the chrominance and the rest of the luminance at reduced precision, and at last the missing bits
    private static readonly JpegProgressiveScan[] ColorScript =
    [
        new(FirstComponent: 0, ComponentCount: 3, Ss: 0, Se: 0, Ah: 0, Al: 1),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 1, Se: 5, Ah: 0, Al: 2),
        new(FirstComponent: 2, ComponentCount: 1, Ss: 1, Se: 63, Ah: 0, Al: 1),
        new(FirstComponent: 1, ComponentCount: 1, Ss: 1, Se: 63, Ah: 0, Al: 1),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 6, Se: 63, Ah: 0, Al: 2),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 1, Se: 63, Ah: 2, Al: 1),
        new(FirstComponent: 0, ComponentCount: 3, Ss: 0, Se: 0, Ah: 1, Al: 0),
        new(FirstComponent: 2, ComponentCount: 1, Ss: 1, Se: 63, Ah: 1, Al: 0),
        new(FirstComponent: 1, ComponentCount: 1, Ss: 1, Se: 63, Ah: 1, Al: 0),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 1, Se: 63, Ah: 1, Al: 0),
    ];

    private static readonly JpegProgressiveScan[] GrayscaleScript =
    [
        new(FirstComponent: 0, ComponentCount: 1, Ss: 0, Se: 0, Ah: 0, Al: 1),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 1, Se: 5, Ah: 0, Al: 2),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 6, Se: 63, Ah: 0, Al: 2),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 1, Se: 63, Ah: 2, Al: 1),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 0, Se: 0, Ah: 1, Al: 0),
        new(FirstComponent: 0, ComponentCount: 1, Ss: 1, Se: 63, Ah: 1, Al: 0),
    ];

    private readonly uint[][] _histograms = [new uint[256], new uint[256]];
    private readonly JpegHuffmanEncoder?[] _tables = new JpegHuffmanEncoder?[2];
    private readonly int[] _predictions = new int[3];

    // The correction bits of the pending run of blocks, first bit in the most significant bit of the first word
    private readonly ulong[] _pendingBits = new ulong[((MaxPendingCorrectionBits + 63) / 64) + 2];
    private JpegProgressiveScan _scan;
    private ulong _band;
    private JpegBitWriter _bits;
    private bool _counting;
    private int _endOfBandRun;
    private int _pendingBitCount;

    /// <summary>Gets the scans of an image, in the order they are written.</summary>
    /// <param name="grayscale"><see langword="true"/> for a single component, <see langword="false"/> for Y, Cb and Cr.</param>
    public static ReadOnlySpan<JpegProgressiveScan> GetScript(bool grayscale) => grayscale ? GrayscaleScript : ColorScript;

    /// <summary>Gets the index of the tables of a component.</summary>
    public static int GetTableIndex(int component) => component == 0 ? 0 : 1;

    /// <summary>Starts a pass over the blocks of a scan.</summary>
    /// <param name="scan">The scan.</param>
    /// <param name="counting"><see langword="true"/> to count the symbols; <see langword="false"/> to write them with the tables built from the counts.</param>
    public void BeginScan(JpegProgressiveScan scan, bool counting)
    {
        _scan = scan;
        _band = (ulong.MaxValue >> (63 - scan.Se)) & (ulong.MaxValue << scan.Ss);
        _counting = counting;
        _endOfBandRun = 0;
        _pendingBitCount = 0;
        _predictions.AsSpan().Clear();
        if (counting)
        {
            _histograms[0].AsSpan().Clear();
            _histograms[1].AsSpan().Clear();
        }
    }

    /// <summary>Gets the number of destination bytes that coding blocks of a scan needs.</summary>
    /// <param name="scan">The scan.</param>
    /// <param name="blockCount">The number of blocks coded by one call.</param>
    public static int GetMaxBytes(JpegProgressiveScan scan, int blockCount) => (blockCount * GetMaxBlockBytes(scan)) + JpegBlockBitWriter.SpeculativeBytes;

    /// <summary>Gets the worst-case size of what coding one block of a scan appends.</summary>
    public static int GetMaxBlockBytes(JpegProgressiveScan scan) => scan.Ss == 0 ? MaxDcBlockBytes : MaxBlockBytes;

    /// <summary>Codes the part of consecutive blocks of one component that belongs to the scan.</summary>
    /// <param name="component">The index of the component of the blocks.</param>
    /// <param name="blocks">The quantized coefficients of the blocks, 64 per block in zig-zag order.</param>
    /// <param name="destination">The destination, with <see cref="GetMaxBytes"/> bytes available; unused when counting.</param>
    /// <param name="position">The write position in <paramref name="destination"/>, advanced.</param>
    public void CodeBlocks(int component, ReadOnlySpan<short> blocks, Span<byte> destination, ref int position)
    {
        if (_counting)
        {
            var none = default(JpegBlockBitWriter);
            CodeBlocks<CountingPass>(component, blocks, ref none);
            return;
        }

        var writer = new JpegBlockBitWriter(destination, position, _bits);
        CodeBlocks<WritingPass>(component, blocks, ref writer);
        position = writer.Complete(ref _bits);
    }

    /// <summary>
    /// Codes consecutive MCUs of an interleaved DC scan of the three components (T.81 A.2.3): the <c>h x v</c> luminance
    /// blocks of each MCU in raster order, then the Cb and Cr blocks.
    /// </summary>
    /// <param name="luma">The luminance blocks of the first block row of the MCUs, from the first MCU.</param>
    /// <param name="lumaSecondRow">The second block row of luminance blocks when <paramref name="v"/> is 2.</param>
    /// <param name="blue">The Cb blocks, from the first MCU.</param>
    /// <param name="red">The Cr blocks, from the first MCU.</param>
    /// <param name="h">The horizontal luminance sampling factor.</param>
    /// <param name="v">The vertical luminance sampling factor.</param>
    /// <param name="count">The number of MCUs.</param>
    /// <param name="destination">The destination, with <see cref="GetMaxBytes"/> bytes available for the blocks; unused when counting.</param>
    /// <param name="position">The write position in <paramref name="destination"/>, advanced.</param>
    public void CodeInterleavedDcBlocks(ReadOnlySpan<short> luma, ReadOnlySpan<short> lumaSecondRow, ReadOnlySpan<short> blue, ReadOnlySpan<short> red, int h, int v, int count, Span<byte> destination, ref int position)
    {
        if (_scan.Ss != 0)
            throw new InvalidOperationException("Only DC scans are interleaved.");

        if (_counting)
        {
            var none = default(JpegBlockBitWriter);
            CodeInterleavedDcBlocks<CountingPass>(luma, lumaSecondRow, blue, red, h, v, count, ref none);
            return;
        }

        var writer = new JpegBlockBitWriter(destination, position, _bits);
        CodeInterleavedDcBlocks<WritingPass>(luma, lumaSecondRow, blue, red, h, v, count, ref writer);
        position = writer.Complete(ref _bits);
    }

    /// <summary>Ends a pass: writes the pending run of blocks and, when writing, completes the last byte.</summary>
    /// <param name="destination">The destination, with <see cref="MaxBlockBytes"/> bytes available; unused when counting.</param>
    /// <param name="position">The write position in <paramref name="destination"/>, advanced.</param>
    public void EndScan(Span<byte> destination, ref int position)
    {
        FlushEndOfBandRun(GetTableIndex(_scan.FirstComponent), destination, ref position);
        if (_counting)
        {
            for (var component = _scan.FirstComponent; component < _scan.FirstComponent + _scan.ComponentCount; component++)
            {
                var table = GetTableIndex(component);
                _tables[table] = JpegHuffmanCodeBuilder.Build(_histograms[table]);
            }
        }
        else
        {
            _bits.Pad(destination, ref position);
        }
    }

    /// <summary>
    /// Ends a restart interval before the next MCU (T.81 G.1.2.2, F.1.2.3): writes the pending run of blocks, so that no
    /// end-of-band run crosses the marker, then, when writing, completes the last byte with 1-bits and writes the RSTn
    /// marker; the DC predictions start again from 0.
    /// </summary>
    /// <param name="number">The number of the marker, 0 to 7.</param>
    /// <param name="destination">The destination, with <see cref="MaxBlockBytes"/> bytes available; unused when counting.</param>
    /// <param name="position">The write position in <paramref name="destination"/>, advanced.</param>
    public void Restart(int number, Span<byte> destination, ref int position)
    {
        FlushEndOfBandRun(GetTableIndex(_scan.FirstComponent), destination, ref position);
        _predictions.AsSpan().Clear();
        if (_counting)
            return;

        _bits.Pad(destination, ref position);
        destination[position++] = 0xFF;
        destination[position++] = (byte)(0xD0 + number);
    }

    /// <summary>Gets a table built by the counting pass of the scan.</summary>
    public JpegHuffmanEncoder GetTable(int index) => _tables[index] ?? throw new InvalidOperationException("The Huffman table of the scan is not built.");

    /// <summary>
    /// Gets a mask of the coefficients whose magnitude is not zero once shifted right by <paramref name="shift"/>: bit
    /// <c>k</c> is set when <c>|block[k]| &gt;&gt; shift</c> is not zero.
    /// </summary>
    /// <param name="block">The 64 coefficients.</param>
    /// <param name="shift">The shift, 0 to 15.</param>
    internal static ulong GetSignificantMask(ReadOnlySpan<short> block, int shift)
    {
        block = block[..64];
        if (!Vector128.IsHardwareAccelerated)
            return GetSignificantMaskScalar(block, shift);

        // The magnitudes as unsigned 16-bit values (|-32768| is 32768), shifted; the comparison results of 16 coefficients
        // narrowed to 16 bytes, one mask bit each
        ref var start = ref unsafe(MemoryMarshal.GetReference(block));
        var zeros = 0UL;
        for (var i = 0; i < 64; i += 16)
        {
            var first = Vector128.ShiftRightLogical(Vector128.Abs(Load(ref start, i)).AsUInt16(), shift);
            var second = Vector128.ShiftRightLogical(Vector128.Abs(Load(ref start, i + 8)).AsUInt16(), shift);
            var equal = Vector128.Narrow(Vector128.Equals(first, Vector128<ushort>.Zero), Vector128.Equals(second, Vector128<ushort>.Zero));
            zeros |= (ulong)equal.ExtractMostSignificantBits() << i;
        }

        return ~zeros;

        static Vector128<short> Load(ref short start, int index) => unsafe(Vector128.LoadUnsafe(ref start, (nuint)index));
    }

    /// <summary>
    /// Gets the masks of <see cref="GetSignificantMask"/> for the shifts <paramref name="shift"/> and
    /// <paramref name="shift"/> + 1 at once: the coefficients nonzero at bit <c>shift</c>, and those already nonzero above it.
    /// </summary>
    /// <param name="block">The 64 coefficients.</param>
    /// <param name="shift">The shift, 0 to 14.</param>
    internal static (ulong Shifted, ulong ShiftedOnceMore) GetSignificantMasks(ReadOnlySpan<short> block, int shift)
    {
        block = block[..64];
        if (!Vector128.IsHardwareAccelerated)
            return (GetSignificantMaskScalar(block, shift), GetSignificantMaskScalar(block, shift + 1));

        ref var start = ref unsafe(MemoryMarshal.GetReference(block));
        var zeros = 0UL;
        var zerosOnceMore = 0UL;
        for (var i = 0; i < 64; i += 16)
        {
            var first = Vector128.ShiftRightLogical(Vector128.Abs(Load(ref start, i)).AsUInt16(), shift);
            var second = Vector128.ShiftRightLogical(Vector128.Abs(Load(ref start, i + 8)).AsUInt16(), shift);
            zeros |= (ulong)Vector128.Narrow(Vector128.Equals(first, Vector128<ushort>.Zero), Vector128.Equals(second, Vector128<ushort>.Zero)).ExtractMostSignificantBits() << i;

            // The values above 1 are those nonzero once shifted once more
            var one = Vector128<ushort>.One;
            zerosOnceMore |= (ulong)Vector128.Narrow(Vector128.LessThanOrEqual(first, one), Vector128.LessThanOrEqual(second, one)).ExtractMostSignificantBits() << i;
        }

        return (~zeros, ~zerosOnceMore);

        static Vector128<short> Load(ref short start, int index) => unsafe(Vector128.LoadUnsafe(ref start, (nuint)index));
    }

    /// <summary>The scalar reference of <see cref="GetSignificantMask"/>.</summary>
    internal static ulong GetSignificantMaskScalar(ReadOnlySpan<short> block, int shift)
    {
        var mask = 0UL;
        for (var k = 0; k < 64; k++)
        {
            if ((uint)Math.Abs((int)block[k]) >> shift != 0)
            {
                mask |= 1UL << k;
            }
        }

        return mask;
    }

    private void CodeBlocks<TPass>(int component, ReadOnlySpan<short> blocks, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        var table = GetTableIndex(component);
        var count = blocks.Length / 64;
        if (_scan.Ss == 0)
        {
            for (var i = 0; i < count; i++)
            {
                CodeDc<TPass>(component, table, blocks[i * 64], ref writer);
            }
        }
        else if (_scan.Ah == 0)
        {
            for (var i = 0; i < count; i++)
            {
                CodeAcFirst<TPass>(table, blocks.Slice(i * 64, 64), ref writer);
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                CodeAcRefinement<TPass>(table, blocks.Slice(i * 64, 64), ref writer);
            }
        }
    }

    private void CodeInterleavedDcBlocks<TPass>(ReadOnlySpan<short> luma, ReadOnlySpan<short> lumaSecondRow, ReadOnlySpan<short> blue, ReadOnlySpan<short> red, int h, int v, int count, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        for (var i = 0; i < count; i++)
        {
            for (var bx = 0; bx < h; bx++)
            {
                CodeDc<TPass>(0, 0, luma[((i * h) + bx) * 64], ref writer);
            }

            if (v == 2)
            {
                for (var bx = 0; bx < h; bx++)
                {
                    CodeDc<TPass>(0, 0, lumaSecondRow[((i * h) + bx) * 64], ref writer);
                }
            }

            CodeDc<TPass>(1, 1, blue[i * 64], ref writer);
            CodeDc<TPass>(2, 1, red[i * 64], ref writer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CodeDc<TPass>(int component, int table, int coefficient, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        if (_scan.Ah == 0)
        {
            CodeDcFirst<TPass>(component, table, coefficient, ref writer);
        }
        else if (!TPass.Counting)
        {
            // The bit Al of the coefficient; the shift keeps the two's complement bits of negative values (G.1.2.1)
            writer.Write((uint)(coefficient >> _scan.Al) & 1, 1);
        }
    }

    /// <summary>DC first scan: the difference of the shifted coefficient with the one of the previous block of the component (T.81 G.1.2.1, F.1.2.1).</summary>
    private void CodeDcFirst<TPass>(int component, int table, int coefficient, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        // The point transform of DC coefficients is an arithmetic shift
        var value = coefficient >> _scan.Al;
        var difference = value - _predictions[component];
        _predictions[component] = value;
        var magnitude = (uint)Math.Abs(difference);
        var size = 32 - BitOperations.LeadingZeroCount(magnitude);
        EmitSymbol<TPass>(table, size, GetAdditionalBits(difference < 0, magnitude, size), size, ref writer);
    }

    /// <summary>
    /// AC first scan (T.81 G.1.2.2): like a sequential block restricted to the band, on the magnitudes divided by
    /// <c>2^Al</c>, except that the end of the band is not coded block by block: consecutive blocks that end early are
    /// counted and coded as one run.
    /// </summary>
    private void CodeAcFirst<TPass>(int table, ReadOnlySpan<short> block, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        int al = _scan.Al;
        var mask = GetSignificantMask(block, al) & _band;
        var previous = _scan.Ss - 1;
        while (mask != 0)
        {
            var k = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var run = k - previous - 1;
            previous = k;
            FlushEndOfBandRun<TPass>(table, ref writer);
            while (run > 15)
            {
                EmitSymbol<TPass>(table, 0xF0, 0, 0, ref writer);
                run -= 16;
            }

            int coefficient = block[k];
            var magnitude = (uint)Math.Abs(coefficient) >> al;
            var size = 32 - BitOperations.LeadingZeroCount(magnitude);
            EmitSymbol<TPass>(table, (run << 4) | size, GetAdditionalBits(coefficient < 0, magnitude, size), size, ref writer);
        }

        // Zeros after the last coefficient: the block ends early
        if (previous < _scan.Se)
        {
            _endOfBandRun++;
            if (_endOfBandRun == MaxEndOfBandRun)
            {
                FlushEndOfBandRun<TPass>(table, ref writer);
            }
        }
    }

    /// <summary>
    /// AC refinement scan (T.81 G.1.2.3). A coefficient that is already nonzero (its magnitude above bit <c>Al</c> is not
    /// zero) sends its bit <c>Al</c> as a correction bit; a coefficient that becomes nonzero (magnitude 1 at bit <c>Al</c>)
    /// is coded with the run of still-zero coefficients before it and its sign. The correction bits of the coefficients
    /// passed over follow the code that passes over them: a ZRL code, a coefficient that becomes nonzero, or the end of the
    /// band, whose correction bits wait until the run of blocks is written.
    /// </summary>
    /// <remarks>
    /// The counting pass only visits the coefficients that become nonzero: a ZRL code is written at a nonzero coefficient
    /// reached with a run of more than 15 zeros since the last code, and takes 16 of them, so the codes before a
    /// coefficient that becomes nonzero are as many ZRL codes as the 16s among the zeros since the previous one, and the
    /// rest of the zeros is the run of its code, wherever the already nonzero coefficients are in between.
    /// </remarks>
    private void CodeAcRefinement<TPass>(int table, ReadOnlySpan<short> block, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        // Nonzero at bit Al, already nonzero before it, and the last coefficient that becomes nonzero: zeros after it
        // belong to the end of the band
        int al = _scan.Al;
        var (nonZero, previouslyNonZero) = GetSignificantMasks(block, al);
        nonZero &= _band;
        previouslyNonZero &= _band;
        var newlyNonZero = nonZero & ~previouslyNonZero;
        var previous = _scan.Ss - 1;
        if (TPass.Counting)
        {
            var zeros = _band & ~nonZero;
            var codes = newlyNonZero;
            while (codes != 0)
            {
                var k = BitOperations.TrailingZeroCount(codes);
                codes &= codes - 1;
                var zeroRun = BitOperations.PopCount(zeros & ((1UL << k) - 1) & ~((2UL << previous) - 1));
                FlushEndOfBandRun<TPass>(table, ref writer);
                _histograms[table][0xF0] += (uint)(zeroRun >> 4);
                EmitSymbol<TPass>(table, ((zeroRun & 15) << 4) | 1, 0, 0, ref writer);
                previous = k;
            }

            // The zeros and the already nonzero coefficients after the last code belong to the end of the band
            var rest = _band & ~((2UL << previous) - 1);
            var restBits = BitOperations.PopCount(previouslyNonZero & rest);
            if ((zeros & rest) != 0 || restBits > 0)
            {
                _endOfBandRun++;
                _pendingBitCount += restBits;
                if (_endOfBandRun == MaxEndOfBandRun || _pendingBitCount > MaxPendingCorrectionBits)
                {
                    FlushEndOfBandRun<TPass>(table, ref writer);
                }
            }

            return;
        }

        var lastNew = newlyNonZero == 0 ? -1 : 63 - BitOperations.LeadingZeroCount(newlyNonZero);

        // The correction bits since the last code, the first one in the most significant position
        var blockBits = 0UL;
        var blockBitCount = 0;
        var run = 0;
        var mask = nonZero;
        while (mask != 0)
        {
            var k = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            run += k - previous - 1;
            previous = k;
            if (k <= lastNew)
            {
                while (run > 15)
                {
                    FlushEndOfBandRun<TPass>(table, ref writer);
                    EmitSymbol<TPass>(table, 0xF0, 0, 0, ref writer);
                    run -= 16;
                    EmitCorrectionBits<TPass>(blockBits, blockBitCount, ref writer);
                    blockBits = 0;
                    blockBitCount = 0;
                }
            }

            if ((previouslyNonZero & (1UL << k)) != 0)
            {
                blockBits = (blockBits << 1) | (((uint)Math.Abs((int)block[k]) >> al) & 1);
                blockBitCount++;
                continue;
            }

            FlushEndOfBandRun<TPass>(table, ref writer);
            EmitSymbol<TPass>(table, (run << 4) | 1, block[k] < 0 ? 0u : 1u, 1, ref writer);
            EmitCorrectionBits<TPass>(blockBits, blockBitCount, ref writer);
            blockBits = 0;
            blockBitCount = 0;
            run = 0;
        }

        if (previous < _scan.Se || blockBitCount > 0)
        {
            _endOfBandRun++;
            AppendPendingBits(blockBits, blockBitCount);
            _pendingBitCount += blockBitCount;
            if (_endOfBandRun == MaxEndOfBandRun || _pendingBitCount > MaxPendingCorrectionBits)
            {
                FlushEndOfBandRun<TPass>(table, ref writer);
            }
        }
    }

    /// <summary>Appends correction bits (at most 63, right-aligned) to those of the pending run of blocks.</summary>
    private void AppendPendingBits(ulong bits, int count)
    {
        if (count == 0)
            return;

        var index = _pendingBitCount >> 6;
        var used = _pendingBitCount & 63;
        var aligned = bits << (64 - count);
        _pendingBits[index] = used == 0 ? aligned : _pendingBits[index] | (aligned >> used);
        if (used + count > 64)
        {
            _pendingBits[index + 1] = aligned << (64 - used);
        }
    }

    private void FlushEndOfBandRun(int table, Span<byte> destination, ref int position)
    {
        if (_counting)
        {
            var none = default(JpegBlockBitWriter);
            FlushEndOfBandRun<CountingPass>(table, ref none);
            return;
        }

        var writer = new JpegBlockBitWriter(destination, position, _bits);
        FlushEndOfBandRun<WritingPass>(table, ref writer);
        position = writer.Complete(ref _bits);
    }

    /// <summary>Writes the pending run of blocks as an EOBn code (<c>n</c> additional bits give the run minus <c>2^n</c>), then its correction bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FlushEndOfBandRun<TPass>(int table, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        if (_endOfBandRun != 0)
        {
            WriteEndOfBandRun<TPass>(table, ref writer);
        }
    }

    private void WriteEndOfBandRun<TPass>(int table, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        var size = BitOperations.Log2((uint)_endOfBandRun);
        EmitSymbol<TPass>(table, size << 4, (uint)_endOfBandRun & ((1u << size) - 1), size, ref writer);
        _endOfBandRun = 0;
        if (!TPass.Counting)
        {
            // Whole words as two writes of 32 bits, then the rest
            var count = _pendingBitCount;
            var words = _pendingBits.AsSpan(0, (count + 63) >> 6);
            foreach (var word in words[..(count >> 6)])
            {
                writer.Write((uint)(word >> 32), 32);
                writer.Write((uint)word, 32);
            }

            var rest = count & 63;
            if (rest > 0)
            {
                EmitCorrectionBits<TPass>(words[^1] >> (64 - rest), rest, ref writer);
            }
        }

        _pendingBitCount = 0;
    }

    /// <summary>Gets the additional bits of a value of a size category: the magnitude, or its complement for a negative value (T.81 F.1.2.1.1).</summary>
    private static uint GetAdditionalBits(bool negative, uint magnitude, int size) => negative ? ~magnitude & ((1u << size) - 1) : magnitude;

    /// <summary>Counts a symbol, or writes its code followed by <paramref name="length"/> additional bits (at most 16).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EmitSymbol<TPass>(int table, int symbol, uint bits, int length, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        if (TPass.Counting)
        {
            _histograms[table][symbol]++;
            return;
        }

        var code = _tables[table]!;
        writer.Write((code.GetCode(symbol) << length) | bits, code.GetLength(symbol) + length);
    }

    /// <summary>Writes correction bits (at most 63, right-aligned, the first one in the most significant position).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitCorrectionBits<TPass>(ulong bits, int count, ref JpegBlockBitWriter writer)
        where TPass : struct, IPass
    {
        if (TPass.Counting || count == 0)
            return;

        if (count > 32)
        {
            writer.Write((uint)(bits >> 32), count - 32);
            writer.Write((uint)bits, 32);
        }
        else
        {
            writer.Write((uint)bits, count);
        }
    }

    /// <summary>Selects, at compile time, whether a pass counts the symbols or writes them.</summary>
    private interface IPass
    {
        static abstract bool Counting { get; }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct CountingPass : IPass
    {
        public static bool Counting => true;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct WritingPass : IPass
    {
        public static bool Counting => false;
    }
}
