using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the blocks of one progressive Huffman scan (ITU-T T.81 section G.1.2) into the coefficients of the components
/// (<see cref="JpegFrameComponent.GetCoefficients"/>): DC first scans (prediction, values shifted left by <c>Al</c>), DC
/// refinements (one bit per block), AC first scans of a spectral band (run-lengths, EOB runs spanning blocks) and AC
/// refinements (sign bits of newly nonzero coefficients and correction bits of the others, ZRL and EOB runs).
/// </summary>
/// <remarks>
/// <para>
/// The walker validated the spectral selection, the successive approximation and the progression of every coefficient
/// (<see cref="JpegStructureParser"/>). Validation here (all <see cref="InvalidImageContentException"/>), besides the scan
/// structure of <see cref="JpegScanDecoder"/>: invalid Huffman codes, DC categories above 11, AC magnitudes above 10 (15
/// and 14 for 12-bit samples), coefficients whose value shifted by <c>Al</c> does not fit in 16 bits (DC: two's complement range; AC:
/// magnitude at most 32,767, so that refinements cannot overflow), runs past the end of the band, and refinement
/// magnitudes other than 1.
/// </para>
/// <para>
/// An EOB run is reset at every restart marker; the part of a run that extends past the end of the scan or of a restart
/// interval is ignored, like the other bits after the last MCU. A ZRL that ends past the band is tolerated like in
/// sequential scans.
/// </para>
/// </remarks>
internal sealed class JpegProgressiveScanDecoder : JpegScanDecoder
{
    // For each byte j of the mask of the nonzero coefficients in natural order and each value of it, the bits of those
    // coefficients in zig-zag order (bit k: the coefficient k of the zig-zag sequence)
    private static readonly ulong[] ZigZagMasks = CreateZigZagMasks();

    [ThreadStatic]
    private static bool s_testUseReferenceRefinement;

    private readonly int _start;
    private readonly int _end;
    private readonly int _low;
    private readonly bool _refinement;
    private readonly bool _referenceRefinement;
    private int _endOfBandRun;

