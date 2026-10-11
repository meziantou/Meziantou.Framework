namespace Meziantou.Framework.Imaging.TestHarness.WebP;

/// <summary>
/// A deliberately simple reference decoder of the WebP lossless bitstream (VP8L), transcribed from the "WebP Lossless
/// Bitstream Specification" (RFC 9649, sections 3-5) for encoder tests. It never uses the library under test and favors
/// directness over speed: bits are read one at a time, prefix codes are canonical codes matched bit by bit, and every
/// transform is applied pixel by pixel in reverse order of appearance. Malformed streams throw <see cref="InvalidDataException"/>.
/// </summary>
public static class ReferenceVp8L
{
    private const int NumLiteralCodes = 256;
    private const int NumLengthCodes = 24;
    private const int NumDistanceCodes = 40;

    private static readonly int[] CodeLengthCodeOrder = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    // RFC 9649 section 4.2.2: the (xi, yi) offsets of distance codes 1 to 120
    private static readonly (int X, int Y)[] DistanceMap =
    [
        (0, 1), (1, 0), (1, 1), (-1, 1), (0, 2), (2, 0), (1, 2), (-1, 2), (2, 1), (-2, 1), (2, 2), (-2, 2), (0, 3), (3, 0), (1, 3), (-1, 3),
        (3, 1), (-3, 1), (2, 3), (-2, 3), (3, 2), (-3, 2), (0, 4), (4, 0), (1, 4), (-1, 4), (4, 1), (-4, 1), (3, 3), (-3, 3), (2, 4), (-2, 4),
        (4, 2), (-4, 2), (0, 5), (3, 4), (-3, 4), (4, 3), (-4, 3), (5, 0), (1, 5), (-1, 5), (5, 1), (-5, 1), (2, 5), (-2, 5), (5, 2), (-5, 2),
        (4, 4), (-4, 4), (3, 5), (-3, 5), (5, 3), (-5, 3), (0, 6), (6, 0), (1, 6), (-1, 6), (6, 1), (-6, 1), (2, 6), (-2, 6), (6, 2), (-6, 2),
        (4, 5), (-4, 5), (5, 4), (-5, 4), (3, 6), (-3, 6), (6, 3), (-6, 3), (0, 7), (7, 0), (1, 7), (-1, 7), (5, 5), (-5, 5), (7, 1), (-7, 1),
        (4, 6), (-4, 6), (6, 4), (-6, 4), (2, 7), (-2, 7), (7, 2), (-7, 2), (3, 7), (-3, 7), (7, 3), (-7, 3), (5, 6), (-5, 6), (6, 5), (-6, 5),
        (8, 0), (4, 7), (-4, 7), (7, 4), (-7, 4), (8, 1), (8, 2), (6, 6), (-6, 6), (8, 3), (5, 7), (-5, 7), (7, 5), (-7, 5), (8, 4), (6, 7),
        (-6, 7), (7, 6), (-7, 6), (8, 5), (7, 7), (-7, 7), (8, 6), (8, 7),
    ];

    /// <summary>Gets the 120 distance-map offsets (for a cross-check of the table transcription).</summary>
    public static IReadOnlyList<(int X, int Y)> DistanceMapOffsets => DistanceMap;

    /// <summary>Decodes a <c>VP8L</c> chunk payload (header and image stream).</summary>
    /// <param name="payload">The chunk payload.</param>
    /// <returns>The image size, the alpha hint and the ARGB pixels (row-major, <c>0xAARRGGBB</c>).</returns>
    public static (int Width, int Height, bool AlphaHint, uint[] Argb) Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new BitReader(payload.ToArray());
        if (reader.Read(8) != 0x2F)
            throw new InvalidDataException("VP8L: bad signature.");

        var width = (int)reader.Read(14) + 1;
        var height = (int)reader.Read(14) + 1;
        var alphaHint = reader.Read(1) == 1;
        if (reader.Read(3) != 0)
            throw new InvalidDataException("VP8L: version is not 0.");

