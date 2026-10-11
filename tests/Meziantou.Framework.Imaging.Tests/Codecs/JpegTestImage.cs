using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A JPEG test image defined by its quantized DCT coefficients, with an independent test-side Huffman encoder
/// (DQT/DHT/SOF/SOS/DRI segments, DC prediction, run-length AC coding with ZRL/EOB, byte stuffing, restart markers,
/// interleaved and non-interleaved scans; progressive scans of ITU-T T.81 G.1.2 written from the specification: DC first and
/// refinement scans, AC first scans with EOB runs, AC refinement scans with correction bits) and the expected decoded pixels
/// computed from the specifications: the direct
/// two-dimensional inverse DCT of ITU-T T.81 A.3.3 evaluated in double precision, and the upsampling and color conversion
/// contract of the library transcribed pixel by pixel. Nothing here uses the library decoder.
/// </summary>
/// <remarks>
/// Random blocks whose exact inverse DCT has a sample within 1e-6 of a rounding boundary are drawn again, so that the
/// expected samples never depend on the floating-point evaluation order (blocks with only a DC coefficient are exact).
/// </remarks>
internal sealed class JpegTestImage
{
    private static readonly double[][] CosineTable = CreateCosineTable();

    private JpegTestImage(int width, int height, bool rgb, Component[] components, ushort[][] quantizationTables)
    {
        Width = width;
        Height = height;
        Rgb = rgb;
        Components = components;
        QuantizationTables = quantizationTables;
        MaxHorizontal = components.Max(c => c.H);
        MaxVertical = components.Max(c => c.V);
        McusX = (width + (8 * MaxHorizontal) - 1) / (8 * MaxHorizontal);
        McusY = (height + (8 * MaxVertical) - 1) / (8 * MaxVertical);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets a value indicating whether the samples are RGB (Adobe APP14 transform 0) rather than YCbCr.</summary>
    public bool Rgb { get; }

    public Component[] Components { get; }

    /// <summary>Gets the quantization tables (natural order): table 0 for the first component, table 1 for the others.</summary>
    public ushort[][] QuantizationTables { get; }

    public int MaxHorizontal { get; }

    public int MaxVertical { get; }

    public int McusX { get; }

    public int McusY { get; }

    public PixelFormat PixelFormat => Components.Length == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24;

    public static ReadOnlySpan<byte> ZigZag =>
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary>Creates seeded random coefficients.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="sampling">The sampling factors of each component (1 or 3 components).</param>
    /// <param name="seed">The seed.</param>
    /// <param name="rgb">RGB samples instead of YCbCr (3 components).</param>
    /// <param name="acDensity">The probability of a nonzero AC coefficient (decreasing with the frequency).</param>
    /// <param name="dcOnlyProbability">The probability that a block has only a DC coefficient.</param>
    /// <param name="largeQuantization">Quantization values up to 1000 (16-bit tables).</param>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public static JpegTestImage CreateRandom(int width, int height, (int H, int V)[] sampling, int seed, bool rgb = false, double acDensity = 0.35, double dcOnlyProbability = 0.2, bool largeQuantization = false)
    {
        var random = new Random(seed);
        var tables = new ushort[2][];
        for (var t = 0; t < 2; t++)
        {
            tables[t] = new ushort[64];
            for (var i = 0; i < 64; i++)
            {
                tables[t][i] = (ushort)(largeQuantization ? random.Next(200, 1001) : random.Next(1, 41));
            }
        }

        var components = CreateComponents(width, height, sampling, rgb);
        foreach (var component in components)
        {
            var quantization = tables[component.QuantizationTable];
            for (var b = 0; b < component.Blocks.Length; b++)
            {
                int[] block;
                do
                {
                    block = CreateBlock(random, quantization, acDensity, dcOnlyProbability);
                }
                while (!TryTransform(block, quantization, out _));

                component.Blocks[b] = block;
            }
        }

        return new JpegTestImage(width, height, rgb, components, tables);
    }

    /// <summary>Creates an image from given quantized coefficients (used to build realistic progressive inputs, e.g. for benchmarks).</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="sampling">The sampling factors of each component (1 or 3 components, YCbCr).</param>
    /// <param name="quantizationTables">The two quantization tables (natural order): table 0 for the first component, table 1 for the others.</param>
    /// <param name="getBlock">Gets the 64 quantized coefficients (natural order) of a component block: component index, block column, block row.</param>
    public static JpegTestImage FromCoefficients(int width, int height, (int H, int V)[] sampling, ushort[][] quantizationTables, Func<int, int, int, int[]> getBlock)
    {
        var components = CreateComponents(width, height, sampling, rgb: false);
        for (var c = 0; c < components.Length; c++)
        {
            var component = components[c];
            for (var by = 0; by < component.BlocksY; by++)
            {
                for (var bx = 0; bx < component.BlocksX; bx++)
                {
                    component.Blocks[(by * component.BlocksX) + bx] = getBlock(c, bx, by);
                }
            }
        }

        return new JpegTestImage(width, height, rgb: false, components, quantizationTables);
    }

    /// <summary>Gets the exact inverse DCT of a block (T.81 A.3.3), level shifted, rounded (ties upward) and clamped.</summary>
    /// <returns><see langword="false"/> if a sample is within 1e-6 of a rounding boundary.</returns>
    public static bool TryTransform(int[] coefficients, ushort[] quantization, out byte[] samples)
    {
        samples = new byte[64];
        if (coefficients.AsSpan(1).IndexOfAnyExcept(0) < 0)
        {
            // Exact: DC * Q / 8, ties upward
            var value = (byte)Math.Clamp((int)Math.Floor((coefficients[0] * (double)quantization[0] / 8) + 0.5) + 128, 0, 255);
            samples.AsSpan().Fill(value);
            return true;
        }

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var sum = 0.0;
                for (var v = 0; v < 8; v++)
                {
                    for (var u = 0; u < 8; u++)
                    {
                        var coefficient = coefficients[(v * 8) + u];
                        if (coefficient != 0)
                        {
                            sum += Scale(u) * Scale(v) * coefficient * quantization[(v * 8) + u] * CosineTable[x][u] * CosineTable[y][v];
                        }
                    }
                }

                var shifted = (sum / 4) + 128;
                var rounded = Math.Floor(shifted + 0.5);
                if (Math.Abs(shifted + 0.5 - rounded) < 1e-6 && rounded is > 0 and <= 255)
                    return false;

                samples[(y * 8) + x] = (byte)Math.Clamp(rounded, 0, 255);
            }
        }

