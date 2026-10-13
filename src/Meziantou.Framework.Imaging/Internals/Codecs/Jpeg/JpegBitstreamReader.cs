using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads a whole JPEG file into its quantized coefficients and the description of everything else it holds
/// (<see cref="JpegBitstreamModel"/>), so that <see cref="JpegBitstreamWriter"/> writes the same bytes: the inverse of the
/// writer, for the files a JPEG XL frame can hold (8-bit Huffman-coded frames, sequential or progressive, of one or three
/// components).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the decoder of the library, which tolerates what a viewer tolerates, this reader accepts nothing it cannot
/// write back: every bit of the entropy-coded data is accounted for. What an encoder is free to choose is recorded where
/// it differs from what the writer does by itself: the blocks before which a run of end-of-band symbols is written
/// although it could grow, the runs of 16 zeros at the end of a block, and the bits that pad an interval. The bytes
/// that follow the last block of a scan are data between markers, like the ones between two segments.
/// </para>
/// <para>
/// A component takes the quantization table its slot holds when its first scan starts: tables may be defined, and
/// defined again, between scans. The description of a file gives the values of a table through a component that uses
/// it, so a table that no component uses is only accepted when it repeats the table defined before it, which is how
/// the description says it (<see cref="ResolveQuantizationTables"/>).
/// </para>
/// <para>
/// The entropy decoding follows ITU-T T.81: section F.2.2 for sequential scans, and section G.2 with the procedures of
/// figures G.3 to G.7 for progressive scans.
/// </para>
/// </remarks>
internal sealed class JpegBitstreamReader : IDisposable
{
    private const int MaxEndOfBandRun = 0x7FFF;
    private const int MaxCorrectionBits = (1 << 16) - 64 + 1;

    private readonly JpegBitstreamModel _model = new();
    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly HuffmanDecoder?[] _dcTables = new HuffmanDecoder?[4];
    private readonly HuffmanDecoder?[] _acTables = new HuffmanDecoder?[4];
    private readonly int[] _quantizationSlots = [-1, -1, -1, -1];
    private readonly int[] _componentSlots = new int[4];
    private readonly bool[] _componentTableResolved = new bool[4];
    private readonly int[] _predictions = new int[4];
    private readonly List<byte> _paddingBits = [];
    private JpegCoefficientStore? _coefficients;
    private bool _hasRestartInterval;

