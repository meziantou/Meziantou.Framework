using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoder of the DWAA and DWAB compressions (compression notes, section 8.14): the channels are classified by the
/// default rules of OpenEXR (red, green, blue, luminance and chroma channels of half or float samples are lossy, alpha is
/// run-length coded, the others are stored with zlib), and the lossy channels are coded as 8×8 blocks of quantized half
/// DCT coefficients, the red, green and blue channels of one layer through the luma-chroma transform.
/// </summary>
/// <remarks>
/// The data layout is the one <see cref="ExrDwa"/> decodes; the encoder choices are those of section 8.14, except the
/// forward DCT, which is the orthonormal DCT computed in double precision (the inverse of the decoder's), and the
/// quantization, which keeps the value with the fewest set bits among the candidates of the simple strategy of step 7.
/// The buffers grow on demand and are reserved for the largest chunk when the encoder is created.
/// </remarks>
internal sealed class ExrDwaEncoder : IDisposable
{
    private const int HeaderLength = 88;
    private const ushort EndOfBlock = 0xFF00;

    // Section 8.14, step 7: the JPEG quantization tables, natural order
    private static ReadOnlySpan<byte> LumaTable =>
    [
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    ];

    private static ReadOnlySpan<byte> ChromaTable =>
    [
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
    ];

    // Section 8.12: the forward colour transform
    private static readonly float RedToLuma = BitConverter.UInt32BitsToSingle(0x3E59B3D0);
    private static readonly float GreenToLuma = BitConverter.UInt32BitsToSingle(0x3F371759);
    private static readonly float BlueToLuma = BitConverter.UInt32BitsToSingle(0x3D93DD98);
    private static readonly float RedToCb = -BitConverter.UInt32BitsToSingle(0x3DEAB368);
    private static readonly float GreenToCb = BitConverter.UInt32BitsToSingle(0x3EC55326);
    private static readonly float GreenToCr = BitConverter.UInt32BitsToSingle(0x3EE88CE7);
    private static readonly float BlueToCr = BitConverter.UInt32BitsToSingle(0x3D3B98C8);

    // The basis of the orthonormal DCT: Basis[k, n] = alpha(k) cos((2n + 1) k pi / 16)
    private static readonly double[] Basis = CreateBasis();

    // The default rules of OpenEXR (section 8.3): suffix, scheme (1 lossy, 2 run-length), colour component
    private static readonly (string Suffix, int Scheme, int Csc, ExrPixelType PixelType)[] DefaultRules =
    [
        ("R", 1, 0, ExrPixelType.Half), ("R", 1, 0, ExrPixelType.Float),
        ("G", 1, 1, ExrPixelType.Half), ("G", 1, 1, ExrPixelType.Float),
        ("B", 1, 2, ExrPixelType.Half), ("B", 1, 2, ExrPixelType.Float),
        ("Y", 1, -1, ExrPixelType.Half), ("Y", 1, -1, ExrPixelType.Float),
        ("BY", 1, -1, ExrPixelType.Half), ("BY", 1, -1, ExrPixelType.Float),
        ("RY", 1, -1, ExrPixelType.Half), ("RY", 1, -1, ExrPixelType.Float),
        ("A", 2, -1, ExrPixelType.UInt), ("A", 2, -1, ExrPixelType.Half), ("A", 2, -1, ExrPixelType.Float),
    ];

    private readonly AllocationScope _scope;
    private readonly float[] _lumaTolerances = new float[64];
    private readonly float[] _chromaTolerances = new float[64];
    private readonly double[] _block = new double[3 * 64];
    private readonly float[] _values = new float[3 * 64];
    private readonly ushort[] _coefficients = new ushort[3 * 64];
    private readonly List<ChannelState> _channels = [];
    private readonly List<int> _rules = [];
    private ExrHuffman? _huffman;
    private PooledBuffer? _planes;
    private PooledBuffer? _ac;
    private PooledBuffer? _dc;
    private PooledBuffer? _section;
    private PooledBuffer? _rle;
    private readonly PooledBuffer? _compressed;

