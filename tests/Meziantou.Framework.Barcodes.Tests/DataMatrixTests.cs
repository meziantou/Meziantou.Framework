using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.Barcodes.Internal.DataMatrix;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class DataMatrixTests
{
    private static readonly (int Rows, int Columns, int DataCodewords)[] Sizes =
    [
        (10, 10, 3), (12, 12, 5), (14, 14, 8), (16, 16, 12), (18, 18, 18), (20, 20, 22), (22, 22, 30), (24, 24, 36), (26, 26, 44),
        (32, 32, 62), (36, 36, 86), (40, 40, 114), (44, 44, 144), (48, 48, 174), (52, 52, 204), (64, 64, 280), (72, 72, 368),
        (80, 80, 456), (88, 88, 576), (96, 96, 696), (104, 104, 816), (120, 120, 1050), (132, 132, 1304), (144, 144, 1558),
        (8, 18, 5), (8, 32, 10), (12, 26, 16), (12, 36, 22), (16, 36, 32), (16, 48, 49),
    ];

    public static TheoryData<int, int, int> AllSizes()
    {
        var data = new TheoryData<int, int, int>();
        foreach (var (rows, columns, dataCodewords) in Sizes)
        {
            data.Add(rows, columns, dataCodewords);
        }

        return data;
    }

    [Fact]
    public void Documentation_CreateDataMatrix()
    {
        #region Barcode2D_DataMatrix
        // The smallest square symbol that holds the data
        var dataMatrix = Barcode2D.CreateDataMatrix("https://www.meziantou.net");
        Assert.Equal((Barcode2DType.DataMatrix, 22, 22), (dataMatrix.Type, dataMatrix.Width, dataMatrix.Height));

        // A rectangular symbol, for a narrow space
        var rectangle = Barcode2D.CreateDataMatrix("ABC-12345", new DataMatrixOptions { Shape = DataMatrixShape.Rectangle });
        Assert.Equal((32, 8), (rectangle.Width, rectangle.Height));

        // A symbol of an exact size: the data is padded
        var fixedSize = Barcode2D.CreateDataMatrix("42", new DataMatrixOptions { Height = 24, Width = 24 });
        Assert.Equal((24, 24), (fixedSize.Width, fixedSize.Height));

        // Binary data
        var binary = Barcode2D.CreateDataMatrix([0x00, 0xFF, 0x10, 0x80]);
        Assert.Equal((14, 14), (binary.Width, binary.Height));
        #endregion
    }

    [Fact]
    public void Documentation_CreateGs1DataMatrix()
    {
        #region Barcode2D_Gs1DataMatrix
        // GTIN, expiration date, batch and serial number: a FNC1 separates the batch, of variable length, from the serial number
        var gs1 = Barcode2D.CreateGs1DataMatrix(
            new Gs1Element("01", "09521234543213"),
            new Gs1Element("17", "250101"),
            new Gs1Element("10", "ABC123"),
            new Gs1Element("21", "XYZ"));
        Assert.Equal((Barcode2DType.DataMatrix, 22, 22), (gs1.Type, gs1.Width, gs1.Height));
        #endregion
    }

    [Fact]
    public void Documentation_RenderBarcode2D()
    {
        #region Barcode2D_Render
        var barcode = Barcode2D.CreateDataMatrix("https://www.meziantou.net");

        // SVG: a viewBox of (22 + 2 × 2) × 4 units, a rectangle of the light color and a path of the dark modules
        var svg = barcode.ToSvg(new Barcode2DSvgOptions { ModuleSize = 4, QuietZoneModules = 2 });
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 104 104\">", svg);

        // PNG: the bytes of a file, to save with File.WriteAllBytes("datamatrix.png", png)
        var png = barcode.ToPng(new Barcode2DPngOptions
        {
            ModuleSize = 6,
            QuietZoneModules = 2,
            DarkColor = Color.FromRgb(0x1f, 0x29, 0x37),
            LightColor = Color.White,
        });
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        #endregion
    }

    [Fact]
    public void DocumentationImages_Barcode2D()
    {
        var options = new Barcode2DPngOptions { ModuleSize = 6, QuietZoneModules = 2 };
        DocumentationImages.AssertPng("DataMatrix", Barcode2D.CreateDataMatrix("https://www.meziantou.net").ToPng(options));
        DocumentationImages.AssertPng("DataMatrixRectangle", Barcode2D.CreateDataMatrix("ABC-12345", new DataMatrixOptions { Shape = DataMatrixShape.Rectangle }).ToPng(options));
        DocumentationImages.AssertPng("Aztec", Barcode2D.CreateAztec("https://www.meziantou.net").ToPng(options));
        DocumentationImages.AssertPng("Pdf417", Barcode2D.CreatePdf417("https://www.meziantou.net").ToPng(new Barcode2DPngOptions { ModuleSize = 2, QuietZoneModules = 2 }));
    }

    [Fact]
    public void CreateDataMatrix_Hello_MatchesZint()
    {
        var code = Barcode2D.CreateDataMatrix("Hello");

        InlineSnapshot.Validate(RenderAsText(code), """
            #.#.#.#.#.#.
            #.##.##.#.##
            ##...##.#...
            ###.##.##..#
            ####...#....
            ####.#######
            #..##.....#.
            ##..####.###
            ####.....##.
            #.#.###..#.#
            ##.#.##.#.#.
            ############
            """);
    }

    [Fact]
    public void CreateDataMatrix_Digits_MatchesIsoExample()
    {
        // ISO/IEC 16022:2006, Annex O: "123456" is 142 164 186, followed by the error correction codewords 114 25 5 88 102
        Assert.Equal([142, 164, 186], DataMatrixHighLevelEncoder.Encode([.. "123456".Select(c => (int)c)], gs1: false, eci: null, capacity: 3));

        Span<int> errorCorrection = stackalloc int[5];
        BinaryGaloisField.Field256.ComputeErrorCorrection([142, 164, 186], errorCorrection);
        Assert.Equal([114, 25, 5, 88, 102], errorCorrection.ToArray());

        var code = Barcode2D.CreateDataMatrix("123456");
        InlineSnapshot.Validate(RenderAsText(code), """
            #.#.#.#.#.
            ##..#.##.#
            ##.....#..
            ##...###.#
            ##....#...
            #.....####
            ###.##....
            ####.##..#
            #..###.#..
            ##########
            """);
    }

    [Theory]
    [InlineData("A", new[] { 66 })]
    [InlineData("12", new[] { 142 })]
    [InlineData("1A", new[] { 50, 66 })]
    [InlineData("é", new[] { 235, 106 })]
    [InlineData("AIMAIMAIM", new[] { 230, 91, 11, 91, 11, 91, 11 })] // C40, ISO/IEC 16022 example
    [InlineData("aimaimaim", new[] { 239, 91, 11, 91, 11, 91, 11 })] // Text
    [InlineData(".A.C1.3.DATA.123DATA.123DATA", new[] { 240, 184, 27, 131, 198, 236, 238, 16, 21, 1, 187, 28, 179, 16, 21, 1, 187, 28, 179, 16, 21, 1 })] // EDIFACT
    public void HighLevelEncoder_EncodesWithTheSchemes(string data, int[] expected)
    {
        var codewords = DataMatrixHighLevelEncoder.Encode([.. data.Select(c => (int)c)], gs1: false, eci: null, capacity: int.MaxValue);
        Assert.Equal(expected, codewords);
    }

    [Fact]
    public void HighLevelEncoder_X12_EndsInAsciiWithoutUnlatchWhenOneCodewordIsLeft()
    {
        // 4 triplets, then the last character in ASCII: the decoder reads the last codeword of the symbol in ASCII
        var codewords = DataMatrixHighLevelEncoder.Encode([.. "ABC>ABC123>AB".Select(c => (int)c)], gs1: false, eci: null, capacity: 10);
        Assert.Equal([238, 89, 233, 14, 192, 100, 207, 44, 31, 67], codewords);

        // With more space, an unlatch precedes the ASCII characters
        codewords = DataMatrixHighLevelEncoder.Encode([.. "ABC>ABC123>AB".Select(c => (int)c)], gs1: false, eci: null, capacity: 12);
        Assert.Equal([238, 89, 233, 14, 192, 100, 207, 44, 31, 254, 67, 129], codewords);
    }

    [Fact]
    public void HighLevelEncoder_Base256_RandomizesTheField()
    {
        // Latch, length 4, and the 4 bytes, randomized by the 255-state algorithm
        var codewords = DataMatrixHighLevelEncoder.Encode([0xAB, 0xE4, 0xF6, 0xFC, 0xE9, 0xBB], gs1: false, eci: null, capacity: int.MaxValue);
        Assert.NotNull(codewords);
        Assert.Equal(231, codewords[0]);
        Assert.Equal([6, 0xAB, 0xE4, 0xF6, 0xFC, 0xE9, 0xBB], codewords.Skip(1).Select((value, index) => Derandomize255(value, index + 2)).ToArray());
    }

    [Fact]
    public void HighLevelEncoder_PadsWithThe253StateAlgorithm()
    {
        // The first pad codeword is 129, the next ones are randomized by their position
        var codewords = DataMatrixHighLevelEncoder.Encode([(int)'A'], gs1: false, eci: null, capacity: 5);
        Assert.Equal([66, 129, 70, 220, 115], codewords);
    }

    [Fact]
    public void HighLevelEncoder_Gs1_StartsWithFnc1()
    {
        var codewords = DataMatrixHighLevelEncoder.Encode([.. "10AB".Select(c => (int)c), DataMatrixHighLevelEncoder.Fnc1, .. "2112".Select(c => (int)c)], gs1: true, eci: null, capacity: int.MaxValue);
        Assert.Equal([232, 140, 66, 67, 232, 151, 142], codewords);
    }

    [Fact]
    public void HighLevelEncoder_Eci_IsTheFirstCodeword()
    {
        Assert.Equal([241, 27, 66], DataMatrixHighLevelEncoder.Encode([(int)'A'], gs1: false, eci: 26, capacity: int.MaxValue));
        Assert.Equal([241, 128, 1, 66], DataMatrixHighLevelEncoder.Encode([(int)'A'], gs1: false, eci: 127, capacity: int.MaxValue));
        Assert.Equal([241, 192, 1, 1, 66], DataMatrixHighLevelEncoder.Encode([(int)'A'], gs1: false, eci: 16383, capacity: int.MaxValue));
    }

    [Fact]
    public void HighLevelEncoder_DoesNotFit_ReturnsNull()
    {
        Assert.Null(DataMatrixHighLevelEncoder.Encode([.. "ABCD".Select(c => (int)c)], gs1: false, eci: null, capacity: 3));
    }

    [Fact]
    public void CreateDataMatrix_NonAsciiText_IsUtf8WithEci()
    {
        var ascii = Barcode2D.CreateDataMatrix("cafe");
        var utf8 = Barcode2D.CreateDataMatrix("café");
        Assert.Equal(12, ascii.Width);
        Assert.Equal(16, utf8.Width);
    }

    [Theory]
    [MemberData(nameof(AllSizes))]
    public void CreateDataMatrix_ExactSize_HasTheFinderPatterns(int rows, int columns, int dataCodewords)
    {
        // Digits pairs fill the symbol exactly, and the symbol of the next size holds one more
        var data = string.Concat(Enumerable.Repeat("12", dataCodewords));
        var code = Barcode2D.CreateDataMatrix(data, new DataMatrixOptions { Height = rows, Width = columns });

        Assert.Equal((rows, columns), (code.Height, code.Width));
        for (var column = 0; column < columns; column++)
        {
            Assert.True(code[rows - 1, column]);
            Assert.Equal(column % 2 == 0, code[0, column]);
        }

        for (var row = 0; row < rows; row++)
        {
            Assert.True(code[row, 0]);
            Assert.Equal(row % 2 == 1, code[row, columns - 1]);
        }

        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreateDataMatrix(data + "1", new DataMatrixOptions { Height = rows, Width = columns }));
    }

    [Fact]
    public void CreateDataMatrix_Shapes()
    {
        var data = "ABCDEFGH";
        Assert.Equal((14, 14), Size(Barcode2D.CreateDataMatrix(data)));
        Assert.Equal((8, 32), Size(Barcode2D.CreateDataMatrix(data, new DataMatrixOptions { Shape = DataMatrixShape.Rectangle })));
        Assert.Equal((14, 14), Size(Barcode2D.CreateDataMatrix(data, new DataMatrixOptions { Shape = DataMatrixShape.Any })));
        Assert.Equal((8, 18), Size(Barcode2D.CreateDataMatrix("ABCDE", new DataMatrixOptions { Shape = DataMatrixShape.Rectangle })));

        // A height alone allows every width, rectangular included
        Assert.Equal((12, 26), Size(Barcode2D.CreateDataMatrix("ABCDEFGHIJKLMNOPQR", new DataMatrixOptions { Height = 12 })));
        Assert.Equal((16, 16), Size(Barcode2D.CreateDataMatrix("ABC", new DataMatrixOptions { Width = 16 })));

        static (int Height, int Width) Size(Barcode2D code) => (code.Height, code.Width);
    }

    [Fact]
    public void CreateDataMatrix_LargestSymbol()
    {
        var code = Barcode2D.CreateDataMatrix(new string('7', 3116));
        Assert.Equal((144, 144), (code.Height, code.Width));
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreateDataMatrix(new string('7', 3117)));

        // Base 256: a latch and the bytes, the length field (0) meaning that they end with the symbol
        var binary = Barcode2D.CreateDataMatrix(Enumerable.Repeat((byte)0xFF, 1556).ToArray());
        Assert.Equal((144, 144), (binary.Height, binary.Width));
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreateDataMatrix(Enumerable.Repeat((byte)0xFF, 1557).ToArray()));
    }

    [Fact]
    public void CreateDataMatrix_InvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => Barcode2D.CreateDataMatrix((string)null!));
        Assert.Throws<ArgumentException>(() => Barcode2D.CreateDataMatrix(""));
        Assert.Throws<ArgumentException>(() => Barcode2D.CreateDataMatrix(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => Barcode2D.CreateDataMatrix("A", new DataMatrixOptions { Height = 8, Width = 10 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataMatrixOptions { Height = 11 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataMatrixOptions { Width = 17 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataMatrixOptions { Shape = (DataMatrixShape)3 });
        Assert.Throws<ArgumentException>(() => Barcode2D.CreateGs1DataMatrix());
        Assert.Throws<ArgumentException>(() => Barcode2D.CreateGs1DataMatrix(new Gs1Element("10", "A"), null!));
    }

    [Fact]
    public void CreateGs1DataMatrix_SeparatesTheVariableLengthElements()
    {
        var withSeparator = Barcode2D.CreateGs1DataMatrix(new Gs1Element("10", "ABC"), new Gs1Element("21", "XY"));
        var withoutSeparator = Barcode2D.CreateGs1DataMatrix(new Gs1Element("01", "09521234543213"), new Gs1Element("10", "ABC"));
        Assert.Equal(Barcode2DType.DataMatrix, withSeparator.Type);
        Assert.Equal(Barcode2DType.DataMatrix, withoutSeparator.Type);
    }

    [Fact]
    public void ToSvg_MergesTheRunsOfARow()
    {
        var code = Barcode2D.CreateDataMatrix("123456");
        var svg = code.ToSvg(new Barcode2DSvgOptions { ModuleSize = 1, QuietZoneModules = 1 });
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 12 12\"><rect width=\"12\" height=\"12\" fill=\"#ffffff\"/><path fill=\"#000000\" d=\"M1 1h1v1h-1z", svg);
        Assert.EndsWith("M1 10h10v1h-10z\"/></svg>", svg);
    }

    [Fact]
    public void ToPng_HasTheSizeOfTheSymbolAndQuietZone()
    {
        var code = Barcode2D.CreateDataMatrix("123456");
        var png = code.ToPng(new Barcode2DPngOptions { ModuleSize = 3, QuietZoneModules = 2 });
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        Assert.Equal(42, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(42, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    private static int Derandomize255(int value, int position)
    {
        var result = value - (((149 * position) % 255) + 1);
        return result >= 0 ? result : result + 256;
    }

    internal static string RenderAsText(Barcode2D code)
    {
        var sb = new StringBuilder();
        for (var row = 0; row < code.Height; row++)
        {
            if (row > 0)
            {
                sb.AppendLine();
            }

            for (var column = 0; column < code.Width; column++)
            {
                sb.Append(code[row, column] ? '#' : '.');
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void Documentation_Decode()
    {
        #region Barcode2DDecoder_Decode
        var dataMatrix = Barcode2D.CreateDataMatrix("https://www.meziantou.net");

        var result = Barcode2DDecoder.Decode(dataMatrix);

        Assert.Equal("https://www.meziantou.net", result.Text);
        Assert.Equal(Barcode2DType.DataMatrix, result.Type);
        #endregion
    }

    [Fact]
    public void Documentation_DecodeGs1()
    {
        #region Barcode2DDecoder_DecodeGs1
        var gs1 = Barcode2D.CreateGs1DataMatrix(
            new Gs1Element("01", "09521234543213"),
            new Gs1Element("10", "ABC123"),
            new Gs1Element("21", "XYZ"));

        var result = Barcode2DDecoder.Decode(gs1);

        // The human-readable interpretation, and the bytes with a GS character for each FNC1 separator
        Assert.True(result.IsGs1);
        Assert.Equal("(01)09521234543213(10)ABC123(21)XYZ", result.Text);
        Assert.Equal("010952123454321310ABC123\u001D21XYZ"u8.ToArray(), result.Bytes.ToArray());
        #endregion
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("HELLO WORLD 0123456789")]
    [InlineData("hello world, lowercase text")]
    [InlineData("*ABC>123*\r*XYZ>456*\r")]
    [InlineData("@ABC^_@DEF^_@GHI^_")]
    [InlineData("0123456789012345678901234567890123456789")]
    [InlineData("Mixed: ABC abc 123 *>\r @^_ \u007f\u0001")]
    [InlineData("caf\u00E9 cr\u00E8me")]
    [InlineData("Ελληνικά 漢字 😀")]
    [InlineData("A")]
    public void Decode_Text(string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        foreach (var shape in Enum.GetValues<DataMatrixShape>())
        {
            Barcode2D barcode;
            try
            {
                barcode = Barcode2D.CreateDataMatrix(text, new DataMatrixOptions { Shape = shape });
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var result = Barcode2DDecoder.Decode(barcode);
            Assert.Equal((text, false, 0), (result.Text, result.IsGs1, result.CorrectedErrorCount));
            Assert.Equal(Ascii.IsValid(text) ? null : 26, result.EciAssignmentNumber);
        }
    }

    [Fact]
    public void Decode_Binary()
    {
        // Base 256 fields of 1 to 249 bytes have a 1-codeword length, longer ones a 2-codeword length
        foreach (var length in (int[])[1, 2, 100, 249, 250, 251, 600, 1555])
        {
            var bytes = Enumerable.Range(0, length).Select(i => (byte)((i * 37) + 128)).ToArray();
            var result = Barcode2DDecoder.Decode(Barcode2D.CreateDataMatrix(bytes));
            Assert.Equal(bytes, result.Bytes.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(AllSizes))]
    public void Decode_AllSizes(int rows, int columns, int dataCodewords)
    {
        var options = new DataMatrixOptions { Height = rows, Width = columns };

        // The digits fill the symbol, two per codeword
        var digits = string.Concat(Enumerable.Range(0, dataCodewords * 2).Select(i => (char)('0' + (i % 10))));
        Assert.Equal(digits, Barcode2DDecoder.Decode(Barcode2D.CreateDataMatrix(digits, options)).Text);
        Assert.Equal("42", Barcode2DDecoder.Decode(Barcode2D.CreateDataMatrix("42", options)).Text);
    }

    [Fact]
    public void Decode_AnyOrientation()
    {
        foreach (var barcode in new[] { Barcode2D.CreateDataMatrix("Orientation test 123"), Barcode2D.CreateDataMatrix("ABC-12345", new DataMatrixOptions { Shape = DataMatrixShape.Rectangle }) })
        {
            for (var transform = 0; transform < MatrixTransforms.Count; transform++)
            {
                var modules = MatrixTransforms.Apply(GetModules(barcode), transform);
                Assert.Equal(barcode.Height == barcode.Width ? "Orientation test 123" : "ABC-12345", Barcode2DDecoder.Decode(modules, Barcode2DType.DataMatrix).Text);
            }
        }
    }

    [Fact]
    public void Decode_CorrectsErrors()
    {
        var barcode = Barcode2D.CreateDataMatrix("Error correction of a Data Matrix symbol", new DataMatrixOptions { Height = 32, Width = 32 });
        var modules = GetModules(barcode);

        // Damage a few modules of the data region
        for (var i = 0; i < 6; i++)
        {
            modules[5 + i, 5 + i] = !modules[5 + i, 5 + i];
        }

        var result = Barcode2DDecoder.Decode(modules, Barcode2DType.DataMatrix);
        Assert.Equal("Error correction of a Data Matrix symbol", result.Text);
        Assert.True(result.CorrectedErrorCount > 0);

        // Damage half of the symbol: too many errors
        for (var row = 1; row < 16; row++)
        {
            for (var column = 1; column < 31; column++)
            {
                modules[row, column] = !modules[row, column];
            }
        }

        Assert.False(Barcode2DDecoder.TryDecode(modules, Barcode2DType.DataMatrix, out _));
        Assert.Throws<FormatException>(() => Barcode2DDecoder.Decode(modules, Barcode2DType.DataMatrix));
    }

    [Fact]
    public void Decode_LargestSymbolWithErrorsInEveryBlock()
    {
        var bytes = Enumerable.Range(0, 1500).Select(i => (byte)i).ToArray();
        var barcode = Barcode2D.CreateDataMatrix(bytes);
        var modules = GetModules(barcode);
        for (var row = 1; row < barcode.Height - 1; row += 7)
        {
            modules[row, 40] = !modules[row, 40];
        }

        var result = Barcode2DDecoder.Decode(modules, Barcode2DType.DataMatrix);
        Assert.Equal(bytes, result.Bytes.ToArray());
        Assert.True(result.CorrectedErrorCount > 0);
    }

    [Fact]
    public void Decode_UnlatchAsTheLastCodeword()
    {
        // zint ends the Text segment of this symbol with an unlatch, although a single codeword is left
        string[] rows = ["AAAA8", "BE76C", "C4048", "E156C", "CB290", "DE164", "C7DF0", "E4E8C", "A0D58", "DD694", "F6E60", "A0E7C", "A8600", "D6894", "D14C0", "F92DC", "F6B90", "FFFFC"];
        var modules = new bool[18, 18];
        for (var row = 0; row < 18; row++)
        {
            for (var column = 0; column < 18; column++)
            {
                modules[row, column] = (Convert.ToInt32(rows[row][column / 4].ToString(), 16) & (8 >> (column % 4))) != 0;
            }
        }

        Assert.Equal("hello world, lowercase", Barcode2DDecoder.Decode(modules, Barcode2DType.DataMatrix).Text);
    }

    [Fact]
    public void Decode_InvalidArguments()
    {
        Assert.Throws<ArgumentNullException>("barcode", () => Barcode2DDecoder.Decode(null!));
        Assert.Throws<ArgumentNullException>("modules", () => Barcode2DDecoder.Decode(null!, Barcode2DType.DataMatrix));
        Assert.Throws<ArgumentOutOfRangeException>("type", () => Barcode2DDecoder.Decode(new bool[10, 10], (Barcode2DType)42));
        Assert.Throws<FormatException>(() => Barcode2DDecoder.Decode(new bool[11, 11], Barcode2DType.DataMatrix));
        Assert.Throws<FormatException>(() => Barcode2DDecoder.Decode(new bool[10, 10], Barcode2DType.DataMatrix));
    }

    internal static bool[,] GetModules(Barcode2D barcode)
    {
        var modules = new bool[barcode.Height, barcode.Width];
        for (var row = 0; row < barcode.Height; row++)
        {
            for (var column = 0; column < barcode.Width; column++)
            {
                modules[row, column] = barcode[row, column];
            }
        }

        return modules;
    }
}
