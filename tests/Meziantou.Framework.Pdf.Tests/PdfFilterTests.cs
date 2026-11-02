using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfFilterTests
{
    private const long Limit = 1 << 24;

    public static TheoryData<byte[]> Samples => new()
    {
        Array.Empty<byte>(),
        "a"u8.ToArray(),
        "Hello, world"u8.ToArray(),
        Enumerable.Repeat((byte)'A', 1000).ToArray(),
        Enumerable.Range(0, 5000).Select(i => (byte)(i * 7 % 251)).ToArray(),
        Enumerable.Range(0, 70000).Select(i => (byte)((i / 13) ^ (i % 5))).ToArray(),
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void FlateRoundTrip(byte[] data) => Assert.Equal(data, FlateFilter.Decode(FlateFilter.Encode(data), Limit));

    [Theory]
    [MemberData(nameof(Samples))]
    public void LzwRoundTrip(byte[] data) => Assert.Equal(data, LzwFilter.Decode(LzwFilter.Encode(data), earlyChange: 1, Limit));

    [Theory]
    [MemberData(nameof(Samples))]
    public void AsciiHexRoundTrip(byte[] data) => Assert.Equal(data, AsciiHexFilter.Decode(AsciiHexFilter.Encode(data), Limit));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Ascii85RoundTrip(byte[] data) => Assert.Equal(data, Ascii85Filter.Decode(Ascii85Filter.Encode(data), Limit));

    [Theory]
    [MemberData(nameof(Samples))]
    public void RunLengthRoundTrip(byte[] data) => Assert.Equal(data, RunLengthFilter.Decode(RunLengthFilter.Encode(data), Limit));

    [Fact]
    public void LzwExampleOfTheSpecification()
    {
        // ISO 32000-2, 7.4.4.2: the example of the LZWDecode filter
        byte[] encoded = [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01];

        Assert.Equal(new byte[] { 45, 45, 45, 45, 45, 65, 45, 45, 45, 66 }, LzwFilter.Decode(encoded, earlyChange: 1, Limit));
        Assert.Equal(encoded, LzwFilter.Encode([45, 45, 45, 45, 45, 65, 45, 45, 45, 66]));
    }

    [Theory]
    [InlineData("9jqo^~>", "Man ")]
    [InlineData("9jqo^BlbD-BleB1DJ+*+F(f,q~>", "Man is distinguished")]
    [InlineData("z~>", "\0\0\0\0")]
    [InlineData("<~9jqo^~>", "Man ")]
    [InlineData("9jq\n o^~>", "Man ")]
    [InlineData("9jqo~>", "Man")]
    [InlineData("~>", "")]
    public void Ascii85Vectors(string encoded, string expected) => Assert.Equal(expected, Encoding.Latin1.GetString(Ascii85Filter.Decode(Encoding.ASCII.GetBytes(encoded), Limit)));

    [Fact]
    public void Ascii85LargestGroup() => Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, Ascii85Filter.Decode("s8W-!~>"u8, Limit));

    [Fact]
    public void Ascii85GroupAboveTheLargestValueThrows() => Assert.Throws<InvalidPdfContentException>(() => Ascii85Filter.Decode("s8W-\"~>"u8, Limit));

    [Fact]
    public void Ascii85InvalidCharacterThrows() => Assert.Throws<InvalidPdfContentException>(() => Ascii85Filter.Decode("9jqo{~>"u8, Limit));

    [Fact]
    public void AsciiHexIgnoresWhitespaceAndPadsOddDigits()
    {
        Assert.Equal(new byte[] { 0x12, 0x34, 0x50 }, AsciiHexFilter.Decode("12 3\n45>trailing"u8, Limit));
        Assert.Throws<InvalidPdfContentException>(() => AsciiHexFilter.Decode("1G>"u8, Limit));
    }

    [Fact]
    public void RunLengthExample()
    {
        // A run of 3 'a' (257 - 254), a literal "bc" (1 + 1), end of data
        Assert.Equal("aaabc"u8.ToArray(), RunLengthFilter.Decode([254, (byte)'a', 1, (byte)'b', (byte)'c', 128, 0xFF], Limit));
    }

    [Fact]
    public void FlateWithoutZlibHeaderIsRawDeflate()
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write("raw deflate data"u8);
        }

        Assert.Equal("raw deflate data", Encoding.ASCII.GetString(FlateFilter.Decode(output.ToArray(), Limit)));
    }

    [Fact]
    public void FlateKeepsTheDataBeforeDamage()
    {
        var data = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 97)).ToArray();
        var encoded = FlateFilter.Encode(data, CompressionLevel.NoCompression);

        // Cut the stream in the middle: the first stored blocks are still decoded
        var decoded = FlateFilter.Decode(encoded.AsSpan(0, encoded.Length / 2), Limit);

        Assert.NotEmpty(decoded);
        Assert.Equal(data.AsSpan(0, decoded.Length).ToArray(), decoded);
    }

    [Fact]
    public void FlateThatIsNotDeflateThrows()
    {
        Assert.Throws<InvalidPdfContentException>(() => FlateFilter.Decode([0x78, 0x9C, 0xFF, 0xFF, 0xFF, 0xFF], Limit));
    }

    [Fact]
    public void DecodedLengthIsLimited()
    {
        var bomb = FlateFilter.Encode(new byte[1_000_000]);

        Assert.Throws<PdfResourceLimitException>(() => FlateFilter.Decode(bomb, 1000));
        Assert.Throws<PdfResourceLimitException>(() => RunLengthFilter.Decode([129, 0, 129, 0, 129, 0, 129, 0, 129, 0, 129, 0, 129, 0, 129, 0, 129, 0], 1000));
    }

    [Fact]
    public void PngPredictors()
    {
        // Four rows of three samples of one component of 8 bits, each row after its filter type
        byte[] filtered =
        [
            1, 10, 5, 5, // Sub: 10, 15, 20
            2, 1, 1, 1, // Up: 11, 16, 21
            3, 2, 2, 2, // Average
            4, 1, 1, 1, // Paeth
        ];
        var parameters = new PdfDictionary { [PdfNames.Predictor] = PdfInteger.Get(12), [PdfNames.Columns] = PdfInteger.Get(3) };

        var decoded = Predictor.Decode(filtered, parameters, Limit);

        Assert.Equal(new byte[] { 10, 15, 20, 11, 16, 21, 7, 13, 19, 8, 14, 20 }, decoded);
    }

    [Fact]
    public void PngUpEncoderIsTheInverseOfTheDecoder()
    {
        var data = Enumerable.Range(0, 60).Select(i => (byte)(i * 13)).ToArray();
        var parameters = new PdfDictionary { [PdfNames.Predictor] = PdfInteger.Get(12), [PdfNames.Columns] = PdfInteger.Get(6) };

        Assert.Equal(data, Predictor.Decode(Predictor.EncodePngUp(data, 6), parameters, Limit));
    }

    [Fact]
    public void TiffPredictor()
    {
        var parameters = new PdfDictionary
        {
            [PdfNames.Predictor] = PdfInteger.Get(2),
            [PdfNames.Columns] = PdfInteger.Get(3),
            [PdfNames.Colors] = PdfInteger.Get(1),
        };

        Assert.Equal(new byte[] { 10, 15, 20, 1, 2, 3 }, Predictor.Decode([10, 5, 5, 1, 1, 1], parameters, Limit));
    }

    [Fact]
    public void TiffPredictorOf16Bits()
    {
        var parameters = new PdfDictionary
        {
            [PdfNames.Predictor] = PdfInteger.Get(2),
            [PdfNames.Columns] = PdfInteger.Get(2),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(16),
        };

        Assert.Equal(new byte[] { 0x01, 0x00, 0x02, 0x01 }, Predictor.Decode([0x01, 0x00, 0x01, 0x01], parameters, Limit));
    }

    [Fact]
    public void TiffPredictorOf4Bits()
    {
        var parameters = new PdfDictionary
        {
            [PdfNames.Predictor] = PdfInteger.Get(2),
            [PdfNames.Columns] = PdfInteger.Get(4),
            [PdfNames.BitsPerComponent] = PdfInteger.Get(4),
        };

        // Samples 1, 1, 1, 1 as differences: 1, 2, 3, 4
        Assert.Equal(new byte[] { 0x12, 0x34 }, Predictor.Decode([0x11, 0x11], parameters, Limit));
    }

    [Fact]
    public void StreamWithSeveralFilters()
    {
        var data = "Several filters"u8.ToArray();
        var stream = new PdfStream(new PdfDictionary
        {
            [PdfNames.Filter] = new PdfArray(PdfNames.ASCIIHexDecode, PdfNames.FlateDecode),
        });
        stream.SetEncodedData(AsciiHexFilter.Encode(FlateFilter.Encode(data)));

        Assert.Equal(data, stream.GetDecodedData());
        Assert.Equal(["ASCIIHexDecode", "FlateDecode"], stream.Filters.Select(f => f.Value));
    }

    [Fact]
    public void AbbreviatedFilterNames()
    {
        var stream = new PdfStream(new PdfDictionary { [PdfNames.Filter] = PdfName.Create("AHx") });
        stream.SetEncodedData("414243>"u8);

        Assert.Equal("ABC"u8.ToArray(), stream.GetDecodedData());
    }

    [Theory]
    [InlineData(4, -1, false, false)]
    [InlineData(4, -1, true, false)]
    [InlineData(3, 0, false, true)]
    [InlineData(2, 0, false, false)]
    public void CcittFax(int tiffCompression, int k, bool blackIs1, bool declareRows)
    {
        const int Width = 77;
        const int Height = 41;
        var bitmap = CreateBitmap(Width, Height);
        var encoded = EncodeCcitt(bitmap, Width, Height, tiffCompression);
        var parameters = new PdfDictionary
        {
            [PdfNames.K] = PdfInteger.Get(k),
            [PdfNames.Columns] = PdfInteger.Get(Width),
            [PdfNames.BlackIs1] = PdfBoolean.Get(blackIs1),

            // Modified Huffman starts each row on a byte
            [PdfName.Create("EncodedByteAlign")] = PdfBoolean.Get(tiffCompression == 2),
        };
        if (declareRows || tiffCompression == 2)
        {
            parameters[PdfName.Create("Rows")] = PdfInteger.Get(Height);
        }

        var decoded = CcittFaxFilter.Decode(encoded, parameters, Limit);

        Assert.Equal(blackIs1 ? bitmap : [.. bitmap.Select(value => (byte)~value)], decoded);
    }

    [Fact]
    public void CcittFaxDamagedDataKeepsTheRowsBeforeIt()
    {
        const int Width = 64;
        const int Height = 20;
        var bitmap = CreateBitmap(Width, Height);
        var encoded = EncodeCcitt(bitmap, Width, Height, 4);
        var parameters = new PdfDictionary
        {
            [PdfNames.K] = PdfInteger.Get(-1),
            [PdfNames.Columns] = PdfInteger.Get(Width),
            [PdfName.Create("Rows")] = PdfInteger.Get(Height),
            [PdfNames.BlackIs1] = PdfBoolean.True,
        };

        var decoded = CcittFaxFilter.Decode(encoded.AsSpan(0, encoded.Length / 2), parameters, Limit);

        // The rows decoded before the end of the data are those of the bitmap, the others are white
        Assert.HasCount(Width / 8 * Height, decoded);
        var decodedRows = Enumerable.Range(0, Height).TakeWhile(row => decoded.AsSpan(row * 8, 8).SequenceEqual(bitmap.AsSpan(row * 8, 8))).Count();
        Assert.True(decodedRows > 0);
        Assert.True(decoded.AsSpan(decodedRows * 8 + 8).IndexOfAnyExcept((byte)0) < 0);
        Assert.Throws<PdfResourceLimitException>(() => CcittFaxFilter.Decode(encoded, new PdfDictionary { [PdfNames.Columns] = PdfInteger.Get(1000), [PdfName.Create("Rows")] = PdfInteger.Get(1_000_000) }, Limit));
    }

    /// <summary>A bitmap of rows of bits, 1 for black: diagonal stripes and a block, so that the runs are of all lengths.</summary>
    private static byte[] CreateBitmap(int width, int height)
    {
        var rowLength = (width + 7) / 8;
        var bitmap = new byte[rowLength * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if ((x + y) % 11 < 4 || (x > 30 && x < 60 && y > 10 && y < 30))
                {
                    bitmap[(y * rowLength) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return bitmap;
    }

    private static byte[] EncodeCcitt(byte[] bitmap, int width, int height, int tiffCompression)
    {
        var scope = Imaging.Internals.AllocationScope.Create(configuration: null, "test");
        using var output = new Imaging.Internals.ImageOutputBuffer(scope);
        var length = (int)Imaging.Internals.TiffCcittEncoder.GetChangeListLength(width);
        Imaging.Internals.TiffCcittEncoder.Encode(bitmap, width, height, tiffCompression, new int[length], new int[length], output);
        return output.WrittenMemory.ToArray();
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(3, true, false)]
    public void Jbig2ArithmeticGenericRegion(int template, bool typicalPrediction, bool customAdaptivePixels)
    {
        const int Width = 70;
        const int Height = 33;
        var bitmap = CreateBitmap(Width, Height);

        // Repeated rows exercise typical prediction
        Array.Copy(bitmap, 9 * 9, bitmap, 10 * 9, 9);
        (int X, int Y)[] adaptivePixels = template == 0
            ? (customAdaptivePixels ? [(4, -2), (-5, -1), (1, -2), (-4, -2)] : [(3, -1), (-3, -1), (2, -2), (-2, -2)])
            : [(customAdaptivePixels ? -4 : template == 1 ? 3 : 2, -1)];
        var coded = EncodeGenericRegion(bitmap, Width, Height, template, typicalPrediction, adaptivePixels);
        var region = new List<byte>();
        region.AddRange(RegionInfo(Width, Height, 0, 0, 0));
        region.Add((byte)((template << 1) | (typicalPrediction ? 8 : 0)));
        foreach (var (x, y) in adaptivePixels)
        {
            region.Add((byte)(sbyte)x);
            region.Add((byte)(sbyte)y);
        }

        region.AddRange(coded);
        var stream = new List<byte>();
        stream.AddRange(Segment(0, 48, [], PageInformation(Width, Height, flags: 0)));
        stream.AddRange(Segment(1, 38, [], [.. region]));
        stream.AddRange(Segment(2, 49, [], []));

        var decoded = Jbig2Filter.Decode([.. stream], parameters: null, Limit);

        Assert.Equal(bitmap.Select(value => (byte)~value), decoded);
    }

    [Fact]
    public void Jbig2MmrRegionsAreCombinedWithThePage()
    {
        const int Width = 40;
        const int Height = 16;
        var bitmap = CreateBitmap(Width, Height);
        var mmr = EncodeCcitt(bitmap, Width, Height, 4);
        static byte[] Region(int x, int y, int combinationOperator, byte[] data) => [.. RegionInfo(40, 16, x, y, combinationOperator), 1, .. data];

        // The page overrides the combination operator: the second region is XORed at an offset, then the page grows to the end of the stripe
        var stream = new List<byte>();
        stream.AddRange(Segment(0, 48, [], PageInformation(Width, uint.MaxValue, flags: 0x40)));
        stream.AddRange(Segment(1, 38, [], Region(0, 0, 0, mmr)));
        stream.AddRange(Segment(2, 38, [], Region(8, 2, 2, mmr)));
        stream.AddRange(Segment(3, 50, [], [0, 0, 0, 19]));
        stream.AddRange(Segment(4, 49, [], []));

        var decoded = Jbig2Filter.Decode([.. stream], parameters: null, Limit);

        Assert.HasCount(5 * 20, decoded);
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var first = y < Height && GetBit(bitmap, 5, x, y);
                var second = y >= 2 && y - 2 < Height && x >= 8 && GetBit(bitmap, 5, x - 8, y - 2);
                Assert.Equal(first ^ second, !GetBit(decoded, 5, x, y));
            }
        }
    }

    [Fact]
    public void Jbig2GlobalSegmentsAreInTheParameters()
    {
        var bitmap = CreateBitmap(16, 8);
        var mmr = EncodeCcitt(bitmap, 16, 8, 4);
        var globals = new PdfStream();
        globals.SetData([.. Segment(0, 48, [], PageInformation(16, 8, flags: 0))]);
        var parameters = new PdfDictionary { [PdfName.Create("JBIG2Globals")] = globals };

        var decoded = Jbig2Filter.Decode([.. Segment(1, 38, [], [.. RegionInfo(16, 8, 0, 0, 0), 1, .. mmr])], parameters, Limit);

        Assert.Equal(bitmap.Select(value => (byte)~value), decoded);
        Assert.Throws<InvalidPdfContentException>(() => Jbig2Filter.Decode([.. Segment(1, 38, [], [.. RegionInfo(16, 8, 0, 0, 0), 1, .. mmr])], parameters: null, Limit));
        Assert.Throws<PdfResourceLimitException>(() => Jbig2Filter.Decode([.. Segment(0, 48, [], PageInformation(1_000_000, 1_000_000, flags: 0))], parameters: null, Limit));
    }

    [Fact]
    public void Jbig2HostileHeadersAreRejected()
    {
        // Global segments that are themselves a JBIG2 image with these globals: decoding them would never end
        var globals = new PdfStream(new PdfDictionary { [PdfNames.Filter] = PdfNames.JBIG2Decode });
        globals.Dictionary[PdfNames.DecodeParms] = new PdfDictionary { [PdfName.Create("JBIG2Globals")] = globals };
        globals.SetEncodedData([.. Segment(0, 48, [], PageInformation(8, 8, flags: 0))]);

        // The long form of the referred-to segments: 2^29 - 1 segments in a header of a few bytes
        byte[] referredCount = [0, 0, 0, 1, 38, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0];

        Assert.Throws<InvalidPdfContentException>(() => Jbig2Filter.Decode([], new PdfDictionary { [PdfName.Create("JBIG2Globals")] = globals }, Limit));
        Assert.Throws<InvalidPdfContentException>(() => Jbig2Filter.Decode(referredCount, parameters: null, Limit));
    }

    [Theory]
    [InlineData("00101", 1, 5)]
    [InlineData("1000000100", 1, 20)]
    [InlineData("110" + "0000000000000001", 1, 273)]
    [InlineData("111" + "00000000000000000000000000000010", 1, 65810)]
    [InlineData("0", 2, 0)]
    [InlineData("111111", 2, null)]
    [InlineData("11111110" + "11111111", 3, -1)]
    [InlineData("11111111" + "00000000000000000000000000000011", 3, -260)]
    [InlineData("101", 14, -1)]
    [InlineData("0", 14, 0)]
    public void Jbig2StandardHuffmanTables(string bits, int table, int? expected)
    {
        var data = new byte[(bits.Length + 7) / 8];
        for (var i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                data[i / 8] |= (byte)(0x80 >> (i % 8));
            }
        }

        var value = Filters.Jbig2.Jbig2HuffmanTable.StandardTables[table].Decode(new Filters.Jbig2.Jbig2BitReader(data, 0, data.Length));

        Assert.Equal(expected, value);
    }

    private static bool GetBit(byte[] data, int stride, int x, int y) => (data[(y * stride) + (x / 8)] & (0x80 >> (x % 8))) != 0;

    private static byte[] Segment(uint number, int type, uint[] referred, byte[] data)
    {
        var header = new List<byte> { (byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number, (byte)type, (byte)(referred.Length << 5) };
        foreach (var segment in referred)
        {
            header.Add((byte)segment);
        }

        header.Add(1);
        header.AddRange(BigEndian((uint)data.Length));
        return [.. header, .. data];
    }

    private static byte[] PageInformation(int width, uint height, int flags) => [.. BigEndian((uint)width), .. BigEndian(height), 0, 0, 0, 0, 0, 0, 0, 0, (byte)flags, 0, 0];

    private static byte[] RegionInfo(int width, int height, int x, int y, int combinationOperator) => [.. BigEndian((uint)width), .. BigEndian((uint)height), .. BigEndian((uint)x), .. BigEndian((uint)y), (byte)combinationOperator];

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>Codes a bitmap as a generic region with the MQ encoder of Meziantou.Framework.Imaging and the contexts of T.88, 6.2.5.3.</summary>
    private static byte[] EncodeGenericRegion(byte[] bitmap, int width, int height, int template, bool typicalPrediction, (int X, int Y)[] at)
    {
        var stride = (width + 7) / 8;
        int Pixel(int x, int y) => x < 0 || x >= width || y < 0 ? 0 : (bitmap[(y * stride) + (x / 8)] >> (7 - (x % 8))) & 1;
        int Context(int x, int y)
        {
            var bits = template switch
            {
                0 => new[] { Pixel(x - 1, y), Pixel(x - 2, y), Pixel(x - 3, y), Pixel(x - 4, y), Pixel(x + at[0].X, y + at[0].Y), Pixel(x + 2, y - 1), Pixel(x + 1, y - 1), Pixel(x, y - 1), Pixel(x - 1, y - 1), Pixel(x - 2, y - 1), Pixel(x + at[1].X, y + at[1].Y), Pixel(x + at[2].X, y + at[2].Y), Pixel(x + 1, y - 2), Pixel(x, y - 2), Pixel(x - 1, y - 2), Pixel(x + at[3].X, y + at[3].Y) },
                1 => [Pixel(x - 1, y), Pixel(x - 2, y), Pixel(x - 3, y), Pixel(x + at[0].X, y + at[0].Y), Pixel(x + 2, y - 1), Pixel(x + 1, y - 1), Pixel(x, y - 1), Pixel(x - 1, y - 1), Pixel(x - 2, y - 1), Pixel(x + 2, y - 2), Pixel(x + 1, y - 2), Pixel(x, y - 2), Pixel(x - 1, y - 2)],
                2 => [Pixel(x - 1, y), Pixel(x - 2, y), Pixel(x + at[0].X, y + at[0].Y), Pixel(x + 1, y - 1), Pixel(x, y - 1), Pixel(x - 1, y - 1), Pixel(x - 2, y - 1), Pixel(x + 1, y - 2), Pixel(x, y - 2), Pixel(x - 1, y - 2)],
                _ => [Pixel(x - 1, y), Pixel(x - 2, y), Pixel(x - 3, y), Pixel(x - 4, y), Pixel(x + at[0].X, y + at[0].Y), Pixel(x + 1, y - 1), Pixel(x, y - 1), Pixel(x - 1, y - 1), Pixel(x - 2, y - 1), Pixel(x - 3, y - 1)],
            };
            var context = 0;
            for (var i = 0; i < bits.Length; i++)
            {
                context |= bits[i] << i;
            }

            return context;
        }

        var scope = Imaging.Internals.AllocationScope.Create(configuration: null, "test");
        using var output = new Imaging.Internals.PooledPayloadWriter(scope);
        var encoder = default(Imaging.Internals.Jpeg2000MqEncoder);
        encoder.Initialize(output);
        var contexts = new byte[1 << 16];
        int[] typicalContexts = [0x9B25, 0x0795, 0x00E5, 0x0195];
        var typical = 0;
        for (var y = 0; y < height; y++)
        {
            if (typicalPrediction)
            {
                var same = y > 0 ? bitmap.AsSpan(y * stride, stride).SequenceEqual(bitmap.AsSpan((y - 1) * stride, stride)) : bitmap.AsSpan(0, stride).IndexOfAnyExcept((byte)0) < 0;
                var value = same ? 1 : 0;
                encoder.Encode(value ^ typical, ref contexts[typicalContexts[template]]);
                typical = value;
                if (same)
                    continue;
            }

            for (var x = 0; x < width; x++)
            {
                encoder.Encode(Pixel(x, y), ref contexts[Context(x, y)]);
            }
        }

        encoder.Flush();
        return output.WrittenMemory.ToArray();
    }

    [Fact]
    public void CcittFaxRowCountIsCheckedWithoutOverflow()
    {
        // 2^61 rows of 8 bytes: the product wraps to 0 in 64 bits
        Assert.Throws<PdfResourceLimitException>(() => CcittFaxFilter.Decode([0], CcittParameters(k: -1, columns: 64, rows: 1L << 61), Limit));
        Assert.Throws<PdfResourceLimitException>(() => CcittFaxFilter.Decode([0], CcittParameters(k: -1, columns: 64, rows: long.MaxValue), Limit));

        // The rows of the limit exactly, then one more
        Assert.HasCount(1 << 16, CcittFaxFilter.Decode([0], CcittParameters(k: -1, columns: 64, rows: 8192), 1 << 16));
        Assert.Throws<PdfResourceLimitException>(() => CcittFaxFilter.Decode([0], CcittParameters(k: -1, columns: 64, rows: 8193), 1 << 16));
    }

    [Fact]
    public void CcittFaxTruncatedDataIsPaddedWithoutCopies()
    {
        // One byte of data and 10000 declared rows of 2000 bytes: the white rows are the output, allocated once
        var before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = CcittFaxFilter.Decode([0], CcittParameters(k: -1, columns: 16000, rows: 10000), 1 << 26);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.HasCount(20_000_000, decoded);
        Assert.False(decoded.AsSpan().ContainsAnyExcept((byte)0xFF));
        Assert.True(allocated < decoded.Length + 1_000_000, $"{allocated} bytes allocated");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CcittFaxWideRowsAreNotAllocatedPerRow(bool blackIs1)
    {
        // Rows of 2000 bytes, inverted (BlackIs1 false) into the output itself
        const int Width = 16000;
        const int Height = 64;
        var bitmap = CreateBitmap(Width, Height);
        var encoded = EncodeCcitt(bitmap, Width, Height, 4);
        var parameters = CcittParameters(k: -1, columns: Width, rows: Height);
        parameters[PdfNames.BlackIs1] = PdfBoolean.Get(blackIs1);

        // The first decoding allocates what the runtime initializes once
        _ = CcittFaxFilter.Decode(encoded, parameters, Limit);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = CcittFaxFilter.Decode(encoded, parameters, Limit);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(blackIs1 ? bitmap : [.. bitmap.Select(value => (byte)~value)], decoded);

        // The output, the two change lists, and no row: an array per row would be 64 x 2000 bytes more
        Assert.True(allocated < decoded.Length + (2 * 4 * (Width + 8)) + 16384, $"{allocated} bytes allocated");
    }

    [Theory]
    [InlineData(false, 0, new byte[] { 0x0F, 0x00, 0x00, 0x00 })]
    [InlineData(true, 0, new byte[] { 0x0F, 0x00, 0x00, 0x00 })]
    [InlineData(true, 1, new byte[] { 0x0F, 0x0F, 0x00, 0xFF })]
    public void CcittFaxDamagedRowsBeforeError(bool endOfLine, int damagedRowsBeforeError, byte[] expected)
    {
        // Group 3, one-dimensional coding of rows of 8 pixels, each after an end-of-line code (T.4, tables 2 and 3):
        // 4 white 4 black, then a damaged row whose black run passes the end of the row, then 8 white, then 8 black
        var encoded = BitString(Eol, "1011" + "011", Eol, "1011" + "000101", Eol, "10011", Eol, "00110101" + "000101", Eol, Eol, Eol, Eol, Eol, Eol);
        var parameters = CcittParameters(k: 0, columns: 8, rows: 4);
        parameters[PdfNames.BlackIs1] = PdfBoolean.True;
        parameters[PdfName.Create("EndOfLine")] = PdfBoolean.Get(endOfLine);
        parameters[PdfName.Create("DamagedRowsBeforeError")] = PdfInteger.Get(damagedRowsBeforeError);

        Assert.Equal(expected, CcittFaxFilter.Decode(encoded, parameters, Limit));
    }

    [Fact]
    public void CcittFaxConsecutiveDamagedRowsAreWhite()
    {
        // The second of two damaged rows has no undamaged row above it: it is white (Table 11)
        var encoded = BitString(Eol, "1011" + "011", Eol, "1011" + "000101", Eol, "1011" + "000101", Eol, "00110101" + "000101", Eol, Eol);
        var parameters = CcittParameters(k: 0, columns: 8, rows: 4);
        parameters[PdfNames.BlackIs1] = PdfBoolean.True;
        parameters[PdfName.Create("EndOfLine")] = PdfBoolean.True;
        parameters[PdfName.Create("DamagedRowsBeforeError")] = PdfInteger.Get(2);

        Assert.Equal([0x0F, 0x0F, 0x00, 0xFF], CcittFaxFilter.Decode(encoded, parameters, Limit));

        // Without /BlackIs1 the bits are inverted, the repeated row too
        parameters.Remove(PdfNames.BlackIs1);
        Assert.Equal([0xF0, 0xF0, 0xFF, 0x00], CcittFaxFilter.Decode(encoded, parameters, Limit));
    }

    [Theory]
    [InlineData(false, 0, new byte[] { 0x0F, 0x00, 0xFF })]
    [InlineData(true, 0, new byte[] { 0x0F, 0x00, 0x00 })]
    [InlineData(true, 1, new byte[] { 0x0F, 0x0F, 0xFF })]
    public void CcittFaxEndOfLine(bool endOfLine, int damagedRowsBeforeError, byte[] expected)
    {
        // The second row has no end-of-line code: it is a row with /EndOfLine false, a damaged row with /EndOfLine true
        var encoded = BitString(Eol, "1011" + "011", "10011", Eol, "00110101" + "000101", Eol, Eol);
        var parameters = CcittParameters(k: 0, columns: 8, rows: 3);
        parameters[PdfNames.BlackIs1] = PdfBoolean.True;
        parameters[PdfName.Create("EndOfLine")] = PdfBoolean.Get(endOfLine);
        parameters[PdfName.Create("DamagedRowsBeforeError")] = PdfInteger.Get(damagedRowsBeforeError);

        Assert.Equal(expected, CcittFaxFilter.Decode(encoded, parameters, Limit));
    }

    [Fact]
    public void CcittFaxWithoutEndOfLine()
    {
        // Group 3 without any end-of-line code, and mixed one- and two-dimensional coding (K 2): tag bit 1, then one dimension
        var parameters = CcittParameters(k: 0, columns: 8, rows: 3);
        parameters[PdfNames.BlackIs1] = PdfBoolean.True;
        Assert.Equal([0x0F, 0x00, 0xFF], CcittFaxFilter.Decode(BitString("1011" + "011", "10011", "00110101" + "000101"), parameters, Limit));

        // The second row repeats the first one in two dimensions: tag bit 0 and two vertical codes V0 (T.4, table 4)
        parameters[PdfNames.K] = PdfInteger.Get(2);
        parameters[PdfName.Create("EndOfLine")] = PdfBoolean.True;
        Assert.Equal([0x0F, 0x0F, 0xFF], CcittFaxFilter.Decode(BitString(Eol, "1" + "1011" + "011", Eol, "0" + "1" + "1", Eol, "1" + "00110101" + "000101", Eol, Eol), parameters, Limit));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 3)]
    [InlineData(77, 41)]
    [InlineData(3000, 17)]
    public void CcittGroup4EncoderRoundTrip(int width, int height)
    {
        var bitmap = CreateBitmap(width, height);
        var encoded = CcittFaxFilter.EncodeGroup4(bitmap, width, height);
        var parameters = CcittParameters(k: -1, columns: width, rows: height);
        parameters[PdfNames.BlackIs1] = PdfBoolean.True;

        Assert.Equal(bitmap, CcittFaxFilter.Decode(encoded, parameters, Limit));

        // Without /Rows, the end-of-facsimile-block code ends the data
        parameters.Remove(PdfName.Create("Rows"));
        Assert.Equal(bitmap, CcittFaxFilter.Decode(encoded, parameters, Limit));

        // The Group 4 encoder of the TIFF codec of Imaging codes the same changes with the same codes
        Assert.Equal(EncodeCcitt(bitmap, width, height, 4), encoded);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 3)]
    [InlineData(77, 41)]
    [InlineData(1000, 64)]
    public void Jbig2EncoderRoundTrip(int width, int height)
    {
        var bitmap = CreateBitmap(width, height);

        // Repeated rows and white rows exercise typical prediction
        var stride = (width + 7) / 8;
        if (height > 3)
        {
            bitmap.AsSpan(stride, stride).CopyTo(bitmap.AsSpan(2 * stride, stride));
            bitmap.AsSpan(0, stride).Clear();
        }

        var encoded = Filters.Jbig2.Jbig2Encoder.Encode(bitmap, width, height);
        var decoded = Jbig2Filter.Decode(encoded, parameters: null, Limit);

        Assert.HasCount(bitmap.Length, decoded);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Assert.Equal(GetBit(bitmap, stride, x, y), !GetBit(decoded, stride, x, y));
            }
        }
    }

    [Fact]
    public void Jbig2EncoderIgnoresThePaddingBits()
    {
        // The bits after the last pixel of a row are not pixels: a row of 4 pixels with garbage after them repeats the row above
        var withPadding = Filters.Jbig2.Jbig2Encoder.Encode([0xA5, 0xAF, 0xA3], 4, 3);
        var withoutPadding = Filters.Jbig2.Jbig2Encoder.Encode([0xA0, 0xA0, 0xA0], 4, 3);

        Assert.Equal(withoutPadding, withPadding);
        Assert.Equal([0x5F, 0x5F, 0x5F], Jbig2Filter.Decode(withPadding, parameters: null, Limit));
    }

    [Fact]
    public void Jbig2BitmapSizesAreCheckedWithoutOverflow()
    {
        // A budget that allows any size: each bitmap is charged to its own
        static Filters.Jbig2.Jbig2Budget Unlimited() => new(long.MaxValue / 64);

        // (int.MaxValue + 7) / 8 overflows in 32 bits; the size of 16 rows of it is more than an array holds
        Assert.Throws<PdfResourceLimitException>(() => new Filters.Jbig2.Jbig2Bitmap(int.MaxValue, 16, Unlimited()));
        Assert.Throws<PdfResourceLimitException>(() => new Filters.Jbig2.Jbig2Bitmap(int.MaxValue, int.MaxValue, Unlimited()));

        // A bitmap of no row has a size of 0 bytes, whatever its width
        var empty = new Filters.Jbig2.Jbig2Bitmap(int.MaxValue, 0, Unlimited());
        Assert.Equal(268_435_456, empty.Stride);
        Assert.Empty(empty.Data);
        Assert.Throws<PdfResourceLimitException>(() => empty.GrowHeight(16, 0, Unlimited()));

        // The memory limit is checked before the allocation
        Assert.Throws<PdfResourceLimitException>(() => new Filters.Jbig2.Jbig2Bitmap(int.MaxValue, 1, new Filters.Jbig2.Jbig2Budget(1 << 20)));
        Assert.Throws<PdfResourceLimitException>(() => Jbig2Filter.Decode([.. Segment(0, 48, [], PageInformation(int.MaxValue, int.MaxValue, flags: 0))], parameters: null, Limit));
        Assert.Empty(Jbig2Filter.Decode([.. Segment(0, 48, [], PageInformation(int.MaxValue, 0, flags: 0))], parameters: null, Limit));
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    [Fact]
    public void Jbig2BitmapCombineAndExtractMatchThePixels()
    {
        var random = new Random(42);
        var budget = new Filters.Jbig2.Jbig2Budget(long.MaxValue / 64);
        for (var i = 0; i < 400; i++)
        {
            // The padding bits of the source are random: they are not pixels
            var target = new Filters.Jbig2.Jbig2Bitmap(random.Next(1, 70), random.Next(1, 20), budget);
            var source = new Filters.Jbig2.Jbig2Bitmap(random.Next(0, 50), random.Next(0, 20), budget);
            random.NextBytes(target.Data);
            random.NextBytes(source.Data);
            var x = random.Next(-60, 80);
            var y = random.Next(-25, 25);
            var combinationOperator = random.Next(0, 5);

            var expected = new Filters.Jbig2.Jbig2Bitmap(target.Width, target.Height, budget);
            target.Data.CopyTo(expected.Data, 0);
            for (var row = 0; row < source.Height; row++)
            {
                for (var column = 0; column < source.Width; column++)
                {
                    var tx = x + column;
                    var ty = y + row;
                    if (tx < 0 || tx >= target.Width || ty < 0 || ty >= target.Height)
                        continue;

                    var value = source.GetPixel(column, row);
                    var existing = expected.GetPixel(tx, ty);
                    expected.SetPixel(tx, ty, combinationOperator switch
                    {
                        0 => existing | value,
                        1 => existing & value,
                        2 => existing ^ value,
                        3 => 1 - (existing ^ value),
                        _ => value,
                    });
                }
            }

            target.Combine(source, x, y, combinationOperator);
            Assert.Equal(expected.Data, target.Data);

            var width = random.Next(0, 60);
            var height = random.Next(0, 20);
            var extracted = target.Extract(x, y, width, height, budget);
            var expectedPart = new Filters.Jbig2.Jbig2Bitmap(width, height, budget);
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    expectedPart.SetPixel(column, row, target.GetPixel(x + column, y + row));
                }
            }

            Assert.Equal(expectedPart.Data, extracted.Data);
        }
    }

    [Fact]
    public void Jbig2PageIsInvertedInPlace()
    {
        // A white page of 4960 x 7016 pixels: the bits of the page are inverted, not copied
        var decoder = new Filters.Jbig2.Jbig2Decoder(Limit);
        decoder.DecodeSegments([.. Segment(0, 48, [], PageInformation(4960, 7016, flags: 0))]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = decoder.GetPage();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.HasCount(620 * 7016, page);
        Assert.False(page.AsSpan().ContainsAnyExcept((byte)0xFF));
        Assert.True(allocated < 1024, $"{allocated} bytes allocated");
        Assert.Throws<InvalidPdfContentException>(() => decoder.GetPage());
    }

    [Fact]
    public void Jbig2TinyStreamsDoNotDecodeHugeRegions()
    {
        // A page of 2048 x 2048 pixels and immediate generic regions of the page with 40 bytes of data each: the
        // arithmetic decoder decodes any number of pixels past the end of the data, so the work is bounded by the page
        static byte[] Region(uint number) => Segment(number, 38, [], [.. RegionInfo(2048, 2048, 0, 0, 0), 0, 3, 0xFF, 0xFD, 0xFF, 2, 0xFE, 0xFE, 0xFE, .. new byte[40]]);

        var page = Segment(0, 48, [], PageInformation(2048, 2048, flags: 0));
        var stream = new List<byte>(page);
        for (var i = 1u; i <= 7; i++)
        {
            stream.AddRange(Region(i));
        }

        // Seven regions are seven times the pixels of the page (and 36 MiB, below the limit): more than 3 times the page and 16 million
        var decoder = new Filters.Jbig2.Jbig2Decoder(PdfReadOptions.DefaultMaxDecodedStreamLength);
        Assert.Throws<PdfResourceLimitException>(() => decoder.DecodeSegments([.. stream]));
        Assert.True(decoder.Budget.Work <= (1 << 24) + (3L * 2048 * 2048) + 2049, $"{decoder.Budget.Work} units of work");

        // Two regions are decoded and placed
        Assert.HasCount(256 * 2048, Jbig2Filter.Decode([.. page, .. Region(1), .. Region(2)], parameters: null, PdfReadOptions.DefaultMaxDecodedStreamLength));
    }

    [Fact]
    public void Jbig2GlobalSegmentsAreDecodedOncePerStream()
    {
        // The globals: a pattern dictionary of two 4 x 4 patterns (white, black), whose collective bitmap is coded with MMR
        static PdfStream CreateGlobals(byte[] collective)
        {
            var globals = new PdfStream();
            globals.SetData([.. Segment(0, 16, [], [1, 4, 4, 0, 0, 0, 1, .. EncodeCcitt(collective, 8, 4, 4)])]);
            return globals;
        }

        // The image: a halftone region of 4 x 4 cells of 4 x 4 pixels, its gray-scale values (one bit plane, MMR) a checkerboard
        var plane = EncodeCcitt([0xA0, 0x50, 0xA0, 0x50], 4, 4, 4);
        byte[] halftone = [.. RegionInfo(16, 16, 0, 0, 0), 1, .. BigEndian(4), .. BigEndian(4), .. BigEndian(0), .. BigEndian(0), 4, 0, 0, 0, .. plane];
        byte[] image = [.. Segment(1, 48, [], PageInformation(16, 16, flags: 0)), .. Segment(2, 22, [0], halftone)];
        var globals = CreateGlobals([0x0F, 0x0F, 0x0F, 0x0F]);
        var parameters = new PdfDictionary { [PdfName.Create("JBIG2Globals")] = globals };

        // 0 is black: the cells of value 1 are the black pattern
        byte[] expected = [.. Enumerable.Range(0, 16).SelectMany(row => (row / 4 % 2 == 0) ? new byte[] { 0x0F, 0x0F } : [0xF0, 0xF0])];
        Assert.Equal(expected, Jbig2Filter.Decode(image, parameters, Limit));
        Assert.True(Jbig2Filter.IsCached(globals, Limit));
        Assert.False(Jbig2Filter.IsCached(globals, Limit / 2));
        Assert.Equal(expected, Jbig2Filter.Decode(image, parameters, Limit));

        // The data of the globals changes: the patterns are swapped
        globals.SetData([.. Segment(0, 16, [], [1, 4, 4, 0, 0, 0, 1, .. EncodeCcitt([0xF0, 0xF0, 0xF0, 0xF0], 8, 4, 4)])]);
        Assert.False(Jbig2Filter.IsCached(globals, Limit));
        Assert.Equal(expected.Select(value => (byte)~value), Jbig2Filter.Decode(image, parameters, Limit));

        // Globals that paint a page are not shared: each image paints its own page
        var withPage = new PdfStream();
        withPage.SetData([.. Segment(0, 48, [], PageInformation(16, 8, flags: 0))]);
        _ = Jbig2Filter.Decode([.. Segment(1, 38, [], [.. RegionInfo(16, 8, 0, 0, 0), 1, .. EncodeCcitt(CreateBitmap(16, 8), 16, 8, 4)])], new PdfDictionary { [PdfName.Create("JBIG2Globals")] = withPage }, Limit);
        Assert.False(Jbig2Filter.IsCached(withPage, Limit));
    }

    private const string Eol = "000000000001";

    private static PdfDictionary CcittParameters(int k, int columns, long rows) => new()
    {
        [PdfNames.K] = PdfInteger.Get(k),
        [PdfNames.Columns] = PdfInteger.Get(columns),
        [PdfName.Create("Rows")] = PdfInteger.Get(rows),
    };

    /// <summary>Packs codes written as strings of 0 and 1, most significant bit first, the last byte padded with zeros.</summary>
    private static byte[] BitString(params string[] codes)
    {
        var bits = string.Concat(codes);
        var data = new byte[(bits.Length + 7) / 8];
        for (var i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                data[i / 8] |= (byte)(0x80 >> (i % 8));
            }
        }

        return data;
    }

    [Fact]
    public void UnknownFilterThrows()
    {
        var stream = new PdfStream(new PdfDictionary { [PdfNames.Filter] = PdfName.Create("Unknown") });
        stream.SetEncodedData("data"u8);

        Assert.Throws<InvalidPdfContentException>(stream.GetDecodedData);
    }
}