    /// <summary>Initializes a new instance of the <see cref="ExrDwaEncoder"/> class.</summary>
    /// <param name="scope">The scope that the buffers are charged to.</param>
    /// <param name="level">The compression level (<c>dwaCompressionLevel</c>).</param>
    /// <param name="maxDataLength">The size of the uncompressed data of the largest chunk.</param>
    /// <param name="maxWidth">The width of the largest chunk.</param>
    /// <param name="maxLines">The number of lines of the largest chunk.</param>
    /// <param name="channelCount">The number of channels.</param>
    /// <param name="huffman">Whether the AC symbols are Huffman coded (otherwise, zlib).</param>
    public ExrDwaEncoder(AllocationScope scope, float level, int maxDataLength, int maxWidth, int maxLines, int channelCount, bool huffman)
    {
        _scope = scope;

        // Section 8.14, step 7: the tolerances of the coefficients, in single precision
        var q = Math.Max(level, 0f) / 100000f;
        for (var n = 0; n < 64; n++)
        {
            _lumaTolerances[n] = q * LumaTable[n] / 10f;
            _chromaTolerances[n] = q * ChromaTable[n] / 17f;
        }

        try
        {
            var blocks = (long)((maxWidth + 7) / 8) * ((maxLines + 7) / 8) * channelCount;
            Rent(ref _planes, maxDataLength);
            Rent(ref _ac, checked((int)(2 * 63 * blocks)));
            Rent(ref _dc, checked((int)(4 * blocks)));
            Rent(ref _section, maxDataLength);
            Rent(ref _rle, maxDataLength);
            Rent(ref _compressed, maxDataLength);
            if (huffman)
            {
                _huffman = new ExrHuffman(scope, encoder: true);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Compresses a chunk.</summary>
    /// <param name="source">The uncompressed data of the chunk (section 1).</param>
    /// <param name="destination">Receives the stored data.</param>
    /// <param name="layout">The geometry of the chunk.</param>
    /// <param name="huffman">Whether the AC symbols are Huffman coded (otherwise, zlib).</param>
    /// <returns>The length of the stored data, or -1 when it does not fit <paramref name="destination"/>.</returns>
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination, in ExrChunkLayout layout, bool huffman)
    {
        var planes = Rent(ref _planes, source.Length);
        layout.DeinterleavePlanes(source, planes);
        Classify(layout);
        if (destination.Length < HeaderLength)
            return -1;

        var position = HeaderLength;
        var ruleLength = WriteRules(destination[position..]);
        if (ruleLength < 0)
            return -1;

        position += ruleLength;

        // Section 8.6: the planes of the other channels, with zlib
        long unknownLength = 0;
        long rleLength = 0;
        long lossyBlocks = 0;
        foreach (var state in _channels)
        {
            switch (state.Scheme)
            {
                case 0:
                    unknownLength += state.PlaneLength;
                    break;

                case 2:
                    rleLength += state.PlaneLength;
                    break;

                default:
                    lossyBlocks += (long)((state.Width + 7) / 8) * ((state.Height + 7) / 8);
                    break;
            }
        }

        var section = Rent(ref _section, (int)Math.Max(unknownLength, rleLength));
        var unknownStored = 0;
        if (unknownLength > 0)
        {
            var offset = 0;
            foreach (var state in _channels)
            {
                if (state.Scheme == 0)
                {
                    planes.Slice(state.PlaneOffset, state.PlaneLength).CopyTo(section[offset..]);
                    offset += state.PlaneLength;
                }
            }

            unknownStored = Deflate(section[..offset], destination, ref position);
            if (unknownStored < 0)
                return -1;
        }

        // Sections 8.5 and 8.8 to 8.13: the lossy channels
        var ac = unsafe(MemoryMarshal.Cast<byte, ushort>(Rent(ref _ac, checked((int)(2 * 63 * lossyBlocks)))));
        var dcBytes = Rent(ref _dc, checked((int)(4 * lossyBlocks)));
        var dc = unsafe(MemoryMarshal.Cast<byte, ushort>(dcBytes[..(int)(2 * lossyBlocks)]));
        var acCount = 0;
        var dcCount = 0;
        foreach (var unit in GetUnits())
        {
            EncodeUnit(unit, planes, ac, ref acCount, dc, ref dcCount);
        }

        var acStored = 0;
        if (acCount > 0)
        {
            if (huffman)
            {
                _huffman ??= new ExrHuffman(_scope, encoder: true);
                acStored = _huffman.Encode(ac[..acCount], destination[position..]);
                if (acStored < 0)
                    return -1;

                position += acStored;
            }
            else
            {
                ToLittleEndian(ac[..acCount]);
                acStored = Deflate(unsafe(MemoryMarshal.AsBytes(ac[..acCount])), destination, ref position);
                if (acStored < 0)
                    return -1;
            }
        }

        // Section 8.9: the DC values, with the byte predictor of section 2
        var dcStored = 0;
        if (dcCount > 0)
        {
            ToLittleEndian(dc[..dcCount]);
            var predicted = dcBytes.Slice(2 * dcCount, 2 * dcCount);
            ExrPredictor.Encode(unsafe(MemoryMarshal.AsBytes(dc[..dcCount])), predicted);
            dcStored = Deflate(predicted, destination, ref position);
            if (dcStored < 0)
                return -1;
        }

        // Section 8.7: the byte planes of the run-length coded channels
        var rleStored = 0;
        var rlePacked = 0;
        if (rleLength > 0)
        {
            var offset = 0;
            foreach (var state in _channels)
            {
                if (state.Scheme != 2)
                    continue;

                var plane = planes.Slice(state.PlaneOffset, state.PlaneLength);
                var bytes = state.Channel.BytesPerSample;
                var count = state.Width * state.Height;
                for (var k = 0; k < bytes; k++)
                {
                    for (var i = 0; i < count; i++)
                    {
                        section[offset + (k * count) + i] = plane[(i * bytes) + k];
                    }
                }

                offset += state.PlaneLength;
            }

            // A literal of up to 127 bytes takes one more byte
            var packed = Rent(ref _rle, offset + (offset / 127) + 1);
            rlePacked = ExrRle.Encode(section[..offset], packed);
            if (rlePacked < 0)
                return -1;

            rleStored = Deflate(packed[..rlePacked], destination, ref position);
            if (rleStored < 0)
                return -1;
        }

        // Section 8.2: the header
        ReadOnlySpan<long> header =
        [
            2, unknownLength, unknownStored, acStored, dcStored, rleStored, rlePacked, rleLength, acCount, dcCount, huffman ? 0 : 1,
        ];

        for (var i = 0; i < header.Length; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(destination[(8 * i)..], header[i]);
        }

        return position;
    }

    public void Dispose()
    {
        _huffman?.Dispose();
        _planes?.Dispose();
        _ac?.Dispose();
        _dc?.Dispose();
        _section?.Dispose();
        _rle?.Dispose();
        _compressed?.Dispose();
    }

    /// <summary>
    /// Quantizes a coefficient (section 8.14, step 7): the half value of the coefficient, 0 when it is smaller than the
    /// tolerance, otherwise the value with the fewest set bits within the tolerance among the values whose low bits are
    /// cleared or rounded up (ties by the smaller difference).
    /// </summary>
    internal static ushort Quantize(float coefficient, float tolerance)
    {
        var value = BitConverter.HalfToUInt16Bits((Half)coefficient);
        if ((value & 0x7C00) == 0x7C00)
            return value;

        var magnitude = value & 0x7FFF;
        var target = (float)BitConverter.UInt16BitsToHalf((ushort)magnitude);
        if (target < tolerance)
            return 0;

        var best = magnitude;
        var bestBits = BitOperations.PopCount((uint)magnitude);
        var bestDifference = 0f;
        for (var j = 1; j < 15; j++)
        {
            var mask = ~((1 << j) - 1);
            Consider(magnitude & mask);
            Consider((magnitude + (1 << j)) & mask);
        }

        return (ushort)((value & 0x8000) | best);

        void Consider(int candidate)
        {
            if (candidate > 0x7BFF)
                return;

            var difference = Math.Abs((float)BitConverter.UInt16BitsToHalf((ushort)candidate) - target);
            if (difference >= tolerance)
                return;

            var bits = BitOperations.PopCount((uint)candidate);
            if (bits < bestBits || (bits == bestBits && difference < bestDifference))
            {
                best = candidate;
                bestBits = bits;
                bestDifference = difference;
            }
        }
    }

    /// <summary>Applies the orthonormal 2D DCT to a block of 64 values, natural order, in double precision.</summary>
    internal static void ForwardDct(Span<double> block)
    {
        Span<double> temporary = stackalloc double[64];
        for (var row = 0; row < 8; row++)
        {
            for (var k = 0; k < 8; k++)
            {
                double sum = 0;
                for (var n = 0; n < 8; n++)
                {
                    sum += Basis[(8 * k) + n] * block[(8 * row) + n];
                }

                temporary[(8 * row) + k] = sum;
            }
        }

        for (var column = 0; column < 8; column++)
        {
            for (var k = 0; k < 8; k++)
            {
                double sum = 0;
                for (var n = 0; n < 8; n++)
                {
                    sum += Basis[(8 * k) + n] * temporary[(8 * n) + column];
                }

                block[(8 * k) + column] = sum;
            }
        }
    }

    private static double[] CreateBasis()
    {
        var basis = new double[64];
        for (var k = 0; k < 8; k++)
        {
            var alpha = k == 0 ? Math.Sqrt(0.125) : 0.5;
            for (var n = 0; n < 8; n++)
            {
                basis[(8 * k) + n] = alpha * Math.Cos(((2 * n) + 1) * k * Math.PI / 16);
            }
        }

        return basis;
    }

    private static void ToLittleEndian(Span<ushort> values)
    {
        if (!BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(values, values);
        }
    }

    /// <summary>Classifies the channels with the default rules (sections 8.3 and 8.4) and gives each one its plane.</summary>
    private void Classify(in ExrChunkLayout layout)
    {
        _channels.Clear();
        _rules.Clear();
        var channels = layout.Channels;
        var groups = new List<(string Prefix, int[] Slots)>();
        var offset = 0;
        for (var c = 0; c < channels.Count; c++)
        {
            var channel = channels[c];
            var dot = channel.Name.LastIndexOf('.', StringComparison.Ordinal);
            var suffix = dot < 0 ? channel.Name : channel.Name[(dot + 1)..];
            var prefix = dot < 0 ? string.Empty : channel.Name[..(dot + 1)];
            var state = new ChannelState(channel, layout.GetSampleCount(c), layout.GetLineCount(c), offset);
            offset += state.PlaneLength;
            var csc = -1;
            for (var r = 0; r < DefaultRules.Length; r++)
            {
                var rule = DefaultRules[r];
                if (rule.PixelType != channel.PixelType || !string.Equals(rule.Suffix, suffix, StringComparison.Ordinal))
                    continue;

                state.Scheme = rule.Scheme;
                csc = rule.Csc;
                if (!_rules.Contains(r))
                {
                    _rules.Add(r);
                }
            }

            var group = groups.FindIndex(item => string.Equals(item.Prefix, prefix, StringComparison.Ordinal));
            if (group < 0)
            {
                groups.Add((prefix, [-1, -1, -1]));
                group = groups.Count - 1;
            }

            if (csc >= 0)
            {
                groups[group].Slots[csc] = c;
            }

            _channels.Add(state);
        }

        _rules.Sort();

        // A group of three channels of the same sampling is a colour set
        var set = 0;
        foreach (var (_, slots) in groups)
        {
            if (slots[0] < 0 || slots[1] < 0 || slots[2] < 0)
                continue;

            var red = channels[slots[0]];
            if (channels[slots[1]].XSampling != red.XSampling || channels[slots[2]].XSampling != red.XSampling
                || channels[slots[1]].YSampling != red.YSampling || channels[slots[2]].YSampling != red.YSampling)
                continue;

            for (var component = 0; component < 3; component++)
            {
                _channels[slots[component]].Set = set;
                _channels[slots[component]].Component = component;
            }

            set++;
        }
    }

    /// <summary>Writes the rule block (section 8.3): the rules of the default table that match a channel, in table order.</summary>
    private int WriteRules(Span<byte> destination)
    {
        var length = 2;
        foreach (var index in _rules)
        {
            var (suffix, scheme, csc, pixelType) = DefaultRules[index];
            var size = suffix.Length + 3;
            if (destination.Length - length < size)
                return -1;

            length += Encoding.ASCII.GetBytes(suffix, destination[length..]);
            destination[length++] = 0;
            destination[length++] = (byte)(((csc + 1) << 4) | (scheme << 2));
            destination[length++] = (byte)pixelType;
        }

        if (destination.Length < 2)
            return -1;

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)length);
        return length;
    }

