using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class BarCode128Tests
{
    private const string NumericData = "123456";
    private const string AlphanumericData = "SKU-123456";
    private const string MixedData = "AB123456CD78";

    [Fact]
    public void Create_NumericPayload_UsesCompactEncoding()
    {
        var barcode = Barcode.CreateCode128(NumericData);

        Assert.Equal(BarcodeType.Code128, barcode.Type);
        Assert.Equal(1, barcode.Height);
        Assert.Equal(68, barcode.Width);
        Assert.True(barcode[0, 0]);
        Assert.True(barcode[0, barcode.Width - 1]);
    }

    [Fact]
    public void Create_Deterministic_SameInputProducesSameOutput()
    {
        var barcode1 = Barcode.CreateCode128("AB12CD34");
        var barcode2 = Barcode.CreateCode128("AB12CD34");

        Assert.Equal(barcode1.Width, barcode2.Width);
        for (var col = 0; col < barcode1.Width; col++)
        {
            Assert.Equal(barcode1[0, col], barcode2[0, col]);
        }
    }

    [Fact]
    public void Create_UnsupportedCharacter_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateCode128("Café"));
    }

    [Fact]
    public void Create_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Barcode.CreateCode128(data: null!));
    }

    [Fact]
    public void Create_EmptyData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateCode128(""));
    }

    [Fact]
    public void ToSvg_Code128_DefaultOptions_Snapshot()
    {
        var barcode = Barcode.CreateCode128(NumericData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Code128_CustomColors_Snapshot()
    {
        var barcode = Barcode.CreateCode128(NumericData);
        var svg = barcode.ToSvg(new BarcodeSvgOptions
        {
            DarkColor = Color.FromRgb(0xff, 0x00, 0x00),
            LightColor = Color.FromRgb(0x00, 0xff, 0x00),
            ModuleWidth = 1,
            ModuleHeight = 3,
            QuietZoneModules = 1,
        });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Code128_TransparentColors_Snapshot()
    {
        var barcode = Barcode.CreateCode128(NumericData);
        var svg = barcode.ToSvg(new BarcodeSvgOptions
        {
            DarkColor = Color.Transparent,
            LightColor = Color.FromArgb(0x80, 0x00, 0xff, 0x00),
            ModuleWidth = 1,
            ModuleHeight = 3,
            QuietZoneModules = 1,
        });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Code128_AlphanumericPayload_Snapshot()
    {
        var barcode = Barcode.CreateCode128(AlphanumericData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Code128_MixedPayload_Snapshot()
    {
        var barcode = Barcode.CreateCode128(MixedData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_DefaultOptions_UsesHorizontalQuietZoneOnly()
    {
        var barcode = Barcode.CreateCode128(AlphanumericData);
        var svg = barcode.ToSvg();

        var document = XDocument.Parse(svg);
        var viewBox = document.Root!.Attribute("viewBox")!.Value.Split(' ');
        var width = int.Parse(viewBox[2], CultureInfo.InvariantCulture);
        var height = int.Parse(viewBox[3], CultureInfo.InvariantCulture);

        Assert.Equal((barcode.Width + (2 * 10)) * 2, width);
        Assert.Equal(barcode.Height * 80, height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToSvg_InvalidModuleWidth_ThrowsArgumentOutOfRangeException(int moduleWidth)
    {
        var barcode = Barcode.CreateCode128(NumericData);

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToSvg(new BarcodeSvgOptions { ModuleWidth = moduleWidth }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToSvg_InvalidModuleHeight_ThrowsArgumentOutOfRangeException(int moduleHeight)
    {
        var barcode = Barcode.CreateCode128(NumericData);

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToSvg(new BarcodeSvgOptions { ModuleHeight = moduleHeight }));
    }

    [Fact]
    public void ToPng_Code128_DefaultOptions_Snapshot()
    {
        var barcode = Barcode.CreateCode128(NumericData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_Code128_AlphanumericPayload_Snapshot()
    {
        var barcode = Barcode.CreateCode128(AlphanumericData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_Code128_MixedPayload_Snapshot()
    {
        var barcode = Barcode.CreateCode128(MixedData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_DefaultOptions_UsesHorizontalQuietZoneOnly()
    {
        var barcode = Barcode.CreateCode128(AlphanumericData);
        var png = barcode.ToPng();

        var (width, height) = GetPngSize(png);
        Assert.Equal((barcode.Width + (2 * 10)) * 2, width);
        Assert.Equal(barcode.Height * 80, height);
    }

    [Fact]
    public void ToPng_Code128_CustomColors_Snapshot()
    {
        var barcode = Barcode.CreateCode128(NumericData);
        var png = barcode.ToPng(new BarcodePngOptions
        {
            ModuleWidth = 1,
            ModuleHeight = 1,
            QuietZoneModules = 0,
            DarkColor = Color.FromRgb(0xff, 0x00, 0x00),
            LightColor = Color.FromRgb(0x00, 0xff, 0x00),
        });

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToPng_InvalidModuleWidth_ThrowsArgumentOutOfRangeException(int moduleWidth)
    {
        var barcode = Barcode.CreateCode128(NumericData);

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToPng(new BarcodePngOptions { ModuleWidth = moduleWidth }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToPng_InvalidModuleHeight_ThrowsArgumentOutOfRangeException(int moduleHeight)
    {
        var barcode = Barcode.CreateCode128(NumericData);

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToPng(new BarcodePngOptions { ModuleHeight = moduleHeight }));
    }

    private static (int Width, int Height) GetPngSize(byte[] data)
    {
        var width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4));

        return (width, height);
    }

    // ───── Module patterns ─────
    //
    // Produced from this encoder's output and confirmed to decode back to the payload with an
    // external reader (ZXing) while the tests were written.

    [Theory]
    [InlineData("SKU-123456", "110100100001101110100010110001110110111011101001101110010111011110101100111001000101100011100010110111011010001100011101011")]
    [InlineData("12345678", "1101001110010110011100100010110001110001011011000010100100011101101100011101011")]
    public void CreateCode128_ProducesTheExpectedModules(string data, string expected)
    {
        Assert.Equal(expected, BarcodeModulePattern.Render(Barcode.CreateCode128(data)));
    }

    [Theory]
    [InlineData('\u007f', 7000)]
    [InlineData('a', 10000)]
    [InlineData('z', 50000)]
    public void CreateCode128_LongInput_ComputesTheChecksumWithoutOverflow(char character, int length)
    {
        var barcode = Barcode.CreateCode128(new string(character, length));

        var codewords = Code128Symbols.Decode(barcode);
        Assert.HasCount(length + 3, codewords);
        Assert.Equal(104, codewords[0]);
        Assert.All(codewords[1..^2], codeword => Assert.Equal(character - ' ', codeword));
        Code128Symbols.AssertValidChecksum(codewords);
    }

    [Theory]
    [InlineData("SKU-123456")]
    [InlineData("12345678")]
    [InlineData("AB123456CD78")]
    public void CreateCode128_ChecksumMatchesIndependentCalculation(string data)
    {
        Code128Symbols.AssertValidChecksum(Code128Symbols.Decode(Barcode.CreateCode128(data)));
    }

    [Fact]
    public void CreateCode128_ControlCharacters()
    {
        #region Barcode_Code128ControlCharacters
        // A Tab between the fields and an Enter at the end, for a scanner that types the data into a form
        var barcode = Barcode.CreateCode128("SKU-123456\tQTY-5\r");
        Assert.Equal("SKU-123456\tQTY-5\r", barcode.Text);
        #endregion
    }

    [Fact]
    public void CreateCode128_FullAsciiRange_RoundTrips()
    {
        var data = string.Concat(Enumerable.Range(0, 128).Select(value => (char)value));
        var barcode = Barcode.CreateCode128(data);

        var codewords = Code128Symbols.Decode(barcode);
        Code128Symbols.AssertValidChecksum(codewords);
        Assert.Equal(data, Code128Symbols.Interpret(codewords));
        Assert.Equal(data, barcode.Text);
    }

    [Theory]
    [InlineData("A\nB", new[] { 103, 33, 74, 34 })] // control character before any lowercase letter: start A
    [InlineData("\u0000", new[] { 103, 64 })]
    [InlineData("abc\tdef", new[] { 104, 65, 66, 67, 98, 73, 68, 69, 70 })] // a lone control character between lowercase letters: SHIFT
    [InlineData("ab\t\ncd", new[] { 104, 65, 66, 101, 73, 74, 100, 67, 68 })] // two control characters: switch to A and back
    [InlineData("\tA\na\rB", new[] { 103, 73, 33, 74, 98, 65, 77, 34 })] // a lone lowercase letter between control characters: SHIFT
    [InlineData("\t123456", new[] { 103, 73, 99, 12, 34, 56 })]
    [InlineData("\t12345a", new[] { 103, 73, 17, 99, 23, 45, 100, 65 })]
    [InlineData("1234\t", new[] { 105, 12, 34, 101, 73 })]
    [InlineData("1234a\t", new[] { 105, 12, 34, 100, 65, 101, 73 })]
    [InlineData("a\t1234\tb", new[] { 104, 65, 101, 73, 99, 12, 34, 101, 73, 100, 66 })]
    public void CreateCode128_ControlCharacters_UseCodeSetA(string data, int[] expected)
    {
        var codewords = Code128Symbols.Decode(Barcode.CreateCode128(data));

        Assert.Equal(expected, codewords[..^2]);
        Code128Symbols.AssertValidChecksum(codewords);
        Assert.Equal(data, Code128Symbols.Interpret(codewords));
    }

    [Theory]
    [InlineData("Caf\u00e9")]
    [InlineData("\u0080")]
    public void CreateCode128_NonAsciiCharacter_ThrowsArgumentException(string data)
    {
        Assert.Throws<ArgumentException>(nameof(data), () => Barcode.CreateCode128(data));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void CreateCode128_RandomAscii_RoundTrips()
    {
        var random = new Random(4204);
        for (var i = 0; i < 2000; i++)
        {
            // Runs from a few classes, so that the encoder switches between the code sets
            string[] classes = ["0123456789", "\u0000\u0001\t\n\r\u001b\u001f", "ABCXYZ !@_^", "abcxyz`{|}~\u007f"];
            var data = new StringBuilder();
            var length = random.Next(1, 40);
            while (data.Length < length)
            {
                var characters = classes[random.Next(classes.Length)];
                var runLength = random.Next(1, 7);
                for (var j = 0; j < runLength; j++)
                {
                    data.Append(characters[random.Next(characters.Length)]);
                }
            }

            var codewords = Code128Symbols.Decode(Barcode.CreateCode128(data.ToString()));
            Code128Symbols.AssertValidChecksum(codewords);
            Assert.Equal(data.ToString(), Code128Symbols.Interpret(codewords));
        }
    }
}