        return true;

        static double Scale(int k) => k == 0 ? 1 / Math.Sqrt(2) : 1;
    }

    private static Component[] CreateComponents(int width, int height, (int H, int V)[] sampling, bool rgb)
    {
        // The only component of a grayscale frame is not subsampled, whatever its declared factors
        var maxH = sampling.Length == 1 ? 1 : sampling.Max(s => s.H);
        var maxV = sampling.Length == 1 ? 1 : sampling.Max(s => s.V);
        var mcusX = (width + (8 * maxH) - 1) / (8 * maxH);
        var mcusY = (height + (8 * maxV) - 1) / (8 * maxV);
        var components = new Component[sampling.Length];
        for (var c = 0; c < sampling.Length; c++)
        {
            var (h, v) = sampling.Length == 1 ? (1, 1) : sampling[c];
            components[c] = new Component(
                id: (byte)(rgb ? "RGB"[c] : c + 1),
                h, v,
                declared: sampling[c],
                width: (int)Math.Ceiling(width * (double)h / maxH),
                height: (int)Math.Ceiling(height * (double)v / maxV),
                blocksX: sampling.Length == 1 ? (width + 7) / 8 : mcusX * h,
                blocksY: sampling.Length == 1 ? (height + 7) / 8 : mcusY * v,
                quantizationTable: c == 0 ? 0 : 1);
        }

        return components;
    }

    /// <summary>Gets the expected samples of a component (its real width and height).</summary>
    public byte[] GetComponentSamples(int index)
    {
        var component = Components[index];
        var quantization = QuantizationTables[component.QuantizationTable];
        var plane = new byte[component.Width * component.Height];
        for (var by = 0; by * 8 < component.Height; by++)
        {
            for (var bx = 0; bx * 8 < component.Width; bx++)
            {
                if (!TryTransform(component.Blocks[(by * component.BlocksX) + bx], quantization, out var samples))
                    throw new InvalidOperationException("Ambiguous block.");

                for (var y = 0; y < 8 && (by * 8) + y < component.Height; y++)
                {
                    for (var x = 0; x < 8 && (bx * 8) + x < component.Width; x++)
                    {
                        plane[(((by * 8) + y) * component.Width) + (bx * 8) + x] = samples[(y * 8) + x];
                    }
                }
            }
        }

        return plane;
    }

    /// <summary>Gets the expected decoded pixels: <see cref="PixelFormat.Gray8"/> or <see cref="PixelFormat.Rgb24"/>.</summary>
    public byte[] GetExpectedPixels()
    {
        var planes = Enumerable.Range(0, Components.Length).Select(GetComponentSamples).ToArray();
        var channels = Components.Length;
        var pixels = new byte[Width * Height * channels];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * channels;
                if (channels == 1)
                {
                    pixels[offset] = planes[0][(y * Width) + x];
                    continue;
                }

                var c0 = Upsample(Components[0], planes[0], x, y);
                var c1 = Upsample(Components[1], planes[1], x, y);
                var c2 = Upsample(Components[2], planes[2], x, y);
                if (Rgb)
                {
                    (pixels[offset], pixels[offset + 1], pixels[offset + 2]) = ((byte)c0, (byte)c1, (byte)c2);
                }
                else
                {
                    // JFIF full range, 16-bit fixed-point coefficients, ties upward
                    var cb = c1 - 128;
                    var cr = c2 - 128;
                    pixels[offset] = Clamp(c0 + (((91881 * cr) + 32768) >> 16));
                    pixels[offset + 1] = Clamp(c0 + (((-22554 * cb) - (46802 * cr) + 32768) >> 16));
                    pixels[offset + 2] = Clamp(c0 + (((116130 * cb) + 32768) >> 16));
                }
            }
        }

        return pixels;

        static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }

    /// <summary>
    /// Gets the image whose coefficients keep only the bits a partial progression sends: the coefficient at zig-zag position
    /// <c>k</c> of component <c>c</c> keeps its bits from <c>lowBit(c, k)</c> up (DC: arithmetic shift, AC: magnitude
    /// shift, T.81 G.1.2.1), or is zero when <c>lowBit</c> is negative (never coded).
    /// </summary>
    public JpegTestImage Approximate(Func<int, int, int> lowBit)
    {
        var components = new Component[Components.Length];
        for (var c = 0; c < components.Length; c++)
        {
            var source = Components[c];
            var copy = new Component(source.Id, source.H, source.V, source.Declared, source.Width, source.Height, source.BlocksX, source.BlocksY, source.QuantizationTable);
            for (var b = 0; b < source.Blocks.Length; b++)
            {
                var block = new int[64];
                for (var k = 0; k < 64; k++)
                {
                    var low = lowBit(c, k);
                    var value = source.Blocks[b][ZigZag[k]];
                    block[ZigZag[k]] = low < 0 ? 0 : k == 0 ? (value >> low) << low : Math.Sign(value) * ((Math.Abs(value) >> low) << low);
                }

                copy.Blocks[b] = block;
            }

            components[c] = copy;
        }

        return new JpegTestImage(Width, Height, Rgb, components, QuantizationTables);
    }

    /// <summary>Encodes the image.</summary>
    public byte[] Encode(JpegTestEncodeOptions? options = null)
    {
        options ??= new JpegTestEncodeOptions();
        var output = new List<byte> { 0xFF, 0xD8 };
        if (Rgb)
        {
            AddSegment(output, 0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 0]);
        }
        else
        {
            AddSegment(output, 0xE0, [.. "JFIF\0"u8, 1, 2, 0, 0, 1, 0, 1, 0, 0]);
        }

        foreach (var segment in options.SegmentsBeforeFrame)
        {
            output.AddRange(segment);
        }

        var sixteenBit = QuantizationTables.Any(table => table.Any(value => value > 255));
        var tableCount = Components.Length == 1 ? 1 : 2;
        for (var t = 0; t < tableCount; t++)
        {
            var dqt = new List<byte> { (byte)((sixteenBit ? 0x10 : 0) | t) };
            for (var k = 0; k < 64; k++)
            {
                var value = QuantizationTables[t][ZigZag[k]];
                if (sixteenBit)
                {
                    dqt.Add((byte)(value >> 8));
                }

                dqt.Add((byte)value);
            }

            AddSegment(output, 0xDB, [.. dqt]);
        }

        var sof = new List<byte> { 8, (byte)(Height >> 8), (byte)Height, (byte)(Width >> 8), (byte)Width, (byte)Components.Length };
        foreach (var component in Components)
        {
            sof.AddRange([component.Id, (byte)((component.Declared.H << 4) | component.Declared.V), (byte)component.QuantizationTable]);
        }

        var progression = options.Progression;
        AddSegment(output, progression is null ? options.FrameMarker : (byte)0xC2, [.. sof]);
        var tables = Enumerable.Range(0, tableCount).Select(t => (Dc: HuffmanCode.Create(false, options.Huffman, options.HuffmanSeed + (2 * t), progression is not null), Ac: HuffmanCode.Create(true, options.Huffman, options.HuffmanSeed + (2 * t) + 1, progression is not null))).ToArray();
        if (!options.TablesBeforeEachScan)
        {
            WriteHuffmanTables(output, tables);
        }

        if (options.RestartInterval > 0)
        {
            AddSegment(output, 0xDD, [(byte)(options.RestartInterval >> 8), (byte)options.RestartInterval]);
        }

        if (progression is not null)
        {
            foreach (var scan in progression)
            {
                if (options.TablesBeforeEachScan)
                {
                    WriteHuffmanTables(output, tables);
                }

                var sos = new List<byte> { (byte)scan.Components.Length };
                foreach (var index in scan.Components)
                {
                    var table = Components[index].QuantizationTable;
                    sos.AddRange([Components[index].Id, (byte)((table << 4) | table)]);
                }

                sos.AddRange([(byte)scan.Start, (byte)scan.End, (byte)((scan.High << 4) | scan.Low)]);
                AddSegment(output, 0xDA, [.. sos]);
                new ProgressiveScanWriter(this, output, scan, tables, options.MaxEndOfBandRun).Write(options.RestartInterval);
            }
        }

        var scans = progression is not null ? [] : options.Scans ?? [[.. Enumerable.Range(0, Components.Length)]];
        foreach (var scan in scans)
        {
            if (options.TablesBeforeEachScan)
            {
                WriteHuffmanTables(output, tables);
            }

            var sos = new List<byte> { (byte)scan.Length };
            foreach (var index in scan)
            {
                var table = Components[index].QuantizationTable;
                sos.AddRange([Components[index].Id, (byte)((table << 4) | table)]);
            }

            sos.AddRange([0, 63, 0]);
            AddSegment(output, 0xDA, [.. sos]);
            EncodeScan(output, scan, tables, options.RestartInterval);
        }

        foreach (var segment in options.SegmentsAfterScans)
        {
            output.AddRange(segment);
        }

        output.AddRange([0xFF, 0xD9]);
        return [.. output];
    }

    /// <summary>Packs codes most significant bit first into entropy-coded bytes (stuffed, padded with 1-bits).</summary>
    public static byte[] PackBits(IEnumerable<(int Bits, int Length)> codes)
    {
        var output = new List<byte>();
        var writer = new BitWriter(output);
        foreach (var (bits, length) in codes)
        {
            writer.Write(bits, length);
        }

        writer.Flush();
        return [.. output];
    }

    public static byte[] Segment(byte marker, ReadOnlySpan<byte> payload)
    {
        var result = new byte[payload.Length + 4];
        result[0] = 0xFF;
        result[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(result.AsSpan(4));
        return result;
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static int[] CreateBlock(Random random, ushort[] quantization, double acDensity, double dcOnlyProbability)
    {
        var block = new int[64];

        // Mean sample offset within the 8-bit range (occasionally beyond, to exercise clamping)
        var mean = random.Next(-140, 141);
        block[0] = (int)Math.Round(mean * 8.0 / quantization[0], MidpointRounding.AwayFromZero);
        if (random.NextDouble() < dcOnlyProbability)
            return block;

        for (var k = 1; k < 64; k++)
        {
            if (random.NextDouble() < acDensity * (1 - (k / 80.0)))
            {
                var natural = ZigZag[k];
                var limit = Math.Clamp(400 / quantization[natural], 1, 1023);
                var value = random.Next(1, limit + 1);
                block[natural] = random.Next(2) == 0 ? value : -value;
            }
        }

        return block;
    }

    private static double[][] CreateCosineTable()
    {
        var table = new double[8][];
        for (var x = 0; x < 8; x++)
        {
            table[x] = new double[8];
            for (var u = 0; u < 8; u++)
            {
                table[x][u] = Math.Cos((((2 * x) + 1) * u * Math.PI) / 16);
            }
        }

        return table;
    }

    private static void AddSegment(List<byte> output, byte marker, byte[] payload) => output.AddRange(Segment(marker, payload));

    private static void WriteHuffmanTables(List<byte> output, (HuffmanCode Dc, HuffmanCode Ac)[] tables)
    {
        for (var t = 0; t < tables.Length; t++)
        {
            AddSegment(output, 0xC4, tables[t].Dc.ToDefinition((byte)t));
            AddSegment(output, 0xC4, tables[t].Ac.ToDefinition((byte)(0x10 | t)));
        }
    }

    private static int GetSize(int value)
    {
        var magnitude = Math.Abs(value);
        var size = 0;
        while (magnitude > 0)
        {
            size++;
            magnitude >>= 1;
        }

        return size;
    }

    /// <summary>The upsampling contract of the library, one output sample at a time.</summary>
    private int Upsample(Component component, byte[] plane, int x, int y)
    {
        var rh = MaxHorizontal / component.H;
        var rv = MaxVertical / component.V;
        int S(int sx, int sy) => plane[(Math.Clamp(sy, 0, component.Height - 1) * component.Width) + Math.Clamp(sx, 0, component.Width - 1)];
        if (rh == 1 && rv == 1)
            return S(x, y);

        // A horizontal factor of 2 on a plane of at most 2 samples replicates in both directions (libjpeg-turbo's rule)
        if (rh == 2 && component.Width <= 2)
            return S(x / rh, y / rv);

        if (rv == 2 && rh <= 2)
        {
            var row = y / 2;
            var far = y % 2 == 0 ? row - 1 : row + 1;
            if (rh == 1)
                return ((3 * S(x, row)) + S(x, far) + (y % 2 == 0 ? 1 : 2)) >> 2;

            var column = x / 2;
            var neighbor = x % 2 == 0 ? column - 1 : column + 1;
            var near = (3 * S(column, row)) + S(column, far);
            var other = (3 * S(neighbor, row)) + S(neighbor, far);
            return ((3 * near) + other + (x % 2 == 0 ? 8 : 7)) >> 4;
        }

        if (rv == 1 && rh == 2)
        {
            var column = x / 2;
            var neighbor = x % 2 == 0 ? column - 1 : column + 1;
            return ((3 * S(column, y)) + S(neighbor, y) + (x % 2 == 0 ? 1 : 2)) >> 2;
        }

        return S(x / rh, y / rv);
    }

    private void EncodeScan(List<byte> output, int[] scan, (HuffmanCode Dc, HuffmanCode Ac)[] tables, int restartInterval)
    {
        var writer = new BitWriter(output);
        var predictions = new int[Components.Length];
        var interleaved = scan.Length > 1;
        var single = Components[scan[0]];
        var mcusX = interleaved ? McusX : (single.Width + 7) / 8;
        var mcusY = interleaved ? McusY : (single.Height + 7) / 8;
        var total = mcusX * mcusY;
        var restartIndex = 0;
        for (var mcu = 0; mcu < total; mcu++)
        {
            if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
            {
                writer.Flush();
                output.AddRange([0xFF, (byte)(0xD0 + (restartIndex & 7))]);
                restartIndex++;
                Array.Clear(predictions);
            }

            var mx = mcu % mcusX;
            var my = mcu / mcusX;
            foreach (var index in scan)
            {
                var component = Components[index];
                var (dc, ac) = tables[component.QuantizationTable];
                var h = interleaved ? component.H : 1;
                var v = interleaved ? component.V : 1;
                for (var by = 0; by < v; by++)
                {
                    for (var bx = 0; bx < h; bx++)
                    {
                        var block = component.Blocks[((((my * v) + by) * component.BlocksX) + (mx * h)) + bx];
                        EncodeBlock(writer, block, ref predictions[index], dc, ac);
                    }
                }
            }
        }

        writer.Flush();
    }

    private static void EncodeBlock(BitWriter writer, int[] block, ref int prediction, HuffmanCode dc, HuffmanCode ac)
    {
        var difference = block[0] - prediction;
        prediction = block[0];
        var size = GetSize(difference);
        dc.Write(writer, size);
        WriteValue(writer, difference, size);
        var run = 0;
        for (var k = 1; k < 64; k++)
        {
            var value = block[ZigZag[k]];
            if (value == 0)
            {
                run++;
                continue;
            }

            while (run > 15)
            {
                ac.Write(writer, 0xF0);
                run -= 16;
            }

            size = GetSize(value);
            ac.Write(writer, (run << 4) | size);
            WriteValue(writer, value, size);
            run = 0;
        }

        if (run > 0)
        {
            ac.Write(writer, 0x00);
        }

        static void WriteValue(BitWriter writer, int value, int size)
        {
            if (size > 0)
            {
                writer.Write(value >= 0 ? value : value + (1 << size) - 1, size);
            }
        }
    }

    /// <summary>A frame component and its quantized coefficients (natural order), over the padded block grid of the frame.</summary>
    internal sealed class Component
    {
        public Component(byte id, int h, int v, (int H, int V) declared, int width, int height, int blocksX, int blocksY, int quantizationTable)
        {
            Id = id;
            H = h;
            V = v;
            Declared = declared;
            Width = width;
            Height = height;
            BlocksX = blocksX;
            BlocksY = blocksY;
            QuantizationTable = quantizationTable;
            Blocks = new int[blocksX * blocksY][];
        }

        public byte Id { get; }

        public int H { get; }

        public int V { get; }

        /// <summary>Gets the sampling factors written in the frame header (those of a grayscale frame play no role).</summary>
        public (int H, int V) Declared { get; }

        public int Width { get; }

        public int Height { get; }

        public int BlocksX { get; }

        public int BlocksY { get; }

        public int QuantizationTable { get; }

        public int[][] Blocks { get; }
    }

    /// <summary>A canonical Huffman code (T.81 C) built from per-length counts, for encoding.</summary>
    private sealed class HuffmanCode
    {
        private readonly byte[] _counts;
        private readonly byte[] _symbols;
        private readonly Dictionary<int, (int Code, int Length)> _codes = [];

        private HuffmanCode(byte[] counts, byte[] symbols)
        {
            _counts = counts;
            _symbols = symbols;
            var code = 0;
            var index = 0;
            for (var length = 1; length <= 16; length++)
            {
                for (var i = 0; i < counts[length - 1]; i++)
                {
                    _codes[symbols[index++]] = (code++, length);
                }

                code <<= 1;
            }
        }

        [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
        public static HuffmanCode Create(bool ac, JpegTestHuffmanStyle style, int seed, bool progressive = false)
        {
            // Progressive AC tables also code the end-of-band runs EOB1-EOB14 (T.81 G.1.2.2)
            var symbols = ac
                ? [0x00, 0xF0, .. Enumerable.Range(0, 16).SelectMany(run => Enumerable.Range(1, 10).Select(size => (byte)((run << 4) | size))), .. progressive ? Enumerable.Range(1, 14).Select(run => (byte)(run << 4)) : []]
                : Enumerable.Range(0, 12).Select(value => (byte)value).ToArray();
            var counts = new byte[16];
            if (style == JpegTestHuffmanStyle.Fixed)
            {
                counts[ac ? 7 : 3] = (byte)symbols.Length;
                return new HuffmanCode(counts, symbols);
            }

            var random = new Random(seed);
            random.Shuffle(symbols);

            // Lengths 2..16: a few short codes, then more and more long ones; one slot of length 16 stays unused (no all-ones code)
            var remaining = symbols.Length;
            long free = 1; // unused codes at the current length
            for (var length = 1; length <= 16 && remaining > 0; length++)
            {
                free *= 2;
                var capacityAfter = (long)1 << (16 - length); // codes of length 16 per unused code of this length
                var desired = length == 16 ? remaining : length < 2 ? 0 : Math.Max(1, (length - 1) * (ac ? 2 : 1) / 3);
                var count = (int)Math.Min(desired, remaining);
                while (count > 0 && ((free - count) * capacityAfter) - 1 < remaining - count)
                {
                    count--;
                }

                counts[length - 1] = (byte)count;
                free -= count;
                remaining -= count;
            }

            if (remaining != 0)
                throw new InvalidOperationException("The skewed Huffman code does not fit in 16 bits.");

            return new HuffmanCode(counts, symbols);
        }

        public byte[] ToDefinition(byte classAndId) => [classAndId, .. _counts, .. _symbols];

        public void Write(BitWriter writer, int symbol)
        {
            var (code, length) = _codes[symbol];
            writer.Write(code, length);
        }
    }

    /// <summary>
    /// Writes the entropy-coded data of one progressive scan, written from ITU-T T.81 G.1.2: DC first scans (point-transformed
    /// differences), DC refinements (one bit), AC first scans (run-lengths, EOB runs across blocks) and AC refinements
    /// (newly nonzero coefficients with their sign, correction bits of the others after the symbol that passes them or after
    /// the EOB run that ends their block).
    /// </summary>
    private sealed class ProgressiveScanWriter
    {
        private readonly JpegTestImage _image;
        private readonly List<byte> _output;
        private readonly JpegTestProgressiveScan _scan;
        private readonly (HuffmanCode Dc, HuffmanCode Ac)[] _tables;
        private readonly int _maxEndOfBandRun;
        private readonly List<int> _endOfBandBits = [];
        private readonly int[] _predictions;
        private readonly BitWriter _writer;
        private int _endOfBandRun;

        public ProgressiveScanWriter(JpegTestImage image, List<byte> output, JpegTestProgressiveScan scan, (HuffmanCode Dc, HuffmanCode Ac)[] tables, int maxEndOfBandRun)
        {
            _image = image;
            _output = output;
            _scan = scan;
            _tables = tables;
            _maxEndOfBandRun = maxEndOfBandRun;
            _predictions = new int[image.Components.Length];
            _writer = new BitWriter(output);
        }

        private HuffmanCode Ac => _tables[_image.Components[_scan.Components[0]].QuantizationTable].Ac;

        public void Write(int restartInterval)
        {
            var interleaved = _scan.Components.Length > 1;
            var single = _image.Components[_scan.Components[0]];
            var mcusX = interleaved ? _image.McusX : (single.Width + 7) / 8;
            var mcusY = interleaved ? _image.McusY : (single.Height + 7) / 8;
            var total = mcusX * mcusY;
            var restartIndex = 0;
            for (var mcu = 0; mcu < total; mcu++)
            {
                if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
                {
                    FlushEndOfBandRun();
                    _writer.Flush();
                    _output.AddRange([0xFF, (byte)(0xD0 + (restartIndex & 7))]);
                    restartIndex++;
                    Array.Clear(_predictions);
                }

                var mx = mcu % mcusX;
                var my = mcu / mcusX;
                foreach (var index in _scan.Components)
                {
                    var component = _image.Components[index];
                    var h = interleaved ? component.H : 1;
                    var v = interleaved ? component.V : 1;
                    for (var by = 0; by < v; by++)
                    {
                        for (var bx = 0; bx < h; bx++)
                        {
                            var block = component.Blocks[((((my * v) + by) * component.BlocksX) + (mx * h)) + bx];
                            WriteBlock(index, block);
                        }
                    }
                }
            }

            FlushEndOfBandRun();
            _writer.Flush();
        }

        private void WriteBlock(int componentIndex, int[] block)
        {
            var low = _scan.Low;
            if (_scan.Start == 0)
            {
                if (_scan.High != 0)
                {
                    _writer.Write((block[0] >> low) & 1, 1);
                    return;
                }

                // Arithmetic shift of the DC value (T.81 G.1.2.1), then the sequential DC coding of the difference
                var value = block[0] >> low;
                var difference = value - _predictions[componentIndex];
                _predictions[componentIndex] = value;
                var size = GetSize(difference);
                _tables[_image.Components[componentIndex].QuantizationTable].Dc.Write(_writer, size);
                WriteValue(difference, size);
                return;
            }

            if (_scan.High == 0)
            {
                WriteAcFirst(block, low);
            }
            else
            {
                WriteAcRefinement(block, low);
            }
        }

        private void WriteAcFirst(int[] block, int low)
        {
            var run = 0;
            for (var k = _scan.Start; k <= Math.Min(_scan.End, 63); k++)
            {
                var coefficient = block[ZigZag[k]];
                var value = Math.Sign(coefficient) * (Math.Abs(coefficient) >> low);
                if (value == 0)
                {
                    run++;
                    continue;
                }

                FlushEndOfBandRun();
                while (run > 15)
                {
                    Ac.Write(_writer, 0xF0);
                    run -= 16;
                }

                var size = GetSize(value);
                Ac.Write(_writer, (run << 4) | size);
                WriteValue(value, size);
                run = 0;
            }

            if (run > 0)
            {
                _endOfBandRun++;
                if (_endOfBandRun == _maxEndOfBandRun)
                {
                    FlushEndOfBandRun();
                }
            }
        }

        private void WriteAcRefinement(int[] block, int low)
        {
            var magnitudes = new int[64];
            var lastNew = -1;
            for (var k = _scan.Start; k <= Math.Min(_scan.End, 63); k++)
            {
                magnitudes[k] = Math.Abs(block[ZigZag[k]]) >> low;
                if (magnitudes[k] == 1)
                {
                    lastNew = k;
                }
            }

            var run = 0;
            var corrections = new List<int>();
            for (var k = _scan.Start; k <= Math.Min(_scan.End, 63); k++)
            {
                var magnitude = magnitudes[k];
                if (magnitude == 0)
                {
                    run++;
                    continue;
                }

                // ZRLs only when a newly nonzero coefficient follows (otherwise the zeros end in the EOB run)
                while (run > 15 && k <= lastNew)
                {
                    FlushEndOfBandRun();
                    Ac.Write(_writer, 0xF0);
                    run -= 16;
                    WriteBits(corrections);
                }

                if (magnitude > 1)
                {
                    corrections.Add(magnitude & 1); // already nonzero: its correction bit follows the next symbol
                    continue;
                }

                FlushEndOfBandRun();
                Ac.Write(_writer, (run << 4) | 1);
                _writer.Write(block[ZigZag[k]] > 0 ? 1 : 0, 1);
                WriteBits(corrections);
                run = 0;
            }

            if (run > 0 || corrections.Count > 0)
            {
                _endOfBandRun++;
                _endOfBandBits.AddRange(corrections);
                if (_endOfBandRun == _maxEndOfBandRun || _endOfBandBits.Count > 900)
                {
                    FlushEndOfBandRun();
                }
            }
        }

        /// <summary>Writes the pending EOBn symbol (n = floor(log2(run)), then the n low bits of the run) and its correction bits.</summary>
        private void FlushEndOfBandRun()
        {
            if (_endOfBandRun == 0)
                return;

            var bits = 0;
            while ((_endOfBandRun >> (bits + 1)) != 0)
            {
                bits++;
            }

            Ac.Write(_writer, bits << 4);
            if (bits > 0)
            {
                _writer.Write(_endOfBandRun - (1 << bits), bits);
            }

            WriteBits(_endOfBandBits);
            _endOfBandRun = 0;
        }

        private void WriteBits(List<int> bits)
        {
            foreach (var bit in bits)
            {
                _writer.Write(bit, 1);
            }

            bits.Clear();
        }

        private void WriteValue(int value, int size)
        {
            if (size > 0)
            {
                _writer.Write(value >= 0 ? value : value + (1 << size) - 1, size);
            }
        }
    }

    /// <summary>Writes bits most significant first, stuffing a zero byte after every 0xFF and padding with 1-bits.</summary>
    private sealed class BitWriter(List<byte> output)
    {
        private int _accumulator;
        private int _count;

        public void Write(int bits, int length)
        {
            for (var i = length - 1; i >= 0; i--)
            {
                _accumulator = (_accumulator << 1) | ((bits >> i) & 1);
                _count++;
                if (_count == 8)
                {
                    Emit();
                }
            }
        }

        public void Flush()
        {
            while (_count != 0)
            {
                Write(1, 1);
            }
        }

        private void Emit()
        {
            var value = (byte)_accumulator;
            output.Add(value);
            if (value == 0xFF)
            {
                output.Add(0x00);
            }

            _accumulator = 0;
            _count = 0;
        }
    }
}