    private JpegBitstreamReader(AllocationScope scope, CancellationToken cancellationToken)
    {
        _scope = scope;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Reads a JPEG file.</summary>
    /// <param name="file">The whole file.</param>
    /// <param name="scope">The scope the coefficients are rented from.</param>
    /// <param name="cancellationToken">The token checked between rows of minimum coded units.</param>
    /// <param name="coefficients">Receives the coefficients of each component; the caller disposes them.</param>
    /// <returns>The description of the file.</returns>
    /// <exception cref="InvalidImageContentException">The file is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The file is one a JPEG XL frame cannot hold, or one the writer cannot write back.</exception>
    public static JpegBitstreamModel Read(ReadOnlySpan<byte> file, AllocationScope scope, CancellationToken cancellationToken, out JpegCoefficientStore coefficients)
    {
        ArgumentNullException.ThrowIfNull(scope);
        using var reader = new JpegBitstreamReader(scope, cancellationToken);
        reader.ReadFile(file);
        coefficients = reader._coefficients ?? throw Invalid("it has no frame.");
        reader._coefficients = null;
        return reader._model;
    }

    public void Dispose()
    {
        _coefficients?.Dispose();
        _coefficients = null;
    }

    private static InvalidImageContentException Invalid(string message) => new("The JPEG file is invalid: " + message, ImageFormat.Jpeg);

    private static UnsupportedImageFeatureException Unsupported(string message, string feature) => new("The JPEG file cannot be recompressed: " + message, ImageFormat.Jpeg, "JPEG recompression: " + feature);

    private void ReadFile(ReadOnlySpan<byte> file)
    {
        var model = _model;
        if (file.Length < 4 || file[0] != 0xFF || file[1] != 0xD8)
            throw Invalid("it does not start with the start of image marker.");

        var position = 2;
        while (true)
        {
            // What is not a marker is data between markers
            var start = position;
            while (position + 1 < file.Length && !(file[position] == 0xFF && file[position + 1] is >= 0xC0 and <= 0xFE))
            {
                position++;
            }

            if (position + 1 >= file.Length)
                throw Invalid("it has no end of image marker.");

            if (position > start)
            {
                if (position - start > ushort.MaxValue)
                    throw Unsupported("more than 65,535 bytes sit between two markers.", "data between markers");

                model.MarkerOrder.Add(JpegBitstreamModel.InterMarkerData);
                model.InterMarkerSegments.Add(file[start..position].ToArray());
            }

            var marker = file[position + 1];
            position += 2;
            model.MarkerOrder.Add(marker);
            if (marker == 0xD9)
            {
                ResolveQuantizationTables();
                model.TailData = file[position..].ToArray();
                if (model.TailData.Length > 4260096)
                    throw Unsupported("more than 4,260,096 bytes follow the end of image.", "data after the end of image");

                break;
            }

            if (marker is (>= 0xC3 and <= 0xCF and not 0xC4) or (>= 0xD0 and <= 0xD8) or (>= 0xDC and <= 0xDF and not 0xDD) or (>= 0xF0 and <= 0xFD))
                throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"it has the marker 0x{marker:X2}."), "markers other than the ones of a baseline or progressive Huffman-coded file");

            if (position + 2 > file.Length)
                throw Invalid("a segment is truncated.");

            var length = BinaryPrimitives.ReadUInt16BigEndian(file[position..]);
            if (length < 2 || position + length > file.Length)
                throw Invalid("a segment is truncated.");

            var payload = file.Slice(position + 2, length - 2);
            switch (marker)
            {
                case 0xC0 or 0xC1 or 0xC2:
                    ReadFrameHeader(payload);
                    break;

                case 0xC4:
                    ReadHuffmanTables(payload);
                    break;

                case 0xDB:
                    ReadQuantizationTables(payload);
                    break;

                case 0xDD:
                    // The description of the file has one restart interval, whatever the number of segments that define it
                    if (payload.Length != 2)
                        throw Invalid("a restart interval is malformed.");

                    if (_hasRestartInterval && model.RestartInterval != BinaryPrimitives.ReadUInt16BigEndian(payload))
                        throw Unsupported("it defines several restart intervals.", "several restart intervals");

                    _hasRestartInterval = true;
                    model.RestartInterval = BinaryPrimitives.ReadUInt16BigEndian(payload);
                    break;

                case 0xDA:
                    position += length;
                    ReadScan(file, payload, ref position);
                    continue;

                case 0xFE:
                    model.CommentSegments.Add(file.Slice(position - 1, length + 1).ToArray());
                    break;

                default:
                    model.ApplicationSegments.Add(file.Slice(position - 1, length + 1).ToArray());
                    break;
            }

            position += length;
        }

        if (_coefficients is null || model.Scans.Count == 0)
            throw Invalid("it has no frame or no scan.");

        // A component that no scan codes is not an image: a decoder has nothing to show for it
        for (var c = 0; c < model.Components.Count; c++)
        {
            if (!model.Scans.Exists(scan => scan.SpectralStart == 0 && scan.Components.Exists(component => component.Component == c)))
                throw Invalid("a component is not coded by any scan.");
        }