    /// <summary>Gets the lossy units in their coding order: the colour sets, then the other lossy channels (section 8.5).</summary>
    private List<ChannelState[]> GetUnits()
    {
        var units = new List<ChannelState[]>();
        for (var set = 0; ; set++)
        {
            var members = _channels.Where(state => state.Set == set).OrderBy(state => state.Component).ToArray();
            if (members.Length == 0)
                break;

            units.Add(members);
        }

        foreach (var state in _channels)
        {
            if (state.Scheme == 1 && state.Set < 0)
            {
                units.Add([state]);
            }
        }

        return units;
    }

    /// <summary>Codes the blocks of a unit: its DC values, component by component, and its AC symbols, block by block.</summary>
    private void EncodeUnit(ChannelState[] unit, ReadOnlySpan<byte> planes, Span<ushort> ac, ref int acCount, Span<ushort> dc, ref int dcCount)
    {
        var width = unit[0].Width;
        var height = unit[0].Height;
        if (width == 0 || height == 0)
            return;

        var blocksX = (width + 7) / 8;
        var blocksY = (height + 7) / 8;
        var blockCount = blocksX * blocksY;
        var components = unit.Length;
        var toNonlinear = ExrDwa.GetToNonlinearTable();
        var zigzag = ExrDwa.ZigzagOfNatural;
        for (var block = 0; block < blockCount; block++)
        {
            var bx = block % blocksX;
            var by = block / blocksX;

            // Steps 3 and 4: the samples with mirrored edges, as half values, through the perceptual curve
            for (var component = 0; component < components; component++)
            {
                var state = unit[component];
                var curve = components == 3 || !state.Channel.PLinear;
                var plane = planes.Slice(state.PlaneOffset, state.PlaneLength);
                for (var y = 0; y < 8; y++)
                {
                    var row = Mirror((8 * by) + y, height);
                    for (var x = 0; x < 8; x++)
                    {
                        var column = Mirror((8 * bx) + x, width);
                        var half = ReadHalf(plane, (row * width) + column, state.Channel.PixelType);
                        if (curve)
                        {
                            half = toNonlinear[half];
                        }

                        _values[(component * 64) + (8 * y) + x] = (float)BitConverter.UInt16BitsToHalf(half);
                    }
                }
            }

            // Step 5: the forward colour transform
            if (components == 3)
            {
                for (var n = 0; n < 64; n++)
                {
                    var red = _values[n];
                    var green = _values[64 + n];
                    var blue = _values[128 + n];
                    _values[n] = (RedToLuma * red) + (GreenToLuma * green) + (BlueToLuma * blue);
                    _values[64 + n] = (RedToCb * red) - (GreenToCb * green) + (0.5f * blue);
                    _values[128 + n] = (0.5f * red) - (GreenToCr * green) - (BlueToCr * blue);
                }
            }

            // Steps 6 to 8: the forward DCT, the quantization, the DC value and the AC symbols
            for (var component = 0; component < components; component++)
            {
                var values = _block.AsSpan(component * 64, 64);
                for (var n = 0; n < 64; n++)
                {
                    values[n] = _values[(component * 64) + n];
                }

                ForwardDct(values);
                var tolerances = component == 0 ? _lumaTolerances : _chromaTolerances;
                var coefficients = _coefficients.AsSpan(component * 64, 64);
                for (var n = 0; n < 64; n++)
                {
                    coefficients[zigzag[n]] = Quantize((float)values[n], tolerances[n]);
                }

                dc[dcCount + (component * blockCount) + block] = coefficients[0];
                WriteAcSymbols(coefficients, ac, ref acCount);
            }
        }

        dcCount += components * blockCount;
    }

