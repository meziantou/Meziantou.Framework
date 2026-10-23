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
    public void UnknownFilterThrows()
    {
        var stream = new PdfStream(new PdfDictionary { [PdfNames.Filter] = PdfName.Create("Unknown") });
        stream.SetEncodedData("data"u8);

        Assert.Throws<InvalidPdfContentException>(stream.GetDecodedData);
    }
}
