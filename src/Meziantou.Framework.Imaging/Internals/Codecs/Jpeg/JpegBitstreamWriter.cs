using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes a JPEG file from its quantized coefficients and from the description of everything else it holds
/// (<see cref="JpegBitstreamModel"/>): the markers in their order, the tables as the file defines them, and each scan,
/// sequential or progressive, coded as ITU-T T.81 describes (annexes F and G), with the choices the description records
/// where an encoder has one.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Where an encoder has a choice: a run of end-of-band symbols is written when it reaches 32767 blocks, when its
/// correction bits could no longer be buffered, at a restart marker and at the end of the scan, and at the blocks the scan
/// lists; a block ends with the runs of 16 zeros the scan lists, which a decoder does not need; the last byte of an
/// interval is padded with ones, or with the bits the description lists.
/// </description></item>
/// <item><description>
/// The file is written to memory, rented from the scope: nothing is given to the caller before the whole file is known to
/// be writable.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegBitstreamWriter : IDisposable
{
    private const int MaxEndOfBandRun = 0x7FFF;

    /// <summary>The number of correction bits above which a run of end-of-band symbols is written.</summary>
    private const int MaxCorrectionBits = (1 << 16) - 64 + 1;

    private readonly JpegBitstreamModel _model;
    private readonly JpegCoefficientStore _coefficients;
    private readonly int[] _componentChannels;
    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly JpegHuffmanEncoder?[] _dcTables = new JpegHuffmanEncoder?[4];
    private readonly JpegHuffmanEncoder?[] _acTables = new JpegHuffmanEncoder?[4];
    private readonly int[] _predictions = new int[4];
    private readonly List<byte> _correctionBits = [];
    private readonly List<byte> _blockCorrectionBits = [];
    private PooledBuffer? _buffer;
    private int _length;
    private JpegBitWriter _bits;
    private int _paddingBitIndex;
    private int _endOfBandRun;
    private JpegHuffmanEncoder? _endOfBandTable;

    /// <param name="model">The description of the file.</param>
    /// <param name="coefficients">The quantized coefficients.</param>
    /// <param name="componentChannels">For each component of the frame, the index of its coefficients in <paramref name="coefficients"/>.</param>
    /// <param name="scope">The scope of the file and of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows of minimum coded units.</param>
    public JpegBitstreamWriter(JpegBitstreamModel model, JpegCoefficientStore coefficients, ReadOnlySpan<int> componentChannels, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(coefficients);
        ArgumentNullException.ThrowIfNull(scope);
        _model = model;
        _componentChannels = componentChannels.ToArray();
        _coefficients = coefficients;
        _scope = scope;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Gets the bytes of the file, once <see cref="Write"/> returned.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer is null ? default : _buffer.RawBuffer.AsMemory(0, _length);

    /// <summary>Writes the file.</summary>
    /// <exception cref="InvalidImageContentException">The description is not the one of a JPEG file, or the coefficients cannot be coded with its tables.</exception>
    public void Write()
    {
        var model = _model;
        var applicationIndex = 0;
        var commentIndex = 0;
        var interMarkerIndex = 0;
        var quantizationIndex = 0;
        var huffmanIndex = 0;
        var scanIndex = 0;
        var hasFrame = false;
        Append([0xFF, 0xD8]);
        foreach (var marker in model.MarkerOrder)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            switch (marker)
            {
                case 0xC0 or 0xC1 or 0xC2:
                    if (hasFrame)
                        throw Invalid("it has two frame headers.");

                    hasFrame = true;
                    WriteFrameHeader(marker);
                    break;

                case 0xC4:
                    WriteHuffmanTables(ref huffmanIndex);
                    break;

                case 0xDB:
                    WriteQuantizationTables(ref quantizationIndex);
                    break;

                case 0xDD:
                    WriteSegment(marker, [(byte)(model.RestartInterval >> 8), (byte)model.RestartInterval]);
                    break;

                case 0xDA:
                    if (!hasFrame || scanIndex >= model.Scans.Count)
                        throw Invalid("a scan has no frame header or no description.");

                    WriteScan(model.Scans[scanIndex++]);
                    break;

                case >= 0xE0 and <= 0xEF:
                    if (applicationIndex >= model.ApplicationSegments.Count)
                        throw Invalid("an application segment has no content.");

                    Append([0xFF]);
                    Append(model.ApplicationSegments[applicationIndex++]);
                    break;

                case 0xFE:
                    if (commentIndex >= model.CommentSegments.Count)
                        throw Invalid("a comment has no content.");

                    Append([0xFF]);
                    Append(model.CommentSegments[commentIndex++]);
                    break;

                case JpegBitstreamModel.InterMarkerData:
                    if (interMarkerIndex >= model.InterMarkerSegments.Count)
                        throw Invalid("data between markers have no content.");

                    Append(model.InterMarkerSegments[interMarkerIndex++]);
                    break;

                case 0xD9:
                    Append([0xFF, 0xD9]);
                    Append(model.TailData);
                    return;

                default:
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the marker 0x{marker:X2} cannot be rebuilt."));
            }
        }

        throw Invalid("it has no end of image.");
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }

    private static InvalidImageContentException Invalid(string message)
        => new("The JPEG reconstruction data of the JPEG XL file are invalid: " + message, ImageFormat.JpegXl);

    private void WriteFrameHeader(byte marker)
    {
        var model = _model;
        var count = model.Components.Count;
        if (count is 0 or > 4 || model.Width is <= 0 or > 65535 || model.Height is <= 0 or > 65535)
            throw Invalid("the frame is not the one of a JPEG file.");

        Span<byte> payload = stackalloc byte[6 + (3 * 4)];
        payload[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(payload[1..], (ushort)model.Height);
        BinaryPrimitives.WriteUInt16BigEndian(payload[3..], (ushort)model.Width);
        payload[5] = (byte)count;
        for (var c = 0; c < count; c++)
        {
            var component = model.Components[c];
            if ((uint)component.QuantizationTable >= (uint)model.QuantizationTables.Count)
                throw Invalid("a component uses a quantization table that does not exist.");

            payload[6 + (3 * c)] = (byte)component.Id;
            payload[7 + (3 * c)] = (byte)((component.HorizontalSampling << 4) | component.VerticalSampling);
            payload[8 + (3 * c)] = (byte)model.QuantizationTables[component.QuantizationTable].Slot;
        }

        WriteSegment(marker, payload[..(6 + (3 * count))]);
    }

    /// <summary>Writes a segment of quantization tables: the tables of the file up to the one that ends its segment.</summary>
    private void WriteQuantizationTables(ref int index)
    {
        Span<byte> payload = stackalloc byte[4 * 129];
        var length = 0;
        var zigZag = JpegIdct.ZigZag;
        while (true)
        {
            if (index >= _model.QuantizationTables.Count || length + 129 > payload.Length)
                throw Invalid("a segment of quantization tables has no end.");

            var table = _model.QuantizationTables[index++];
            if (!table.HasValues)
                throw Invalid("a quantization table that no component uses cannot be rebuilt.");

            payload[length++] = (byte)((table.Precision << 4) | table.Slot);
            for (var k = 0; k < 64; k++)
            {
                var value = table.Values[zigZag[k]];
                if (table.Precision != 0)
                {
                    payload[length++] = (byte)(value >> 8);
                }
                else if (value > 255)
                {
                    throw Invalid("a quantization table of 8 bits has a larger value.");
                }

                payload[length++] = (byte)value;
            }

            if (table.IsLast)
                break;
        }

        WriteSegment(0xDB, payload[..length]);
    }

    /// <summary>Writes a segment of Huffman tables: the tables of the file up to the one that ends its segment, which become the tables of the scans that follow.</summary>
    private void WriteHuffmanTables(ref int index)
    {
        using var payload = _scope.Rent(8 * (17 + 256), AllocationKind.Temporary, clear: false);
        var span = payload.RawBuffer.AsSpan(0, 8 * (17 + 256));
        var length = 0;
        while (true)
        {
            if (index >= _model.HuffmanTables.Count || length + 17 + 256 > span.Length)
                throw Invalid("a segment of Huffman tables has no end.");

            var table = _model.HuffmanTables[index++];
            span[length++] = (byte)((table.IsAc ? 0x10 : 0) | table.Slot);
            table.Counts.CopyTo(span[length..]);
            length += 16;
            table.Values.CopyTo(span[length..]);
            length += table.Values.Length;
            try
            {
                (table.IsAc ? _acTables : _dcTables)[table.Slot] = new JpegHuffmanEncoder(table.Counts, table.Values);
            }
            catch (ArgumentException)
            {
                throw Invalid("a Huffman table is not a prefix code.");
            }

            if (table.IsLast)
                break;
        }

        WriteSegment(0xC4, span[..length]);
    }

    private void WriteSegment(byte marker, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)];
        Append(header);
        Append(payload);
    }

    private void WriteScan(JpegBitstreamScan scan)
    {
        var model = _model;
        var isProgressive = model.FrameMarker == 0xC2;
        var count = scan.Components.Count;
        if (count is 0 or > 4 || scan.SpectralStart > scan.SpectralEnd || scan.SpectralEnd > 63)
            throw Invalid("a scan is not the one of a JPEG file.");

        if (isProgressive ? (scan.SpectralStart == 0) != (scan.SpectralEnd == 0) || (scan.SpectralStart != 0 && count != 1) : scan.SpectralStart != 0 || scan.SpectralEnd != 63 || scan.SuccessiveHigh != 0 || scan.SuccessiveLow != 0)
            throw Invalid("a scan does not code the coefficients its frame allows.");

        Span<byte> header = stackalloc byte[1 + (2 * 4) + 3];
        header[0] = (byte)count;
        var maxHorizontal = 1;
        var maxVertical = 1;
        foreach (var component in model.Components)
        {
            maxHorizontal = Math.Max(maxHorizontal, component.HorizontalSampling);
            maxVertical = Math.Max(maxVertical, component.VerticalSampling);
        }

        for (var i = 0; i < count; i++)
        {
            var scanComponent = scan.Components[i];
            if ((uint)scanComponent.Component >= (uint)model.Components.Count)
                throw Invalid("a scan codes a component that does not exist.");

            header[1 + (2 * i)] = (byte)model.Components[scanComponent.Component].Id;
            header[2 + (2 * i)] = (byte)((scanComponent.DcTable << 4) | scanComponent.AcTable);
        }

        var tail = 1 + (2 * count);
        header[tail] = (byte)scan.SpectralStart;
        header[tail + 1] = (byte)scan.SpectralEnd;
        header[tail + 2] = (byte)((scan.SuccessiveHigh << 4) | scan.SuccessiveLow);
        WriteSegment(0xDA, header[..(tail + 3)]);

        // The minimum coded units: one block of the component of a scan of one component; otherwise, for each component, the
        // blocks of a rectangle of its sampling factors
        int unitsWide;
        int unitsHigh;
        if (count == 1)
        {
            var component = model.Components[scan.Components[0].Component];
            unitsWide = DivideCeiling(DivideCeiling(model.Width * component.HorizontalSampling, maxHorizontal), 8);
            unitsHigh = DivideCeiling(DivideCeiling(model.Height * component.VerticalSampling, maxVertical), 8);
        }
        else
        {
            unitsWide = DivideCeiling(model.Width, 8 * maxHorizontal);
            unitsHigh = DivideCeiling(model.Height, 8 * maxVertical);
        }

        _bits.Reset();
        _predictions.AsSpan().Clear();
        _endOfBandRun = 0;
        _endOfBandTable = null;
        _correctionBits.Clear();
        var restartsToGo = model.RestartInterval;
        var nextRestart = 0;
        var blockIndex = 0;
        var resetIndex = 0;
        var zeroRunIndex = 0;
        for (var unitY = 0; unitY < unitsHigh; unitY++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (var unitX = 0; unitX < unitsWide; unitX++)
            {
                if (model.RestartInterval > 0 && restartsToGo == 0)
                {
                    // A restart marker: the entropy coder starts again
                    WriteEndOfBandRun();
                    PadToByte();
                    Append([0xFF, (byte)(0xD0 + nextRestart)]);
                    nextRestart = (nextRestart + 1) & 7;
                    _predictions.AsSpan().Clear();
                    restartsToGo = model.RestartInterval;
                }

                foreach (var scanComponent in scan.Components)
                {
                    var component = model.Components[scanComponent.Component];
                    var horizontal = count == 1 ? 1 : component.HorizontalSampling;
                    var vertical = count == 1 ? 1 : component.VerticalSampling;
                    for (var v = 0; v < vertical; v++)
                    {
                        for (var h = 0; h < horizontal; h++)
                        {
                            var x = (unitX * horizontal) + h;
                            var y = (unitY * vertical) + v;
                            var channel = _componentChannels[scanComponent.Component];
                            if (x >= _coefficients.GetBlocksWide(channel) || y >= _coefficients.GetBlocksHigh(channel))
                                throw Invalid("a scan codes a block the image does not have.");

                            if (resetIndex < scan.ResetPoints.Count && scan.ResetPoints[resetIndex] == blockIndex)
                            {
                                WriteEndOfBandRun();
                                resetIndex++;
                            }

                            var extraZeroRuns = 0;
                            if (zeroRunIndex < scan.ExtraZeroRuns.Count && scan.ExtraZeroRuns[zeroRunIndex].Block == blockIndex)
                            {
                                extraZeroRuns = scan.ExtraZeroRuns[zeroRunIndex++].Count;
                            }

                            var block = _coefficients.GetBlock(channel, x, y);
                            if (!isProgressive)
                            {
                                WriteSequentialBlock(block, scanComponent, extraZeroRuns);
                            }
                            else if (scan.SpectralStart == 0)
                            {
                                WriteProgressiveDc(block, scanComponent, scan);
                            }
                            else if (scan.SuccessiveHigh == 0)
                            {
                                WriteProgressiveAcFirst(block, scanComponent, scan, extraZeroRuns);
                            }
                            else
                            {
                                WriteProgressiveAcRefinement(block, scanComponent, scan, extraZeroRuns);
                            }

                            blockIndex++;
                        }
                    }
                }

                restartsToGo--;
            }
        }

        if (resetIndex != scan.ResetPoints.Count || zeroRunIndex != scan.ExtraZeroRuns.Count)
            throw Invalid("a scan describes blocks it does not have.");

        WriteEndOfBandRun();
        PadToByte();
    }

    private static JpegHuffmanEncoder GetTable(JpegHuffmanEncoder?[] tables, int slot)
        => tables[slot] ?? throw Invalid("a scan uses a Huffman table that is not defined.");

    /// <summary>A block of a sequential scan (section F.1.2): the difference of its DC coefficient with the previous one, then its AC coefficients as runs of zeros and values.</summary>
    private void WriteSequentialBlock(ReadOnlySpan<short> block, JpegBitstreamScanComponent component, int extraZeroRuns)
    {
        var dc = GetTable(_dcTables, component.DcTable);
        var ac = GetTable(_acTables, component.AcTable);
        var zigZag = JpegIdct.ZigZag;
        var span = GetSpan(64 * 8);
        var position = 0;
        try
        {
            var difference = block[0] - _predictions[component.Component];
            _predictions[component.Component] = block[0];
            _bits.WriteValue(span, ref position, dc, symbolHigh: 0, difference);
            var run = 0;
            for (var k = 1; k < 64; k++)
            {
                var value = block[zigZag[k]];
                if (value == 0)
                {
                    run++;
                    continue;
                }

                while (run > 15)
                {
                    _bits.WriteSymbol(span, ref position, ac, 0xF0);
                    run -= 16;
                }

                _bits.WriteValue(span, ref position, ac, run, value);
                run = 0;
            }

            for (var i = 0; i < extraZeroRuns; i++)
            {
                _bits.WriteSymbol(span, ref position, ac, 0xF0);
                run -= 16;
            }

            if (run > 0)
            {
                _bits.WriteSymbol(span, ref position, ac, 0x00);
            }
        }
        catch (InvalidOperationException)
        {
            throw Invalid("a coefficient has no code in the Huffman table of its scan.");
        }

        _length += position;
    }

    /// <summary>The DC coefficient of a block in a progressive scan (section G.1.2.1): its high bits as a difference, or one more bit.</summary>
    private void WriteProgressiveDc(ReadOnlySpan<short> block, JpegBitstreamScanComponent component, JpegBitstreamScan scan)
    {
        var span = GetSpan(16);
        var position = 0;
        if (scan.SuccessiveHigh != 0)
        {
            _bits.WriteBits(span, ref position, (uint)((block[0] >> scan.SuccessiveLow) & 1), 1);
        }
        else
        {
            var value = block[0] >> scan.SuccessiveLow;
            var difference = value - _predictions[component.Component];
            _predictions[component.Component] = value;
            try
            {
                _bits.WriteValue(span, ref position, GetTable(_dcTables, component.DcTable), symbolHigh: 0, difference);
            }
            catch (InvalidOperationException)
            {
                throw Invalid("a coefficient has no code in the Huffman table of its scan.");
            }
        }

        _length += position;
    }

    /// <summary>The first scan of a band of AC coefficients of a block (section G.1.2.2): the high bits of the coefficients, and runs of blocks that have none.</summary>
    private void WriteProgressiveAcFirst(ReadOnlySpan<short> block, JpegBitstreamScanComponent component, JpegBitstreamScan scan, int extraZeroRuns)
    {
        var ac = GetTable(_acTables, component.AcTable);
        var zigZag = JpegIdct.ZigZag;
        var run = 0;
        for (var k = scan.SpectralStart; k <= scan.SpectralEnd; k++)
        {
            int value = block[zigZag[k]];
            if (value == 0)
            {
                run++;
                continue;
            }

            // The magnitude is shifted; a negative value is coded as the complement of its magnitude
            var magnitude = Math.Abs(value) >> scan.SuccessiveLow;
            if (magnitude == 0)
            {
                run++;
                continue;
            }

            WriteEndOfBandRun();
            var span = GetSpan(64);
            var position = 0;
            try
            {
                while (run > 15)
                {
                    _bits.WriteSymbol(span, ref position, ac, 0xF0);
                    run -= 16;
                }

                var size = 32 - BitOperations.LeadingZeroCount((uint)magnitude);
                _bits.WriteSymbol(span, ref position, ac, (run << 4) | size);
                _bits.WriteBits(span, ref position, (uint)(value < 0 ? ~magnitude : magnitude) & ((1u << size) - 1), size);
            }
            catch (InvalidOperationException)
            {
                throw Invalid("a coefficient has no code in the Huffman table of its scan.");
            }

            _length += position;
            run = 0;
        }

        if (extraZeroRuns > 0)
        {
            WriteEndOfBandRun();
            var span = GetSpan(extraZeroRuns * 4);
            var position = 0;
            try
            {
                for (var i = 0; i < extraZeroRuns; i++)
                {
                    _bits.WriteSymbol(span, ref position, ac, 0xF0);
                    run -= 16;
                }
            }
            catch (InvalidOperationException)
            {
                throw Invalid("a coefficient has no code in the Huffman table of its scan.");
            }

            _length += position;
        }

        if (run > 0)
        {
            _endOfBandTable = ac;
            _endOfBandRun++;
            if (_endOfBandRun == MaxEndOfBandRun)
            {
                WriteEndOfBandRun();
            }
        }
    }

    /// <summary>
    /// A later scan of a band of AC coefficients of a block (section G.1.2.3): the coefficients that become non-zero, each
    /// after the run of the zeros that precede it, and one more bit of the coefficients that already are.
    /// </summary>
    private void WriteProgressiveAcRefinement(ReadOnlySpan<short> block, JpegBitstreamScanComponent component, JpegBitstreamScan scan, int extraZeroRuns)
    {
        var ac = GetTable(_acTables, component.AcTable);
        var zigZag = JpegIdct.ZigZag;
        Span<int> magnitudes = stackalloc int[64];
        var last = -1;
        for (var k = scan.SpectralStart; k <= scan.SpectralEnd; k++)
        {
            magnitudes[k] = Math.Abs((int)block[zigZag[k]]) >> scan.SuccessiveLow;
            if (magnitudes[k] == 1)
            {
                last = k;
            }
        }

        var run = 0;
        _blockCorrectionBits.Clear();
        try
        {
            for (var k = scan.SpectralStart; k <= scan.SpectralEnd; k++)
            {
                var magnitude = magnitudes[k];
                if (magnitude == 0)
                {
                    run++;
                    continue;
                }

                // A run of 16 zeros is only needed before a coefficient that becomes non-zero
                while (run > 15 && k <= last)
                {
                    WriteEndOfBandRun();
                    WriteSymbolWithCorrectionBits(ac, 0xF0, signBit: -1);
                    run -= 16;
                }

                if (magnitude > 1)
                {
                    // The coefficient is already non-zero: one more bit of it
                    _blockCorrectionBits.Add((byte)(magnitude & 1));
                    continue;
                }

                WriteEndOfBandRun();
                WriteSymbolWithCorrectionBits(ac, (run << 4) | 1, signBit: block[zigZag[k]] < 0 ? 0 : 1);
                run = 0;
            }

            for (var i = 0; i < extraZeroRuns; i++)
            {
                WriteEndOfBandRun();
                WriteSymbolWithCorrectionBits(ac, 0xF0, signBit: -1);
                run -= 16;
            }
        }
        catch (InvalidOperationException)
        {
            throw Invalid("a coefficient has no code in the Huffman table of its scan.");
        }

        if (run > 0 || _blockCorrectionBits.Count > 0)
        {
            // The block ends the band: its correction bits follow the ones of the run it joins
            _endOfBandTable = ac;
            _endOfBandRun++;
            _correctionBits.AddRange(_blockCorrectionBits);
            _blockCorrectionBits.Clear();
            if (_endOfBandRun == MaxEndOfBandRun || _correctionBits.Count > MaxCorrectionBits)
            {
                WriteEndOfBandRun();
            }
        }
    }

    /// <summary>Writes a symbol of a refinement scan, the sign of the coefficient it introduces, then the correction bits of the coefficients it passes.</summary>
    private void WriteSymbolWithCorrectionBits(JpegHuffmanEncoder table, int symbol, int signBit)
    {
        var span = GetSpan(16 + (_blockCorrectionBits.Count / 4));
        var position = 0;
        _bits.WriteSymbol(span, ref position, table, symbol);
        if (signBit >= 0)
        {
            _bits.WriteBits(span, ref position, (uint)signBit, 1);
        }

        foreach (var bit in _blockCorrectionBits)
        {
            _bits.WriteBits(span, ref position, bit, 1);
        }

        _blockCorrectionBits.Clear();
        _length += position;
    }

    /// <summary>Writes the run of blocks that end their band, if any: the symbol of its length, its low bits, then the correction bits of its blocks.</summary>
    private void WriteEndOfBandRun()
    {
        if (_endOfBandRun == 0)
            return;

        var table = _endOfBandTable!;
        var size = BitOperations.Log2((uint)_endOfBandRun);
        var span = GetSpan(16 + (_correctionBits.Count / 4));
        var position = 0;
        try
        {
            _bits.WriteSymbol(span, ref position, table, size << 4);
        }
        catch (InvalidOperationException)
        {
            throw Invalid("a run of blocks has no code in the Huffman table of its scan.");
        }

        if (size > 0)
        {
            _bits.WriteBits(span, ref position, (uint)_endOfBandRun & ((1u << size) - 1), size);
        }

        foreach (var bit in _correctionBits)
        {
            _bits.WriteBits(span, ref position, bit, 1);
        }

        _length += position;
        _correctionBits.Clear();
        _endOfBandRun = 0;
    }

    /// <summary>Completes the last byte of an interval: with ones, or with the bits the file had.</summary>
    private void PadToByte()
    {
        var count = (8 - _bits.PendingBitCount) & 7;
        if (count == 0)
            return;

        var padding = (1u << count) - 1;
        if (_model.PaddingBits is { } bits)
        {
            if (_paddingBitIndex + count > bits.Length)
                throw Invalid("the padding bits of the scans are missing.");

            padding = 0;
            for (var i = 0; i < count; i++)
            {
                padding = (padding << 1) | bits[_paddingBitIndex++];
            }
        }

        var span = GetSpan(2);
        var position = 0;
        _bits.WriteBits(span, ref position, padding, count);
        _length += position;
    }

    private static int DivideCeiling(int value, int divisor) => (value + divisor - 1) / divisor;

    private void Append(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(GetSpan(bytes.Length));
        _length += bytes.Length;
    }

    /// <summary>Gets the memory that follows the bytes written so far: at least <paramref name="count"/> bytes.</summary>
    private Span<byte> GetSpan(int count)
    {
        var required = (long)_length + count;
        if (_buffer is null || required > _buffer.RawBuffer.Length)
        {
            if (required > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_scope.Limits);

            var capacity = (int)Math.Min(CheckedSizes.MaxBufferLength, Math.Max(required, Math.Max(64 * 1024, (long)(_buffer?.RawBuffer.Length ?? 0) * 2)));
            var larger = _scope.Rent(capacity, AllocationKind.Temporary, clear: false);
            _buffer?.RawBuffer.AsSpan(0, _length).CopyTo(larger.RawBuffer);
            _buffer?.Dispose();
            _buffer = larger;
        }

        return _buffer.RawBuffer.AsSpan(_length);
    }
}
