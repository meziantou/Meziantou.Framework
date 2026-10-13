using System.Numerics;

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
/// </remarks>
internal sealed class JpegProgressiveScanEncoder
{
    /// <summary>
    /// The worst-case size of what coding one block appends: a pending end-of-band run (16 + 14 bits) with its correction
    /// bits (at most <see cref="MaxPendingCorrectionBits"/> + 63), 63 coefficients (at most 16 + 10 bits each), three ZRL
    /// codes and the correction bits of the block, doubled for byte stuffing.
    /// </summary>
    internal const int MaxBlockBytes = 1024;

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
    private readonly byte[] _pendingBits = new byte[MaxPendingCorrectionBits + 64];
    private readonly byte[] _blockBits = new byte[64];
    private readonly byte[] _magnitudes = new byte[64];
    private JpegProgressiveScan _scan;
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

    /// <summary>Codes the part of a block that belongs to the scan.</summary>
    /// <param name="component">The index of the component of the block.</param>
    /// <param name="block">The 64 quantized coefficients, in zig-zag order.</param>
    /// <param name="destination">The destination, with <see cref="MaxBlockBytes"/> bytes available; unused when counting.</param>
    /// <param name="position">The write position in <paramref name="destination"/>, advanced.</param>
    public void CodeBlock(int component, ReadOnlySpan<short> block, Span<byte> destination, ref int position)
    {
        var table = GetTableIndex(component);
        if (_scan.Ss == 0)
        {
            if (_scan.Ah == 0)
            {
                CodeDcFirst(component, table, block[0], destination, ref position);
            }
            else
            {
                // The bit Al of the coefficient; the shift keeps the two's complement bits of negative values (G.1.2.1)
                EmitBits(destination, ref position, (uint)(block[0] >> _scan.Al) & 1, 1);
            }
        }
        else if (_scan.Ah == 0)
        {
            CodeAcFirst(table, block, destination, ref position);
        }
        else
        {
            CodeAcRefinement(table, block, destination, ref position);
        }
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

    /// <summary>Gets a table built by the counting pass of the scan.</summary>
    public JpegHuffmanEncoder GetTable(int index) => _tables[index] ?? throw new InvalidOperationException("The Huffman table of the scan is not built.");

    /// <summary>DC first scan: the difference of the shifted coefficient with the one of the previous block of the component (T.81 G.1.2.1, F.1.2.1).</summary>
    private void CodeDcFirst(int component, int table, int coefficient, Span<byte> destination, ref int position)
    {
        // The point transform of DC coefficients is an arithmetic shift
        var value = coefficient >> _scan.Al;
        var difference = value - _predictions[component];
        _predictions[component] = value;
        var magnitude = (uint)Math.Abs(difference);
        var size = 32 - BitOperations.LeadingZeroCount(magnitude);
        EmitSymbol(table, size, destination, ref position);
        if (size > 0)
        {
            EmitBits(destination, ref position, GetAdditionalBits(difference < 0, magnitude, size), size);
        }
    }

    /// <summary>
    /// AC first scan (T.81 G.1.2.2): like a sequential block restricted to the band, on the magnitudes divided by
    /// <c>2^Al</c>, except that the end of the band is not coded block by block: consecutive blocks that end early are
    /// counted and coded as one run.
    /// </summary>
    private void CodeAcFirst(int table, ReadOnlySpan<short> block, Span<byte> destination, ref int position)
    {
        var run = 0;
        for (var k = (int)_scan.Ss; k <= _scan.Se; k++)
        {
            int coefficient = block[k];
            var magnitude = (uint)Math.Abs(coefficient) >> _scan.Al;
            if (magnitude == 0)
            {
                run++;
                continue;
            }

            FlushEndOfBandRun(table, destination, ref position);
            while (run > 15)
            {
                EmitSymbol(table, 0xF0, destination, ref position);
                run -= 16;
            }

            var size = 32 - BitOperations.LeadingZeroCount(magnitude);
            EmitSymbol(table, (run << 4) | size, destination, ref position);
            EmitBits(destination, ref position, GetAdditionalBits(coefficient < 0, magnitude, size), size);
            run = 0;
        }

        if (run > 0)
        {
            _endOfBandRun++;
            if (_endOfBandRun == MaxEndOfBandRun)
            {
                FlushEndOfBandRun(table, destination, ref position);
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
    private void CodeAcRefinement(int table, ReadOnlySpan<short> block, Span<byte> destination, ref int position)
    {
        // Magnitudes at bit Al and the last coefficient that becomes nonzero: zeros after it belong to the end of the band
        var magnitudes = _magnitudes.AsSpan();
        var lastNew = -1;
        for (var k = (int)_scan.Ss; k <= _scan.Se; k++)
        {
            var magnitude = (uint)Math.Abs((int)block[k]) >> _scan.Al;

            // Above 1, only the correction bit matters: it is kept in the low bit
            magnitudes[k] = magnitude > 1 ? (byte)(2 | (magnitude & 1)) : (byte)magnitude;
            if (magnitude == 1)
            {
                lastNew = k;
            }
        }

        var run = 0;
        var blockBitCount = 0;
        for (var k = (int)_scan.Ss; k <= _scan.Se; k++)
        {
            var magnitude = magnitudes[k];
            if (magnitude == 0)
            {
                run++;
                continue;
            }

            while (run > 15 && k <= lastNew)
            {
                FlushEndOfBandRun(table, destination, ref position);
                EmitSymbol(table, 0xF0, destination, ref position);
                run -= 16;
                EmitCorrectionBits(_blockBits.AsSpan(0, blockBitCount), destination, ref position);
                blockBitCount = 0;
            }

            if (magnitude > 1)
            {
                _blockBits[blockBitCount++] = (byte)(magnitude & 1);
                continue;
            }

            FlushEndOfBandRun(table, destination, ref position);
            EmitSymbol(table, (run << 4) | 1, destination, ref position);
            EmitBits(destination, ref position, block[k] < 0 ? 0u : 1u, 1);
            EmitCorrectionBits(_blockBits.AsSpan(0, blockBitCount), destination, ref position);
            blockBitCount = 0;
            run = 0;
        }

        if (run > 0 || blockBitCount > 0)
        {
            _endOfBandRun++;
            _blockBits.AsSpan(0, blockBitCount).CopyTo(_pendingBits.AsSpan(_pendingBitCount));
            _pendingBitCount += blockBitCount;
            if (_endOfBandRun == MaxEndOfBandRun || _pendingBitCount > MaxPendingCorrectionBits)
            {
                FlushEndOfBandRun(table, destination, ref position);
            }
        }
    }

    /// <summary>Writes the pending run of blocks as an EOBn code (<c>n</c> additional bits give the run minus <c>2^n</c>), then its correction bits.</summary>
    private void FlushEndOfBandRun(int table, Span<byte> destination, ref int position)
    {
        if (_endOfBandRun == 0)
            return;

        var size = BitOperations.Log2((uint)_endOfBandRun);
        EmitSymbol(table, size << 4, destination, ref position);
        if (size > 0)
        {
            EmitBits(destination, ref position, (uint)_endOfBandRun & ((1u << size) - 1), size);
        }

        _endOfBandRun = 0;
        EmitCorrectionBits(_pendingBits.AsSpan(0, _pendingBitCount), destination, ref position);
        _pendingBitCount = 0;
    }

    /// <summary>Gets the additional bits of a value of a size category: the magnitude, or its complement for a negative value (T.81 F.1.2.1.1).</summary>
    private static uint GetAdditionalBits(bool negative, uint magnitude, int size) => negative ? ~magnitude & ((1u << size) - 1) : magnitude;

    private void EmitSymbol(int table, int symbol, Span<byte> destination, ref int position)
    {
        if (_counting)
        {
            _histograms[table][symbol]++;
            return;
        }

        var code = GetTable(table);
        _bits.Write(destination, ref position, code.GetCode(symbol), code.GetLength(symbol));
    }

    private void EmitBits(Span<byte> destination, ref int position, uint bits, int length)
    {
        if (!_counting)
        {
            _bits.Write(destination, ref position, bits, length);
        }
    }

    private void EmitCorrectionBits(ReadOnlySpan<byte> bits, Span<byte> destination, ref int position)
    {
        if (_counting)
            return;

        foreach (var bit in bits)
        {
            _bits.Write(destination, ref position, bit, 1);
        }
    }
}