        // The padding bits are only recorded when one of them is zero
        if (_paddingBits.Contains(0))
        {
            if (_paddingBits.Count >= 1 << 24)
                throw Unsupported("it has too many padding bits that are zero.", "padding bits");

            model.PaddingBits = [.. _paddingBits];
        }
    }

    private void ReadFrameHeader(ReadOnlySpan<byte> payload)
    {
        var model = _model;
        if (_coefficients is not null)
            throw Invalid("it has two frame headers.");

        if (payload.Length < 6 || payload.Length != 6 + (3 * payload[5]))
            throw Invalid("its frame header is malformed.");

        if (payload[0] != 8)
            throw Unsupported("its samples do not have 8 bits.", "sample precision");

        model.Height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
        model.Width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
        var count = payload[5];
        if (model.Width == 0 || model.Height == 0)
            throw Unsupported("its frame header does not give its size.", "size defined after the first scan");

        if (count is not (1 or 3))
            throw Unsupported(string.Create(CultureInfo.InvariantCulture, $"it has {count} components: a JPEG XL frame holds one or three."), count == 4 ? "four components" : "component count");

        Span<int> blocksWide = stackalloc int[count];
        Span<int> blocksHigh = stackalloc int[count];
        var maxHorizontal = 1;
        var maxVertical = 1;
        for (var c = 0; c < count; c++)
        {
            var horizontal = payload[7 + (3 * c)] >> 4;
            var vertical = payload[7 + (3 * c)] & 15;
            if (horizontal is not (1 or 2) || vertical is not (1 or 2))
                throw Unsupported("a component has a sampling factor other than 1 or 2.", "sampling factors");

            maxHorizontal = Math.Max(maxHorizontal, horizontal);
            maxVertical = Math.Max(maxVertical, vertical);
            _componentSlots[c] = payload[8 + (3 * c)];
            if (_componentSlots[c] > 3)
                throw Invalid("a component uses a quantization table that does not exist.");

            // The table of the component is the one its destination holds when its first scan starts
            model.Components.Add(new JpegBitstreamComponent(payload[6 + (3 * c)], QuantizationTable: 0) { HorizontalSampling = horizontal, VerticalSampling = vertical });
        }

        for (var c = 0; c < count; c++)
        {
            for (var other = 0; other < c; other++)
            {
                if (model.Components[c].Id == model.Components[other].Id)
                    throw Invalid("two components have the same identifier.");
            }

            blocksWide[c] = DivideCeiling(model.Width, 8 * maxHorizontal) * model.Components[c].HorizontalSampling;
            blocksHigh[c] = DivideCeiling(model.Height, 8 * maxVertical) * model.Components[c].VerticalSampling;
        }

        _coefficients = new JpegCoefficientStore(blocksWide, blocksHigh, _scope);
    }

    private void ReadQuantizationTables(ReadOnlySpan<byte> payload)
    {
        var zigZag = JpegIdct.ZigZag;
        if (payload.IsEmpty)
            throw Unsupported("a segment of quantization tables is empty.", "empty table segment");

        while (!payload.IsEmpty)
        {
            var precision = payload[0] >> 4;
            var slot = payload[0] & 15;
            var size = 1 + (precision == 0 ? 64 : 128);
            if (precision > 1 || slot > 3 || payload.Length < size)
                throw Invalid("a quantization table is malformed.");

            if (_model.QuantizationTables.Count == 4)
                throw Unsupported("it defines more than four quantization tables.", "more than four quantization tables");

            var table = new JpegBitstreamQuantizationTable(precision, slot, IsLast: payload.Length == size) { HasValues = true };
            for (var k = 0; k < 64; k++)
            {
                table.Values[zigZag[k]] = precision == 0 ? payload[1 + k] : BinaryPrimitives.ReadUInt16BigEndian(payload[(1 + (2 * k))..]);
                if (table.Values[zigZag[k]] == 0)
                    throw Invalid("a quantization value is zero.");
            }

            _quantizationSlots[slot] = _model.QuantizationTables.Count;
            _model.QuantizationTables.Add(table);
            payload = payload[size..];
        }
    }

    private void ReadHuffmanTables(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            throw Unsupported("a segment of Huffman tables is empty.", "empty table segment");

        while (!payload.IsEmpty)
        {
            if (payload.Length < 17)
                throw Invalid("a Huffman table is malformed.");

            var isAc = (payload[0] >> 4) == 1;
            var slot = payload[0] & 15;
            var counts = payload.Slice(1, 16);
            var total = 0;
            foreach (var count in counts)
            {
                total += count;
            }

            if ((payload[0] >> 4) > 1 || slot > 3 || total > 256 || payload.Length < 17 + total)
                throw Invalid("a Huffman table is malformed.");

            if (total == 0)
                throw Unsupported("a Huffman table has no code.", "empty Huffman table");

            var values = payload.Slice(17, total);
            (isAc ? _acTables : _dcTables)[slot] = HuffmanDecoder.Create(counts, values);
            _model.HuffmanTables.Add(new JpegBitstreamHuffmanTable(isAc, slot, IsLast: payload.Length == 17 + total, counts.ToArray(), values.ToArray()));
            payload = payload[(17 + total)..];
        }
    }

    private void ReadScan(ReadOnlySpan<byte> file, ReadOnlySpan<byte> header, ref int position)
    {
        var model = _model;
        var coefficients = _coefficients ?? throw Invalid("a scan precedes the frame header.");
        if (header.Length < 1 || header[0] is 0 or > 4 || header.Length != 4 + (2 * header[0]))
            throw Invalid("a scan header is malformed.");

        var count = header[0];
        var tail = 1 + (2 * count);
        var scan = new JpegBitstreamScan(header[tail], header[tail + 1], header[tail + 2] >> 4, header[tail + 2] & 15);
        var isProgressive = model.FrameMarker == 0xC2;
        if (scan.SpectralStart > scan.SpectralEnd || scan.SpectralEnd > 63 || scan.SuccessiveLow > 13 || scan.SuccessiveHigh > 13)
            throw Invalid("a scan header is malformed.");

        if (isProgressive ? (scan.SpectralStart == 0) != (scan.SpectralEnd == 0) || (scan.SpectralStart != 0 && count != 1) : scan.SpectralStart != 0 || scan.SpectralEnd != 63 || scan.SuccessiveHigh != 0 || scan.SuccessiveLow != 0)
            throw Invalid("a scan does not code the coefficients its frame allows.");

        var maxHorizontal = 1;
        var maxVertical = 1;
        foreach (var component in model.Components)
        {
            maxHorizontal = Math.Max(maxHorizontal, component.HorizontalSampling);
            maxVertical = Math.Max(maxVertical, component.VerticalSampling);
        }

        for (var i = 0; i < count; i++)
        {
            var identifier = header[1 + (2 * i)];
            var index = model.Components.FindIndex(component => component.Id == identifier);
            var dcTable = header[2 + (2 * i)] >> 4;
            var acTable = header[2 + (2 * i)] & 15;
            if (index < 0 || dcTable > 3 || acTable > 3 || scan.Components.Exists(component => component.Component >= index))
                throw Invalid("a scan header names a component that does not exist, or its components are not in the order of the frame.");

            scan.Components.Add(new JpegBitstreamScanComponent(index, dcTable, acTable));
            ResolveQuantizationTable(index);
        }

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

        var bits = new BitReader(file, position);
        _predictions.AsSpan().Clear();
        var state = default(RunState);
        var restartsToGo = model.RestartInterval;
        var nextRestart = 0;
        var blockIndex = 0;
        for (var unitY = 0; unitY < unitsHigh; unitY++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            for (var unitX = 0; unitX < unitsWide; unitX++)
            {
                if (model.RestartInterval > 0 && restartsToGo == 0)
                {
                    if (state.Remaining != 0)
                        throw Invalid("a run of blocks crosses a restart marker.");

                    ReadPadding(ref bits);
                    if (!bits.TryReadMarker(out var restart) || restart != 0xD0 + nextRestart)
                        throw Invalid("a restart marker is missing or out of sequence.");

                    nextRestart = (nextRestart + 1) & 7;
                    _predictions.AsSpan().Clear();
                    state = default;
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
                            var block = coefficients.GetBlock(scanComponent.Component, (unitX * horizontal) + h, (unitY * vertical) + v);
                            var extraZeroRuns = 0;
                            if (!isProgressive)
                            {
                                ReadSequentialBlock(ref bits, block, scanComponent, out extraZeroRuns);
                            }
                            else if (scan.SpectralStart == 0)
                            {
                                ReadProgressiveDc(ref bits, block, scanComponent, scan);
                            }
                            else
                            {
                                var outcome = scan.SuccessiveHigh == 0
                                    ? ReadProgressiveAcFirst(ref bits, block, scanComponent, scan, ref state)
                                    : ReadProgressiveAcRefinement(ref bits, block, scanComponent, scan, ref state);
                                extraZeroRuns = outcome.ExtraZeroRuns;

                                // What the writer does by itself: it writes the run it holds before the first symbol of a block,
                                // and a run grows until it holds 32767 blocks or until its correction bits can no longer be
                                // buffered
                                if (outcome.HasSymbols || extraZeroRuns > 0)
                                {
                                    state.Pending = 0;
                                    state.PendingBits = 0;
                                }

                                if (outcome.EndsBand)
                                {
                                    if (outcome.StartsRun && state.Pending > 0)
                                    {
                                        // The file starts a new run where the writer would extend the one it holds
                                        scan.ResetPoints.Add(blockIndex);
                                        state.Pending = 0;
                                        state.PendingBits = 0;
                                    }

                                    state.Pending++;
                                    state.PendingBits += outcome.TrailingBits;
                                    if (state.Pending == MaxEndOfBandRun || state.PendingBits > MaxCorrectionBits)
                                    {
                                        state.Pending = 0;
                                        state.PendingBits = 0;
                                    }
                                }
                            }

                            if (extraZeroRuns > 0)
                            {
                                scan.ExtraZeroRuns.Add((blockIndex, extraZeroRuns));
                            }

                            blockIndex++;
                        }
                    }
                }

                restartsToGo--;
            }
        }

        if (state.Remaining != 0)
            throw Invalid("a run of blocks extends past the end of its scan.");

        // What follows the last block of the scan, up to the next marker, is data between markers like any other
        ReadPadding(ref bits);
        position = bits.Position;
        model.Scans.Add(scan);
    }

    /// <summary>Gives a component the quantization table its destination holds, when its first scan starts.</summary>
    private void ResolveQuantizationTable(int component)
    {
        if (_componentTableResolved[component])
            return;

        var index = _quantizationSlots[_componentSlots[component]];
        if (index < 0)
            throw Invalid("a component uses a quantization table that is not defined.");

        _componentTableResolved[component] = true;
        _model.Components[component] = _model.Components[component] with { QuantizationTable = index };
    }

    /// <summary>
    /// Checks, at the end of the file, that the frame of a JPEG XL file can hold its quantization tables: the values of a
    /// table are the ones of the channel of a component that uses it, and a table that no component uses can only be
    /// described as a copy of the table that precedes it.
    /// </summary>
    private void ResolveQuantizationTables()
    {
        var model = _model;
        var tables = model.QuantizationTables;
        for (var c = 0; c < model.Components.Count; c++)
        {
            // A component without scan
            ResolveQuantizationTable(c);

            // A table that is defined again with the same values: the component uses the first of them, the others are its copies
            var index = model.Components[c].QuantizationTable;
            while (index > 0 && tables[index - 1].Slot == tables[index].Slot && IsCopy(tables[index], tables[index - 1]))
            {
                index--;
            }

            model.Components[c] = model.Components[c] with { QuantizationTable = index };
        }

        for (var i = 0; i < tables.Count; i++)
        {
            if (model.Components.Exists(component => component.QuantizationTable == i))
                continue;

            if (i == 0 || !IsCopy(tables[i], tables[i - 1]))
                throw Unsupported("it defines a quantization table that no component uses, and that does not repeat the table defined before it.", "unused quantization table");
        }

        static bool IsCopy(JpegBitstreamQuantizationTable table, JpegBitstreamQuantizationTable other)
            => table.Precision == other.Precision && table.Values.AsSpan().SequenceEqual(other.Values);
    }

    private static HuffmanDecoder GetTable(HuffmanDecoder?[] tables, int slot) => tables[slot] ?? throw Invalid("a scan uses a Huffman table that is not defined.");

    private void ReadSequentialBlock(ref BitReader bits, Span<short> block, JpegBitstreamScanComponent component, out int extraZeroRuns)
    {
        var zigZag = JpegIdct.ZigZag;
        var size = GetTable(_dcTables, component.DcTable).Decode(ref bits);
        if (size > 11)
            throw Invalid("a DC coefficient is out of range.");

        _predictions[component.Component] += bits.ReadExtended(size);
        block[0] = ToCoefficient(_predictions[component.Component]);
        var ac = GetTable(_acTables, component.AcTable);
        extraZeroRuns = 0;
        var k = 1;
        while (k < 64)
        {
            var symbol = ac.Decode(ref bits);
            var run = symbol >> 4;
            size = symbol & 15;
            if (size == 0)
            {
                if (run == 0)
                    break;

                if (run != 15 || k + 16 > 64)
                    throw Invalid("a run of zeros extends past the end of its block.");

                k += 16;
                extraZeroRuns++;
                continue;
            }

            k += run;
            if (k > 63 || size > 10)
                throw Invalid("a coefficient is outside its block or out of range.");

            block[zigZag[k++]] = ToCoefficient(bits.ReadExtended(size));
            extraZeroRuns = 0;
        }
    }

    private void ReadProgressiveDc(ref BitReader bits, Span<short> block, JpegBitstreamScanComponent component, JpegBitstreamScan scan)
    {
        if (scan.SuccessiveHigh != 0)
        {
            if (bits.ReadBits(1) != 0)
            {
                block[0] = ToCoefficient(block[0] + (1 << scan.SuccessiveLow));
            }

            return;
        }

        var size = GetTable(_dcTables, component.DcTable).Decode(ref bits);
        if (size > 11)
            throw Invalid("a DC coefficient is out of range.");

        _predictions[component.Component] += bits.ReadExtended(size);
        block[0] = ToCoefficient(_predictions[component.Component] << scan.SuccessiveLow);
    }

    /// <summary>A block of the first scan of a band of AC coefficients (section G.2, figure G.3 for the runs of blocks).</summary>
    private BlockOutcome ReadProgressiveAcFirst(ref BitReader bits, Span<short> block, JpegBitstreamScanComponent component, JpegBitstreamScan scan, ref RunState state)
    {
        if (state.Remaining > 0)
        {
            state.Remaining--;
            return new BlockOutcome(HasSymbols: false, EndsBand: true, StartsRun: false, TrailingBits: 0, ExtraZeroRuns: 0);
        }

        var zigZag = JpegIdct.ZigZag;
        var ac = GetTable(_acTables, component.AcTable);
        var hasSymbols = false;
        var endsBand = false;
        var zeroRuns = 0;
        var k = scan.SpectralStart;
        while (k <= scan.SpectralEnd)
        {
            var symbol = ac.Decode(ref bits);
            var run = symbol >> 4;
            var size = symbol & 15;
            if (size == 0)
            {
                if (run == 15)
                {
                    if (k + 16 > scan.SpectralEnd + 1)
                        throw Invalid("a run of zeros extends past the end of its band.");

                    k += 16;
                    zeroRuns++;
                    continue;
                }

                // The block and the ones that follow it end their band here
                state.Remaining = (1 << run) + (run > 0 ? bits.ReadBits(run) : 0) - 1;
                endsBand = true;
                break;
            }

            k += run;
            if (k > scan.SpectralEnd || size > 10)
                throw Invalid("a coefficient is outside its band or out of range.");

            block[zigZag[k++]] = ToCoefficient(bits.ReadExtended(size) * (1 << scan.SuccessiveLow));
            zeroRuns = 0;
            hasSymbols = true;
        }

        return new BlockOutcome(hasSymbols, endsBand, StartsRun: endsBand, TrailingBits: 0, zeroRuns);
    }

    /// <summary>A block of a later scan of a band of AC coefficients (section G.2, figures G.4 to G.7).</summary>
    private BlockOutcome ReadProgressiveAcRefinement(ref BitReader bits, Span<short> block, JpegBitstreamScanComponent component, JpegBitstreamScan scan, ref RunState state)
    {
        var zigZag = JpegIdct.ZigZag;
        var positive = 1 << scan.SuccessiveLow;
        var negative = -positive;
        var hasSymbols = false;
        var startsRun = false;
        var zeroRuns = 0;
        var trailingBits = 0;
        var k = scan.SpectralStart;
        if (state.Remaining == 0)
        {
            var ac = GetTable(_acTables, component.AcTable);
            while (k <= scan.SpectralEnd)
            {
                var symbol = ac.Decode(ref bits);
                var run = symbol >> 4;
                var size = symbol & 15;
                var value = 0;
                if (size != 0)
                {
                    if (size != 1)
                        throw Invalid("a coefficient of a refinement scan has more than one bit.");

                    value = bits.ReadBits(1) != 0 ? positive : negative;
                }
                else if (run != 15)
                {
                    state.Remaining = (1 << run) + (run > 0 ? bits.ReadBits(run) : 0);
                    startsRun = true;
                    break;
                }

                // The zeros of the run are the coefficients that are still zero: the others have a correction bit each, which
                // follows the symbol
                trailingBits = 0;
                while (k <= scan.SpectralEnd)
                {
                    ref var coefficient = ref block[zigZag[k]];
                    if (coefficient != 0)
                    {
                        Refine(ref bits, ref coefficient, positive, negative);
                    }
                    else if (--run < 0)
                    {
                        break;
                    }

                    k++;
                }

                if (size != 0)
                {
                    if (k > scan.SpectralEnd)
                        throw Invalid("a coefficient is outside its band.");

                    block[zigZag[k++]] = (short)value;
                    zeroRuns = 0;
                    hasSymbols = true;
                }
                else
                {
                    // The sixteenth zero of the run is passed too
                    if (k <= scan.SpectralEnd)
                    {
                        k++;
                    }

                    zeroRuns++;
                }
            }
        }

        var endsBand = false;
        if (state.Remaining > 0)
        {
            // The block ends its band: only the correction bits of its coefficients that are not zero remain
            for (; k <= scan.SpectralEnd; k++)
            {
                ref var coefficient = ref block[zigZag[k]];
                if (coefficient != 0)
                {
                    Refine(ref bits, ref coefficient, positive, negative);
                    trailingBits++;
                }
            }

            state.Remaining--;
            endsBand = true;
        }

        return new BlockOutcome(hasSymbols, endsBand, startsRun, trailingBits, zeroRuns);
    }

    private static void Refine(ref BitReader bits, ref short coefficient, int positive, int negative)
    {
        if (bits.ReadBits(1) != 0 && (coefficient & positive) == 0)
        {
            coefficient = ToCoefficient(coefficient + (coefficient >= 0 ? positive : negative));
        }
    }

    /// <summary>Reads the bits that complete the current byte of the entropy-coded data.</summary>
    private void ReadPadding(ref BitReader bits)
    {
        while (bits.PendingBitCount > 0)
        {
            _paddingBits.Add((byte)bits.ReadBits(1));
        }
    }

    private static short ToCoefficient(int value)
    {
        if (value is < short.MinValue or > short.MaxValue)
            throw Invalid("a coefficient is out of range.");

        return (short)value;
    }

    private static int DivideCeiling(int value, int divisor) => (value + divisor - 1) / divisor;

    /// <summary>What a block of a progressive scan of AC coefficients held.</summary>
    /// <param name="HasSymbols">Whether a coefficient was coded: the writer writes the run it holds before it.</param>
    /// <param name="EndsBand">Whether the block ends its band in a run of blocks.</param>
    /// <param name="StartsRun">Whether the run was started by the block, in the file.</param>
    /// <param name="TrailingBits">The number of correction bits the block adds to the run.</param>
    /// <param name="ExtraZeroRuns">The number of runs of 16 zeros that follow its last coefficient.</param>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct BlockOutcome(bool HasSymbols, bool EndsBand, bool StartsRun, int TrailingBits, int ExtraZeroRuns);

    /// <summary>The runs of blocks that end their band: what the file holds, and what the writer would hold at the same block.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private struct RunState
    {
        /// <summary>The number of blocks of the run of the file that are not read yet.</summary>
        public int Remaining;

        /// <summary>The number of blocks of the run the writer holds.</summary>
        public int Pending;

        /// <summary>The number of correction bits of the run the writer holds.</summary>
        public int PendingBits;
    }

    /// <summary>The bits of the entropy-coded data, most significant bit first, without the zero byte that follows each byte 0xFF.</summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;
        private int _current;
        private int _bitCount;

        public BitReader(ReadOnlySpan<byte> data, int position)
        {
            _data = data;
            _position = position;
        }

        /// <summary>Gets the position of the byte that follows the last one a bit was read from.</summary>
        public readonly int Position => _position;

        /// <summary>Gets the number of bits of the current byte that are not read.</summary>
        public readonly int PendingBitCount => _bitCount;

        public int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                if (_bitCount == 0)
                {
                    if (_position >= _data.Length)
                        throw Invalid("the data of a scan are truncated.");

                    _current = _data[_position++];
                    if (_current == 0xFF)
                    {
                        // A byte 0xFF of the data is followed by a zero byte; anything else is a marker
                        if (_position >= _data.Length || _data[_position] != 0)
                            throw Invalid("the data of a scan end before its last block.");

                        _position++;
                    }

                    _bitCount = 8;
                }

                _bitCount--;
                value = (value << 1) | ((_current >> _bitCount) & 1);
            }

            return value;
        }

        /// <summary>Reads a value of <paramref name="size"/> bits whose first bit tells the sign (section F.2.2.1).</summary>
        public int ReadExtended(int size)
        {
            if (size == 0)
                return 0;

            var value = ReadBits(size);
            return value < (1 << (size - 1)) ? value - (1 << size) + 1 : value;
        }

        /// <summary>Reads a marker at a byte boundary.</summary>
        public bool TryReadMarker(out int marker)
        {
            marker = 0;
            if (_bitCount != 0 || _position + 1 >= _data.Length || _data[_position] != 0xFF)
                return false;

            marker = _data[_position + 1];
            _position += 2;
            return true;
        }
    }

    /// <summary>A Huffman table: the codes of each length are consecutive, and the first code of a length follows the last one of the length before (section F.2.2.3).</summary>
    private sealed class HuffmanDecoder
    {
        private readonly int[] _minCode = new int[17];
        private readonly int[] _maxCode = new int[17];
        private readonly int[] _firstValue = new int[17];
        private readonly byte[] _values;

        private HuffmanDecoder(byte[] values) => _values = values;

        public static HuffmanDecoder Create(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            var decoder = new HuffmanDecoder(values.ToArray());
            var code = 0;
            var index = 0;
            var lastLength = 0;
            for (var length = 1; length <= 16; length++)
            {
                decoder._firstValue[length] = index;
                decoder._minCode[length] = code;
                code += counts[length - 1];
                index += counts[length - 1];
                decoder._maxCode[length] = counts[length - 1] == 0 ? -1 : code - 1;
                if (code > 1 << length)
                    throw Invalid("a Huffman table is not a prefix code.");

                if (counts[length - 1] != 0)
                {
                    lastLength = length;
                }

                code <<= 1;
            }

            // The description of the file gives the code of all ones of the longest length to a symbol that no value has
            if (decoder._maxCode[lastLength] == (1 << lastLength) - 1)
                throw Unsupported("a Huffman table uses the code of all ones.", "Huffman code of all ones");

            for (var i = 0; i < values.Length; i++)
            {
                if (values[..i].Contains(values[i]))
                    throw Invalid("a Huffman table has the same symbol twice.");
            }

            return decoder;
        }

        public int Decode(ref BitReader bits)
        {
            var code = 0;
            for (var length = 1; length <= 16; length++)
            {
                code = (code << 1) | bits.ReadBits(1);
                if (code <= _maxCode[length])
                    return _values[_firstValue[length] + code - _minCode[length]];
            }

            throw Invalid("a code of the data of a scan is not in its Huffman table.");
        }
    }
}