    /// <summary>Writes the run-length symbols of the AC coefficients of a block (section 8.8).</summary>
    internal static void WriteAcSymbols(ReadOnlySpan<ushort> coefficients, Span<ushort> ac, ref int count)
    {
        var z = 1;
        while (z < 64)
        {
            var value = coefficients[z];
            if (value != 0)
            {
                // A negative value that is not a number would read as a run symbol
                ac[count++] = (value & 0xFF00) == EndOfBlock ? (ushort)0x7E00 : value;
                z++;
                continue;
            }

            var end = z + 1;
            while (end < 64 && coefficients[end] == 0)
            {
                end++;
            }

            var run = end - z;
            if (end == 64)
            {
                ac[count++] = EndOfBlock;
            }
            else if (run == 1)
            {
                ac[count++] = 0;
            }
            else
            {
                ac[count++] = (ushort)(EndOfBlock | run);
            }

            z = end;
        }
    }

    /// <summary>Mirrors a coordinate past the end of a plane (section 8.14, step 3).</summary>
    private static int Mirror(int value, int size)
    {
        if (value < size)
            return value;

        var mirrored = (2 * size) - 1 - value;
        return mirrored < 0 ? size - 1 : mirrored;
    }

    /// <summary>Reads a sample as a half value: a float sample is clamped to the range of halves (section 8.14, step 2).</summary>
    private static ushort ReadHalf(ReadOnlySpan<byte> plane, int index, ExrPixelType pixelType)
    {
        if (pixelType == ExrPixelType.Half)
            return BinaryPrimitives.ReadUInt16LittleEndian(plane[(2 * index)..]);

        var value = BinaryPrimitives.ReadSingleLittleEndian(plane[(4 * index)..]);
        if (!float.IsNaN(value))
        {
            value = Math.Clamp(value, -65504f, 65504f);
        }

        return BitConverter.HalfToUInt16Bits((Half)value);
    }