        return (width, height, alphaHint, DecodeImageStream(reader, width, height));
    }

    /// <summary>Decodes a headerless VP8L image stream of a known size (the compressed data of an <c>ALPH</c> chunk).</summary>
    /// <param name="data">The stream.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The ARGB pixels.</returns>
    public static uint[] DecodeImageStream(ReadOnlySpan<byte> data, int width, int height) => DecodeImageStream(new BitReader(data.ToArray()), width, height);

    private static uint[] DecodeImageStream(BitReader reader, int width, int height)
    {
        // Transforms, in order of appearance; inverted in reverse order
        var transforms = new List<(int Type, int Bits, uint[] Data, int XSize)>();
        var xsize = width;
        var seen = new HashSet<int>();
        while (reader.Read(1) == 1)
        {
            var type = (int)reader.Read(2);
            if (!seen.Add(type))
                throw new InvalidDataException("VP8L: a transform is used twice.");

            switch (type)
            {
                case 0:
                case 1:
                {
                    var bits = (int)reader.Read(3) + 2;
                    var data = DecodeEntropyCodedImage(reader, DivRoundUp(xsize, bits), DivRoundUp(height, bits));
                    transforms.Add((type, bits, data, xsize));
                    break;
                }

                case 2:
                    transforms.Add((type, 0, [], xsize));
                    break;
                default:
                {
                    var size = (int)reader.Read(8) + 1;
                    var table = DecodeEntropyCodedImage(reader, size, 1);
                    for (var i = 1; i < table.Length; i++)
                    {
                        table[i] = AddPixels(table[i], table[i - 1]);
                    }

                    var widthBits = size <= 2 ? 3 : size <= 4 ? 2 : size <= 16 ? 1 : 0;
                    transforms.Add((type, widthBits, table, xsize));
                    xsize = DivRoundUp(xsize, widthBits);
                    break;
                }
            }
        }

        var pixels = DecodeSpatiallyCodedImage(reader, xsize, height, isMainImage: true);
        for (var i = transforms.Count - 1; i >= 0; i--)
        {
            var (type, bits, data, transformXSize) = transforms[i];
            pixels = type switch
            {
                0 => InversePredictor(pixels, transformXSize, height, bits, data),
                1 => InverseColorTransform(pixels, transformXSize, height, bits, data),
                2 => AddGreen(pixels),
                _ => InverseColorIndexing(pixels, transformXSize, height, bits, data),
            };
        }

        return pixels;
    }

    private static uint[] DecodeEntropyCodedImage(BitReader reader, int width, int height) => DecodeSpatiallyCodedImage(reader, width, height, isMainImage: false);

    private static uint[] DecodeSpatiallyCodedImage(BitReader reader, int width, int height, bool isMainImage)
    {
        var cacheBits = 0;
        if (reader.Read(1) == 1)
        {
            cacheBits = (int)reader.Read(4);
            if (cacheBits is < 1 or > 11)
                throw new InvalidDataException("VP8L: invalid color cache size.");
        }

        var prefixBits = 0;
        uint[]? entropyImage = null;
        var groupCount = 1;
        if (isMainImage && reader.Read(1) == 1)
        {
            prefixBits = (int)reader.Read(3) + 2;
            entropyImage = DecodeEntropyCodedImage(reader, DivRoundUp(width, prefixBits), DivRoundUp(height, prefixBits));
            groupCount = (int)entropyImage.Max(pixel => (pixel >> 8) & 0xFFFF) + 1;
        }

        var cacheSize = cacheBits == 0 ? 0 : 1 << cacheBits;
        var groups = new PrefixCode[groupCount][];
        for (var g = 0; g < groupCount; g++)
        {
            groups[g] =
            [
                ReadPrefixCode(reader, NumLiteralCodes + NumLengthCodes + cacheSize),
                ReadPrefixCode(reader, NumLiteralCodes),
                ReadPrefixCode(reader, NumLiteralCodes),
                ReadPrefixCode(reader, NumLiteralCodes),
                ReadPrefixCode(reader, NumDistanceCodes),
            ];
        }

        var pixels = new uint[width * height];
        var cache = new uint[cacheSize];
        var position = 0;
        var cached = 0;
        while (position < pixels.Length)
        {
            var group = groups[0];
            if (entropyImage is not null)
            {
                var x = position % width;
                var y = position / width;
                group = groups[(entropyImage[((y >> prefixBits) * DivRoundUp(width, prefixBits)) + (x >> prefixBits)] >> 8) & 0xFFFF];
            }

            var symbol = group[0].ReadSymbol(reader);
            if (symbol < NumLiteralCodes)
            {
                var red = group[1].ReadSymbol(reader);
                var blue = group[2].ReadSymbol(reader);
                var alpha = group[3].ReadSymbol(reader);
                pixels[position++] = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)symbol << 8) | (uint)blue;
            }
            else if (symbol < NumLiteralCodes + NumLengthCodes)
            {
                var length = ReadPrefixedValue(reader, symbol - NumLiteralCodes);
                var distanceCode = ReadPrefixedValue(reader, group[4].ReadSymbol(reader));
                int distance;
                if (distanceCode > 120)
                {
                    distance = distanceCode - 120;
                }
                else
                {
                    var (xi, yi) = DistanceMap[distanceCode - 1];
                    distance = Math.Max(1, xi + (yi * width));
                }

                if (distance > position || position + length > pixels.Length)
                    throw new InvalidDataException("VP8L: backward reference out of range.");

                for (var i = 0; i < length; i++)
                {
                    pixels[position] = pixels[position - distance];
                    position++;
                }
            }
            else
            {
                var index = symbol - NumLiteralCodes - NumLengthCodes;
                if (index >= cacheSize)
                    throw new InvalidDataException("VP8L: color cache index out of range.");

                // Every pixel decoded before this one is in the cache (inserted below)
                for (; cached < position; cached++)
                {
                    cache[ColorCacheKey(pixels[cached], cacheBits)] = pixels[cached];
                }

                pixels[position++] = cache[index];
            }

            if (cacheSize > 0)
            {
                for (; cached < position; cached++)
                {
                    cache[ColorCacheKey(pixels[cached], cacheBits)] = pixels[cached];
                }
            }
        }

        return pixels;
    }

    private static int ColorCacheKey(uint argb, int bits) => (int)((0x1E35A7BDu * argb) >> (32 - bits));

    private static int ReadPrefixedValue(BitReader reader, int prefix)
    {
        if (prefix < 4)
            return prefix + 1;

        var extraBits = (prefix - 2) >> 1;
        var offset = (2 + (prefix & 1)) << extraBits;
        return offset + (int)reader.Read(extraBits) + 1;
    }

    private static PrefixCode ReadPrefixCode(BitReader reader, int alphabetSize)
    {
        var lengths = new int[alphabetSize];
        if (reader.Read(1) == 1)
        {
            // Simple code: one or two symbols
            var count = (int)reader.Read(1) + 1;
            var firstBits = reader.Read(1) == 1 ? 8 : 1;
            var first = (int)reader.Read(firstBits);
            Check(first < alphabetSize);
            lengths[first] = 1;
            if (count == 2)
            {
                var second = (int)reader.Read(8);
                Check(second < alphabetSize);
                lengths[second] = 1;
            }

            return new PrefixCode(lengths);
        }

        var codeLengthCodeLengths = new int[CodeLengthCodeOrder.Length];
        var codeLengthCount = 4 + (int)reader.Read(4);
        for (var i = 0; i < codeLengthCount; i++)
        {
            codeLengthCodeLengths[CodeLengthCodeOrder[i]] = (int)reader.Read(3);
        }

        var codeLengthCode = new PrefixCode(codeLengthCodeLengths);
        var maxSymbol = alphabetSize;
        if (reader.Read(1) == 1)
        {
            var lengthBits = 2 + (2 * (int)reader.Read(3));
            maxSymbol = 2 + (int)reader.Read(lengthBits);
            Check(maxSymbol <= alphabetSize);
        }

        var symbol = 0;
        var previous = 8;
        while (symbol < alphabetSize)
        {
            if (maxSymbol-- == 0)
                break;

            var code = codeLengthCode.ReadSymbol(reader);
            if (code < 16)
            {
                lengths[symbol++] = code;
                if (code != 0)
                {
                    previous = code;
                }

                continue;
            }

            var (repeat, value) = code switch
            {
                16 => (3 + (int)reader.Read(2), previous),
                17 => (3 + (int)reader.Read(3), 0),
                _ => (11 + (int)reader.Read(7), 0),
            };
            Check(symbol + repeat <= alphabetSize);
            for (var i = 0; i < repeat; i++)
            {
                lengths[symbol++] = value;
            }
        }

        return new PrefixCode(lengths);
    }

    private static uint[] InversePredictor(uint[] pixels, int width, int height, int bits, uint[] modes)
    {
        var tilesPerRow = DivRoundUp(width, bits);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                uint prediction;
                if (x == 0 && y == 0)
                {
                    prediction = 0xFF000000;
                }
                else if (y == 0)
                {
                    prediction = pixels[index - 1];
                }
                else if (x == 0)
                {
                    prediction = pixels[index - width];
                }
                else
                {
                    var mode = (int)((modes[((y >> bits) * tilesPerRow) + (x >> bits)] >> 8) & 0xF);
                    var left = pixels[index - 1];
                    var top = pixels[index - width];
                    var topLeft = pixels[index - width - 1];
                    var topRight = pixels[index - width + 1]; // the rightmost pixel uses the leftmost pixel of the current row
                    prediction = mode switch
                    {
                        0 => 0xFF000000,
                        1 => left,
                        2 => top,
                        3 => topRight,
                        4 => topLeft,
                        5 => Average2(Average2(left, topRight), top),
                        6 => Average2(left, topLeft),
                        7 => Average2(left, top),
                        8 => Average2(topLeft, top),
                        9 => Average2(top, topRight),
                        10 => Average2(Average2(left, topLeft), Average2(top, topRight)),
                        11 => Select(left, top, topLeft),
                        12 => PerChannel(left, top, topLeft, static (a, b, c) => Math.Clamp(a + b - c, 0, 255)),
                        13 => PerChannel(Average2(left, top), topLeft, 0, static (a, b, _) => Math.Clamp(a + ((a - b) / 2), 0, 255)),
                        _ => 0xFF000000, // modes 14 and 15 are not defined; decoders treat them as black
                    };
                }

                pixels[index] = AddPixels(pixels[index], prediction);
            }
        }

        return pixels;
    }

    private static uint[] InverseColorTransform(uint[] pixels, int width, int height, int bits, uint[] elements)
    {
        var tilesPerRow = DivRoundUp(width, bits);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var element = elements[((y >> bits) * tilesPerRow) + (x >> bits)];
                var greenToRed = (sbyte)(element & 0xFF);
                var greenToBlue = (sbyte)((element >> 8) & 0xFF);
                var redToBlue = (sbyte)((element >> 16) & 0xFF);
                var index = (y * width) + x;
                var argb = pixels[index];
                var green = (sbyte)((argb >> 8) & 0xFF);
                var red = (int)((argb >> 16) & 0xFF);
                var blue = (int)(argb & 0xFF);
                red = (red + ((greenToRed * green) >> 5)) & 0xFF;
                blue = (blue + ((greenToBlue * green) >> 5)) & 0xFF;
                blue = (blue + ((redToBlue * (sbyte)red) >> 5)) & 0xFF;
                pixels[index] = (argb & 0xFF00FF00) | ((uint)red << 16) | (uint)blue;
            }
        }

        return pixels;
    }

    private static uint[] AddGreen(uint[] pixels)
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            var argb = pixels[i];
            var green = (argb >> 8) & 0xFF;
            var red = (((argb >> 16) & 0xFF) + green) & 0xFF;
            var blue = ((argb & 0xFF) + green) & 0xFF;
            pixels[i] = (argb & 0xFF00FF00) | (red << 16) | blue;
        }

        return pixels;
    }

    private static uint[] InverseColorIndexing(uint[] packed, int width, int height, int widthBits, uint[] table)
    {
        var packedWidth = DivRoundUp(width, widthBits);
        var bitsPerIndex = 8 >> widthBits;
        var mask = (1 << bitsPerIndex) - 1;
        var result = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var green = (int)((packed[(y * packedWidth) + (x >> widthBits)] >> 8) & 0xFF);
                var index = (green >> (bitsPerIndex * (x & ((1 << widthBits) - 1)))) & mask;
                result[(y * width) + x] = index < table.Length ? table[index] : 0; // out of the table: transparent black
            }
        }

        return result;
    }

    private static uint Average2(uint a, uint b) => PerChannel(a, b, 0, static (x, y, _) => (x + y) / 2);

    private static uint Select(uint left, uint top, uint topLeft)
    {
        // p = L + T - TL per channel; the candidate (L or T) closer to p in Manhattan distance
        var distanceToLeft = 0;
        var distanceToTop = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var l = (int)((left >> shift) & 0xFF);
            var t = (int)((top >> shift) & 0xFF);
            var tl = (int)((topLeft >> shift) & 0xFF);
            var p = l + t - tl;
            distanceToLeft += Math.Abs(p - l);
            distanceToTop += Math.Abs(p - t);
        }

        return distanceToLeft < distanceToTop ? left : top;
    }

    private static uint PerChannel(uint a, uint b, uint c, Func<int, int, int, int> operation)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            result |= (uint)(operation((int)((a >> shift) & 0xFF), (int)((b >> shift) & 0xFF), (int)((c >> shift) & 0xFF)) & 0xFF) << shift;
        }

        return result;
    }

    private static uint AddPixels(uint a, uint b) => PerChannel(a, b, 0, static (x, y, _) => x + y);

    private static int DivRoundUp(int value, int bits) => (value + (1 << bits) - 1) >> bits;

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidDataException("VP8L: invalid prefix code.");
    }

    /// <summary>LSB-first bit reader; reading past the end is an error.</summary>
    private sealed class BitReader(byte[] data)
    {
        private long _position;

        public uint Read(int count)
        {
            uint value = 0;
            for (var i = 0; i < count; i++)
            {
                var byteIndex = _position >> 3;
                if (byteIndex >= data.Length)
                    throw new InvalidDataException("VP8L: the bitstream is truncated.");

                value |= (uint)((data[byteIndex] >> (int)(_position & 7)) & 1) << i;
                _position++;
            }

            return value;
        }
    }

    /// <summary>A canonical prefix code from its code lengths, decoded bit by bit (codes are read most significant bit first).</summary>
    private sealed class PrefixCode
    {
        private readonly Dictionary<(int Length, int Code), int> _symbols = [];
        private readonly int _single = -1;

        public PrefixCode(int[] lengths)
        {
            var used = lengths.Count(length => length > 0);
            if (used == 0)
                throw new InvalidDataException("VP8L: a prefix code has no symbol.");

            if (used == 1)
            {
                _single = Array.FindIndex(lengths, length => length > 0); // one symbol: zero bits
                return;
            }

            var maxLength = lengths.Max();
            var code = 0;
            var kraft = 0L;
            for (var length = 1; length <= maxLength; length++)
            {
                for (var symbol = 0; symbol < lengths.Length; symbol++)
                {
                    if (lengths[symbol] == length)
                    {
                        _symbols.Add((length, code), symbol);
                        code++;
                        kraft += 1L << (maxLength - length);
                    }
                }

                code <<= 1;
            }

            if (kraft != 1L << maxLength)
                throw new InvalidDataException("VP8L: a prefix code is not complete.");
        }

        public int ReadSymbol(BitReader reader)
        {
            if (_single >= 0)
                return _single;

            var code = 0;
            for (var length = 1; length <= 15; length++)
            {
                code = (code << 1) | (int)reader.Read(1);
                if (_symbols.TryGetValue((length, code), out var symbol))
                    return symbol;
            }

            throw new InvalidDataException("VP8L: invalid prefix code.");
        }
    }
}
