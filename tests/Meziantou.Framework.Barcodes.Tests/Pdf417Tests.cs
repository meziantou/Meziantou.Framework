using System.Globalization;
using System.Text;
using Meziantou.Framework.Barcodes.Internal.Pdf417;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class Pdf417Tests
{
    [Fact]
    public void CreatePdf417_Example()
    {
        #region Barcode2D_Pdf417
        var options = new Pdf417Options
        {
            Columns = 4,       // 4 data columns: 17 × (4 + 4) + 1 = 137 modules wide
            SecurityLevel = 3, // 16 error correction codewords
            RowHeight = 3,     // each row of the symbol is 3 modules high
        };
        var pdf417 = Barcode2D.CreatePdf417("Hello, PDF417!", options);

        Assert.Equal(Barcode2DType.Pdf417, pdf417.Type);
        Assert.Equal(137, pdf417.Width);
        Assert.Equal(21, pdf417.Height); // 7 rows of 3 modules
        string svg = pdf417.ToSvg();
        #endregion

        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 3)]
    [InlineData(5, 3)]
    [InlineData(17, 7)]
    [InlineData(30, 20)]
    public void CreatePdf417_Size(int columns, int rowHeight)
    {
        var pdf417 = Barcode2D.CreatePdf417("SIZE TEST 0123456789", new Pdf417Options { Columns = columns, RowHeight = rowHeight });

        Assert.Equal((17 * (columns + 4)) + 1, pdf417.Width);
        Assert.Equal(0, pdf417.Height % rowHeight);
        Assert.InRange(pdf417.Height / rowHeight, 3, 90);
        for (var row = 0; row < pdf417.Height; row++)
        {
            AssertStartAndStopPatterns(pdf417, row);
            for (var column = 0; column < pdf417.Width; column++)
            {
                Assert.Equal(pdf417[row - (row % rowHeight), column], pdf417[row, column]);
            }
        }
    }

    [Fact]
    public void CreatePdf417_DefaultRowHeight()
    {
        var withDefaults = Barcode2D.CreatePdf417("ROW HEIGHT", new Pdf417Options { Columns = 2 });
        var withOneModule = Barcode2D.CreatePdf417("ROW HEIGHT", new Pdf417Options { Columns = 2, RowHeight = 1 });

        Assert.Equal(withOneModule.Height * 3, withDefaults.Height);
        for (var row = 0; row < withDefaults.Height; row++)
        {
            for (var column = 0; column < withDefaults.Width; column++)
            {
                Assert.Equal(withOneModule[row / 3, column], withDefaults[row, column]);
            }
        }
    }

    [Fact]
    public void CreatePdf417_MinimumRows()
    {
        var pdf417 = Barcode2D.CreatePdf417("A", new Pdf417Options { Columns = 30, SecurityLevel = 0, RowHeight = 1 });

        Assert.Equal(3, pdf417.Height);
        Assert.Equal(579, pdf417.Width);
    }

    [Fact]
    public void CreatePdf417_MatchesZint()
    {
        // zint -b 55 --cols=2 --secure=2 --dump -d "Hello"
        var pdf417 = Barcode2D.CreatePdf417("Hello", new Pdf417Options { Columns = 2, SecurityLevel = 2, RowHeight = 1 });

        string[] expected =
        [
            "FF 54 7A BC 3D 4F 1E C9 CF 57 87 F4 52",
            "FF 54 7A 84 3D 1B 16 4F CF D5 C7 F4 52",
            "FF 54 75 7E 3D 3D 18 F2 CA 87 87 F4 52",
            "FF 54 57 9E 33 19 91 44 0A F3 C7 F4 52",
            "FF 54 6B 82 3F 7B 5E 46 CE B9 87 F4 52",
            "FF 54 7A F4 23 85 9B 05 EF 5F 67 F4 52",
        ];
        Assert.Equal(expected.Length, pdf417.Height);
        for (var row = 0; row < pdf417.Height; row++)
        {
            var bytes = expected[row].Split(' ').Select(value => byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            for (var column = 0; column < pdf417.Width; column++)
            {
                Assert.Equal((bytes[column / 8] & (0x80 >> (column % 8))) != 0, pdf417[row, column]);
            }
        }
    }

    [Fact]
    public void CreatePdf417_Snapshot()
    {
        var pdf417 = Barcode2D.CreatePdf417("PDF417", new Pdf417Options { Columns = 1, SecurityLevel = 0, RowHeight = 1 });

        InlineSnapshot.Validate(RenderAsText(pdf417), """
            ########.#.#.#...#####.#.#.#####..#####.#.#..#####.###.#.#.###......#######.#...#.#..#
            ########.#.#.#...#####.#.#.##.....##.##.....####.#.####.#.#..#......#######.#...#.#..#
            ########.#.#.#...##.#.#.#####.....#.##.#.....###...##.#.#.#####.....#######.#...#.#..#
            ########.#.#.#...##.#.####..#####.#.#....##.....##.##.#.####.#####..#######.#...#.#..#
            ########.#.#.#...##.#.###..#......####..#.###..###.####.#.###..###..#######.#...#.#..#
            ########.#.#.#...#####.#.###....#.##...##.#.####...#####.#.###....#.#######.#...#.#..#
            ########.#.#.#...###.#..###.#####.####..###.#...##.#.#..###.###.....#######.#...#.#..#
            """);
    }

    [Fact]
    public void CreatePdf417_DefaultOptions()
    {
        var withNull = Barcode2D.CreatePdf417("DEFAULT OPTIONS");
        var withDefaults = Barcode2D.CreatePdf417("DEFAULT OPTIONS", new Pdf417Options());

        Assert.Equal(RenderAsText(withNull), RenderAsText(withDefaults));
    }

    [Theory]
    [InlineData(0, 4)]  // 2 data codewords and 2 error correction codewords
    [InlineData(1, 6)]
    [InlineData(4, 34)]
    [InlineData(5, 66)]
    public void CreatePdf417_SecurityLevel(int level, int expectedRows)
    {
        var pdf417 = Barcode2D.CreatePdf417("A", new Pdf417Options { Columns = 1, SecurityLevel = level, RowHeight = 1 });
        Assert.Equal(expectedRows, pdf417.Height);
    }

    [Fact]
    public void CreatePdf417_SecurityLevel_TooManyRows()
    {
        // 2 data codewords and 128 error correction codewords do not fit in 90 rows of 1 column
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreatePdf417("A", new Pdf417Options { Columns = 1, SecurityLevel = 6 }));
        Assert.Equal(13, Barcode2D.CreatePdf417("A", new Pdf417Options { Columns = 10, SecurityLevel = 6, RowHeight = 1 }).Height);
    }

    [Theory]
    [InlineData(5, 2)]    // 4 data codewords
    [InlineData(115, 3)]  // 42 data codewords
    [InlineData(600, 4)]  // 207 data codewords
    [InlineData(1000, 5)] // 343 data codewords
    public void CreatePdf417_RecommendedSecurityLevel(int digitCount, int expectedLevel)
    {
        var data = string.Concat(Enumerable.Repeat("1234567890", (digitCount / 10) + 1))[..digitCount];
        var automatic = Barcode2D.CreatePdf417(data, new Pdf417Options { Columns = 10, RowHeight = 1 });
        var expected = Barcode2D.CreatePdf417(data, new Pdf417Options { Columns = 10, SecurityLevel = expectedLevel, RowHeight = 1 });
        var other = Barcode2D.CreatePdf417(data, new Pdf417Options { Columns = 10, SecurityLevel = expectedLevel - 1, RowHeight = 1 });

        Assert.Equal(RenderAsText(expected), RenderAsText(automatic));
        Assert.NotEqual(RenderAsText(other), RenderAsText(automatic));
    }

    [Fact]
    public void CreatePdf417_AutomaticColumns()
    {
        var small = Barcode2D.CreatePdf417("A", new Pdf417Options { RowHeight = 1 });
        var large = Barcode2D.CreatePdf417(new string('A', 1000), new Pdf417Options { RowHeight = 1 });

        // 2 data codewords and 8 error correction codewords; 501 data codewords and 64 error correction codewords
        Assert.Equal((2, 5), (GetColumns(small), small.Height));
        Assert.Equal((14, 41), (GetColumns(large), large.Height));
    }

    [Fact]
    public void CreatePdf417_Columns()
    {
        var narrow = Barcode2D.CreatePdf417("THE COLUMNS OF THE SYMBOL", new Pdf417Options { Columns = 2, RowHeight = 1 });
        var wide = Barcode2D.CreatePdf417("THE COLUMNS OF THE SYMBOL", new Pdf417Options { Columns = 8, RowHeight = 1 });

        Assert.Equal(2, GetColumns(narrow));
        Assert.Equal(8, GetColumns(wide));
        Assert.True(narrow.Height > wide.Height);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void Pdf417Options_InvalidSecurityLevel(int value)
    {
        var options = new Pdf417Options();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.SecurityLevel = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Pdf417Options_InvalidColumns(int value)
    {
        var options = new Pdf417Options();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Columns = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Pdf417Options_InvalidRowHeight(int value)
    {
        var options = new Pdf417Options();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.RowHeight = value);
    }

    [Fact]
    public void CreatePdf417_EmptyData()
    {
        Assert.Throws<ArgumentException>(() => Barcode2D.CreatePdf417(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void CreatePdf417_MaximumCapacity()
    {
        // 2710 digits: 1 + 1 + 61 × 15 + 14 data codewords and 2 error correction codewords
        var digits = string.Concat(Enumerable.Repeat("0123456789", 271));
        var largest = Barcode2D.CreatePdf417(digits, new Pdf417Options { SecurityLevel = 0, RowHeight = 1 });
        Assert.InRange(largest.Height, 3, 90);

        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreatePdf417(digits + "0", new Pdf417Options { SecurityLevel = 0 }));
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreatePdf417(digits));
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreatePdf417(new byte[1200]));
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreatePdf417(digits, new Pdf417Options { SecurityLevel = 0, Columns = 1 }));
    }

    [Fact]
    public void CreatePdf417_Binary()
    {
        var bytes = new byte[] { 0x00, 0x80, 0xFF, 0x10, 0x20, 0x7F, 0xC3 };
        var pdf417 = Barcode2D.CreatePdf417(bytes, new Pdf417Options { Columns = 3, RowHeight = 1 });

        Assert.Equal(Barcode2DType.Pdf417, pdf417.Type);
        Assert.Equal(120, pdf417.Width);
    }

    [Fact]
    public void CreatePdf417_EveryCharacterInEverySubMode()
    {
        // Each character after a letter of each sub-mode, and after a character of each sub-mode
        foreach (var prefix in new[] { "A", "a", "1", ";;" })
        {
            for (var value = 0; value < 128; value++)
            {
                var text = prefix + (char)value + prefix + "xyz";
                var pdf417 = Barcode2D.CreatePdf417(text, new Pdf417Options { RowHeight = 1 });
                Assert.Equal(1, pdf417.Width % 17);
            }
        }

        var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        Assert.Equal(Barcode2DType.Pdf417, Barcode2D.CreatePdf417(bytes).Type);
    }

    [Fact]
    public void CreatePdf417_NonAsciiTextHasEci()
    {
        var text = Barcode2D.CreatePdf417("héllo", new Pdf417Options { Columns = 3, RowHeight = 1 });
        var bytes = Barcode2D.CreatePdf417(Encoding.UTF8.GetBytes("héllo"), new Pdf417Options { Columns = 3, RowHeight = 1 });
        var ascii = Barcode2D.CreatePdf417("hello", new Pdf417Options { Columns = 3, RowHeight = 1 });
        var asciiBytes = Barcode2D.CreatePdf417("hello"u8, new Pdf417Options { Columns = 3, RowHeight = 1 });

        Assert.NotEqual(RenderAsText(bytes), RenderAsText(text));
        Assert.Equal(RenderAsText(asciiBytes), RenderAsText(ascii));
    }

    [Theory]
    [InlineData(0, "927,0")]
    [InlineData(26, "927,26")]
    [InlineData(899, "927,899")]
    [InlineData(900, "926,0,0")]
    [InlineData(810899, "926,899,899")]
    [InlineData(810900, "925,0")]
    [InlineData(811799, "925,899")]
    public void Encode_Eci(int eci, string expected)
    {
        Assert.Equal(expected + ",59", GetCodewords("B"u8, eci)); // B and the padding value
    }

    [Fact]
    public void Encode_TextSubModes()
    {
        // Alpha, Lower, shift to Alpha, Mixed, latch to Punctuation, shift to Punctuation, and the padding value 29
        Assert.Equal("1", GetCodewords("AB"u8));
        Assert.Equal("1,89", GetCodewords("ABC"u8)); // C and the padding value
        Assert.Equal("810,32", GetCodewords("abc"u8)); // Latch to Lower and a, then b and c
        InlineSnapshot.Validate(GetCodewords("Hello World! 123 #tag; a@b.c\t[x]\n"u8), "237,131,344,807,674,521,119,326,841,63,795,829,6,870,780,873,58,537,88,389,147,719,209,479");
    }

    [Fact]
    public void Encode_Bytes()
    {
        // 6 bytes in base 900: 0x000102030405 = 4328719365
        Assert.Equal("924,0,5,844,88,165", GetCodewords([0x00, 0x01, 0x02, 0x03, 0x04, 0x05]));
        Assert.Equal("901,0,5,844,88,165,128", GetCodewords([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x80]));
        Assert.Equal("913,128", GetCodewords([0x80])); // A symbol starts in the Text compaction
        Assert.Equal("901,65,66,128,65,66", GetCodewords([(byte)'A', (byte)'B', 0x80, (byte)'A', (byte)'B']));

        // A single byte between texts is a shift from the Text compaction, which resumes after it
        Assert.Equal("214,341,449,913,128,674,521,119", GetCodewords([.. "HELLO"u8, 0x80, .. "WORLD"u8]));
    }

    [Fact]
    public void Encode_Numeric()
    {
        // 13 digits or more use the Numeric compaction: "1" followed by the digits, in base 900 (ISO/IEC 15438, 5.4.4)
        Assert.Equal("902,1,624,434,632,282,200", GetCodewords("000213298174000"u8));
        Assert.Equal("902,15,386,694,721,112", GetCodewords("0123456789012"u8));

        // 12 digits use the Text compaction
        InlineSnapshot.Validate(GetCodewords("012345678901"u8), "840,32,94,156,218,270,59");
    }

    private static string GetCodewords(ReadOnlySpan<byte> data, int? eci = null)
    {
        var codewords = new List<int>();
        Pdf417HighLevelEncoder.Encode(data, eci, codewords);
        return string.Join(',', codewords.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    private static int GetColumns(Barcode2D barcode) => ((barcode.Width - 1) / 17) - 4;

    private static void AssertStartAndStopPatterns(Barcode2D barcode, int row)
    {
        const string Start = "11111111010101000";
        const string Stop = "111111101000101001";
        for (var i = 0; i < Start.Length; i++)
        {
            Assert.Equal(Start[i] == '1', barcode[row, i]);
        }

        for (var i = 0; i < Stop.Length; i++)
        {
            Assert.Equal(Stop[i] == '1', barcode[row, barcode.Width - Stop.Length + i]);
        }
    }

    private static string RenderAsText(Barcode2D barcode)
    {
        var sb = new StringBuilder();
        for (var row = 0; row < barcode.Height; row++)
        {
            if (row > 0)
            {
                sb.AppendLine();
            }

            for (var column = 0; column < barcode.Width; column++)
            {
                sb.Append(barcode[row, column] ? '#' : '.');
            }
        }

        return sb.ToString();
    }
}