    /// <summary>Compresses a section with zlib into the destination.</summary>
    /// <returns>The length of the section, or -1 when it does not fit.</returns>
    private int Deflate(ReadOnlySpan<byte> source, Span<byte> destination, ref int position)
    {
        var compressed = _compressed!;
        var length = ExrZlib.Deflate(source, compressed);
        if (length < 0 || length > destination.Length - position)
            return -1;

        compressed.Span[..length].CopyTo(destination[position..]);
        position += length;
        return length;
    }

    private Span<byte> Rent(ref PooledBuffer? buffer, int length)
    {
        if (buffer is null || buffer.Length < length)
        {
            buffer?.Dispose();
            buffer = null;
            buffer = _scope.Rent(Math.Max(length, 1), AllocationKind.Temporary, clear: false);
        }

        return buffer.Span[..length];
    }

    /// <summary>A channel of the chunk: its scheme, its colour set, and its plane.</summary>
    private sealed class ChannelState(ExrChannel channel, int width, int height, int planeOffset)
    {
        public ExrChannel Channel { get; } = channel;

        /// <summary>Gets the number of samples of a line of the channel in the chunk.</summary>
        public int Width { get; } = width;

        /// <summary>Gets the number of sample lines of the channel in the chunk.</summary>
        public int Height { get; } = height;

        public int PlaneOffset { get; } = planeOffset;

        public int PlaneLength => Width * Height * Channel.BytesPerSample;

        /// <summary>Gets or sets the scheme: 0 for zlib, 1 for lossy, 2 for run-length coding.</summary>
        public int Scheme { get; set; }

        /// <summary>Gets or sets the index of the colour set of the channel, or -1.</summary>
        public int Set { get; set; } = -1;

        /// <summary>Gets or sets the component of the channel in its colour set: 0 for red, 1 for green, 2 for blue.</summary>
        public int Component { get; set; }
    }
}
