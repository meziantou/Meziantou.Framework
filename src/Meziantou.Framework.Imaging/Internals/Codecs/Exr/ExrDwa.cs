using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoder of the DWAA and DWAB compressions (compression notes, section 8): the channels are classified by rules
/// (lossy DCT, run-length, or zlib), the lossy channels are coded as 8×8 blocks of half DCT coefficients, a red, green and
/// blue channel of one layer through a luma-chroma transform, and every value goes through a perceptual curve.
/// </summary>
/// <remarks>
/// The arithmetic is that of the scalar path of OpenEXR, in single precision, each operation rounded in the order of the
/// description (no fused multiply-add), so that the decoded samples are bit-exact.
/// </remarks>
internal sealed class ExrDwa : IDisposable
{
    private const int HeaderLength = 88;
    private const ushort EndOfBlock = 0xFF00;

    // Section 8.10: the constants of the inverse DCT, as their bits
    private static readonly float A = BitConverter.UInt32BitsToSingle(0x3EB504FB);
    private static readonly float B = BitConverter.UInt32BitsToSingle(0x3EFB14BF);
    private static readonly float C = BitConverter.UInt32BitsToSingle(0x3EEC8361);
    private static readonly float D = BitConverter.UInt32BitsToSingle(0x3ED4DB36);
    private static readonly float E = BitConverter.UInt32BitsToSingle(0x3E8E39E5);
    private static readonly float F = BitConverter.UInt32BitsToSingle(0x3E43EF33);
    private static readonly float G = BitConverter.UInt32BitsToSingle(0x3DC7C60B);
    private static readonly float K = BitConverter.UInt32BitsToSingle(0x3EB504FA);

    // Section 8.12: the inverse colour transform
    private static readonly float CrToRed = BitConverter.UInt32BitsToSingle(0x3FC98FC5);
    private static readonly float CbToGreen = BitConverter.UInt32BitsToSingle(0x3E3FCB92);
    private static readonly float CrToGreen = BitConverter.UInt32BitsToSingle(0x3EEFB7E9);
    private static readonly float CbToBlue = BitConverter.UInt32BitsToSingle(0x3FED844D);

    private static ushort[]? s_toLinear;

    private readonly AllocationScope _scope;
    private readonly List<ChannelState> _channels = [];
    private readonly List<Rule> _rules = [];
    private readonly float[] _block = new float[3 * 64];
    private readonly ushort[] _coefficients = new ushort[3 * 64];
    private ExrHuffman? _huffman;
    private PooledBuffer? _unknown;
    private PooledBuffer? _ac;
    private PooledBuffer? _dc;
    private PooledBuffer? _rle;
    private PooledBuffer? _rlePacked;
    private PooledBuffer? _planes;

    public ExrDwa(AllocationScope scope)
    {
        _scope = scope;
    }

    private enum Scheme
    {
        Unknown = 0,
        LossyDct = 1,
        Rle = 2,
    }

    /// <summary>
    /// Gets the table that maps a half value from the perceptual domain of the coded values to linear light (section 8.13,
    /// toLinear), computed in single precision.
    /// </summary>
    internal static ushort[] GetToLinearTable()
    {
        var table = Volatile.Read(ref s_toLinear);
        if (table is not null)
            return table;

        table = new ushort[65536];
        var exponent = (double)2.2f;
        var @base = (double)BitConverter.UInt32BitsToSingle(0x41106674);
        for (var x = 0; x < table.Length; x++)
        {
            if (x is 0 or 0x8000 || (x & 0x7C00) == 0x7C00)
                continue;

            var value = (float)BitConverter.UInt16BitsToHalf((ushort)x);
            var sign = value < 0 ? -1f : 1f;
            var magnitude = Math.Abs(value);
            var z = magnitude <= 1f ? (float)Math.Pow(magnitude, exponent) : (float)Math.Pow(@base, magnitude - 1f);
            table[x] = BitConverter.HalfToUInt16Bits((Half)(sign * z));
        }

        Interlocked.CompareExchange(ref s_toLinear, table, null);
        return s_toLinear;
    }

