using System.Buffers.Binary;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>
/// A small reference reader for baseline and progressive JPEG files, written from ITU-T T.81 and JFIF 1.02 for encoder
/// tests. It never uses the library under test. <see cref="Parse"/> checks the marker structure strictly: SOI first; marker
/// segments with exact lengths and no fill bytes; only APPn, COM, DQT, DHT, at most one DRI and one SOF0 or SOF2 before the
/// first SOS; 8-bit DQT tables with nonzero values; Huffman tables with consistent code counts; an 8-bit frame with 1 or 3
/// components whose sampling factors are 1 to 4 and at most 10 blocks per MCU; entropy-coded data with restart markers only
/// after a DRI segment of a nonzero interval; EOI last with nothing after it. A baseline file has at most two DC and two AC Huffman tables and one scan coding every component with
/// Ss = 0, Se = 63, Ah = Al = 0 and defined tables.
/// </summary>
/// <remarks>
/// <para>
/// A progressive file (SOF2) has several scans, with only DHT segments between them. Each scan is checked against T.81
/// G.1.1.1: a DC scan (Ss = Se = 0) of one or more components in frame order, or an AC scan (1 &lt;= Ss &lt;= Se &lt;= 63) of
/// one component whose DC coefficient was already coded; Ah is 0 for the first scan of a coefficient and the Al of its
/// previous scan otherwise, with Al = Ah - 1; the table each scan needs is defined. At EOI every coefficient of every
/// component must have been coded down to bit 0. <see cref="DecodeCoefficients"/> then decodes every scan (DC and AC,
/// first scans and refinements, end-of-band runs) and requires each to end exactly after its last MCU.
/// </para>
/// <para>
/// <see cref="DecodeCoefficients"/> decodes the entropy-coded segment (its own canonical Huffman decoding, DC prediction,
/// run-length AC decoding) to the quantized coefficients and requires the data to end exactly after the last MCU, padded
/// with 1-bits to the byte boundary (T.81 F.1.2.3). With a restart interval, every interval of a scan but the last (the MCUs
/// counted from the start of the scan) must end the same way and be followed by the next marker of RST0 to RST7, and
/// nothing else: no marker inside an interval, the DC predictions start again from 0, and no end-of-band run crosses a
/// marker (T.81 F.1.2.3, G.1.2.2). <see cref="DecodePlanes"/> applies the direct (non-separable) 2-D
/// inverse DCT of T.81 A.3.3 in double precision with <see cref="Math.Cos"/>, the level shift and rounding to nearest.
/// <see cref="DecodePixels"/> upsamples chroma by replication (nearest sample, the simplest reconstruction) and applies
/// the JFIF YCbCr to RGB equations in double precision. These choices are deliberately the plainest reading of the
/// specifications: the reader validates encoder output and measures reconstruction quality, it is not tuned to any decoder.
/// </para>
/// </remarks>
public sealed class ReferenceJpeg
{
    private const byte MarkerSoi = 0xD8;
    private const byte MarkerEoi = 0xD9;
    private const byte MarkerSos = 0xDA;
    private const byte MarkerDqt = 0xDB;
    private const byte MarkerDht = 0xC4;
    private const byte MarkerSof0 = 0xC0;
    private const byte MarkerSof2 = 0xC2;
    private const byte MarkerCom = 0xFE;
    private const byte MarkerDri = 0xDD;

    private static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private readonly int[]?[] _quantization;
    private readonly HuffmanTable?[] _dcTables;
    private readonly HuffmanTable?[] _acTables;
    private readonly IReadOnlyList<ScanData> _scans;

