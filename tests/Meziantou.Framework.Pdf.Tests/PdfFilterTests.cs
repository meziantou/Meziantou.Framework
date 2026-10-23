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

    [Fact]
    public void UnknownFilterThrows()
    {
        var stream = new PdfStream(new PdfDictionary { [PdfNames.Filter] = PdfName.Create("Unknown") });
        stream.SetEncodedData("data"u8);

        Assert.Throws<InvalidPdfContentException>(stream.GetDecodedData);
    }
}