    /// <summary>Decompresses a chunk that is not stored raw (section 8.16).</summary>
    /// <param name="stored">The stored data of the chunk.</param>
    /// <param name="destination">Receives the uncompressed data (section 1).</param>
    /// <param name="layout">The geometry of the chunk.</param>
    /// <exception cref="InvalidImageContentException">The data are malformed.</exception>
    public void Decompress(ReadOnlySpan<byte> stored, Span<byte> destination, in ExrChunkLayout layout)
    {
        if (stored.Length < HeaderLength)
            throw Invalid("the chunk is shorter than its header.");

        Span<long> header = stackalloc long[11];
        for (var i = 0; i < header.Length; i++)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(stored[(8 * i)..]);
            if (value > int.MaxValue)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the field {i} of the header is {value}."));

            header[i] = (long)value;
        }

        var version = header[0];
        var unknownLength = (int)header[1];
        var unknownStored = (int)header[2];
        var acStored = (int)header[3];
        var dcStored = (int)header[4];
        var rleStored = (int)header[5];
        var rlePackedLength = (int)header[6];
        var rleLength = (int)header[7];
        var acCount = (int)header[8];
        var dcCount = (int)header[9];
        var acCompression = header[10];
        if (version > 2)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the version {version} is not defined."));

        if (acStored > 0 && acCompression is not (0 or 1))
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the AC compression {acCompression} is not defined."));

        var position = HeaderLength;
        if (version == 2)
        {
            position += ReadRules(stored[HeaderLength..]);
        }
        else
        {
            SetLegacyRules();
        }

        if ((long)position + unknownStored + acStored + dcStored + rleStored > stored.Length)
            throw Invalid("the sections extend past the end of the chunk.");

        Classify(layout);

        // Section 8.2: the sizes of the sections and those of the channels they hold
        long needUnknown = 0;
        long needRle = 0;
        foreach (var channel in _channels)
        {
            if (channel.Scheme == Scheme.Unknown)
            {
                needUnknown += channel.PlaneLength;
            }
            else if (channel.Scheme == Scheme.Rle)
            {
                needRle += channel.PlaneLength;
            }
        }

        if ((long)unknownLength + rleLength > destination.Length)
            throw Invalid("the sizes of the sections are larger than the chunk.");

        CheckSection(needUnknown, unknownLength, unknownStored, "UNKNOWN");
        CheckSection(needRle, rleLength, rleStored, "RLE");
        if (needRle == 0 && rlePackedLength != 0)
            throw Invalid("the RLE section has a size, but no channel is run-length coded.");

        if (dcCount > 0 && dcStored == 0)
            throw Invalid("the DC section is empty.");

        if (acCount > 0 && acStored == 0)
            throw Invalid("the AC section is empty.");

        // Section 8.6: the planes of the channels coded with zlib
        var unknownSection = stored.Slice(position, unknownStored);
        position += unknownStored;
        var unknown = Rent(ref _unknown, unknownLength);
        if (unknownLength > 0)
        {
            using var copy = Copy(unknownSection);
            ExrZlib.Inflate(copy, unknown);
        }

        // Section 8.8: the AC symbols
        var acSection = stored.Slice(position, acStored);
        position += acStored;
        var ac = unsafe(MemoryMarshal.Cast<byte, ushort>(Rent(ref _ac, 2 * acCount)));
        if (acCount > 0)
        {
            if (acCompression == 0)
            {
                _huffman ??= new ExrHuffman(_scope, encoder: false);
                _huffman.Decode(acSection, ac);
            }
            else
            {
                using var copy = Copy(acSection);
                ExrZlib.Inflate(copy, unsafe(MemoryMarshal.AsBytes(ac)));
                ToLittleEndian(ac);
            }
        }

        // Section 8.9: the DC values, with the byte predictor
        var dcSection = stored.Slice(position, dcStored);
        position += dcStored;
        var dcBytes = Rent(ref _dc, 4 * dcCount);
        var dc = unsafe(MemoryMarshal.Cast<byte, ushort>(dcBytes[..(2 * dcCount)]));
        if (dcCount > 0)
        {
            var predicted = dcBytes.Slice(2 * dcCount, 2 * dcCount);
            using var copy = Copy(dcSection);
            ExrZlib.Inflate(copy, predicted);
            ExrPredictor.Decode(predicted, unsafe(MemoryMarshal.AsBytes(dc)));
            ToLittleEndian(dc);
        }

