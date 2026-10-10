using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The WebP lossless (VP8L) decoder, written from the WebP Lossless Bitstream specification: image header, transforms
/// (predictor, color, subtract green, color indexing with pixel bundling), color cache, meta prefix codes (entropy image),
/// canonical prefix codes, LZ77 backward references with the 120-entry distance map. It decodes a fully buffered payload
/// into ARGB pixels (<c>0xAARRGGBB</c> per <see cref="uint"/>), rented from the operation scope.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Every defect is <see cref="InvalidImageContentException"/>: a wrong signature or version, a repeated transform, a color
/// cache size outside 1-11 bits, invalid or incomplete prefix codes, code-length repeats past the alphabet, a maximum symbol
/// count larger than the alphabet, a backward reference before the first pixel or past the last one, a color cache code
/// without cache or outside it, and a bitstream that ends before the last pixel. Bytes after the last pixel are ignored.
/// </description></item>
/// <item><description>
/// Undefined values are decoded like the reference implementation: predictor modes 14 and 15 (the green value's low four
/// bits) predict <c>0xff000000</c> (mode 0), and color indexes outside the color table produce transparent black
/// (specification).
/// </description></item>
/// <item><description>
/// Memory (all <see cref="AllocationKind.DecoderState"/>): the ARGB image, the transform and entropy subresolution images,
/// the color cache and the prefix-code tables. The image is decoded at its final width (transforms are applied in place).
/// </description></item>
/// </list>
/// </remarks>
internal sealed class Vp8LDecoder : IDisposable
{
    /// <summary>The length of the VP8L header: signature, 14-bit width and height minus one, alpha hint and 3-bit version.</summary>
    public const int HeaderLength = 5;

    /// <summary>The VP8L signature byte.</summary>
    public const byte Signature = 0x2F;

    private const int NumLiteralCodes = 256;
    private const int NumLengthCodes = 24;
    private const int NumDistanceCodes = 40;
    private const int CodeLengthCodes = 19;
    private const int ColorCacheMultiplier = 0x1E35A7BD;

    private static ReadOnlySpan<byte> CodeLengthCodeOrder => [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    // The distance map of the specification (section 5.2.2): (xi, yi) pairs, dist = xi + yi * width
    private static ReadOnlySpan<sbyte> DistanceMap =>
    [
        0, 1, 1, 0, 1, 1, -1, 1, 0, 2, 2, 0, 1, 2,
        -1, 2, 2, 1, -2, 1, 2, 2, -2, 2, 0, 3, 3, 0,
        1, 3, -1, 3, 3, 1, -3, 1, 2, 3, -2, 3, 3, 2,
        -3, 2, 0, 4, 4, 0, 1, 4, -1, 4, 4, 1, -4, 1,
        3, 3, -3, 3, 2, 4, -2, 4, 4, 2, -4, 2, 0, 5,
        3, 4, -3, 4, 4, 3, -4, 3, 5, 0, 1, 5, -1, 5,
        5, 1, -5, 1, 2, 5, -2, 5, 5, 2, -5, 2, 4, 4,
        -4, 4, 3, 5, -3, 5, 5, 3, -5, 3, 0, 6, 6, 0,
        1, 6, -1, 6, 6, 1, -6, 1, 2, 6, -2, 6, 6, 2,
        -6, 2, 4, 5, -4, 5, 5, 4, -5, 4, 3, 6, -3, 6,
        6, 3, -6, 3, 0, 7, 7, 0, 1, 7, -1, 7, 5, 5,
        -5, 5, 7, 1, -7, 1, 4, 6, -4, 6, 6, 4, -6, 4,
        2, 7, -2, 7, 7, 2, -7, 2, 3, 7, -3, 7, 7, 3,
        -7, 3, 5, 6, -5, 6, 6, 5, -6, 5, 8, 0, 4, 7,
        -4, 7, 7, 4, -7, 4, 8, 1, 8, 2, 6, 6, -6, 6,
        8, 3, 5, 7, -5, 7, 7, 5, -7, 5, 8, 4, 6, 7,
        -6, 7, 7, 6, -7, 6, 8, 5, 7, 7, -7, 7, 8, 6,
        8, 7,
    ];

    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly Vp8LBitReader _reader;
    private readonly Vp8LPrefixCodes _codes;
    private readonly byte[] _codeLengths = new byte[NumLiteralCodes + NumLengthCodes + (1 << 11)];
    private readonly List<PooledBuffer> _temporaries = [];

    private Vp8LDecoder(AllocationScope scope, byte[] data, int offset, int length, CancellationToken cancellationToken)
    {
        _scope = scope;
        _cancellationToken = cancellationToken;
        _reader = new Vp8LBitReader(data, offset, length);
        _codes = new Vp8LPrefixCodes(scope);
    }

    private enum TransformType
    {
        Predictor = 0,
        Color = 1,
        SubtractGreen = 2,
        ColorIndexing = 3,
    }

    /// <summary>Parses the 5-byte VP8L header.</summary>
    /// <exception cref="InvalidImageContentException">The signature or the version is invalid, or the header is truncated.</exception>
    public static Vp8LHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength)
            throw Invalid("The WebP lossless (VP8L) chunk is shorter than its 5-byte header.");

