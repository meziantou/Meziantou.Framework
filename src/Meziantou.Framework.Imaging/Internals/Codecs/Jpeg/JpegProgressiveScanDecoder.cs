using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

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

    // The natural indexes of the coefficients of the band
    private readonly ulong _naturalBand;
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
        for (var k = start; k <= end; k++)
        {
            _naturalBand |= 1UL << JpegIdct.ZigZag[k];
        }
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
        var block = scanComponent.GetCoefficients(blockRow, blockColumn);
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
            // The blocks whose bytes are buffered are decoded by DecodeMcus
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

    /// <remarks>
    /// <para>
    /// The MCUs of a DC scan whose bytes are buffered (<see cref="JpegScanDecoder.UncheckedReads"/>) share one
    /// <see cref="JpegEntropyReader.UncheckedCursor"/>, up to the end of their row.
    /// </para>
    /// <para>
    /// So do the blocks of an AC refinement scan: a block of an EOB run, which only has the correction bits of its nonzero
    /// coefficients (most often none), is refined in the loop; the other blocks are decoded by
    /// <see cref="DecodeAcRefinementDeferred"/> (x64 with BMI2) or <see cref="DecodeAcRefinementUnchecked"/>.
    /// </para>
    /// </remarks>
    protected override int DecodeMcus(int mcuX, int mcuY, int count)
    {
        if (_start == 0)
            return DecodeDcMcus(mcuX, mcuY, count);

        if (!_refinement)
            return DecodeAcFirstBlocks(mcuX, mcuY, count);

        if (!_referenceRefinement)
            return DecodeAcRefinementBlocks(mcuX, mcuY, count);

        return base.DecodeMcus(mcuX, mcuY, count);
    }

    /// <summary>
    /// The blocks of an AC first scan whose bytes are buffered, like <see cref="DecodeAcFirst"/>: one lookup of
    /// <see cref="JpegHuffmanTable.FastLookup"/> per symbol, which also gives the coefficient when its magnitude bits fit,
    /// and the same checks in the same order. The blocks of an EOB run that the row has are counted as decoded; the rest
    /// of the run is left to the scan (<see cref="JpegScanDecoder.SkippableMcus"/>).
    /// </summary>
    private int DecodeAcFirstBlocks(int mcuX, int mcuY, int count)
    {
        Debug.Assert(SkippableMcus == 0);
        var scanComponent = Components[0];
        var table = scanComponent.AcTable!;
        var lookup = table.FastLookup;
        var reader = Reader;
        var cursor = reader.GetUncheckedCursor();
        ref var zigZag = ref unsafe(MemoryMarshal.GetReference(JpegIdct.ZigZag));
        var start = _start;
        var end = _end;
        var low = _low;
        var skippable = 0;
        var decoded = 0;
        do
        {
            ref var coefficients = ref unsafe(MemoryMarshal.GetReference(scanComponent.GetCoefficients(mcuY, mcuX + decoded)));
            var k = start;
            do
            {
                cursor.Refill();
                var entry = cursor.Lookup(lookup);
                int symbol;
                var coefficient = 0;
                if ((entry & JpegHuffmanTable.FastLookupComplete) != 0)
                {
                    cursor.Consume(entry);
                    symbol = (entry >> JpegHuffmanTable.FastLookupSymbolShift) & 0xFF;
                    coefficient = entry >> JpegHuffmanTable.FastLookupValueShift;
                }
                else
                {
                    if (entry != 0)
                    {
                        cursor.Consume(entry);
                        symbol = (entry >> JpegHuffmanTable.FastLookupSymbolShift) & 0xFF;
                    }
                    else
                    {
                        symbol = cursor.DecodeLongCode(table);
                    }

                    var size = symbol & 0x0F;
                    if (size != 0)
                    {
                        if (k + (symbol >> 4) > end)
                            throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

                        if (size > MaxAcCategory)
                            throw CreateAcCategoryException();

                        coefficient = cursor.ReceiveExtend(size);
                    }
                }

                var run = symbol >> 4;
                if ((symbol & 0x0F) == 0)
                {
                    if (run != 15)
                    {
                        // EOBn: this block and the next 2^n - 1 + (n extra bits) blocks end here
                        skippable = (1 << run) - 1 + (run == 0 ? 0 : cursor.ReadBits(run));
                        break;
                    }

                    k += 16; // ZRL
                    continue;
                }

                k += run;
                if (k > end)
                    throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

                var shifted = (long)coefficient << low;
                if (Math.Abs(shifted) > short.MaxValue)
                    throw Invalid("The JPEG AC coefficient is out of range.");

                // The zig-zag position is at most 63 and the block has 64 coefficients
                unsafe { Unsafe.Add(ref coefficients, Unsafe.Add(ref zigZag, k)) = (short)shifted; }
                k++;
            }
            while (k <= end);

            decoded++;
            if (skippable > 0)
            {
                // The blocks of the run code nothing: as many as the row has are decoded
                var skipped = Math.Min(skippable, count - decoded);
                decoded += skipped;
                skippable -= skipped;
                if (skippable > 0)
                    break;
            }
        }
        while (decoded < count && HasUncheckedBytes(cursor));

        cursor.Commit(reader);
        SkippableMcus = skippable;
        return decoded;
    }

    private int DecodeDcMcus(int mcuX, int mcuY, int count)
    {
        var reader = Reader;
        var cursor = reader.GetUncheckedCursor();
        var decoded = 0;
        do
        {
            var x = mcuX + decoded;
            if (!IsInterleaved)
            {
                DecodeDcUnchecked(ref cursor, Components[0], mcuY, x);
            }
            else
            {
                foreach (var scanComponent in Components)
                {
                    var component = scanComponent.Component;
                    var h = component.HorizontalSampling;
                    var v = component.VerticalSampling;
                    for (var by = 0; by < v; by++)
                    {
                        for (var bx = 0; bx < h; bx++)
                        {
                            DecodeDcUnchecked(ref cursor, scanComponent, (mcuY * v) + by, (x * h) + bx);
                        }
                    }
                }
            }

            decoded++;
        }
        while (decoded < count && HasUncheckedBytes(cursor));

        cursor.Commit(reader);
        return decoded;
    }

    private int DecodeAcRefinementBlocks(int mcuX, int mcuY, int count)
    {
        // An AC scan has one component, and its MCU is one block
        var scanComponent = Components[0];
        var table = scanComponent.AcTable!;
        var reader = Reader;
        var naturalBand = _naturalBand;
        var positive = 1 << _low;
        var cursor = reader.GetUncheckedCursor();
        var decoded = 0;
        do
        {
            var block = scanComponent.GetCoefficients(mcuY, mcuX + decoded);
            if (_endOfBandRun > 0)
            {
                // The correction bits of the nonzero coefficients of the band, in zig-zag order
                var natural = JpegIdct.GetNonzeroMask(block) & naturalBand;
                if (natural != 0)
                {
                    RefineAll(ref cursor, block, ToZigZagMask(natural), positive);
                }

                _endOfBandRun--;
            }
            else
            {
                cursor.Commit(reader);
                if (Bmi2.X64.IsSupported)
                {
                    DecodeAcRefinementDeferred(table, scanComponent.AcLookup, block);
                }
                else
                {
                    DecodeAcRefinementUnchecked(table, block);
                }

                cursor = reader.GetUncheckedCursor();
            }

            decoded++;
        }
        while (decoded < count && HasUncheckedBytes(cursor));

        cursor.Commit(reader);
        return decoded;
    }

    /// <summary>
    /// The DC coefficient of a block of a DC scan, like <see cref="DecodeBlock"/>: its refinement bit, or the difference
    /// from the prediction with the code and the magnitude bits in one lookup of <see cref="JpegHuffmanTable.FastLookup"/>
    /// when they fit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DecodeDcUnchecked(ref JpegEntropyReader.UncheckedCursor cursor, ScanComponent scanComponent, int blockRow, int blockColumn)
    {
        ref var coefficient = ref unsafe(MemoryMarshal.GetReference(scanComponent.GetCoefficients(blockRow, blockColumn)));
        cursor.Refill();
        if (_refinement)
        {
            // T.81 G.1.2.1: the next bit of the DC value
            coefficient |= (short)(cursor.ReadBits(1) << _low);
            return;
        }

        var entry = cursor.Lookup(scanComponent.DcLookup);
        int difference;
        if ((entry & JpegHuffmanTable.FastLookupComplete) != 0)
        {
            cursor.Consume(entry);
            difference = entry >> JpegHuffmanTable.FastLookupValueShift;
        }
        else
        {
            int category;
            if (entry != 0)
            {
                cursor.Consume(entry);
                category = (entry >> JpegHuffmanTable.FastLookupSymbolShift) & 0xFF;
            }
            else
            {
                category = cursor.DecodeLongCode(scanComponent.DcTable!);
            }

            if (category > MaxDcCategory)
                throw CreateDcCategoryException();

            difference = category == 0 ? 0 : cursor.ReceiveExtend(category);
        }

        var dc = scanComponent.Predictor + difference;
        var value = (long)dc << _low;
        if (value is < short.MinValue or > short.MaxValue)
            throw CreateDcRangeException();

        scanComponent.Predictor = dc;
        coefficient = (short)value;
    }

    // The exceptions are created out of line, which keeps their message formatting out of the inlined loops
    private InvalidImageContentException CreateDcCategoryException() => Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG DC coefficient category is greater than {MaxDcCategory}."));

    private static InvalidImageContentException CreateDcRangeException() => Invalid("The JPEG DC coefficient is out of range.");

    private InvalidImageContentException CreateAcCategoryException() => Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG AC coefficient magnitude category is greater than {MaxAcCategory}."));

    /// <summary>T.81 G.1.2.1: the DC difference from the prediction, in the point-transformed domain.</summary>
    private void DecodeDcFirst(ScanComponent scanComponent, Span<short> block)
    {
        var reader = Reader;
        var category = reader.DecodeHuffman(scanComponent.DcTable!);
        if (category > MaxDcCategory)
            throw CreateDcCategoryException();

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
        // The blocks of an EOB run code nothing: the scan skips them (SkippableMcus)
        Debug.Assert(SkippableMcus == 0);

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
                    SkippableMcus = (1 << run) - 1 + (run == 0 ? 0 : reader.ReadBits(run));
                    break;
                }

                k += 16; // ZRL
                continue;
            }

            k += run;
            if (k > end)
                throw Invalid("The JPEG AC coefficients run past the end of the spectral band.");

            if (size > MaxAcCategory)
                throw CreateAcCategoryException();

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

    /// <summary>
    /// <see cref="DecodeAcRefinement"/> for a block whose bytes are buffered (<see cref="JpegScanDecoder.UncheckedReads"/>):
    /// one lookup of <see cref="JpegHuffmanTable.FastLookup"/> per symbol, which also gives the sign bit of a newly nonzero
    /// coefficient when it fits, and the same bits read in the same order, with the same errors.
    /// </summary>
    private void DecodeAcRefinementUnchecked(JpegHuffmanTable table, Span<short> block)
    {
        var lookup = table.FastLookup;
        var reader = Reader;
        var cursor = reader.GetUncheckedCursor();
        var positive = 1 << _low;
        var end = _end;
        var endOfBandRun = _endOfBandRun;
        var k = _start;
        var nonzero = GetNonzeroMask(block);
        if (endOfBandRun == 0)
        {
            while (k <= end)
            {
                cursor.Refill();
                var entry = cursor.Lookup(lookup);
                int symbol;
                var sign = 0;
                if ((entry & JpegHuffmanTable.FastLookupComplete) != 0)
                {
                    // The coefficient of a magnitude of one bit is the sign: -1 or 1
                    cursor.Consume(entry);
                    symbol = (entry >> JpegHuffmanTable.FastLookupSymbolShift) & 0xFF;
                    sign = entry >> JpegHuffmanTable.FastLookupValueShift;
                }
                else
                {
                    if (entry != 0)
                    {
                        cursor.Consume(entry);
                        symbol = (entry >> JpegHuffmanTable.FastLookupSymbolShift) & 0xFF;
                    }
                    else
                    {
                        symbol = cursor.DecodeLongCode(table);
                    }

                    if ((symbol & 0x0F) == 1)
                    {
                        sign = cursor.ReadBits(1) != 0 ? 1 : -1;
                    }
                }

                var run = symbol >> 4;
                var size = symbol & 0x0F;
                var value = 0;
                if (size != 0)
                {
                    if (size != 1)
                        throw Invalid("A JPEG AC refinement scan codes a magnitude other than 1.");

                    value = sign > 0 ? positive : -positive;
                }
                else if (run != 15)
                {
                    // EOBn: the rest of this block and of the next blocks of the run only have correction bits
                    endOfBandRun = (1 << run) + (run == 0 ? 0 : cursor.ReadBits(run));
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
                RefineAll(ref cursor, block, nonzero & GetBandMask(k, stop - 1), positive);
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
            RefineAll(ref cursor, block, nonzero & GetBandMask(k, end), positive);
            endOfBandRun--;
        }

        cursor.Commit(reader);
        _endOfBandRun = endOfBandRun;
    }

    /// <summary>
    /// <see cref="DecodeAcRefinementUnchecked"/> with the correction bits applied once, at the end of the block, instead of
    /// symbol by symbol (x64 with BMI2): the coefficients they refine are the ones that were nonzero before the block, which
    /// the walk does not change. The correction bits of each symbol are deposited at the positions of their coefficients
    /// (PDEP, in bit-reversed zig-zag order, where the first bit read is the highest), and the zero coefficient after a run is
    /// found by one deposit instead of a loop: the same bits are read in the same order, with the same errors.
    /// </summary>
    private void DecodeAcRefinementDeferred(JpegHuffmanTable table, int[] lookup, Span<short> block)
    {
        var reader = Reader;
        var cursor = reader.GetUncheckedCursor();
        var positive = 1 << _low;
        var end = _end;
        var endOfBandRun = _endOfBandRun;
        var k = _start;

        // Only the coefficients of the band are refined: often none of them is nonzero yet
        var natural = JpegIdct.GetNonzeroMask(block) & _naturalBand;
        ulong nonzero = 0;
        ulong reversedNonzero = 0;
        if (natural != 0)
        {
            nonzero = ToZigZagMask(natural);
            reversedNonzero = ReverseBits(nonzero);
        }

        // The zero coefficients of the band up to its end (the positions from k are taken at each symbol)
        var zeroMask = ~nonzero & (ulong.MaxValue >> (63 - end));

        // The coefficients whose correction bit is set, at bit 63 - k for the zig-zag position k
        ulong corrections = 0;
        if (endOfBandRun == 0)
        {
            while (k <= end)
            {
                cursor.Refill();
                var entry = cursor.Lookup(lookup);
                int symbol;
                var sign = 0;
                if ((entry & JpegHuffmanTable.FastLookupComplete) != 0)
                {
                    cursor.Consume(entry);
                    symbol = (entry >> JpegHuffmanTable.FastLookupSymbolShift) & 0xFF;
                    sign = entry >> JpegHuffmanTable.FastLookupValueShift;
                }
                else
                {
                    if (entry != 0)
                    {
                        cursor.Consume(entry);
                        symbol = (entry >> JpegHuffmanTable.FastLookupSymbolShift) & 0xFF;
                    }
                    else
                    {
                        symbol = cursor.DecodeLongCode(table);
                    }

                    if ((symbol & 0x0F) == 1)
                    {
                        sign = (cursor.ReadBits(1) << 1) - 1;
                    }
                }

                var run = symbol >> 4;
                var size = symbol & 0x0F;
                var value = 0;
                if (size != 0)
                {
                    if (size != 1)
                        throw Invalid("A JPEG AC refinement scan codes a magnitude other than 1.");

                    // 2^Al with the sign (1 or -1), without a branch on it
                    var negative = sign >> 31;
                    value = (positive ^ negative) - negative;
                }
                else if (run != 15)
                {
                    // EOBn: the rest of this block and of the next blocks of the run only have correction bits
                    endOfBandRun = (1 << run) + (run == 0 ? 0 : cursor.ReadBits(run));
                    break;
                }

                // The zero coefficient after 'run' zero coefficients (16 for ZRL): the run + 1st zero of the band from k,
                // if any (k is at most 63 here; the trailing zero count of no bit is 64)
                var next = Bmi2.X64.ParallelBitDeposit(1UL << run, zeroMask & (ulong.MaxValue << k));
                var stop = Math.Min(BitOperations.TrailingZeroCount(next), end + 1);

                // The nonzero coefficients from k to stop - 1: the bits 64 - stop to 63 - k of the reversed mask (none
                // when stop is k)
                corrections |= ReadCorrections(ref cursor, reversedNonzero & (ulong.MaxValue << (64 - stop)) & (ulong.MaxValue >> k));
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
            corrections |= ReadCorrections(ref cursor, reversedNonzero & GetBandMask(63 - end, 63 - k));
            endOfBandRun--;
        }

        cursor.Commit(reader);
        _endOfBandRun = endOfBandRun;
        ApplyCorrections(block, corrections, positive);

        // The correction bits of the coefficients of a mask (bit-reversed zig-zag order), deposited at their positions
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong ReadCorrections(ref JpegEntropyReader.UncheckedCursor cursor, ulong reversedMask)
        {
            var count = BitOperations.PopCount(reversedMask);
            if (count == 0)
                return 0;

            // The first bit read is the one of the lowest zig-zag position: the highest bit of the reversed mask
            cursor.Refill();
            ulong bits;
            if (count <= 32)
            {
                bits = cursor.PeekAndSkip(count);
            }
            else
            {
                bits = cursor.PeekAndSkip(32) << (count - 32);
                cursor.Refill();
                bits |= cursor.PeekAndSkip(count - 32);
            }

            return Bmi2.X64.ParallelBitDeposit(bits, reversedMask);
        }
    }

    /// <summary>
    /// Adds <c>2^Al</c> to the magnitude of the coefficients of a mask (bit 63 - k for the zig-zag position k) whose that bit
    /// is clear, as the correction bits of <see cref="RefineAll(ref JpegEntropyReader.Cursor, Span{short}, ulong, int)"/> do.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyCorrections(Span<short> block, ulong reversedMask, int bit)
    {
        var mask = reversedMask;
        Debug.Assert(block.Length == 64);
        ref var coefficients = ref unsafe(MemoryMarshal.GetReference(block));
        ref var zigZag = ref unsafe(MemoryMarshal.GetReference(JpegIdct.ZigZag));
        while (mask != 0)
        {
            // The positions of the mask are zig-zag positions (at most 63), and the natural indexes are at most 63
            ref var coefficient = ref unsafe(Unsafe.Add(ref coefficients, Unsafe.Add(ref zigZag, 63 - BitOperations.TrailingZeroCount(mask))));
            mask &= mask - 1;
            int value = coefficient;
            var sign = value >> 31;
            var clear = (int)((uint)((((value ^ sign) - sign) & bit) - 1) >> 31);
            coefficient = (short)(value + (((bit & -clear) ^ sign) - sign));
        }
    }

    /// <summary>Reverses the order of the bits of a mask.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReverseBits(ulong value)
    {
        value = BinaryPrimitives.ReverseEndianness(value);
        value = ((value >> 4) & 0x0F0F_0F0F_0F0F_0F0FUL) | ((value & 0x0F0F_0F0F_0F0F_0F0FUL) << 4);
        value = ((value >> 2) & 0x3333_3333_3333_3333UL) | ((value & 0x3333_3333_3333_3333UL) << 2);
        return ((value >> 1) & 0x5555_5555_5555_5555UL) | ((value & 0x5555_5555_5555_5555UL) << 1);
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

    /// <inheritdoc cref="RefineAll(ref JpegEntropyReader.Cursor, Span{short}, ulong, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RefineAll(ref JpegEntropyReader.UncheckedCursor reader, Span<short> block, ulong mask, int bit)
    {
        Debug.Assert(block.Length == 64);
        ref var coefficients = ref unsafe(MemoryMarshal.GetReference(block));
        ref var zigZag = ref unsafe(MemoryMarshal.GetReference(JpegIdct.ZigZag));
        var count = BitOperations.PopCount(mask);
        while (count > 0)
        {
            // Up to 32 bits at once, the first one at the top
            var length = Math.Min(count, 32);
            reader.Refill();
            var bits = reader.PeekAndSkip(length) << (64 - length);
            count -= length;
            for (var i = 0; i < length; i++)
            {
                // The positions of the mask are zig-zag positions (at most 63), and the natural indexes are at most 63
                ref var coefficient = ref unsafe(Unsafe.Add(ref coefficients, Unsafe.Add(ref zigZag, BitOperations.TrailingZeroCount(mask))));
                mask &= mask - 1;
                int value = coefficient;
                var sign = value >> 31;
                var clear = (int)((uint)((((value ^ sign) - sign) & bit) - 1) >> 31);
                var add = (int)(bits >> 63) & clear;
                bits <<= 1;
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
    private static ulong GetNonzeroMask(Span<short> block) => ToZigZagMask(JpegIdct.GetNonzeroMask(block));

    /// <summary>Converts a mask of coefficients in natural order (bit <c>i</c>: the coefficient <c>i</c>) to zig-zag order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ToZigZagMask(ulong natural)
    {
        var tables = ZigZagMasks;
        return tables[(int)(natural & 0xFF)]
            | tables[256 + (int)((natural >> 8) & 0xFF)]
            | tables[512 + (int)((natural >> 16) & 0xFF)]
            | tables[768 + (int)((natural >> 24) & 0xFF)]
            | tables[1024 + (int)((natural >> 32) & 0xFF)]
            | tables[1280 + (int)((natural >> 40) & 0xFF)]
            | tables[1536 + (int)((natural >> 48) & 0xFF)]
            | tables[1792 + (int)(natural >> 56)];
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