    /// <param name="components">The scan components with their tables, in scan order.</param>
    /// <param name="reader">The entropy reader (reset by this scan).</param>
    /// <param name="mcusPerLine">For interleaved (DC) scans, the MCUs per MCU row of the frame.</param>
    /// <param name="mcuRows">For interleaved (DC) scans, the MCU rows of the frame.</param>
    /// <param name="restartInterval">The restart interval in MCUs, or 0.</param>
    /// <param name="start">The first coefficient of the band (<c>Ss</c>, zig-zag order).</param>
    /// <param name="end">The last coefficient of the band (<c>Se</c>).</param>
    /// <param name="high">The bit position of the previous scan of the band (<c>Ah</c>), 0 for a first scan.</param>
    /// <param name="low">The bit position of this scan (<c>Al</c>).</param>
    /// <param name="cancellationToken">Checked after each MCU row.</param>
    public JpegProgressiveScanDecoder(ScanComponent[] components, JpegEntropyReader reader, int mcusPerLine, int mcuRows, int restartInterval, int start, int end, int high, int low, CancellationToken cancellationToken)
        : base(components, reader, mcusPerLine, mcuRows, restartInterval, mcuRowDecoded: null, cancellationToken)
    {
        _start = start;
        _end = end;
        _low = low;
        _refinement = high != 0;
        _referenceRefinement = s_testUseReferenceRefinement;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the scans created on the current thread decode AC refinements with
    /// <see cref="DecodeAcRefinementReference"/> (tests: both paths must agree bit for bit).
    /// </summary>
    internal static bool TestUseReferenceRefinement
    {
        get => s_testUseReferenceRefinement;
        set => s_testUseReferenceRefinement = value;
    }

    protected override void OnRestart()
    {
        base.OnRestart();
        _endOfBandRun = 0;
    }

    protected override void DecodeBlock(ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        var block = scanComponent.Component.GetCoefficients(blockRow, blockColumn);
        if (_start == 0)
        {
            if (_refinement)
            {
                // T.81 G.1.2.1: the next bit of the DC value (two's complement, as the first scan shifted it arithmetically)
                if (Reader.ReadBits(1) != 0)
                {
                    block[0] |= (short)(1 << _low);
                }
            }
            else
            {
                DecodeDcFirst(scanComponent, block);
            }
        }
        else if (_refinement)
        {
            if (_referenceRefinement)
            {
                DecodeAcRefinementReference(scanComponent.AcTable!, block);
            }
            else
            {
                DecodeAcRefinement(scanComponent.AcTable!, block);
            }
        }
        else
        {
            DecodeAcFirst(scanComponent.AcTable!, block);
        }
    }

    /// <summary>T.81 G.1.2.1: the DC difference from the prediction, in the point-transformed domain.</summary>
    private void DecodeDcFirst(ScanComponent scanComponent, Span<short> block)
    {
        var reader = Reader;
        var category = reader.DecodeHuffman(scanComponent.DcTable!);
        if (category > MaxDcCategory)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG DC coefficient category is greater than {MaxDcCategory}."));

        var dc = scanComponent.Predictor + (category == 0 ? 0 : reader.ReceiveExtend(category));
        var value = (long)dc << _low;
        if (value is < short.MinValue or > short.MaxValue)
            throw Invalid("The JPEG DC coefficient is out of range.");

        scanComponent.Predictor = dc;
        block[0] = (short)value;
    }

    /// <summary>T.81 G.1.2.2: run-length coding of the band, with end-of-band runs that span blocks.</summary>
    /// <remarks>
    /// The bit state is kept in a <see cref="JpegEntropyReader.Cursor"/> for the block. Short codes with small magnitudes
    /// are decoded with <see cref="JpegHuffmanTable.FastAc"/>, with the checks of the regular path in the same order.
    /// </remarks>
    private void DecodeAcFirst(JpegHuffmanTable table, Span<short> block)
    {
        if (_endOfBandRun > 0)
        {
            _endOfBandRun--;
            return;
        }

        var reader = Reader.GetCursor();
        var zigZag = JpegIdct.ZigZag;
        var fastAc = table.FastAc;
        var end = _end;
        var low = _low;
        for (var k = _start; k <= end;)
        {
            var fast = reader.TryDecodeFastAc(fastAc);
            if (fast != 0)
            {
                k += (fast >> 4) & 0x0F;
                if (k > end)
                    throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

                var shifted = (long)(fast >> 8) << low;
                if (Math.Abs(shifted) > short.MaxValue)
                    throw Invalid("The JPEG AC coefficient is out of range.");

                block[zigZag[k]] = (short)shifted;
                k++;
                continue;
            }

            var symbol = reader.DecodeHuffman(table);
            var run = symbol >> 4;
            var size = symbol & 0x0F;
            if (size == 0)
            {
                if (run != 15)
                {
                    // EOBn: this block and the next 2^n - 1 + (n extra bits) blocks end here
                    _endOfBandRun = (1 << run) - 1 + (run == 0 ? 0 : reader.ReadBits(run));
                    break;
                }

                k += 16; // ZRL
                continue;
            }

            k += run;
            if (k > end)
                throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

            if (size > MaxAcCategory)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG AC coefficient magnitude category is greater than {MaxAcCategory}."));

            var value = (long)reader.ReceiveExtend(size) << low;
            if (Math.Abs(value) > short.MaxValue)
                throw Invalid("The JPEG AC coefficient is out of range.");

            block[zigZag[k]] = (short)value;
            k++;
        }

        reader.Commit();
    }

    /// <summary>
    /// T.81 G.1.2.3: in the band, each symbol places one newly nonzero coefficient (magnitude <c>2^Al</c>, its sign bit) after
    /// <c>run</c> coefficients that are still zero; a correction bit follows for every coefficient already nonzero that the
    /// symbol passes over (or, for an EOB run, for every remaining one in each block of the run).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bit state is kept in a <see cref="JpegEntropyReader.Cursor"/> for the block. The coefficients that are already
    /// nonzero are a mask in zig-zag order, read once per block: refining never makes a coefficient zero, and the walk only
    /// moves forward, past the coefficients this scan makes nonzero. A run of coefficients is then skipped by clearing the
    /// lowest bits of the zero mask, and the correction bits of the nonzero coefficients it passes over are read together.
    /// </para>
    /// <para>
    /// The bits are read in the order of <see cref="DecodeAcRefinementReference"/>, the bit-at-a-time reference: the
    /// coefficients, and the errors with their messages, are the same. Only the coefficients refined before a truncation
    /// error differ, and the decoder never shows them (a frame is only shown as far as whole MCUs were decoded).
    /// </para>
    /// </remarks>
    private void DecodeAcRefinement(JpegHuffmanTable table, Span<short> block)
    {
        var reader = Reader.GetCursor();
        var positive = 1 << _low;
        var end = _end;
        var endOfBandRun = _endOfBandRun;
        var k = _start;
        var nonzero = GetNonzeroMask(block);
        if (endOfBandRun == 0)
        {
            while (k <= end)
            {
                var symbol = reader.DecodeHuffman(table);
                var run = symbol >> 4;
                var size = symbol & 0x0F;
                var value = 0;
                if (size != 0)
                {
                    if (size != 1)
                        throw Invalid("A JPEG AC refinement scan codes a magnitude other than 1.");

                    value = reader.ReadBits(1) != 0 ? positive : -positive;
                }
                else if (run != 15)
                {
                    // EOBn: the rest of this block and of the next blocks of the run only have correction bits
                    endOfBandRun = (1 << run) + (run == 0 ? 0 : reader.ReadBits(run));
                    break;
                }

                // Skip 'run' coefficients that are still zero (16 for ZRL), refining the nonzero ones on the way: the walk
                // stops at the zero coefficient that follows them, or past the band
                var zeros = ~nonzero & GetBandMask(k, end);
                for (; run > 0 && zeros != 0; run--)
                {
                    zeros &= zeros - 1;
                }

                var stop = zeros == 0 ? end + 1 : BitOperations.TrailingZeroCount(zeros);
                RefineAll(ref reader, block, nonzero & GetBandMask(k, stop - 1), positive);
                k = stop;
                if (value != 0)
                {
                    if (k > end)
                        throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

                    block[JpegIdct.ZigZag[k]] = (short)value;
                }

                k++;
            }
        }

        if (endOfBandRun > 0)
        {
            RefineAll(ref reader, block, nonzero & GetBandMask(k, end), positive);
            endOfBandRun--;
        }

        reader.Commit();
        _endOfBandRun = endOfBandRun;
    }

    /// <summary>The bit-at-a-time reference of <see cref="DecodeAcRefinement"/>.</summary>
    /// <remarks>The bit state is kept in a <see cref="JpegEntropyReader.Cursor"/> for the block.</remarks>
    private void DecodeAcRefinementReference(JpegHuffmanTable table, Span<short> block)
    {
        var reader = Reader.GetCursor();
        var zigZag = JpegIdct.ZigZag;
        var positive = 1 << _low;
        var end = _end;
        var endOfBandRun = _endOfBandRun;
        var k = _start;
        if (endOfBandRun == 0)
        {
            for (; k <= end; k++)
            {
                var symbol = reader.DecodeHuffman(table);
                var run = symbol >> 4;
                var size = symbol & 0x0F;
                var value = 0;
                if (size != 0)
                {
                    if (size != 1)
                        throw Invalid("A JPEG AC refinement scan codes a magnitude other than 1.");

                    value = reader.ReadBits(1) != 0 ? positive : -positive;
                }
                else if (run != 15)
                {
                    // EOBn: the rest of this block and of the next blocks of the run only have correction bits
                    endOfBandRun = (1 << run) + (run == 0 ? 0 : reader.ReadBits(run));
                    break;
                }

                // Skip 'run' coefficients that are still zero (16 for ZRL), refining the nonzero ones on the way
                for (; k <= end; k++)
                {
                    ref var coefficient = ref block[zigZag[k]];
                    if (coefficient != 0)
                    {
                        Refine(ref reader, ref coefficient, positive);
                    }
                    else
                    {
                        if (run == 0)
                            break;

                        run--;
                    }
                }

                if (value != 0)
                {
                    if (k > end)
                        throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

                    block[zigZag[k]] = (short)value;
                }
            }
        }

        if (endOfBandRun > 0)
        {
            for (; k <= end; k++)
            {
                ref var coefficient = ref block[zigZag[k]];
                if (coefficient != 0)
                {
                    Refine(ref reader, ref coefficient, positive);
                }
            }

            endOfBandRun--;
        }

        reader.Commit();
        _endOfBandRun = endOfBandRun;
    }

    /// <summary>
    /// Applies the correction bits of the coefficients of a mask (zig-zag order), which are read in that order: adds
    /// <c>2^Al</c> to the magnitude of each coefficient whose bit is set, like <see cref="Refine"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RefineAll(ref JpegEntropyReader.Cursor reader, Span<short> block, ulong mask, int bit)
    {
        var count = BitOperations.PopCount(mask);
        var zigZag = JpegIdct.ZigZag;
        while (count > 0)
        {
            var length = Math.Min(count, 16);
            var bits = reader.ReadBits(length);
            count -= length;
            for (var shift = length - 1; shift >= 0; shift--)
            {
                ref var coefficient = ref block[zigZag[BitOperations.TrailingZeroCount(mask)]];
                mask &= mask - 1;

                // Without branches on the bits: the magnitude grows by 2^Al when the bit is set and that bit of the
                // magnitude is clear (the bits below the previous position are zero, so it stays at most 32,767)
                int value = coefficient;
                var sign = value >> 31;
                var clear = (int)((uint)((((value ^ sign) - sign) & bit) - 1) >> 31);
                var add = (bits >> shift) & clear;
                coefficient = (short)(value + (((bit & -add) ^ sign) - sign));
            }
        }
    }

    /// <summary>Applies the correction bit of a coefficient that is already nonzero: adds <c>2^Al</c> to its magnitude when set.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Refine(ref JpegEntropyReader.Cursor reader, ref short coefficient, int bit)
    {
        // The bits below the previous position are zero, so the magnitude stays at most 32,767 (checked by the first scan)
        if (reader.ReadBits(1) != 0 && (Math.Abs((int)coefficient) & bit) == 0)
        {
            coefficient = (short)(coefficient >= 0 ? coefficient + bit : coefficient - bit);
        }
    }

    /// <summary>Gets the bits <paramref name="first"/> to <paramref name="last"/> (inclusive) of a mask; none when <paramref name="first"/> is greater.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetBandMask(int first, int last) => first > last ? 0 : (ulong.MaxValue << first) & (ulong.MaxValue >> (63 - last));

    /// <summary>Gets the mask of the nonzero coefficients of a block, in zig-zag order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetNonzeroMask(Span<short> block)
    {
        ulong natural;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var start = ref unsafe(MemoryMarshal.GetReference(block));
            natural = (ulong)GetNonzeroBits(ref start, 0)
                | ((ulong)GetNonzeroBits(ref start, 16) << 16)
                | ((ulong)GetNonzeroBits(ref start, 32) << 32)
                | ((ulong)GetNonzeroBits(ref start, 48) << 48);
        }
        else
        {
            natural = 0;
            for (var i = 0; i < 64; i++)
            {
                natural |= (block[i] != 0 ? 1UL : 0) << i;
            }
        }

        var tables = ZigZagMasks;
        return tables[(int)(natural & 0xFF)]
            | tables[256 + (int)((natural >> 8) & 0xFF)]
            | tables[512 + (int)((natural >> 16) & 0xFF)]
            | tables[768 + (int)((natural >> 24) & 0xFF)]
            | tables[1024 + (int)((natural >> 32) & 0xFF)]
            | tables[1280 + (int)((natural >> 40) & 0xFF)]
            | tables[1536 + (int)((natural >> 48) & 0xFF)]
            | tables[1792 + (int)(natural >> 56)];

        // Bit i: the coefficient offset + i is nonzero
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint GetNonzeroBits(ref short start, nuint offset)
        {
            var low = Vector128.Equals(unsafe(Vector128.LoadUnsafe(ref start, offset)), Vector128<short>.Zero);
            var high = Vector128.Equals(unsafe(Vector128.LoadUnsafe(ref start, offset + 8)), Vector128<short>.Zero);
            return ~Vector128.Narrow(low, high).ExtractMostSignificantBits() & 0xFFFF;
        }
    }

    private static ulong[] CreateZigZagMasks()
    {
        var zigZag = JpegIdct.ZigZag;
        var positions = new int[64];
        for (var k = 0; k < 64; k++)
        {
            positions[zigZag[k]] = k;
        }

        var masks = new ulong[8 * 256];
        for (var group = 0; group < 8; group++)
        {
            for (var value = 0; value < 256; value++)
            {
                ulong mask = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((value & (1 << bit)) != 0)
                    {
                        mask |= 1UL << positions[(group * 8) + bit];
                    }
                }

                masks[(group * 256) + value] = mask;
            }
        }

        return masks;
    }
}
