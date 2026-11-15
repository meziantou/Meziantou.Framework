using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class QRCodeConsoleRendererTests
{
    [Fact]
    public void ReadmeExample_ToConsoleString()
    {
        #region QRCode_ToConsoleString
        var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);
        var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

        qrCode.WriteToConsole();
        var text = qrCode.ToConsoleString();

        var microText = microQr.ToConsoleString(new ConsoleRenderOptions
        {
            QuietZoneModules = 1,
            ModuleWidth = 2,
            ModuleHeight = 2, // 2x bigger output that can look more square on many terminals
        });

        Assert.Equal("""

              ██████████████  ██  ██
              ██          ██  ████
              ██  ██████  ██  ██
              ██  ██████  ██
              ██  ██████  ██  ██████
              ██          ██    ████
              ██████████████  ██
                                ████
              ████    ██████    ████
                ██  ██      ████
              ████████          ████

            """, microText, ignoreLineEndingDifferences: true);
        #endregion

        Assert.NotEmpty(text);
    }

    [Fact]
    public void ToConsoleString_DefaultOptions()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString();

        Assert.NotNull(text);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void ToConsoleString_MicroQR_Snapshot()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 });

        InlineSnapshot.Validate(text, """
            ██████████████  ██  ██
            ██          ██
            ██  ██████  ██  ██████
            ██  ██████  ██    ████
            ██  ██████  ██      ██
            ██          ██  ████
            ██████████████  ██
                            ██
            ████          ██
              ██    ██████████  ██
            ██    ████████████  ██
            """);
    }

    [Fact]
    public void ToConsoleString_RMQR_Snapshot()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var text = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 });

        InlineSnapshot.Validate(text, """
            ██████████████  ██  ██  ██  ██  ██  ██  ██  ██  ██████
            ██          ██  ██████  ██  ████        ██  ██  ██  ██
            ██  ██████  ██    ████████    ████    ██  ██      ████
            ██  ██████  ██      ██  ████    ████████  ██████
            ██  ██████  ██        ██    ██      ████    ██████  ██
            ██          ██    ████  ████████  ████  ██████  ████
            ██████████████  ██████  ████  ██  ██      ████████████
                            ██    ██  ██████        ██  ██      ██
            ████████████████████  ██  ██    ████  ██  ████  ██  ██
            ██  ████  ██      ████████  ██      ██  ██  ██      ██
            ██████  ██  ██  ██  ██  ██  ██  ██  ██  ██  ██████████
            """);
    }

    [Fact]
    public void WriteTo_MicroQR_MatchesToConsoleString()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var options = new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 };

        var expected = qr.ToConsoleString(options);
        using var writer = new StringWriter();
        qr.WriteTo(writer, options);
        var actual = writer.ToString();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteTo_RMQR_MatchesToConsoleString()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var options = new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 };

        var expected = qr.ToConsoleString(options);
        using var writer = new StringWriter();
        qr.WriteTo(writer, options);
        var actual = writer.ToString();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToConsoleString_SmallQRCode_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 });

        InlineSnapshot.Validate(text, """
            ██████████████    ██  ████  ██████████████
            ██          ██    ██████    ██          ██
            ██  ██████  ██  ████  ████  ██  ██████  ██
            ██  ██████  ██    ██  ██    ██  ██████  ██
            ██  ██████  ██      ██  ██  ██  ██████  ██
            ██          ██          ██  ██          ██
            ██████████████  ██  ██  ██  ██████████████
                            ████  ████
            ██████  ████████████████  ████      ██
              ██████      ██    ██      ██      ████
                  ████████      ██  ██      ██      ██
              ██      ██  ████████      ██      ████
            ██████  ██████  ████    ██  ██  ██  ██████
                            ████  ██  ██  ██  ██
            ██████████████  ██  ████  ██████  ████
            ██          ██  ████████████  ██████  ██
            ██  ██████  ██  ██    ██  ██████  ████  ██
            ██  ██████  ██    ██        ██      ████
            ██  ██████  ██  ██████  ██      ██      ██
            ██          ██  ██  ██      ██      ██████
            ██████████████  ██████  ██  ██  ██  ██  ██
            """);
    }

    [Fact]
    public void ToConsoleString_InvertedColors()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var normal = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 });
        var inverted = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2, InvertColors = true });

        Assert.NotEqual(normal, inverted);
        InlineSnapshot.Validate(inverted, """
                          ████  ██    ██
              ██████████  ████      ████  ██████████
              ██      ██  ██    ██    ██  ██      ██
              ██      ██  ████  ██  ████  ██      ██
              ██      ██  ██████  ██  ██  ██      ██
              ██████████  ██████████  ██  ██████████
                          ██  ██  ██  ██
            ████████████████    ██    ████████████████
                  ██                ██    ██████  ████
            ██      ██████  ████  ██████  ██████    ██
            ██████        ██████  ██  ██████  ██████
            ██  ██████  ██        ██████  ██████    ██
                  ██      ██    ████  ██  ██  ██
            ████████████████    ██  ██  ██  ██  ██████
                          ██  ██    ██      ██    ████
              ██████████  ██            ██      ██  ██
              ██      ██  ██  ████  ██      ██    ██
              ██      ██  ████  ████████  ██████    ██
              ██      ██  ██      ██  ██████  ██████
              ██████████  ██  ██  ██████  ██████
                          ██      ██  ██  ██  ██  ██
            """);
    }

    [Fact]
    public void ToConsoleString_WithQuietZone()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var withoutQuiet = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 });
        var withQuiet = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 2, ModuleWidth = 2, ModuleHeight = 2 });

        Assert.HasCountGreaterThan(withoutQuiet.Length, withQuiet);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToConsoleString_InvalidModuleWidth_ThrowsArgumentOutOfRangeException(int moduleWidth)
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToConsoleString(new ConsoleRenderOptions { ModuleWidth = moduleWidth }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToConsoleString_InvalidModuleHeight_ThrowsArgumentOutOfRangeException(int moduleHeight)
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToConsoleString(new ConsoleRenderOptions { ModuleHeight = moduleHeight }));
    }

    [Fact]
    public void WriteTo_TextWriter_MatchesToConsoleString()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 };

        var expected = qr.ToConsoleString(options);
        using var writer = new StringWriter();
        qr.WriteTo(writer, options);
        var actual = writer.ToString();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToConsoleString_FullSnapshot()
    {
        var qr = QRCode.Create("HELLO", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2 });

        InlineSnapshot.Validate(text, """
            ██████████████    ██    ██  ██████████████
            ██          ██  ██    ██    ██          ██
            ██  ██████  ██    ██        ██  ██████  ██
            ██  ██████  ██  ██    ██    ██  ██████  ██
            ██  ██████  ██      ██████  ██  ██████  ██
            ██          ██  ██████  ██  ██          ██
            ██████████████  ██  ██  ██  ██████████████
                                ██████
            ██████████  ████████    ████  ██  ██  ██
            ██  ██    ██            ██    ██  ████████
                        ██  ████████  ██    ████    ██
              ██████████  ██    ██        ████
              ████    ████  ████████  ██    ██  ██
                            ████  ████████    ██  ████
            ██████████████  ██  ██  ██  ████    ██  ██
            ██          ██    ████████████    ██    ██
            ██  ██████  ██  ██      ██    ██    ██
            ██  ██████  ██  ██  ██  ██    ██    ██
            ██  ██████  ██  ██    ██  ██    ██  ██
            ██          ██  ██  ██        ████  ██  ██
            ██████████████  ██    ██  ██    ██  ██
            """);
    }

    [Fact]
    public void WriteTo_DefaultOptions_MatchesToConsoleString()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        var expected = qr.ToConsoleString();
        using var writer = new StringWriter();
        qr.WriteTo(writer);
        var actual = writer.ToString();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToConsoleString_DefaultOptions_UsesQuietZone2()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var withDefault = qr.ToConsoleString();
        var withQuiet2 = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 2 });

        Assert.Equal(withDefault, withQuiet2);
    }

    [Fact]
    public void ToConsoleString_InvertedSnapshot()
    {
        var qr = QRCode.Create("HELLO", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, ModuleWidth = 2, ModuleHeight = 2, InvertColors = true });

        InlineSnapshot.Validate(text, """
                          ████  ████  ██
              ██████████  ██  ████  ████  ██████████
              ██      ██  ████  ████████  ██      ██
              ██      ██  ██  ████  ████  ██      ██
              ██      ██  ██████      ██  ██      ██
              ██████████  ██      ██  ██  ██████████
                          ██  ██  ██  ██
            ████████████████████      ████████████████
                      ██        ████    ██  ██  ██  ██
              ██  ████  ████████████  ████  ██
            ████████████  ██        ██  ████    ████
            ██          ██  ████  ████████    ████████
            ██    ████    ██        ██  ████  ██  ████
            ████████████████    ██        ████  ██
                          ██  ██  ██  ██    ████  ██
              ██████████  ████            ████  ████
              ██      ██  ██  ██████  ████  ████  ████
              ██      ██  ██  ██  ██  ████  ████  ████
              ██      ██  ██  ████  ██  ████  ██  ████
              ██████████  ██  ██  ████████    ██  ██
                          ██  ████  ██  ████  ██  ████
            """);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ToConsoleString_NegativeQuietZone_Throws(int quietZoneModules)
    {
        // This used to return an empty string, while every other renderer threw.
        var qr = QRCode.Create("HELLO", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = quietZoneModules }));
    }

    [Theory]
    [InlineData(1, 100_000_000, 0)]
    [InlineData(1, 1, 1_073_741_824)]
    [InlineData(1, 1, int.MaxValue)]
    [InlineData(int.MaxValue, 1, 0)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(100_000, 100_000, 0)]
    public void ToConsoleString_OverflowingDimensions_Throws(int moduleWidth, int moduleHeight, int quietZoneModules)
    {
        // These used to wrap around and silently return an empty or truncated string.
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new ConsoleRenderOptions { ModuleWidth = moduleWidth, ModuleHeight = moduleHeight, QuietZoneModules = quietZoneModules };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToConsoleString(options));
        Assert.Equal("options", exception.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => qr.WriteTo(TextWriter.Null, options));
    }

    [Fact]
    public void Documentation_LinearAndTwoDimensionalBarcodes()
    {
        #region Barcode_ToConsoleString
        var barcode = Barcode.CreateEan8("9638507");
        var text = barcode.ToConsoleString(new ConsoleRenderOptions
        {
            QuietZoneModules = 2,
            BarHeight = 2, // lines of text
            ShowText = true, // the human-readable text, centered under the bars
        });

        var dataMatrix = Barcode2D.CreateDataMatrix("123456");
        var dataMatrixText = dataMatrix.ToConsoleString();
        #endregion

        var lines = text.Split(Environment.NewLine);
        Assert.HasCount(3, lines);
        Assert.Equal(lines[0], lines[1]);
        Assert.Equal((barcode.Width + 4) / 2 - 4, lines[2].IndexOf("96385074", StringComparison.Ordinal));
        Assert.HasCount((dataMatrix.Height + 4 + 1) / 2, dataMatrixText.Split(Environment.NewLine));
    }

    [Fact]
    public void Barcode_ToConsoleString_MatchesTheModules()
    {
        var barcode = Barcode.CreateCode128("Hi");
        var text = barcode.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 1, ModuleWidth = 2, BarHeight = 3 });
        var lines = text.Split(Environment.NewLine);

        Assert.HasCount(3, lines);
        foreach (var line in lines)
        {
            var padded = line.PadRight((barcode.Width + 2) * 2);
            for (var x = 0; x < padded.Length; x++)
            {
                var column = (x / 2) - 1;
                var dark = column >= 0 && column < barcode.Width && barcode[0, column];
                Assert.Equal(dark ? '\u2588' : ' ', padded[x]);
            }
        }
    }

    [Fact]
    public void Barcode_ToConsoleString_DefaultQuietZoneIs10()
    {
        var barcode = Barcode.CreateCode128("Hi");
        var text = barcode.ToConsoleString();

        Assert.Equal(new string(' ', 10) + '\u2588', text.Split(Environment.NewLine)[0][..11]);
        Assert.HasCount(4, text.Split(Environment.NewLine));
    }

    [Fact]
    public void Barcode_ToConsoleString_InvertColors()
    {
        var barcode = Barcode.CreateCode128("Hi");
        var text = barcode.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0, BarHeight = 1, InvertColors = true });

        Assert.Equal(barcode[0, 1] ? ' ' : '\u2588', text[1]);
        Assert.Equal('\u2588', text[^1]); // the trailing light module is drawn dark when inverted
    }

    [Fact]
    public void Barcode_ToConsoleString_StackedRowsAreHalfLines()
    {
        var barcode = Barcode.CreateGs1DataBarStacked("0950110153001");
        var text = barcode.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0 });

        Assert.HasCount((barcode.Height + 1) / 2, text.Split(Environment.NewLine));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Barcode_ToConsoleString_InvalidBarHeight_Throws(int barHeight)
    {
        var barcode = Barcode.CreateCode128("Hi");

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToConsoleString(new ConsoleRenderOptions { BarHeight = barHeight }));
    }

    [Fact]
    public void Barcode_WriteTo_MatchesToConsoleString()
    {
        var barcode = Barcode.CreateCode128("Hi");
        var dataMatrix = Barcode2D.CreateDataMatrix("123456");
        var options = new ConsoleRenderOptions { ShowText = true };
        using var writer = new StringWriter();
        barcode.WriteTo(writer, options);
        dataMatrix.WriteTo(writer, options);

        Assert.Equal(barcode.ToConsoleString(options) + dataMatrix.ToConsoleString(options), writer.ToString());
    }

    [Fact]
    public void Barcode2D_ToConsoleString_Snapshot()
    {
        var dataMatrix = Barcode2D.CreateDataMatrix("123456");

        InlineSnapshot.Validate(dataMatrix.ToConsoleString(new ConsoleRenderOptions { QuietZoneModules = 0 }), """
            █▄▀ █ █▄▀▄
            ██   ▄▄█ ▄
            █▀    █▄▄▄
            ███▄▀█▄  ▄
            █▄▄███▄█▄▄
            """);
    }

    [Fact]
    public void Barcode_ToConsoleString_ControlCharactersOfTheTextAreEscaped()
    {
        // Code 128 encodes every ASCII character: the text must not add lines or send escape sequences to the terminal
        var barcode = Barcode.CreateCode128("AB\nCD\u001b[2J");
        var text = barcode.ToConsoleString(new ConsoleRenderOptions { BarHeight = 1, ShowText = true });
        var lines = text.Split(Environment.NewLine);

        Assert.HasCount(2, lines);
        Assert.EndsWith("AB?CD?[2J", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', text);
    }
}