    private ReferenceJpeg(int width, int height, bool progressive, int restartInterval, IReadOnlyList<ReferenceJpegComponent> components, IReadOnlyList<ReferenceJpegSegment> segments, int[]?[] quantization, HuffmanTable?[] dcTables, HuffmanTable?[] acTables, IReadOnlyList<ScanData> scans)
    {
        Width = width;
        Height = height;
        IsProgressive = progressive;
        RestartInterval = restartInterval;
        Components = components;
        Segments = segments;
        _quantization = quantization;
        _dcTables = dcTables;
        _acTables = acTables;
        _scans = scans;
        Scans = [.. scans.Select(scan => scan.Header)];
        MaxH = components.Max(component => component.H);
        MaxV = components.Max(component => component.V);
        McusX = (width + (8 * MaxH) - 1) / (8 * MaxH);
        McusY = (height + (8 * MaxV) - 1) / (8 * MaxV);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets a value indicating whether the frame is progressive (SOF2) rather than baseline (SOF0).</summary>
    public bool IsProgressive { get; }

    /// <summary>Gets the restart interval of the DRI segment, in MCUs, or 0 without restart markers.</summary>
    public int RestartInterval { get; }

    /// <summary>
    /// Gets the frame components in frame order, with the Huffman tables selected by the scan (for a progressive file, by
    /// the first DC scan and the first AC scan of the component).
    /// </summary>
    public IReadOnlyList<ReferenceJpegComponent> Components { get; }

    /// <summary>Gets the scans in file order.</summary>
    public IReadOnlyList<ReferenceJpegScan> Scans { get; }

    /// <summary>Gets every marker segment in file order (SOI, the entropy-coded data and EOI excluded).</summary>
    public IReadOnlyList<ReferenceJpegSegment> Segments { get; }

    /// <summary>Gets the segment names in file order, e.g. <c>APP0, DQT, SOF0, DHT, SOS</c>.</summary>
    public IReadOnlyList<string> SegmentNames => [.. Segments.Select(segment => segment.Name)];

    /// <summary>Gets the largest horizontal sampling factor.</summary>
    public int MaxH { get; }

    /// <summary>Gets the largest vertical sampling factor.</summary>
    public int MaxV { get; }

    /// <summary>Gets the number of MCU columns of the interleaved scan.</summary>
    public int McusX { get; }

    /// <summary>Gets the number of MCU rows of the interleaved scan.</summary>
    public int McusY { get; }

    /// <summary>Gets the length of the entropy-coded segments (stuffed bytes included).</summary>
    public int EntropyCodedLength => _scans.Sum(scan => scan.Entropy.Length);

    /// <summary>Gets the raw layout <see cref="DecodePixels"/> produces.</summary>
    public RawPixelLayout Layout => Components.Count == 1 ? RawPixelLayout.Gray8 : RawPixelLayout.Rgb8;

    /// <summary>Gets a value indicating whether a JFIF APP0 segment immediately follows SOI.</summary>
    public bool HasJfifFirst => Segments.Count > 0 && Segments[0].Marker == 0xE0 && Segments[0].Payload.Span.StartsWith("JFIF\0"u8);

    /// <summary>Gets a value indicating whether an Adobe APP14 segment is present.</summary>
    public bool HasAdobeSegment => Segments.Any(segment => segment.Marker == 0xEE && segment.Payload.Span.StartsWith("Adobe"u8));

    /// <summary>Parses and validates the marker structure of a baseline or progressive JPEG file.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="InvalidDataException">The structure is invalid, or neither baseline nor progressive.</exception>
    public static ReferenceJpeg Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 4 || span[0] != 0xFF || span[1] != MarkerSoi)
            throw new InvalidDataException("The file does not start with SOI.");

        var segments = new List<ReferenceJpegSegment>();
        var quantization = new int[]?[4];
        var dcTables = new HuffmanTable?[4];
        var acTables = new HuffmanTable?[4];
        (int Id, int H, int V, int Tq)[]? frame = null;
        var progressive = false;
        int? restartInterval = null;
        var scans = new List<ScanData>();

        // Progressive: the Al of the last scan of each coefficient of each component, or -1 before its first scan
        int[][]? approximation = null;
        var width = 0;
        var height = 0;
        var offset = 2;
        while (true)
        {
            Require(span, offset, 2, "marker");
            if (span[offset] != 0xFF || span[offset + 1] is 0xFF or 0x00)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Expected a marker at offset {offset} (fill bytes are not expected)."));

            var marker = span[offset + 1];
            if (marker == MarkerEoi && scans.Count > 0)
            {
                if (offset + 2 != span.Length)
                    throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"{span.Length - offset - 2} byte(s) follow EOI."));

                if (approximation is not null)
                {
                    for (var c = 0; c < approximation.Length; c++)
                    {
                        var k = Array.FindIndex(approximation[c], al => al != 0);
                        if (k >= 0)
                            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The coefficient {k} of the component {c} is not coded down to bit 0."));
                    }
                }