        if (data[0] != Signature)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP lossless (VP8L) signature is 0x{data[0]:X2}, not 0x2F."));

        var bits = unsafe(MemoryMarshal.Read<uint>(data[1..]));
        if (!BitConverter.IsLittleEndian)
        {
            bits = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(bits);
        }

        var width = (int)(bits & 0x3FFF) + 1;
        var height = (int)((bits >> 14) & 0x3FFF) + 1;
        var alphaIsUsed = ((bits >> 28) & 1) != 0;
        var version = (int)(bits >> 29);
        if (version != 0)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP lossless (VP8L) version {version} is not 0."));

        return new Vp8LHeader(width, height, alphaIsUsed);
    }

    /// <summary>Decodes a VP8L chunk payload (header included) into ARGB pixels.</summary>
    /// <param name="scope">The scope charged for the pixels and the decoder state.</param>
    /// <param name="data">The buffer holding the payload.</param>
    /// <param name="offset">The offset of the payload.</param>
    /// <param name="length">The payload length.</param>
    /// <param name="cancellationToken">Checked between rows.</param>
    /// <param name="header">The parsed header.</param>
    /// <returns><c>width * height</c> ARGB pixels (at least that many bytes times four); the caller disposes them.</returns>
    public static PooledBuffer Decode(AllocationScope scope, byte[] data, int offset, int length, CancellationToken cancellationToken, out Vp8LHeader header)
    {
        header = ReadHeader(data.AsSpan(offset, length));
        using var decoder = new Vp8LDecoder(scope, data, offset + HeaderLength, length - HeaderLength, cancellationToken);
        return decoder.DecodeImageStream(header.Width, header.Height);
    }

    /// <summary>Decodes a headerless image stream of known dimensions (an <c>ALPH</c> chunk compressed with the lossless format).</summary>
    /// <returns><c>width * height</c> ARGB pixels; the caller disposes them.</returns>
    public static PooledBuffer DecodeImageStream(AllocationScope scope, byte[] data, int offset, int length, int width, int height, CancellationToken cancellationToken)
    {
        using var decoder = new Vp8LDecoder(scope, data, offset, length, cancellationToken);
        return decoder.DecodeImageStream(width, height);
    }

    public void Dispose()
    {
        foreach (var buffer in _temporaries)
        {
            buffer.Dispose();
        }

        _temporaries.Clear();
        _codes.Dispose();
    }

    internal static uint ColorCacheHash(uint argb, int bits) => (uint)(ColorCacheMultiplier * argb) >> (32 - bits);

    internal static int DivRoundUp(int value, int bits) => (value + (1 << bits) - 1) >> bits;

    private static Span<uint> AsPixels(PooledBuffer buffer, long count) => unsafe(MemoryMarshal.Cast<byte, uint>(buffer.RawBuffer.AsSpan()))[..(int)count];

    private PooledBuffer RentPixels(int width, int height)
    {
        var bytes = (long)width * height * sizeof(uint);
        if (bytes > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        return _scope.Rent((int)bytes, AllocationKind.DecoderState, clear: false);
    }

    private PooledBuffer RentTemporary(int length, bool clear)
    {
        var buffer = _scope.Rent(length, AllocationKind.DecoderState, clear);
        _temporaries.Add(buffer);
        return buffer;
    }

    /// <summary>Decodes the main image stream: transforms, then the spatially coded image, then the inverse transforms.</summary>
    private PooledBuffer DecodeImageStream(int width, int height)
    {
        var transforms = new List<Transform>(4);
        var seen = 0;
        var codedWidth = width;
        while (_reader.ReadBits(1) != 0)
        {
            var type = (TransformType)_reader.ReadBits(2);
            if ((seen & (1 << (int)type)) != 0)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP lossless transform {type} is used more than once."));

            seen |= 1 << (int)type;
            transforms.Add(ReadTransform(type, ref codedWidth, height));
        }

        var pixels = RentPixels(width, height);
        try
        {
            var data = AsPixels(pixels, (long)width * height);
            DecodeSpatiallyCodedImage(data[..(codedWidth * height)], codedWidth, height, isMainImage: true);
            for (var i = transforms.Count - 1; i >= 0; i--)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                transforms[i].ApplyInverse(data, height);
            }

            return pixels;
        }
        catch
        {
            pixels.Dispose();
            throw;
        }
    }

    private Transform ReadTransform(TransformType type, ref int width, int height)
    {
        switch (type)
        {
            case TransformType.Predictor:
            case TransformType.Color:
            {
                var sizeBits = (int)_reader.ReadBits(3) + 2;
                var blockWidth = DivRoundUp(width, sizeBits);
                var blockHeight = DivRoundUp(height, sizeBits);
                var image = DecodeEntropyCodedImage(blockWidth, blockHeight);
                return new Transform(type, width, width, sizeBits, image, blockWidth);
            }

            case TransformType.SubtractGreen:
                return new Transform(type, width, width, 0, null, 0);

            default:
            {
                var tableSize = (int)_reader.ReadBits(8) + 1;
                var widthBits = tableSize <= 2 ? 3 : tableSize <= 4 ? 2 : tableSize <= 16 ? 1 : 0;
                var table = DecodeEntropyCodedImage(tableSize, 1);
                var colors = AsPixels(table, tableSize);

                // The table is subtraction coded: each entry adds to the previous one, per component
                for (var i = 1; i < colors.Length; i++)
                {
                    colors[i] = AddPixels(colors[i], colors[i - 1]);
                }

                var palette = RentTemporary(256 * sizeof(uint), clear: true);
                colors.CopyTo(AsPixels(palette, 256));
                var outputWidth = width;
                width = DivRoundUp(width, widthBits);
                return new Transform(type, width, outputWidth, widthBits, palette, tableSize);
            }
        }
    }

    /// <summary>Decodes a subresolution image or color table: color cache information and one prefix code group, no transform, no meta prefix code.</summary>
    private PooledBuffer DecodeEntropyCodedImage(int width, int height)
    {
        var buffer = RentTemporary((int)Math.Min(CheckedSizes.MaxBufferLength, (long)width * height * sizeof(uint)), clear: false);
        DecodeSpatiallyCodedImage(AsPixels(buffer, (long)width * height), width, height, isMainImage: false);
        return buffer;
    }

    private void DecodeSpatiallyCodedImage(Span<uint> pixels, int width, int height, bool isMainImage)
    {
        var cacheBits = 0;
        if (_reader.ReadBits(1) != 0)
        {
            cacheBits = (int)_reader.ReadBits(4);
            if (cacheBits is < 1 or > 11)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP lossless color cache size of {cacheBits} bits is not between 1 and 11."));
        }

        var prefixBits = 0;
        var entropyWidth = 0;
        Span<uint> entropyImage = default;
        var groupCount = 1;
        if (isMainImage && _reader.ReadBits(1) != 0)
        {
            prefixBits = (int)_reader.ReadBits(3) + 2;
            entropyWidth = DivRoundUp(width, prefixBits);
            var entropyHeight = DivRoundUp(height, prefixBits);
            var entropyBuffer = DecodeEntropyCodedImage(entropyWidth, entropyHeight);
            entropyImage = AsPixels(entropyBuffer, (long)entropyWidth * entropyHeight);
            var maxCode = 0;
            foreach (var value in entropyImage)
            {
                maxCode = Math.Max(maxCode, (int)((value >> 8) & 0xFFFF));
            }

            groupCount = maxCode + 1;
        }

        // The prefix codes of the subresolution images decoded so far are no longer needed
        _codes.Reset();
        var cacheSize = cacheBits == 0 ? 0 : 1 << cacheBits;
        using var groupBuffer = _scope.Rent(groupCount * GroupSize * 2 * sizeof(int), AllocationKind.DecoderState, clear: false);
        var groups = unsafe(MemoryMarshal.Cast<byte, Vp8LPrefixCode>(groupBuffer.RawBuffer.AsSpan()))[..(groupCount * GroupSize)];
        for (var group = 0; group < groupCount; group++)
        {
            ReadGroup(groups.Slice(group * GroupSize, GroupSize), cacheSize);
        }

        using var cacheBuffer = cacheSize == 0 ? null : _scope.Rent(cacheSize * sizeof(uint), AllocationKind.DecoderState, clear: true);
        var cache = cacheBuffer is null ? default : AsPixels(cacheBuffer, cacheSize);
        DecodePixels(pixels, width, groups, entropyImage, entropyWidth, prefixBits, cache, cacheBits);
        _codes.Reset();
    }

    private const int GroupSize = 5;

    private void ReadGroup(Span<Vp8LPrefixCode> group, int cacheSize)
    {
        group[0] = ReadPrefixCode(NumLiteralCodes + NumLengthCodes + cacheSize);
        group[1] = ReadPrefixCode(NumLiteralCodes);
        group[2] = ReadPrefixCode(NumLiteralCodes);
        group[3] = ReadPrefixCode(NumLiteralCodes);
        group[4] = ReadPrefixCode(NumDistanceCodes);
    }

    private Vp8LPrefixCode ReadPrefixCode(int alphabetSize)
    {
        var lengths = _codeLengths.AsSpan(0, alphabetSize);
        lengths.Clear();
        if (_reader.ReadBits(1) != 0)
        {
            // Simple code length code: one or two symbols of length 1
            var symbolCount = (int)_reader.ReadBits(1) + 1;
            var firstIsEightBits = _reader.ReadBits(1) != 0;
            var symbol0 = (int)_reader.ReadBits(firstIsEightBits ? 8 : 1);
            SetSimpleSymbol(lengths, symbol0);
            if (symbolCount == 2)
            {
                SetSimpleSymbol(lengths, (int)_reader.ReadBits(8));
            }

            return _codes.Build(lengths);
        }

        // Normal code length code
        Span<byte> codeLengthCodeLengths = stackalloc byte[CodeLengthCodes];
        codeLengthCodeLengths.Clear();
        var count = (int)_reader.ReadBits(4) + 4;
        for (var i = 0; i < count; i++)
        {
            codeLengthCodeLengths[CodeLengthCodeOrder[i]] = (byte)_reader.ReadBits(3);
        }

        var mark = _codes.Mark;
        var lengthCode = _codes.Build(codeLengthCodeLengths);
        var maxSymbol = alphabetSize;
        if (_reader.ReadBits(1) != 0)
        {
            var lengthBits = 2 + (2 * (int)_reader.ReadBits(3));
            maxSymbol = 2 + (int)_reader.ReadBits(lengthBits);
            if (maxSymbol > alphabetSize)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP lossless code declares {maxSymbol} code lengths for an alphabet of {alphabetSize} symbols."));
        }

        var symbol = 0;
        byte previous = 8;
        while (symbol < alphabetSize)
        {
            if (maxSymbol-- == 0)
                break;

            var code = _codes.ReadSymbol(_reader, lengthCode);
            if (code < 16)
            {
                lengths[symbol++] = (byte)code;
                if (code != 0)
                {
                    previous = (byte)code;
                }

                continue;
            }

            var (extraBits, offset) = code switch
            {
                16 => (2, 3),
                17 => (3, 3),
                _ => (7, 11),
            };
            var repeat = (int)_reader.ReadBits(extraBits) + offset;
            if (symbol + repeat > alphabetSize)
                throw Invalid("A WebP lossless code-length repetition extends past the end of the alphabet.");

            lengths.Slice(symbol, repeat).Fill(code == 16 ? previous : (byte)0);
            symbol += repeat;
        }

        // The code-length code is not needed anymore: its table is overwritten by the next code
        _codes.Release(mark);
        return _codes.Build(lengths);
    }

    private static void SetSimpleSymbol(Span<byte> lengths, int symbol)
    {
        if (symbol >= lengths.Length)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The WebP lossless simple code symbol {symbol} is outside the alphabet of {lengths.Length} symbols."));

        lengths[symbol] = 1;
    }

    private void DecodePixels(Span<uint> pixels, int width, ReadOnlySpan<Vp8LPrefixCode> groups, ReadOnlySpan<uint> entropyImage, int entropyWidth, int prefixBits, Span<uint> cache, int cacheBits)
    {
        var reader = _reader;
        var entries = _codes.GetEntries();
        var total = pixels.Length;
        var position = 0;
        var x = 0;
        var y = 0;
        var blockMask = entropyImage.IsEmpty ? -1 : (1 << prefixBits) - 1;
        var group = GetGroup(groups, entropyImage, entropyWidth, prefixBits, 0, 0);
        var cacheSize = cache.Length;
        while (position < total)
        {
            if ((x & blockMask) == 0)
            {
                group = GetGroup(groups, entropyImage, entropyWidth, prefixBits, x, y);
            }

            var code = Vp8LPrefixCodes.ReadSymbol(reader, entries, group[0]);
            if (code < NumLiteralCodes)
            {
                var red = Vp8LPrefixCodes.ReadSymbol(reader, entries, group[1]);
                var blue = Vp8LPrefixCodes.ReadSymbol(reader, entries, group[2]);
                var alpha = Vp8LPrefixCodes.ReadSymbol(reader, entries, group[3]);
                var argb = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)code << 8) | (uint)blue;
                pixels[position++] = argb;
                if (cacheSize != 0)
                {
                    cache[(int)ColorCacheHash(argb, cacheBits)] = argb;
                }

                if (++x == width)
                {
                    x = 0;
                    y++;
                    _cancellationToken.ThrowIfCancellationRequested();
                }

                continue;
            }

            if (code < NumLiteralCodes + NumLengthCodes)
            {
                var length = ReadPrefixedValue(code - NumLiteralCodes);
                var distanceSymbol = Vp8LPrefixCodes.ReadSymbol(reader, entries, group[4]);
                var distance = ToPlaneDistance(ReadPrefixedValue(distanceSymbol), width);
                if (distance > position)
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"A WebP lossless backward reference at pixel {position} has the distance {distance}, before the first pixel."));

                if (length > total - position)
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"A WebP lossless backward reference at pixel {position} copies {length} pixels, past the last pixel."));

                // Overlapping copies repeat the pattern: copy forward, one pixel at a time when they overlap
                if (distance >= length)
                {
                    pixels.Slice(position - distance, length).CopyTo(pixels.Slice(position, length));
                }
                else
                {
                    for (var i = 0; i < length; i++)
                    {
                        pixels[position + i] = pixels[position + i - distance];
                    }
                }

                if (cacheSize != 0)
                {
                    foreach (var argb in pixels.Slice(position, length))
                    {
                        cache[(int)ColorCacheHash(argb, cacheBits)] = argb;
                    }
                }

                position += length;
                x += length;
                if (x >= width)
                {
                    y += x / width;
                    x %= width;
                    _cancellationToken.ThrowIfCancellationRequested();
                }

                if (position < total)
                {
                    group = GetGroup(groups, entropyImage, entropyWidth, prefixBits, x, y);
                }

                continue;
            }

            var key = code - (NumLiteralCodes + NumLengthCodes);
            if (key >= cacheSize)
                throw Invalid("A WebP lossless color cache code is outside the color cache.");

            pixels[position++] = cache[key];
            if (++x == width)
            {
                x = 0;
                y++;
                _cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static ReadOnlySpan<Vp8LPrefixCode> GetGroup(ReadOnlySpan<Vp8LPrefixCode> groups, ReadOnlySpan<uint> entropyImage, int entropyWidth, int prefixBits, int x, int y)
    {
        if (entropyImage.IsEmpty)
            return groups[..GroupSize];

        var metaCode = (int)((entropyImage[((y >> prefixBits) * entropyWidth) + (x >> prefixBits)] >> 8) & 0xFFFF);
        return groups.Slice(metaCode * GroupSize, GroupSize);
    }

    /// <summary>Reads a length or distance from its prefix symbol and extra bits (specification section 5.2.2).</summary>
    private int ReadPrefixedValue(int prefix)
    {
        if (prefix < 4)
            return prefix + 1;

        var extraBits = (prefix - 2) >> 1;
        var offset = (2 + (prefix & 1)) << extraBits;
        return offset + (int)_reader.ReadBits(extraBits) + 1;
    }

    /// <summary>Gets the neighbor offset (<c>xi</c>, <c>yi</c>) of a short distance code (1 to 120).</summary>
    internal static (int Xi, int Yi) GetDistanceMapEntry(int distanceCode) => (DistanceMap[2 * (distanceCode - 1)], DistanceMap[(2 * (distanceCode - 1)) + 1]);

    private static int ToPlaneDistance(int distanceCode, int width)
    {
        if (distanceCode > 120)
            return distanceCode - 120;

        var xi = DistanceMap[2 * (distanceCode - 1)];
        var yi = DistanceMap[(2 * (distanceCode - 1)) + 1];
        var distance = xi + (yi * width);
        return distance < 1 ? 1 : distance;
    }

    /// <summary>Adds two ARGB pixels component by component, modulo 256.</summary>
    internal static uint AddPixels(uint a, uint b)
    {
        var alphaGreen = (a & 0xFF00FF00u) + (b & 0xFF00FF00u);
        var redBlue = (a & 0x00FF00FFu) + (b & 0x00FF00FFu);
        return (alphaGreen & 0xFF00FF00u) | (redBlue & 0x00FF00FFu);
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.WebP);

    /// <summary>One transform read from the bitstream, applied in reverse order after the image data is decoded.</summary>
    private sealed class Transform(TransformType type, int width, int outputWidth, int bits, PooledBuffer? data, int dataWidth)
    {
        public void ApplyInverse(Span<uint> pixels, int height)
        {
            switch (type)
            {
                case TransformType.Predictor:
                    Vp8LTransforms.InversePredictor(pixels[..(width * height)], width, height, bits, AsPixels(data!, (long)dataWidth * DivRoundUp(height, bits)), dataWidth);
                    break;
                case TransformType.Color:
                    Vp8LTransforms.InverseColorTransform(pixels[..(width * height)], width, height, bits, AsPixels(data!, (long)dataWidth * DivRoundUp(height, bits)), dataWidth);
                    break;
                case TransformType.SubtractGreen:
                    Vp8LTransforms.AddGreen(pixels[..(width * height)]);
                    break;
                default:
                    Vp8LTransforms.InverseColorIndexing(pixels[..(outputWidth * height)], width, outputWidth, height, bits, AsPixels(data!, 256));
                    break;
            }
        }
    }
}