        // Section 8.7: the byte planes of the run-length coded channels
        var rleSection = stored.Slice(position, rleStored);
        var rle = Rent(ref _rle, rleLength);
        if (rleLength > 0)
        {
            var packed = Rent(ref _rlePacked, rlePackedLength);
            using var copy = Copy(rleSection);
            ExrZlib.Inflate(copy, packed);
            ExrRle.Decode(packed, rle);
        }

        // Sections 8.5 and 8.10 to 8.13: the lossy channels, decoded to planes of half values
        var planes = unsafe(MemoryMarshal.Cast<byte, ushort>(Rent(ref _planes, PreparePlanes())));
        var acPosition = 0;
        var dcPosition = 0;
        var toLinear = GetToLinearTable();
        foreach (var unit in GetUnits())
        {
            DecodeUnit(unit, ac, ref acPosition, dc, ref dcPosition, planes, toLinear);
        }

        if (acPosition != acCount || dcPosition != dcCount)
            throw Invalid("the chunk has AC symbols or DC values that no block uses.");

        Assemble(layout, destination, unknown, rle, planes);
    }

    public void Dispose()
    {
        _huffman?.Dispose();
        _unknown?.Dispose();
        _ac?.Dispose();
        _dc?.Dispose();
        _rle?.Dispose();
        _rlePacked?.Dispose();
        _planes?.Dispose();
    }

    private static InvalidImageContentException Invalid(string message) => ExrFormat.Invalid("the DWA data of a chunk are malformed: " + message);

    private static void CheckSection(long need, int length, int stored, string name)
    {
        if (need > 0 ? length < need || stored == 0 : length != 0 || stored != 0)
            throw Invalid($"the size of the {name} section does not match its channels.");
    }

    private static void ToLittleEndian(Span<ushort> values)
    {
        if (!BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(values, values);
        }
    }

    /// <summary>Inverse DCT of one block in place (section 8.10, step 2b): the rows, then the columns.</summary>
    private static void InverseDct(Span<float> block)
    {
        for (var row = 0; row < 8; row++)
        {
            InverseDct8(block.Slice(8 * row, 8), 1);
        }

        for (var column = 0; column < 8; column++)
        {
            InverseDct8(block[column..], 8);
        }
    }

    private static void InverseDct8(Span<float> data, int stride)
    {
        var y0 = data[0];
        var y1 = data[stride];
        var y2 = data[2 * stride];
        var y3 = data[3 * stride];
        var y4 = data[4 * stride];
        var y5 = data[5 * stride];
        var y6 = data[6 * stride];
        var y7 = data[7 * stride];

        var alpha0 = C * y2;
        var alpha1 = F * y2;
        var alpha2 = C * y6;
        var alpha3 = F * y6;

        var beta0 = Add(Add(Add(Mul(B, y1), Mul(D, y3)), Mul(E, y5)), Mul(G, y7));
        var beta1 = Sub(Sub(Sub(Mul(D, y1), Mul(G, y3)), Mul(B, y5)), Mul(E, y7));
        var beta2 = Add(Add(Sub(Mul(E, y1), Mul(B, y3)), Mul(G, y5)), Mul(D, y7));
        var beta3 = Sub(Add(Sub(Mul(G, y1), Mul(E, y3)), Mul(D, y5)), Mul(B, y7));

        var theta0 = Mul(A, Add(y0, y4));
        var theta3 = Mul(A, Sub(y0, y4));
        var theta1 = Add(alpha0, alpha3);
        var theta2 = Sub(alpha1, alpha2);

        var gamma0 = Add(theta0, theta1);
        var gamma1 = Add(theta3, theta2);
        var gamma2 = Sub(theta3, theta2);
        var gamma3 = Sub(theta0, theta1);

        data[0] = Add(gamma0, beta0);
        data[stride] = Add(gamma1, beta1);
        data[2 * stride] = Add(gamma2, beta2);
        data[3 * stride] = Add(gamma3, beta3);
        data[4 * stride] = Sub(gamma3, beta3);
        data[5 * stride] = Sub(gamma2, beta2);
        data[6 * stride] = Sub(gamma1, beta1);
        data[7 * stride] = Sub(gamma0, beta0);
    }

    // Each operation is rounded to single precision on its own: the JIT never contracts them into fused operations
    private static float Mul(float left, float right) => left * right;

    private static float Add(float left, float right) => left + right;

    private static float Sub(float left, float right) => left - right;

    private static ushort ToHalf(float value) => BitConverter.HalfToUInt16Bits((Half)value);

    private static bool SuffixEquals(string suffix, string rule, bool ignoreCase)
    {
        if (!ignoreCase)
            return string.Equals(suffix, rule, StringComparison.Ordinal);

        if (suffix.Length != rule.Length)
            return false;

        for (var i = 0; i < suffix.Length; i++)
        {
            var left = suffix[i];
            var right = rule[i];
            if (left is >= 'A' and <= 'Z')
            {
                left = (char)(left + 32);
            }

            if (right is >= 'A' and <= 'Z')
            {
                right = (char)(right + 32);
            }

            if (left != right)
                return false;
        }

        return true;
    }

    /// <summary>Reads the rule block of version 2 (section 8.3).</summary>
    /// <returns>The size of the block.</returns>
    private int ReadRules(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            throw Invalid("the rule block is truncated.");

        var size = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (size < 2 || size > data.Length)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the rule block has the size {size}."));

        _rules.Clear();
        var rules = data[2..size];
        while (!rules.IsEmpty)
        {
            var end = rules[..Math.Min(rules.Length, 129)].IndexOf((byte)0);
            if (end < 0 || rules.Length - end - 1 < 2)
                throw Invalid("a rule is truncated.");

            var suffix = Encoding.Latin1.GetString(rules[..end]);
            var packed = rules[end + 1];
            var type = rules[end + 2];
            var csc = (packed >> 4) - 1;
            var scheme = (packed >> 2) & 3;
            if (csc > 2 || scheme > 2 || type > 2)
                throw Invalid("a rule is not valid.");

            _rules.Add(new Rule(suffix, (Scheme)scheme, csc, (ExrPixelType)type, (packed & 1) != 0));
            rules = rules[(end + 3)..];
        }

        return size;
    }

    /// <summary>Sets the rules of versions 0 and 1 (section 8.3).</summary>
    private void SetLegacyRules()
    {
        _rules.Clear();
        foreach (var (suffix, csc) in new[] { ("r", 0), ("red", 0), ("g", 1), ("grn", 1), ("green", 1), ("b", 2), ("blu", 2), ("blue", 2), ("y", -1), ("by", -1), ("ry", -1) })
        {
            _rules.Add(new Rule(suffix, Scheme.LossyDct, csc, ExrPixelType.Half, IgnoreCase: true));
            _rules.Add(new Rule(suffix, Scheme.LossyDct, csc, ExrPixelType.Float, IgnoreCase: true));
        }

        foreach (var type in (ExrPixelType[])[ExrPixelType.UInt, ExrPixelType.Half, ExrPixelType.Float])
        {
            _rules.Add(new Rule("a", Scheme.Rle, -1, type, IgnoreCase: true));
        }
    }

    /// <summary>Classifies the channels and forms the colour sets (section 8.4).</summary>
    private void Classify(in ExrChunkLayout layout)
    {
        _channels.Clear();
        var channels = layout.Channels;
        var groups = new List<(string Prefix, int[] Slots)>();
        for (var c = 0; c < channels.Count; c++)
        {
            var channel = channels[c];
            var dot = channel.Name.LastIndexOf('.', StringComparison.Ordinal);
            var suffix = dot < 0 ? channel.Name : channel.Name[(dot + 1)..];
            var prefix = dot < 0 ? string.Empty : channel.Name[..(dot + 1)];
            var state = new ChannelState(c, channel, layout.GetSampleCount(c), layout.GetLineCount(c));
            var csc = -1;
            foreach (var rule in _rules)
            {
                if (rule.PixelType != channel.PixelType || !SuffixEquals(suffix, rule.Suffix, rule.IgnoreCase))
                    continue;

                state.Scheme = rule.Scheme;
                if (rule.Csc >= 0)
                {
                    csc = rule.Csc;
                }
            }

            if (state.Scheme == Scheme.LossyDct && channel.PixelType == ExrPixelType.UInt)
                throw Invalid($"the UINT channel '{channel.Name}' is classified as lossy.");

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
                var state = _channels[slots[component]];
                if (state.Scheme != Scheme.LossyDct)
                    throw Invalid($"the channel '{state.Channel.Name}' of a colour set is not lossy.");

                state.Set = set;
                state.Component = component;
            }

            set++;
        }
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
            if (state.Scheme == Scheme.LossyDct && state.Set < 0)
            {
                units.Add([state]);
            }
        }

        return units;
    }

    /// <summary>Gives each lossy channel its plane of half values.</summary>
    /// <returns>The size of the planes, in bytes.</returns>
    private int PreparePlanes()
    {
        long offset = 0;
        foreach (var state in _channels)
        {
            if (state.Scheme != Scheme.LossyDct)
                continue;

            state.PlaneOffset = (int)offset;
            offset += (long)state.Width * state.Height;
        }

        return checked((int)(2 * offset));
    }

    /// <summary>Decodes the blocks of a unit into the planes of its channels (sections 8.5 and 8.10 to 8.13).</summary>
    private void DecodeUnit(ChannelState[] unit, ReadOnlySpan<ushort> ac, ref int acPosition, ReadOnlySpan<ushort> dc, ref int dcPosition, Span<ushort> planes, ushort[] toLinear)
    {
        var width = unit[0].Width;
        var height = unit[0].Height;
        if (width == 0 || height == 0)
            return;

        var blocksX = (width + 7) / 8;
        var blocksY = (height + 7) / 8;
        var blockCount = blocksX * blocksY;
        var components = unit.Length;
        if ((long)components * blockCount > dc.Length - dcPosition)
            throw Invalid("the chunk has fewer DC values than blocks.");

        var zigzag = ZigzagOfNatural;
        Span<int> lastNonZero = stackalloc int[3];
        for (var block = 0; block < blockCount; block++)
        {
            for (var component = 0; component < components; component++)
            {
                // Section 8.8: the AC run-length symbols of the block
                var coefficients = _coefficients.AsSpan(component * 64, 64);
                coefficients.Clear();
                coefficients[0] = dc[dcPosition + (component * blockCount) + block];
                var last = 0;
                var z = 1;
                while (z < 64)
                {
                    if (acPosition >= ac.Length)
                        throw Invalid("the chunk has fewer AC symbols than its blocks need.");

                    var symbol = ac[acPosition++];
                    if ((symbol & 0xFF00) == EndOfBlock)
                    {
                        var run = symbol & 0xFF;
                        z = run == 0 ? 64 : z + run;
                        continue;
                    }

                    coefficients[z] = symbol;
                    last = z;
                    z++;
                }

                lastNonZero[component] = last;

                // Section 8.10, steps 1 and 2
                var values = _block.AsSpan(component * 64, 64);
                if (last == 0)
                {
                    var value = Mul(Mul((float)BitConverter.UInt16BitsToHalf(coefficients[0]), K), K);
                    values.Fill(value);
                }
                else
                {
                    for (var n = 0; n < 64; n++)
                    {
                        values[n] = (float)BitConverter.UInt16BitsToHalf(coefficients[zigzag[n]]);
                    }

                    InverseDct(values);
                }
            }

            // Steps 3 to 6: the colour transform, half values, the perceptual curve, the pixels of the plane
            var bx = block % blocksX;
            var by = block / blocksX;
            var columns = Math.Min(8, width - (8 * bx));
            var rows = Math.Min(8, height - (8 * by));
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    var n = (8 * y) + x;
                    var target = (((8 * by) + y) * width) + (8 * bx) + x;
                    if (components == 3)
                    {
                        var luma = _block[n];
                        var cb = _block[64 + n];
                        var cr = _block[128 + n];
                        var red = Add(luma, Mul(CrToRed, cr));
                        var green = Sub(Sub(luma, Mul(CbToGreen, cb)), Mul(CrToGreen, cr));
                        var blue = Add(luma, Mul(CbToBlue, cb));
                        planes[unit[0].PlaneOffset + target] = toLinear[ToHalf(red)];
                        planes[unit[1].PlaneOffset + target] = toLinear[ToHalf(green)];
                        planes[unit[2].PlaneOffset + target] = toLinear[ToHalf(blue)];
                    }
                    else
                    {
                        var half = ToHalf(_block[n]);
                        planes[unit[0].PlaneOffset + target] = unit[0].Channel.PLinear ? half : toLinear[half];
                    }
                }
            }
        }

        dcPosition += components * blockCount;
    }

    /// <summary>Writes the samples of every channel to the uncompressed data of the chunk (section 8.16, step 5).</summary>
    private void Assemble(in ExrChunkLayout layout, Span<byte> destination, ReadOnlySpan<byte> unknown, ReadOnlySpan<byte> rle, ReadOnlySpan<ushort> planes)
    {
        destination.Clear();

        // The offsets of the channels in the UNKNOWN and the RLE buffers
        var unknownOffset = 0;
        var rleOffset = 0;
        foreach (var state in _channels)
        {
            if (state.Scheme == Scheme.Unknown)
            {
                state.PlaneOffset = unknownOffset;
                unknownOffset += state.PlaneLength;
            }
            else if (state.Scheme == Scheme.Rle)
            {
                state.PlaneOffset = rleOffset;
                rleOffset += state.PlaneLength;
            }

            state.Row = 0;
        }

        var position = 0;
        for (var line = 0; line < layout.Lines; line++)
        {
            foreach (var state in _channels)
            {
                if (!layout.HasLine(state.Index, line))
                    continue;

                var width = state.Width;
                var size = state.Channel.BytesPerSample;
                var row = destination.Slice(position, width * size);
                switch (state.Scheme)
                {
                    case Scheme.Unknown:
                        unknown.Slice(state.PlaneOffset + (state.Row * width * size), width * size).CopyTo(row);
                        break;

                    case Scheme.Rle:
                    {
                        var count = width * state.Height;
                        for (var x = 0; x < width; x++)
                        {
                            for (var k = 0; k < size; k++)
                            {
                                row[(x * size) + k] = rle[state.PlaneOffset + (k * count) + (state.Row * width) + x];
                            }
                        }

                        break;
                    }

                    default:
                    {
                        var samples = planes.Slice(state.PlaneOffset + (state.Row * width), width);
                        for (var x = 0; x < width; x++)
                        {
                            if (size == 2)
                            {
                                BinaryPrimitives.WriteUInt16LittleEndian(row[(2 * x)..], samples[x]);
                            }
                            else
                            {
                                BinaryPrimitives.WriteSingleLittleEndian(row[(4 * x)..], (float)BitConverter.UInt16BitsToHalf(samples[x]));
                            }
                        }

                        break;
                    }
                }

                state.Row++;
                position += width * size;
            }
        }
    }

    /// <summary>Gets the zig-zag position of each natural position of a block (section 8.8).</summary>
    private static ReadOnlySpan<byte> ZigzagOfNatural =>
    [
        0, 1, 5, 6, 14, 15, 27, 28,
        2, 4, 7, 13, 16, 26, 29, 42,
        3, 8, 12, 17, 25, 30, 41, 43,
        9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63,
    ];

    /// <summary>Copies a section to a buffer of the scope, which the zlib stream reads.</summary>
    private PooledBuffer Copy(ReadOnlySpan<byte> section)
    {
        var copy = _scope.Rent(section.Length, AllocationKind.DecoderState, clear: false);
        section.CopyTo(copy.Span);
        return copy;
    }

    private Span<byte> Rent(ref PooledBuffer? buffer, int length)
    {
        if (buffer is null || buffer.Length < length)
        {
            buffer?.Dispose();
            buffer = null;
            buffer = _scope.Rent(Math.Max(length, 1), AllocationKind.DecoderState, clear: false);
        }

        return buffer.Span[..length];
    }

    /// <summary>A classification rule (section 8.3).</summary>
    private sealed record Rule(string Suffix, Scheme Scheme, int Csc, ExrPixelType PixelType, bool IgnoreCase);

    /// <summary>A channel of the chunk: its scheme, its colour set, and its plane.</summary>
    private sealed class ChannelState(int index, ExrChannel channel, int width, int height)
    {
        public int Index { get; } = index;

        public ExrChannel Channel { get; } = channel;

        /// <summary>Gets the number of samples of a line of the channel in the chunk.</summary>
        public int Width { get; } = width;

        /// <summary>Gets the number of sample lines of the channel in the chunk.</summary>
        public int Height { get; } = height;

        public int PlaneLength => Width * Height * Channel.BytesPerSample;

        public Scheme Scheme { get; set; }

        /// <summary>Gets or sets the index of the colour set of the channel, or -1.</summary>
        public int Set { get; set; } = -1;

        /// <summary>Gets or sets the component of the channel in its colour set: 0 for red, 1 for green, 2 for blue.</summary>
        public int Component { get; set; }

        public int PlaneOffset { get; set; }

        public int Row { get; set; }
    }
}