                return new ReferenceJpeg(width, height, progressive, restartInterval ?? 0, GetComponents(frame!, scans), segments, quantization, dcTables, acTables, scans);
            }

            if (marker is MarkerEoi or MarkerSoi or (>= 0xD0 and <= 0xD7))
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Unexpected marker 0xFF{marker:X2} at offset {offset}."));

            if (scans.Count > 0 && marker is not (MarkerDht or MarkerSos))
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Unexpected marker 0xFF{marker:X2} between two scans (only DHT is expected)."));

            Require(span, offset, 4, "marker segment");
            var length = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 2)..]);
            if (length < 2)
                throw new InvalidDataException("A marker segment length is below 2.");

            Require(span, offset + 2, length, "marker segment payload");
            var payload = data.Slice(offset + 4, length - 2);
            segments.Add(new ReferenceJpegSegment(marker, payload));
            offset += 2 + length;
            switch (marker)
            {
                case >= 0xE0 and <= 0xEF or MarkerCom:
                    break;

                case MarkerDqt:
                    ParseQuantization(payload.Span, quantization);
                    break;

                case MarkerDht:
                    ParseHuffman(payload.Span, dcTables, acTables);
                    break;

                case MarkerDri:
                    if (restartInterval is not null || payload.Length != 2)
                        throw new InvalidDataException("A file written by the encoder has at most one DRI segment, of 4 bytes.");

                    restartInterval = BinaryPrimitives.ReadUInt16BigEndian(payload.Span);
                    break;

                case MarkerSof0 or MarkerSof2:
                    if (frame is not null)
                        throw new InvalidDataException("Several frame headers.");

                    (width, height, frame) = ParseFrame(payload.Span);
                    progressive = marker == MarkerSof2;
                    if (progressive)
                    {
                        approximation = new int[frame.Length][];
                        for (var c = 0; c < frame.Length; c++)
                        {
                            approximation[c] = new int[64];
                            Array.Fill(approximation[c], -1);
                        }
                    }

                    break;

                case MarkerSos:
                {
                    if (frame is null)
                        throw new InvalidDataException("SOS before the frame header.");

                    var header = approximation is null
                        ? ParseScan(payload.Span, frame, quantization, dcTables, acTables)
                        : ParseProgressiveScan(payload.Span, frame, quantization, dcTables, acTables, approximation);

                    // Entropy-coded data: every 0xFF is followed by a stuffed 0x00 or, with a restart interval, is a restart
                    // marker (checked when the scan is decoded), up to the next marker
                    var start = offset;
                    while (true)
                    {
                        Require(span, offset, 1, "entropy-coded data");
                        if (span[offset] != 0xFF)
                        {
                            offset++;
                            continue;
                        }

                        Require(span, offset, 2, "entropy-coded data");
                        if (span[offset + 1] == 0x00 || (restartInterval > 0 && span[offset + 1] is >= 0xD0 and <= 0xD7))
                        {
                            offset += 2;
                            continue;
                        }

                        break;
                    }

                    // A scan uses the tables defined when it starts: later DHT segments may replace them
                    scans.Add(new ScanData(header, [.. dcTables], [.. acTables], data[start..offset]));
                    if (!progressive && span[offset + 1] != MarkerEoi)
                        throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Expected EOI after the scan, found 0xFF{span[offset + 1]:X2} (a single scan without restart markers is expected)."));

                    break;
                }

                default:
                    throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Unexpected marker 0xFF{marker:X2} in a file written by the encoder."));
            }
        }
    }

    /// <summary>Gets a quantization table (natural order).</summary>
    /// <param name="index">The table identifier.</param>
    /// <returns>The 64 values.</returns>
    public IReadOnlyList<int> GetQuantizationTable(int index) => _quantization[index] ?? throw new InvalidOperationException("The quantization table is not defined.");

    /// <summary>Gets the code length of each symbol of a Huffman table (0 when undefined).</summary>
    /// <param name="tableClass">0 for DC, 1 for AC.</param>
    /// <param name="index">The table identifier.</param>
    /// <returns>256 code lengths.</returns>
    public IReadOnlyList<int> GetHuffmanCodeLengths(int tableClass, int index)
    {
        var table = (tableClass == 0 ? _dcTables : _acTables)[index] ?? throw new InvalidOperationException("The Huffman table is not defined.");
        return table.Lengths;
    }

    /// <summary>Gets the segments with a marker, in file order.</summary>
    /// <param name="marker">The marker code.</param>
    /// <returns>The segments.</returns>
    public IEnumerable<ReferenceJpegSegment> GetSegments(byte marker) => Segments.Where(segment => segment.Marker == marker);

    /// <summary>Decodes the quantized coefficients of every block of every component (natural order, before dequantization).</summary>
    /// <returns>One block grid per component.</returns>
    /// <exception cref="InvalidDataException">The entropy-coded data is invalid, truncated, longer than the MCUs, or not padded with 1-bits.</exception>
    public IReadOnlyList<ReferenceJpegBlocks> DecodeCoefficients()
    {
        var sizes = new (int BlocksX, int BlocksY)[Components.Count];
        var data = new int[Components.Count][];
        for (var c = 0; c < Components.Count; c++)
        {
            var component = Components[c];

            // Interleaved scan: the block grid covers whole MCUs; a single-component scan covers the component's own blocks
            var blocksX = Components.Count == 1 ? (GetComponentWidth(c) + 7) / 8 : McusX * component.H;
            var blocksY = Components.Count == 1 ? (GetComponentHeight(c) + 7) / 8 : McusY * component.V;
            sizes[c] = (blocksX, blocksY);
            data[c] = new int[blocksX * blocksY * 64];
        }

        foreach (var scan in _scans)
        {
            DecodeScan(scan, sizes, data);
        }

        return
        [
            .. sizes.Select((size, c) => new ReferenceJpegBlocks(size.BlocksX, size.BlocksY, data[c])
            {
                // The AC scans of a progressive file only code the blocks that hold samples of the component (T.81 A.2.2)
                AcBlocksX = IsProgressive ? (GetComponentWidth(c) + 7) / 8 : size.BlocksX,
                AcBlocksY = IsProgressive ? (GetComponentHeight(c) + 7) / 8 : size.BlocksY,
            }),
        ];
    }

    /// <summary>Gets the width of a component's sample plane: <c>ceil(X * H / Hmax)</c>.</summary>
    /// <param name="component">The component index.</param>
    /// <returns>The width.</returns>
    public int GetComponentWidth(int component) => ((Width * Components[component].H) + MaxH - 1) / MaxH;

    /// <summary>Gets the height of a component's sample plane: <c>ceil(Y * V / Vmax)</c>.</summary>
    /// <param name="component">The component index.</param>
    /// <returns>The height.</returns>
    public int GetComponentHeight(int component) => ((Height * Components[component].V) + MaxV - 1) / MaxV;

    /// <summary>Decodes the sample planes (dequantization, direct 2-D IDCT, level shift, rounding, clamping), cropped to the component sizes.</summary>
    /// <returns>One plane per component (row-major, <see cref="GetComponentWidth"/> by <see cref="GetComponentHeight"/>).</returns>
    public IReadOnlyList<byte[]> DecodePlanes()
    {
        var grids = DecodeCoefficients();
        var cosines = new double[64];
        for (var x = 0; x < 8; x++)
        {
            for (var u = 0; u < 8; u++)
            {
                cosines[(x * 8) + u] = (u == 0 ? 1 / Math.Sqrt(2) : 1) * Math.Cos(((2 * x) + 1) * u * Math.PI / 16);
            }
        }

        var planes = new byte[Components.Count][];
        for (var c = 0; c < Components.Count; c++)
        {
            var grid = grids[c];
            var quantization = _quantization[Components[c].QuantizationTable]!;
            var planeWidth = GetComponentWidth(c);
            var planeHeight = GetComponentHeight(c);
            var plane = new byte[planeWidth * planeHeight];
            var dequantized = new double[64];
            for (var by = 0; by < grid.BlocksY; by++)
            {
                for (var bx = 0; bx < grid.BlocksX; bx++)
                {
                    if (bx * 8 >= planeWidth || by * 8 >= planeHeight)
                        continue;

                    var coefficients = grid.GetBlock(bx, by);
                    for (var i = 0; i < 64; i++)
                    {
                        dequantized[i] = coefficients[i] * quantization[i];
                    }

                    for (var y = 0; y < 8 && (by * 8) + y < planeHeight; y++)
                    {
                        for (var x = 0; x < 8 && (bx * 8) + x < planeWidth; x++)
                        {
                            // s(x, y) = 1/4 sum_u sum_v C(u) C(v) S(v, u) cos((2x+1) u pi/16) cos((2y+1) v pi/16)
                            var sum = 0d;
                            for (var v = 0; v < 8; v++)
                            {
                                for (var u = 0; u < 8; u++)
                                {
                                    sum += dequantized[(v * 8) + u] * cosines[(x * 8) + u] * cosines[(y * 8) + v];
                                }
                            }

                            plane[(((by * 8) + y) * planeWidth) + (bx * 8) + x] = (byte)Math.Clamp(Math.Floor((sum / 4) + 128.5), 0, 255);
                        }
                    }
                }
            }

            planes[c] = plane;
        }

        return planes;
    }

    /// <summary>Decodes the image: grayscale samples, or YCbCr upsampled by replication and converted with the JFIF equations.</summary>
    /// <returns>The pixels in <see cref="Layout"/>.</returns>
    public RawPixelBuffer DecodePixels()
    {
        var planes = DecodePlanes();
        if (Components.Count == 1)
            return RawPixelBuffer.Create(Width, Height, RawPixelLayout.Gray8, planes[0]);

        var output = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var luma = Sample(0, x, y);
                var cb = Sample(1, x, y) - 128d;
                var cr = Sample(2, x, y) - 128d;
                var offset = ((y * Width) + x) * 3;
                output[offset] = ToByte(luma + (1.402 * cr));
                output[offset + 1] = ToByte(luma - (0.344136286 * cb) - (0.714136286 * cr));
                output[offset + 2] = ToByte(luma + (1.772 * cb));
            }
        }

        return RawPixelBuffer.Create(Width, Height, RawPixelLayout.Rgb8, output);

        double Sample(int component, int x, int y)
        {
            var sx = x * Components[component].H / MaxH;
            var sy = y * Components[component].V / MaxV;
            return planes[component][(sy * GetComponentWidth(component)) + sx];
        }

        static byte ToByte(double value) => (byte)Math.Clamp(Math.Floor(value + 0.5), 0, 255);
    }

    private static void Require(ReadOnlySpan<byte> data, int offset, int count, string what)
    {
        if (offset < 0 || count < 0 || offset > data.Length - count)
            throw new InvalidDataException($"The {what} is truncated.");
    }

    private static void ParseQuantization(ReadOnlySpan<byte> payload, int[]?[] tables)
    {
        while (payload.Length > 0)
        {
            var precision = payload[0] >> 4;
            var id = payload[0] & 0x0F;
            if (precision != 0 || id > 3)
                throw new InvalidDataException("Baseline DQT tables are 8-bit with identifiers 0 to 3.");

            if (payload.Length < 65)
                throw new InvalidDataException("The DQT segment is truncated.");

            var table = new int[64];
            for (var k = 0; k < 64; k++)
            {
                table[ZigZag[k]] = payload[1 + k];
                if (payload[1 + k] == 0)
                    throw new InvalidDataException("A quantization value is zero.");
            }

            tables[id] = table;
            payload = payload[65..];
        }
    }

    private static void ParseHuffman(ReadOnlySpan<byte> payload, HuffmanTable?[] dcTables, HuffmanTable?[] acTables)
    {
        while (payload.Length > 0)
        {
            var tableClass = payload[0] >> 4;
            var id = payload[0] & 0x0F;
            if (tableClass > 1 || id > 3)
                throw new InvalidDataException("DHT tables are DC or AC with identifiers 0 to 3.");

            if (payload.Length < 17)
                throw new InvalidDataException("The DHT segment is truncated.");

            var bits = payload.Slice(1, 16);
            var count = 0;
            foreach (var value in bits)
            {
                count += value;
            }

            if (count == 0 || count > 256 || payload.Length < 17 + count)
                throw new InvalidDataException("The DHT code counts are invalid.");

            var table = new HuffmanTable(bits, payload.Slice(17, count), tableClass == 0 ? 11 : 0xFA);
            (tableClass == 0 ? dcTables : acTables)[id] = table;
            payload = payload[(17 + count)..];
        }
    }

    private static (int Width, int Height, (int Id, int H, int V, int Tq)[] Components) ParseFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6 || payload[0] != 8)
            throw new InvalidDataException("The frame header must have 8-bit precision.");

        var height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
        var width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
        var count = payload[5];
        if (width == 0 || height == 0 || count is not (1 or 3) || payload.Length != 6 + (3 * count))
            throw new InvalidDataException("The frame header has invalid dimensions or component count.");

        var components = new (int Id, int H, int V, int Tq)[count];
        var blocks = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = payload.Slice(6 + (3 * i), 3);
            components[i] = (entry[0], entry[1] >> 4, entry[1] & 0x0F, entry[2]);
            if (components[i].H is < 1 or > 4 || components[i].V is < 1 or > 4 || components[i].Tq > 3)
                throw new InvalidDataException("A component has invalid sampling factors or quantization table.");

            if (components.Take(i).Any(other => other.Id == components[i].Id))
                throw new InvalidDataException("Two components share an identifier.");

            blocks += components[i].H * components[i].V;
        }

        if (count > 1 && blocks > 10)
            throw new InvalidDataException("An MCU has more than 10 blocks.");

        return (width, height, components);
    }

    private static ReferenceJpegScan ParseScan(ReadOnlySpan<byte> payload, (int Id, int H, int V, int Tq)[] frame, int[]?[] quantization, HuffmanTable?[] dcTables, HuffmanTable?[] acTables)
    {
        var count = payload.Length > 0 ? payload[0] : 0;
        if (count != frame.Length || payload.Length != 1 + (2 * count) + 3)
            throw new InvalidDataException("The scan must code every component in one interleaved scan.");

        if (dcTables[2] is not null || dcTables[3] is not null || acTables[2] is not null || acTables[3] is not null)
            throw new InvalidDataException("Baseline DHT tables have the identifiers 0 or 1.");

        var components = new ReferenceJpegScanComponent[count];
        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var tables = payload[2 + (2 * i)];
            var (frameId, _, _, tq) = frame[i];
            if (id != frameId)
                throw new InvalidDataException("The scan components are not in frame order.");

            var dc = tables >> 4;
            var ac = tables & 0x0F;
            if (dc > 1 || ac > 1 || dcTables[dc] is null || acTables[ac] is null || quantization[tq] is null)
                throw new InvalidDataException("The scan refers to an undefined table.");

            components[i] = new ReferenceJpegScanComponent(i, dc, ac);
        }

        var tail = payload[(1 + (2 * count))..];
        if (tail[0] != 0 || tail[1] != 63 || tail[2] != 0)
            throw new InvalidDataException("A baseline scan has Ss = 0, Se = 63 and Ah = Al = 0.");

        return new ReferenceJpegScan(components, 0, 63, 0, 0);
    }

    private static ReferenceJpegScan ParseProgressiveScan(ReadOnlySpan<byte> payload, (int Id, int H, int V, int Tq)[] frame, int[]?[] quantization, HuffmanTable?[] dcTables, HuffmanTable?[] acTables, int[][] approximation)
    {
        var count = payload.Length > 0 ? payload[0] : 0;
        if (count < 1 || count > frame.Length || payload.Length != 1 + (2 * count) + 3)
            throw new InvalidDataException("The scan header has an invalid component count or length.");

        var tail = payload[(1 + (2 * count))..];
        int start = tail[0];
        int end = tail[1];
        var high = tail[2] >> 4;
        var low = tail[2] & 0x0F;
        if (start == 0 ? end != 0 : count != 1 || end < start || end > 63)
            throw new InvalidDataException("A progressive scan codes the DC coefficient (Ss = Se = 0) or a band of AC coefficients of one component.");

        if (low > 13 || (high != 0 && high != low + 1))
            throw new InvalidDataException("A progressive scan has Al of at most 13 and Ah equal to 0 or Al + 1.");

        var components = new ReferenceJpegScanComponent[count];
        var previous = -1;
        for (var i = 0; i < count; i++)
        {
            var id = payload[1 + (2 * i)];
            var tables = payload[2 + (2 * i)];
            var index = Array.FindIndex(frame, component => component.Id == id);
            if (index <= previous)
                throw new InvalidDataException("The scan components are unknown or not in frame order.");

            previous = index;
            var dc = tables >> 4;
            var ac = tables & 0x0F;
            if (dc > 3 || ac > 3 || quantization[frame[index].Tq] is null)
                throw new InvalidDataException("The scan refers to an undefined table.");

            // A DC refinement scan has no Huffman-coded symbol
            if (start == 0 ? high == 0 && dcTables[dc] is null : acTables[ac] is null)
                throw new InvalidDataException("The scan refers to an undefined Huffman table.");

            if (start > 0 && approximation[index][0] < 0)
                throw new InvalidDataException("An AC scan precedes the first DC scan of its component.");

            for (var k = start; k <= end; k++)
            {
                if (approximation[index][k] != (high == 0 ? -1 : high))
                    throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The scan (Ah {high}, Al {low}) does not follow the previous scan of the coefficient {k} of the component {index}."));

                approximation[index][k] = low;
            }

            components[i] = new ReferenceJpegScanComponent(index, dc, ac);
        }

        return new ReferenceJpegScan(components, start, end, high, low);
    }

    private static ReferenceJpegComponent[] GetComponents((int Id, int H, int V, int Tq)[] frame, List<ScanData> scans)
    {
        var components = new ReferenceJpegComponent[frame.Length];
        for (var c = 0; c < frame.Length; c++)
        {
            var dcScan = scans.FirstOrDefault(scan => scan.Header.SpectralStart == 0 && scan.Header.Components.Any(component => component.Index == c))
                ?? throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"No scan codes the component {c}."));
            var acScan = scans.FirstOrDefault(scan => scan.Header.SpectralEnd > 0 && scan.Header.Components.Any(component => component.Index == c))
                ?? throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"No scan codes the AC coefficients of the component {c}."));
            var (id, h, v, tq) = frame[c];
            components[c] = new ReferenceJpegComponent(id, h, v, tq, dcScan.Header.Components.First(component => component.Index == c).DcTable, acScan.Header.Components.First(component => component.Index == c).AcTable);
        }

        return components;
    }

    /// <summary>Decodes one scan into the block grids: the MCUs of an interleaved scan, else the blocks of the component's own grid (T.81 A.2).</summary>
    private void DecodeScan(ScanData scan, (int BlocksX, int BlocksY)[] sizes, int[][] data)
    {
        var header = scan.Header;
        var reader = new BitReader(scan.Entropy.Span);
        var predictions = new int[Components.Count];
        var endOfBandRun = 0;
        var mcu = 0;
        if (header.Components.Count == 1)
        {
            var selection = header.Components[0];
            var c = selection.Index;
            var blocksX = (GetComponentWidth(c) + 7) / 8;
            var blocksY = (GetComponentHeight(c) + 7) / 8;
            for (var by = 0; by < blocksY; by++)
            {
                for (var bx = 0; bx < blocksX; bx++)
                {
                    Restart(ref reader, mcu++, predictions, ref endOfBandRun);
                    DecodeBlock(ref reader, scan, selection, predictions, ref endOfBandRun, data[c].AsSpan(((by * sizes[c].BlocksX) + bx) * 64, 64));
                }
            }
        }
        else
        {
            for (var my = 0; my < McusY; my++)
            {
                for (var mx = 0; mx < McusX; mx++)
                {
                    Restart(ref reader, mcu++, predictions, ref endOfBandRun);
                    foreach (var selection in header.Components)
                    {
                        var c = selection.Index;
                        var component = Components[c];
                        for (var by = 0; by < component.V; by++)
                        {
                            for (var bx = 0; bx < component.H; bx++)
                            {
                                var index = (((my * component.V) + by) * sizes[c].BlocksX) + (mx * component.H) + bx;
                                DecodeBlock(ref reader, scan, selection, predictions, ref endOfBandRun, data[c].AsSpan(index * 64, 64));
                            }
                        }
                    }
                }
            }
        }

        if (endOfBandRun != 0)
            throw new InvalidDataException("An end-of-band run passes the last block of its scan.");

        reader.EnsureEndPadding();
    }

    /// <summary>Reads the restart marker before an MCU that starts a restart interval, and starts the decoding again.</summary>
    private void Restart(ref BitReader reader, int mcu, int[] predictions, ref int endOfBandRun)
    {
        if (RestartInterval == 0 || mcu == 0 || mcu % RestartInterval != 0)
            return;

        if (endOfBandRun != 0)
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"An end-of-band run passes the restart marker before the MCU {mcu}."));

        reader.ReadRestartMarker(((mcu / RestartInterval) - 1) & 7);
        Array.Clear(predictions);
    }

    private void DecodeBlock(ref BitReader reader, ScanData scan, ReferenceJpegScanComponent selection, int[] predictions, ref int endOfBandRun, Span<int> natural)
    {
        var header = scan.Header;
        if (!IsProgressive)
        {
            DecodeSequentialBlock(ref reader, scan.DcTables[selection.DcTable]!, scan.AcTables[selection.AcTable]!, ref predictions[selection.Index], natural);
        }
        else if (header.SpectralStart == 0)
        {
            if (header.SuccessiveHigh == 0)
            {
                // DC first scan: the difference of the coefficients shifted right by Al (T.81 G.1.2.1)
                var size = scan.DcTables[selection.DcTable]!.Decode(ref reader);
                if (size > 11)
                    throw new InvalidDataException("A DC category is above 11.");

                predictions[selection.Index] += Extend(reader.ReadBits(size), size);
                natural[0] = predictions[selection.Index] * (1 << header.SuccessiveLow);
            }
            else if (reader.ReadBits(1) == 1)
            {
                // DC refinement scan: the bit Al of the two's complement value
                natural[0] |= 1 << header.SuccessiveLow;
            }
        }
        else if (header.SuccessiveHigh == 0)
        {
            DecodeAcFirst(ref reader, scan.AcTables[selection.AcTable]!, header, ref endOfBandRun, natural);
        }
        else
        {
            DecodeAcRefinement(ref reader, scan.AcTables[selection.AcTable]!, header, ref endOfBandRun, natural);
        }
    }

    private static void DecodeSequentialBlock(ref BitReader reader, HuffmanTable dcTable, HuffmanTable acTable, ref int prediction, Span<int> natural)
    {
        var size = dcTable.Decode(ref reader);
        if (size > 11)
            throw new InvalidDataException("A DC category is above 11.");

        prediction += Extend(reader.ReadBits(size), size);
        natural[0] = prediction;
        for (var k = 1; k < 64;)
        {
            var symbol = acTable.Decode(ref reader);
            var run = symbol >> 4;
            var magnitude = symbol & 0x0F;
            if (magnitude == 0)
            {
                if (run == 0)
                    return; // EOB

                if (run != 15)
                    throw new InvalidDataException("Invalid AC symbol.");

                k += 16; // ZRL
                continue;
            }

            k += run;
            if (k > 63 || magnitude > 10)
                throw new InvalidDataException("An AC run passes the last coefficient or a magnitude is above 10.");

            natural[ZigZag[k]] = Extend(reader.ReadBits(magnitude), magnitude);
            k++;
        }
    }

    /// <summary>AC first scan (T.81 G.1.2.2): run/size codes within the band, the values scaled by <c>2^Al</c>, and EOBn codes ending a run of blocks.</summary>
    private static void DecodeAcFirst(ref BitReader reader, HuffmanTable table, ReferenceJpegScan header, ref int endOfBandRun, Span<int> natural)
    {
        if (endOfBandRun > 0)
        {
            endOfBandRun--;
            return;
        }

        for (var k = header.SpectralStart; k <= header.SpectralEnd;)
        {
            var symbol = table.Decode(ref reader);
            var run = symbol >> 4;
            var magnitude = symbol & 0x0F;
            if (magnitude == 0)
            {
                if (run == 15)
                {
                    k += 16; // ZRL
                    continue;
                }

                // EOBn: this block and the next 2^n + bits - 1 blocks have no more coefficient in the band
                endOfBandRun = (1 << run) + reader.ReadBits(run) - 1;
                return;
            }

            k += run;
            if (k > header.SpectralEnd || magnitude > 10)
                throw new InvalidDataException("An AC run passes the end of the band or a magnitude is above 10.");

            natural[ZigZag[k]] = Extend(reader.ReadBits(magnitude), magnitude) * (1 << header.SuccessiveLow);
            k++;
        }
    }

    /// <summary>
    /// AC refinement scan (T.81 G.1.2.3): each code gives the number of still-zero coefficients to pass and either a
    /// coefficient that becomes nonzero (magnitude 1 at bit Al, with its sign) or nothing (ZRL, EOBn); every nonzero
    /// coefficient passed over, including those of the blocks of an end-of-band run, has one correction bit.
    /// </summary>
    private static void DecodeAcRefinement(ref BitReader reader, HuffmanTable table, ReferenceJpegScan header, ref int endOfBandRun, Span<int> natural)
    {
        var bit = 1 << header.SuccessiveLow;
        var k = header.SpectralStart;
        while (endOfBandRun == 0 && k <= header.SpectralEnd)
        {
            var symbol = table.Decode(ref reader);
            var run = symbol >> 4;
            var magnitude = symbol & 0x0F;
            var value = 0;
            if (magnitude == 1)
            {
                value = reader.ReadBits(1) == 1 ? bit : -bit;
            }
            else if (magnitude != 0)
            {
                throw new InvalidDataException("A refinement scan codes a magnitude other than 1.");
            }
            else if (run != 15)
            {
                endOfBandRun = (1 << run) + reader.ReadBits(run);
                break;
            }

            // Pass the run of zeros (16 for ZRL), correcting the nonzero coefficients on the way
            for (; k <= header.SpectralEnd; k++)
            {
                ref var coefficient = ref natural[ZigZag[k]];
                if (coefficient != 0)
                {
                    Correct(ref reader, ref coefficient, bit);
                }
                else if (run-- == 0)
                {
                    break;
                }
            }

            if (magnitude == 1)
            {
                if (k > header.SpectralEnd)
                    throw new InvalidDataException("An AC run passes the end of the band.");

                natural[ZigZag[k]] = value;
            }

            k++;
        }

        if (endOfBandRun > 0)
        {
            for (; k <= header.SpectralEnd; k++)
            {
                ref var coefficient = ref natural[ZigZag[k]];
                if (coefficient != 0)
                {
                    Correct(ref reader, ref coefficient, bit);
                }
            }

            endOfBandRun--;
        }

        static void Correct(ref BitReader reader, ref int coefficient, int bit)
        {
            if (reader.ReadBits(1) == 1)
            {
                coefficient += coefficient > 0 ? bit : -bit;
            }
        }
    }

    private static int Extend(int bits, int size) => size == 0 ? 0 : bits < (1 << (size - 1)) ? bits - (1 << size) + 1 : bits;

    /// <summary>A scan with the Huffman tables defined when it starts and its entropy-coded data.</summary>
    private sealed record ScanData(ReferenceJpegScan Header, HuffmanTable?[] DcTables, HuffmanTable?[] AcTables, ReadOnlyMemory<byte> Entropy);

    private sealed class HuffmanTable
    {
        private readonly Dictionary<(int Length, int Code), int> _symbols = [];

        public HuffmanTable(ReadOnlySpan<byte> bits, ReadOnlySpan<byte> values, int maxSymbol)
        {
            var lengths = new int[256];
            var code = 0;
            var k = 0;
            for (var length = 1; length <= 16; length++)
            {
                for (var i = 0; i < bits[length - 1]; i++)
                {
                    var symbol = values[k++];
                    if (lengths[symbol] != 0)
                        throw new InvalidDataException("A Huffman symbol is defined twice.");

                    if (symbol > maxSymbol)
                        throw new InvalidDataException("A Huffman symbol is outside the baseline range.");

                    lengths[symbol] = length;
                    _symbols.Add((length, code), symbol);
                    code++;
                }

                if (code > 1 << length)
                    throw new InvalidDataException("The Huffman code lengths are oversubscribed.");

                code <<= 1;
            }

            Lengths = lengths;
        }

        public IReadOnlyList<int> Lengths { get; }

        public int Decode(ref BitReader reader)
        {
            var code = 0;
            for (var length = 1; length <= 16; length++)
            {
                code = (code << 1) | reader.ReadBits(1);
                if (_symbols.TryGetValue((length, code), out var symbol))
                    return symbol;
            }

            throw new InvalidDataException("Invalid Huffman code.");
        }
    }

    /// <summary>Reads bits most significant first from entropy-coded data, removing the stuffed zero after each 0xFF.</summary>
    private ref struct BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;
        private int _byte;
        private int _bitsLeft;

        public int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                if (_bitsLeft == 0)
                {
                    if (_offset >= _data.Length)
                        throw new InvalidDataException("The entropy-coded data ends before the last MCU.");

                    _byte = _data[_offset++];
                    if (_byte == 0xFF)
                    {
                        // The stuffed 0x00; the parser also let restart markers through
                        if (_data[_offset] != 0x00)
                            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"A marker 0xFF{_data[_offset]:X2} is inside a restart interval or follows its end too early."));

                        _offset++;
                    }

                    _bitsLeft = 8;
                }

                _bitsLeft--;
                value = (value << 1) | ((_byte >> _bitsLeft) & 1);
            }

            return value;
        }

        /// <summary>Requires the end of an interval: the current byte padded with 1-bits, then the restart marker <c>RSTn</c>.</summary>
        public void ReadRestartMarker(int number)
        {
            var mask = (1 << _bitsLeft) - 1;
            if ((_byte & mask) != mask)
                throw new InvalidDataException("The last entropy-coded byte of a restart interval is not padded with 1-bits.");

            if (_offset + 2 > _data.Length || _data[_offset] != 0xFF || _data[_offset + 1] != 0xD0 + number)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Expected the restart marker RST{number} at the end of an interval."));

            _offset += 2;
            _bitsLeft = 0;
        }

        public readonly void EnsureEndPadding()
        {
            if (_offset != _data.Length)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"{_data.Length - _offset} entropy-coded byte(s) follow the last MCU."));

            var mask = (1 << _bitsLeft) - 1;
            if ((_byte & mask) != mask)
                throw new InvalidDataException("The last entropy-coded byte is not padded with 1-bits.");
        }
    }
}
