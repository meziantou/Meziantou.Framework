using System.Text;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class AztecTests
{
    [Fact]
    public void Barcode2D_Aztec()
    {
        #region Barcode2D_Aztec
        var barcode = Barcode2D.CreateAztec("Hello, World!", new AztecOptions
        {
            ErrorCorrectionPercent = 50, // 50% of the symbol and 3 codewords are error correction
            Compact = true, // 1 to 4 layers: 15 × 15 to 27 × 27 modules
        });

        Assert.Equal(Barcode2DType.Aztec, barcode.Type);
        Assert.Equal(19, barcode.Width);
        Assert.Equal(19, barcode.Height);
        #endregion
    }

    // Matches zint: zint -b 92 --vers=1 -d "HELLO"
    [Fact]
    public void CreateAztec_Compact1Layer()
    {
        var barcode = Barcode2D.CreateAztec("HELLO");

        Assert.Equal(Barcode2DType.Aztec, barcode.Type);
        InlineSnapshot.Validate(RenderAsText(barcode), """
            ..##..#..#.#..#
            .#.###.##.#....
            ..##.....#..#.#
            #.#############
            .#.#.......#.#.
            #..#.#####.##.#
            .###.#...#.#.#.
            #..#.#.#.#.###.
            #..#.#...#.#.#.
            ##.#.#####.#.##
            .#.#.......#.##
            #..###########.
            ....###.###..#.
            #####.##..####.
            .##.#.#.#.#.###
            """);
    }

    // Matches zint: zint -b 92 --vers=2 -d "12345678901234567890"
    [Fact]
    public void CreateAztec_Compact2Layers_Digits()
    {
        var barcode = Barcode2D.CreateAztec("12345678901234567890");

        InlineSnapshot.Validate(RenderAsText(barcode), """
            ##.###..#.#.#..#.##
            ##.####.........#..
            ...###..###..##.##.
            .##..#.#####..###.#
            #...##.#..###.#..##
            #...###############
            ..##.#.......#...#.
            #....#.#####.######
            #.#.##.#...#.#..###
            ###..#.#.#.#.#..###
            ...#.#.#...#.##.##.
            ####.#.#####.##.##.
            ######.......#..#.#
            ...#.#############.
            .#.#..###......#.#.
            ..###.#.###.#.#..#.
            ##.#####.#....#..##
            ###.#.#.....#.##...
            ...#...##.###.##.#.
            """);
    }

    // Matches zint: zint -b 92 --vers=5 -d "AZTEC"
    [Fact]
    public void CreateAztec_FullRange1Layer()
    {
        var barcode = Barcode2D.CreateAztec("AZTEC", new AztecOptions { Compact = false });

        InlineSnapshot.Validate(RenderAsText(barcode), """
            ...#.#.#....#.#....
            ..#.##.#.#.#.######
            .###............###
            .################..
            #.##...........#..#
            ##.#.#########.#..#
            #.##.#.......#.#...
            #..#.#.#####.#.##..
            #..#.#.#...#.#.#...
            .#.#.#.#.#.#.#.#.#.
            .###.#.#...#.#.#.##
            #.##.#.#####.#.##..
            ..##.#.......#.####
            #.##.#########.####
            .#.#...........##..
            ##.##############..
            ##..#...#.#.#.....#
            #..###..##..#.##.##
            .##..#.#......##...
            """);
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 19)]
    [InlineData(3, 23)]
    [InlineData(4, 27)]
    public void CreateAztec_CompactSizes(int layers, int size)
    {
        var barcode = Barcode2D.CreateAztec("A", new AztecOptions { Compact = true, Layers = layers });

        Assert.Equal(size, barcode.Width);
        Assert.Equal(size, barcode.Height);
        AssertFinderPattern(barcode, compact: true);
    }

    [Theory]
    [InlineData(1, 19)]
    [InlineData(2, 23)]
    [InlineData(3, 27)]
    [InlineData(4, 31)]
    [InlineData(5, 37)] // 19 + 4 × 5 modules, and 2 lines of the reference grid
    [InlineData(10, 57)]
    [InlineData(12, 67)] // The outermost lines of the reference grid are 32 modules from the center, on the second module of each side
    [InlineData(15, 79)]
    [InlineData(27, 131)] // The outermost lines of the reference grid are 64 modules from the center
    [InlineData(32, 151)]
    public void CreateAztec_FullRangeSizes(int layers, int size)
    {
        var barcode = Barcode2D.CreateAztec("A", new AztecOptions { Compact = false, Layers = layers });

        Assert.Equal(size, barcode.Width);
        Assert.Equal(size, barcode.Height);
        AssertFinderPattern(barcode, compact: false);
        AssertReferenceGrid(barcode);
    }

    [Fact]
    public void CreateAztec_LayersWithoutCompact_PrefersCompact()
    {
        Assert.Equal(23, Barcode2D.CreateAztec("A", new AztecOptions { Layers = 3 }).Width);
        Assert.Equal(37, Barcode2D.CreateAztec("A", new AztecOptions { Layers = 5 }).Width);
    }

    [Fact]
    public void CreateAztec_LayersWithoutCompact_UsesFullRangeWhenCompactIsTooSmall()
    {
        // 15 digits take 11 codewords of 6 bits: the compact symbol of 1 layer holds 10 with 23% of error correction,
        // the full-range one 13
        var barcode = Barcode2D.CreateAztec(new string('1', 15), new AztecOptions { Layers = 1 });

        Assert.Equal(19, barcode.Width);
        AssertFinderPattern(barcode, compact: false);
    }

    [Fact]
    public void CreateAztec_DefaultSize_GrowsWithData()
    {
        Assert.Equal(15, Barcode2D.CreateAztec("Hello").Width);
        Assert.Equal(19, Barcode2D.CreateAztec("Hello, World! Hello").Width);
        Assert.Equal(31, Barcode2D.CreateAztec(new string('x', 100)).Width);
        Assert.Equal(139, Barcode2D.CreateAztec(new string('x', 2500)).Width);
    }

    [Fact]
    public void CreateAztec_ErrorCorrectionPercent_GrowsTheSymbol()
    {
        var low = Barcode2D.CreateAztec("Hello, World!", new AztecOptions { ErrorCorrectionPercent = 5 });
        var high = Barcode2D.CreateAztec("Hello, World!", new AztecOptions { ErrorCorrectionPercent = 80 });

        Assert.Equal(19, low.Width);
        Assert.Equal(27, high.Width);
    }

    [Fact]
    public void CreateAztec_Compact_TooLong_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreateAztec(new string('x', 100), new AztecOptions { Compact = true }));
        Assert.Equal(31, Barcode2D.CreateAztec(new string('x', 100), new AztecOptions { Compact = false }).Width);
    }

    [Fact]
    public void CreateAztec_Layers_TooSmall_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreateAztec(new string('x', 50), new AztecOptions { Layers = 2 }));
    }

    [Fact]
    public void CreateAztec_MaximumCapacity()
    {
        // 1278 codewords of 12 bits: a binary shift of 21 bits and 1914 bytes
        var largest = Barcode2D.CreateAztec(Enumerable.Repeat((byte)0x80, 1914).ToArray());

        Assert.Equal(151, largest.Width);
        Assert.Throws<InvalidOperationException>(() => Barcode2D.CreateAztec(Enumerable.Repeat((byte)0x80, 1915).ToArray()));
    }

    [Fact]
    public void CreateAztec_CompactWithMoreThan4Layers_Throws()
    {
        Assert.Throws<ArgumentException>(() => Barcode2D.CreateAztec("A", new AztecOptions { Compact = true, Layers = 5 }));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(96)]
    public void AztecOptions_ErrorCorrectionPercent_OutOfRange_Throws(int value)
    {
        var options = new AztecOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.ErrorCorrectionPercent = value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void AztecOptions_Layers_OutOfRange_Throws(int value)
    {
        var options = new AztecOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Layers = value);
    }

    [Fact]
    public void CreateAztec_EmptyOrNull_Throws()
    {
        Assert.Throws<ArgumentException>(() => Barcode2D.CreateAztec(""));
        Assert.Throws<ArgumentException>(() => Barcode2D.CreateAztec(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => Barcode2D.CreateAztec((string)null!));
    }

    [Fact]
    public void CreateAztec_AsciiText_IsTheSameAsItsBytes()
    {
        var text = Barcode2D.CreateAztec("Hello, World!\r\n");
        var binary = Barcode2D.CreateAztec("Hello, World!\r\n"u8);

        Assert.Equal(RenderAsText(text), RenderAsText(binary));
    }

    [Fact]
    public void CreateAztec_NonAsciiText_DeclaresUtf8()
    {
        // The text gets the ECI designator of UTF-8 (P/S FLG(n), 2 digits: 21 bits), the bytes do not
        var text = Barcode2D.CreateAztec("é", new AztecOptions { Compact = true, Layers = 1 });
        var binary = Barcode2D.CreateAztec(Encoding.UTF8.GetBytes("é"), new AztecOptions { Compact = true, Layers = 1 });

        Assert.NotEqual(RenderAsText(text), RenderAsText(binary));
    }

    [Fact]
    public void CreateAztec_BinaryData()
    {
        var barcode = Barcode2D.CreateAztec([0x00, 0xFF, 0x10, 0x80]);

        Assert.Equal(15, barcode.Width);
        AssertFinderPattern(barcode, compact: true);
    }

    [Fact]
    public void CreateAztec_LongBinaryShift()
    {
        // More than 62 bytes take one binary shift with an 11-bit length, more than 2078 bytes take two
        var data = new byte[2100];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(128 + (i % 128));
        }

        var barcode = Barcode2D.CreateAztec(data, new AztecOptions { ErrorCorrectionPercent = 5 });

        AssertFinderPattern(barcode, compact: false);
    }

    private static void AssertFinderPattern(Barcode2D barcode, bool compact)
    {
        var center = barcode.Width / 2;
        var radius = compact ? 5 : 7;
        for (var row = center - radius + 1; row < center + radius; row++)
        {
            for (var column = center - radius + 1; column < center + radius; column++)
            {
                var distance = Math.Max(Math.Abs(row - center), Math.Abs(column - center));
                Assert.Equal(distance % 2 == 0, barcode[row, column]);
            }
        }

        // The orientation marks: 3 dark modules at the top left, 2 at the top right, 1 at the bottom right, none at the bottom left
        Assert.True(barcode[center - radius, center - radius] && barcode[center - radius, center - radius + 1] && barcode[center - radius + 1, center - radius]);
        Assert.True(barcode[center - radius, center + radius] && barcode[center - radius + 1, center + radius] && !barcode[center - radius, center + radius - 1]);
        Assert.True(barcode[center + radius - 1, center + radius] && !barcode[center + radius, center + radius] && !barcode[center + radius, center + radius - 1]);
        Assert.True(!barcode[center + radius, center - radius] && !barcode[center + radius - 1, center - radius] && !barcode[center + radius, center - radius + 1]);
    }

    // Every 16th row and column from the center alternates dark and light modules, dark on the center lines
    private static void AssertReferenceGrid(Barcode2D barcode)
    {
        var center = barcode.Width / 2;
        for (var offset = 0; offset <= center; offset += 16)
        {
            foreach (var line in new[] { center - offset, center + offset })
            {
                for (var i = 0; i < barcode.Width; i++)
                {
                    var expected = (i - center) % 2 == 0;
                    Assert.Equal(expected, barcode[line, i]);
                    Assert.Equal(expected, barcode[i, line]);
                }
            }
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

    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("HELLO WORLD")]
    [InlineData("hello world")]
    [InlineData("0123456789, 3.14")]
    [InlineData("Mixed: @\\^_`|~ \u0001\u001b\u007f")]
    [InlineData("Punct: !\"#$%&'()*+,-./:;<=>?[]{}")]
    [InlineData("Pairs\r\n. , : end")]
    [InlineData("aB1 cD2 eF3 MiXeD 9z")]
    [InlineData("caf\u00E9 cr\u00E8me")]
    [InlineData("Ελληνικά 漢字 😀")]
    [InlineData("A")]
    public void Decode_Text(string text)
    {
        text = System.Text.RegularExpressions.Regex.Unescape(text);
        var result = Barcode2DDecoder.Decode(Barcode2D.CreateAztec(text));
        Assert.Equal((text, 0), (result.Text, result.CorrectedErrorCount));
        Assert.Equal(Ascii.IsValid(text) ? null : 26, result.EciAssignmentNumber);
    }

    [Fact]
    public void Decode_Binary()
    {
        // Binary shifts of 1 to 31 bytes have a 5-bit length, longer ones an 11-bit length
        foreach (var length in (int[])[1, 31, 32, 62, 63, 100, 1000])
        {
            var bytes = Enumerable.Range(0, length).Select(i => (byte)(128 + (i % 128))).ToArray();
            Assert.Equal(bytes, Barcode2DDecoder.Decode(Barcode2D.CreateAztec(bytes)).Bytes.ToArray());
        }
    }

    [Fact]
    public void Decode_AllSizes()
    {
        for (var layers = 1; layers <= 32; layers++)
        {
            foreach (var compact in (bool[])[true, false])
            {
                if (compact && layers > 4)
                    continue;

                var options = new AztecOptions { Compact = compact, Layers = layers };
                var text = "";
                foreach (var length in (int[])[1, 10, 50, 200, 1000, 3000])
                {
                    var candidate = string.Concat(Enumerable.Range(0, length).Select(i => "Aztec 0123456789 abc!"[i % 21]));
                    try
                    {
                        Barcode2D.CreateAztec(candidate, options);
                        text = candidate;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                }

                var result = Barcode2DDecoder.Decode(Barcode2D.CreateAztec(text, options));
                Assert.Equal(text, result.Text);
            }
        }
    }

    [Fact]
    public void Decode_AnyOrientation()
    {
        foreach (var barcode in new[] { Barcode2D.CreateAztec("Compact orientation"), Barcode2D.CreateAztec("Full-range orientation", new AztecOptions { Compact = false }) })
        {
            var modules = DataMatrixTests.GetModules(barcode);
            for (var transform = 0; transform < Internal.MatrixTransforms.Count; transform++)
            {
                var text = Barcode2DDecoder.Decode(Internal.MatrixTransforms.Apply(modules, transform), Barcode2DType.Aztec).Text;
                Assert.EndsWith("orientation", text);
            }
        }
    }

    [Fact]
    public void Decode_CorrectsErrors()
    {
        var barcode = Barcode2D.CreateAztec("Error correction of an Aztec symbol", new AztecOptions { Compact = false, Layers = 4 });
        var modules = DataMatrixTests.GetModules(barcode);

        // Damage the outer layer, and one bit of the mode message
        for (var i = 2; i < 12; i++)
        {
            modules[0, i] = !modules[0, i];
        }

        var center = barcode.Height / 2;
        modules[center - 7, center - 5] = !modules[center - 7, center - 5];

        var result = Barcode2DDecoder.Decode(modules, Barcode2DType.Aztec);
        Assert.Equal("Error correction of an Aztec symbol", result.Text);
        Assert.True(result.CorrectedErrorCount > 0);
    }

    [Fact]
    public void Decode_InvalidMatrix()
    {
        Assert.Throws<FormatException>(() => Barcode2DDecoder.Decode(new bool[16, 16], Barcode2DType.Aztec));
        Assert.Throws<FormatException>(() => Barcode2DDecoder.Decode(new bool[15, 15], Barcode2DType.Aztec));
        Assert.Throws<FormatException>(() => Barcode2DDecoder.Decode(new bool[15, 19], Barcode2DType.Aztec));
    }
}
