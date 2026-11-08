using Meziantou.Framework.Imaging;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Barcodes.Tests;

public class BarCodeAdditionalTypesTests
{
    [Fact]
    public void ReadmeExample_CreateBarcodes()
    {
        #region Barcode_Create
        var code39 = Barcode.CreateCode39("ABC-123", includeChecksum: true);
        var code93 = Barcode.CreateCode93("ABC123");
        var code128 = Barcode.CreateCode128("SKU-123456");
        var ean8 = Barcode.CreateEan8("5512345");
        var ean13 = Barcode.CreateEan13("400638133393");
        var upcA = Barcode.CreateUpcA("03600029145", extension: "12");
        var codabar = Barcode.CreateCodabar("40156", startCharacter: 'A', stopCharacter: 'B');
        var itf = Barcode.CreateItf("12345678");
        var upcE = Barcode.CreateUpcE("0425261");
        var dataBar = Barcode.CreateGs1DataBar("0950110153000");

        // Type, width in modules and height (a linear barcode is one module high)
        Assert.Equal((BarcodeType.Code39, 129, 1), (code39.Type, code39.Width, code39.Height));
        Assert.Equal((BarcodeType.Code93, 91, 1), (code93.Type, code93.Width, code93.Height));
        Assert.Equal((BarcodeType.Code128, 123, 1), (code128.Type, code128.Width, code128.Height));
        Assert.Equal((BarcodeType.Ean8, 67, 1), (ean8.Type, ean8.Width, ean8.Height));
        Assert.Equal((BarcodeType.Ean13, 95, 1), (ean13.Type, ean13.Width, ean13.Height));
        Assert.Equal((BarcodeType.UpcA, 124, 1), (upcA.Type, upcA.Width, upcA.Height));
        Assert.Equal((BarcodeType.Codabar, 71, 1), (codabar.Type, codabar.Width, codabar.Height));
        Assert.Equal((BarcodeType.Itf, 81, 1), (itf.Type, itf.Width, itf.Height));
        Assert.Equal((BarcodeType.UpcE, 51, 1), (upcE.Type, upcE.Width, upcE.Height));
        Assert.Equal((BarcodeType.Gs1DataBar, 96, 1), (dataBar.Type, dataBar.Width, dataBar.Height));

        // The human-readable text, with the characters the barcode adds (check digits, add-on, Codabar guards)
        Assert.Equal("ABC-123W", code39.Text);
        Assert.Equal("55123457", ean8.Text);
        Assert.Equal("4006381333931", ean13.Text);
        Assert.Equal("036000291452 12", upcA.Text);
        Assert.Equal("A40156B", codabar.Text);
        Assert.Equal("04252614", upcE.Text);
        Assert.Equal("(01)09501101530003", dataBar.Text);
        #endregion
    }

    [Fact]
    public void Documentation_GettingStarted()
    {
        #region Barcode_GettingStarted
        var barcode = Barcode.CreateCode128("SKU-123456");

        // The bytes of a PNG file, to save with File.WriteAllBytes("barcode.png", png)
        var png = barcode.ToPng(new BarcodePngOptions { ModuleHeight = 60 });
        #endregion

        DocumentationImages.AssertPng("GettingStartedBarcode", png);
    }

    [Fact]
    public void Documentation_Ean13WithAddOn()
    {
        #region Barcode_Ean13WithAddOn
        // A book: its ISBN without the check digit, and a 5-digit add-on
        var barcode = Barcode.CreateEan13("978020137962", extension: "52499");
        Assert.Equal("9780201379624 52499", barcode.Text); // the check digit is added

        var png = barcode.ToPng(new BarcodePngOptions { ModuleHeight = 60 });
        #endregion

        DocumentationImages.AssertPng("Ean13WithAddOn", png);
    }

    [Fact]
    public void Documentation_InvalidData()
    {
        #region Barcode_InvalidData
        // A check digit that does not match the data
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateEan13("4006381333932"));

        // A character the symbology cannot encode
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateCode39("abc"));

        // ITF encodes pairs of digits
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateItf("12345"));
        #endregion
    }

    [Fact]
    public void Documentation_Modules()
    {
        #region Barcode_Modules
        var barcode = Barcode.CreateEan8("5512345");

        // One row of modules: true for a bar
        var bars = new StringBuilder();
        for (var column = 0; column < barcode.Width; column++)
        {
            bars.Append(barcode[0, column] ? '|' : ' ');
        }

        Assert.StartsWith("| |", bars.ToString()); // the start guard
        #endregion
    }

    [Theory]
    [InlineData(BarcodeType.Code39, "ABC-123", "ABC-123")]
    [InlineData(BarcodeType.Code93, "ABC123xyz", "ABC123xyz")]
    [InlineData(BarcodeType.Code128, "SKU-123456", "SKU-123456")]
    [InlineData(BarcodeType.Ean8, "55123457", "55123457")]
    [InlineData(BarcodeType.Ean13, "590123412345", "5901234123457")]
    [InlineData(BarcodeType.UpcA, "036000291452", "036000291452")]
    [InlineData(BarcodeType.Codabar, "40156", "A40156B")]
    [InlineData(BarcodeType.Itf, "12345678", "12345678")]
    [InlineData(BarcodeType.UpcE, "0425261", "04252614")]
    [InlineData(BarcodeType.Gs1DataBar, "0950110153000", "(01)09501101530003")]
    [InlineData(BarcodeType.Gs1DataBarLimited, "1501234567890", "(01)15012345678907")]
    public void Text_IsTheHumanReadableInterpretation(BarcodeType type, string data, string expected)
    {
        var barcode = type switch
        {
            BarcodeType.Code39 => Barcode.CreateCode39(data),
            BarcodeType.Code93 => Barcode.CreateCode93(data),
            BarcodeType.Code128 => Barcode.CreateCode128(data),
            BarcodeType.Ean8 => Barcode.CreateEan8(data),
            BarcodeType.Ean13 => Barcode.CreateEan13(data),
            BarcodeType.UpcA => Barcode.CreateUpcA(data),
            BarcodeType.Codabar => Barcode.CreateCodabar(data),
            BarcodeType.Itf => Barcode.CreateItf(data),
            BarcodeType.UpcE => Barcode.CreateUpcE(data),
            BarcodeType.Gs1DataBar => Barcode.CreateGs1DataBar(data),
            BarcodeType.Gs1DataBarLimited => Barcode.CreateGs1DataBarLimited(data),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };

        Assert.Equal(expected, barcode.Text);
    }

    [Fact]
    public void Text_IncludesTheAddOnAndTheNormalizedCodabarGuards()
    {
        Assert.Equal("5901234123457 12345", Barcode.CreateEan13("590123412345", extension: "12345").Text);
        Assert.Equal("55123457 12", Barcode.CreateEan8("5512345", extension: "12").Text);
        Assert.Equal("C12D", Barcode.CreateCodabar("12", startCharacter: 'c', stopCharacter: 'd').Text);
    }

    private const string Code93SmallData = "A1";
    private const string Code93LargeData = "ABC123xyz-.$/+%";
    private const string Ean8SmallData = "5512345";
    private const string Ean8LargeData = "5512345";
    private const string Ean13SmallData = "400638133393";
    private const string Ean13LargeData = "123456789012";
    private const string UpcASmallData = "03600029145";
    private const string UpcALargeData = "12345678901";
    private const string CodabarSmallData = "12";
    private const string CodabarLargeData = "1234567890-$:/.+";
    private const string ItfSmallData = "123456";
    private const string ItfLargeData = "12345678901234567890";

    [Fact]
    public void CreateCode93_ReturnsExpectedMetadata()
    {
        var barcode = Barcode.CreateCode93("A");

        Assert.Equal(BarcodeType.Code93, barcode.Type);
        Assert.Equal(1, barcode.Height);
        Assert.Equal(46, barcode.Width);
        Assert.True(barcode[0, 0]);
        Assert.True(barcode[0, barcode.Width - 1]);
    }

    [Fact]
    public void CreateCode93_ExtendedEncoding_IsDeterministic()
    {
        var barcode1 = Barcode.CreateCode93("abc");
        var barcode2 = Barcode.CreateCode93("abc");

        AssertBarcodesEqual(barcode1, barcode2);
    }

    [Fact]
    public void CreateCode93_UnsupportedCharacter_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateCode93("é"));
    }

    [Fact]
    public void CreateEan8_SevenOrEightDigits_ProduceSameBarcode()
    {
        var barcodeWithoutChecksum = Barcode.CreateEan8("5512345");
        var barcodeWithChecksum = Barcode.CreateEan8("55123457");

        Assert.Equal(BarcodeType.Ean8, barcodeWithoutChecksum.Type);
        Assert.Equal(67, barcodeWithoutChecksum.Width);
        AssertBarcodesEqual(barcodeWithoutChecksum, barcodeWithChecksum);
    }

    [Fact]
    public void CreateEan8_InvalidChecksum_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateEan8("55123458"));
    }

    [Fact]
    public void CreateEan8_WithTwoDigitExtension_AppendsExtension()
    {
        var barcode = Barcode.CreateEan8("5512345", extension: "12");

        Assert.Equal(BarcodeType.Ean8, barcode.Type);
        Assert.Equal(94, barcode.Width);
    }

    [Fact]
    public void CreateEan13_TwelveOrThirteenDigits_ProduceSameBarcode()
    {
        var barcodeWithoutChecksum = Barcode.CreateEan13("400638133393");
        var barcodeWithChecksum = Barcode.CreateEan13("4006381333931");

        Assert.Equal(BarcodeType.Ean13, barcodeWithoutChecksum.Type);
        Assert.Equal(95, barcodeWithoutChecksum.Width);
        AssertBarcodesEqual(barcodeWithoutChecksum, barcodeWithChecksum);
    }

    [Fact]
    public void CreateEan13_WithFiveDigitExtension_AppendsExtension()
    {
        var barcode = Barcode.CreateEan13("400638133393", extension: "51234");

        Assert.Equal(BarcodeType.Ean13, barcode.Type);
        Assert.Equal(149, barcode.Width);
    }

    [Fact]
    public void CreateUpcA_ElevenOrTwelveDigits_ProduceSameBarcode()
    {
        var barcodeWithoutChecksum = Barcode.CreateUpcA("03600029145");
        var barcodeWithChecksum = Barcode.CreateUpcA("036000291452");

        Assert.Equal(BarcodeType.UpcA, barcodeWithoutChecksum.Type);
        Assert.Equal(95, barcodeWithoutChecksum.Width);
        AssertBarcodesEqual(barcodeWithoutChecksum, barcodeWithChecksum);
    }

    [Fact]
    public void CreateUpcA_WithTwoDigitExtension_AppendsExtension()
    {
        var barcode = Barcode.CreateUpcA("03600029145", extension: "12");

        Assert.Equal(BarcodeType.UpcA, barcode.Type);
        Assert.Equal(124, barcode.Width); // 95 modules, a 9-module gap, a 20-module add-on
    }

    [Fact]
    public void CreateUpcA_InvalidExtensionLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateUpcA("03600029145", extension: "1234"));
    }

    [Fact]
    public void CreateCodabar_ReturnsExpectedMetadata()
    {
        var barcode = Barcode.CreateCodabar("40156");

        Assert.Equal(BarcodeType.Codabar, barcode.Type);
        Assert.Equal(1, barcode.Height);
        Assert.True(barcode[0, 0]);
        Assert.True(barcode[0, barcode.Width - 1]);
    }

    [Fact]
    public void CreateCodabar_AlternativeGuards_AreSupported()
    {
        var barcode1 = Barcode.CreateCodabar("40156", startCharacter: 'A', stopCharacter: 'B');
        var barcode2 = Barcode.CreateCodabar("40156", startCharacter: 'T', stopCharacter: 'N');

        AssertBarcodesEqual(barcode1, barcode2);
    }

    [Fact]
    public void CreateCodabar_InvalidPayload_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateCodabar("ABC"));
    }

    [Fact]
    public void CreateCodabar_InvalidGuard_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateCodabar("123", startCharacter: 'X'));
    }

    [Fact]
    public void CreateItf_ReturnsExpectedMetadata()
    {
        var barcode = Barcode.CreateItf("123456");

        Assert.Equal(BarcodeType.Itf, barcode.Type);
        Assert.Equal(1, barcode.Height);
        Assert.Equal(63, barcode.Width);
        Assert.True(barcode[0, 0]);
        Assert.True(barcode[0, barcode.Width - 1]);
    }

    [Fact]
    public void CreateItf_OddLength_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateItf("12345"));
    }

    [Fact]
    public void CreateItf_NonDigitCharacter_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateItf("12A4"));
    }

    [Fact]
    public void ToSvg_Code93_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateCode93(Code93SmallData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Code93_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateCode93(Code93LargeData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToPng_Code93_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateCode93(Code93SmallData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_Code93_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateCode93(Code93LargeData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToSvg_Ean8_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateEan8(Ean8SmallData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Ean8_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateEan8(Ean8LargeData, extension: "51234");
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToPng_Ean8_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateEan8(Ean8SmallData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_Ean8_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateEan8(Ean8LargeData, extension: "51234");
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToSvg_Ean13_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateEan13(Ean13SmallData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Ean13_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateEan13(Ean13LargeData, extension: "51234");
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToPng_Ean13_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateEan13(Ean13SmallData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_Ean13_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateEan13(Ean13LargeData, extension: "51234");
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToSvg_UpcA_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateUpcA(UpcASmallData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_UpcA_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateUpcA(UpcALargeData, extension: "51234");
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToPng_UpcA_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateUpcA(UpcASmallData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_UpcA_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateUpcA(UpcALargeData, extension: "51234");
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToSvg_Codabar_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateCodabar(CodabarSmallData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Codabar_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateCodabar(CodabarLargeData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToPng_Codabar_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateCodabar(CodabarSmallData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_Codabar_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateCodabar(CodabarLargeData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToSvg_Itf_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateItf(ItfSmallData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Itf_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateItf(ItfLargeData);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToPng_Itf_SmallData_Snapshot()
    {
        var barcode = Barcode.CreateItf(ItfSmallData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_Itf_LargeData_Snapshot()
    {
        var barcode = Barcode.CreateItf(ItfLargeData);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void Documentation_UpcE()
    {
        #region Barcode_UpcE
        // The number system (0 or 1) and 6 digits: the check digit is the one of the UPC-A number it stands for
        var barcode = Barcode.CreateUpcE("0425261");
        Assert.Equal("04252614", barcode.Text);

        // The same symbol from the UPC-A number, 04210000526, which has the zeros UPC-E removes
        var fromUpcA = Barcode.CreateUpcE("04210000526");
        Assert.Equal("04252614", fromUpcA.Text);

        // A UPC-A number without these zeros has no UPC-E form
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateUpcE("03600029145"));

        var png = barcode.ToPng(new BarcodePngOptions { ModuleHeight = 60 });
        #endregion

        DocumentationImages.AssertPng("UpcE", png);
    }

    [Fact]
    public void Documentation_Gs1DataBar()
    {
        #region Barcode_Gs1DataBar
        // A GTIN of 13 digits (the check digit is added) or 14 digits
        var omnidirectional = Barcode.CreateGs1DataBar("09501101530003");
        var limited = Barcode.CreateGs1DataBarLimited("09501101530003"); // the GTIN starts with 0 or 1
        Assert.Equal("(01)09501101530003", omnidirectional.Text);
        Assert.Equal((96, 74), (omnidirectional.Width, limited.Width));

        // The stacked variants have one row per module of height: 5 + 1 + 7 for GS1 DataBar Stacked
        var stacked = Barcode.CreateGs1DataBarStacked("09501101530003");
        Assert.True(stacked.IsStacked);
        Assert.Equal((50, 13), (stacked.Width, stacked.Height));

        // The renderers draw the rows of a stacked barcode as high as a module is wide
        var png = stacked.ToPng(new BarcodePngOptions { ModuleWidth = 3 });
        #endregion

        DocumentationImages.AssertPng("Gs1DataBarStacked", png);
    }

    [Fact]
    public void Documentation_Gs1DataBarExpanded()
    {
        #region Barcode_Gs1DataBarExpanded
        // A GTIN, an expiration date and a batch number
        Gs1Element[] elements = [new("01", "09501101530003"), new("17", "250101"), new("10", "ABC123")];
        var barcode = Barcode.CreateGs1DataBarExpanded(elements);
        Assert.Equal("(01)09501101530003(17)250101(10)ABC123", barcode.Text);

        // The same data in rows of 4 segments: 34-module rows, separated by 3 rows of separator patterns
        var stacked = Barcode.CreateGs1DataBarExpandedStacked(4, elements);
        Assert.Equal((102, 3 * 34 + 2 * 3), (stacked.Width, stacked.Height));

        var png = stacked.ToPng(new BarcodePngOptions { ModuleWidth = 2 });
        #endregion

        DocumentationImages.AssertPng("Gs1DataBarExpandedStacked", png);
    }

    [Theory]
    [InlineData("425261")]
    [InlineData("0425261")]
    [InlineData("04252614")]
    [InlineData("04210000526")]
    [InlineData("042100005264")]
    public void CreateUpcE_AllInputForms_ProduceSameBarcode(string data)
    {
        AssertBarcodesEqual(Barcode.CreateUpcE("04252614"), Barcode.CreateUpcE(data));
    }

    [Theory]
    [InlineData("0123456", "01234565", "101011001100100110111101001110101110010101111010101")]
    [InlineData("1234567", "12345670", "101001001101111010100011011100100001010010001010101")]
    [InlineData("03600000029", "03602900", "101010000100001010100111001001100010110001101010101")]
    public void CreateUpcE_ProducesTheExpectedModules(string data, string text, string modules)
    {
        // Each pattern matches the zint encoder: the parities give the number system and the check digit
        var barcode = Barcode.CreateUpcE(data);
        Assert.Equal(text, barcode.Text);
        Assert.Equal(modules, BarcodeModulePattern.Render(barcode));
    }

    [Fact]
    public void CreateUpcE_WithExtension_AppendsExtension()
    {
        var barcode = Barcode.CreateUpcE("0123456", extension: "12");
        Assert.Equal("01234565 12", barcode.Text);
        Assert.Equal(51 + 9 + 20, barcode.Width);
    }

    [Theory]
    [InlineData("01234")] // too short
    [InlineData("012345678")] // between UPC-E and UPC-A lengths
    [InlineData("2123456")] // number system 2
    [InlineData("01234564")] // invalid check digit
    [InlineData("0182433")] // 01820000043 is written 0180432 in UPC-E
    [InlineData("03600029145")] // a UPC-A number without the zeros of UPC-E
    [InlineData("012345A")]
    public void CreateUpcE_InvalidData_ThrowsArgumentException(string value)
    {
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateUpcE(value));
    }

    [Fact]
    public void CreateGs1DataBar_ProducesTheExpectedModules()
    {
        // The patterns match the zint encoder
        Assert.Equal(
            "010000010100000101000111110000010111101101011100100011011101000101100000000111001110110111001101",
            BarcodeModulePattern.Render(Barcode.CreateGs1DataBar("0950110153001")));
        Assert.Equal(
            "01000110011000110110101001110100101011010011010010010110001101110011001101",
            BarcodeModulePattern.Render(Barcode.CreateGs1DataBarLimited("1501234567890")));
        Assert.Equal(
            "01001000011000110110111111110000101110000110010100011010000001100010101111110000111010011100000010010100111110111001100011111100001011101100000100100100011110010110001011111111001110001101111010000101",
            BarcodeModulePattern.Render(Barcode.CreateGs1DataBarExpanded(new Gs1Element("01", "98898765432106"), new Gs1Element("3202", "012345"), new Gs1Element("15", "991231"))));
    }

    [Theory]
    [InlineData("0658200098646", "01011001011011100011000010110110101010110001010000000100010100000101010101")] // right character in group 6
    [InlineData("0000001979845", "01010101010100000010000001110101110100101001010101010101000000100000000101")] // right character at the start of group 6
    public void CreateGs1DataBarLimited_AllCharacterGroups_ProduceTheExpectedModules(string value, string modules)
    {
        // The patterns match the zint encoder
        Assert.Equal(modules, BarcodeModulePattern.Render(Barcode.CreateGs1DataBarLimited(value)));
    }

    [Fact]
    public void CreateGs1DataBarStacked_ProducesTheExpectedRows()
    {
        // The rows and their heights in modules; the patterns match the zint encoder
        Assert.Equal(
            [
                ("01000001010000010100011111000001011110110101110010", 5),
                ("00001100101010101010101010111010100001001010100000", 1),
                ("10100011011101000101100000000111001110110111001101", 7),
            ],
            GetRows(Barcode.CreateGs1DataBarStacked("0950110153001")));

        Assert.Equal(
            [
                ("01000001010000010100011111000001011110110101110010", 33),
                ("00001110101111101010100000101010100001001010000000", 1),
                ("00000101010101010101010101010101010101010101010000", 1),
                ("00001100100010111010010101010000110001001000110000", 1),
                ("10100011011101000101100000000111001110110111001101", 33),
            ],
            GetRows(Barcode.CreateGs1DataBarStackedOmnidirectional("0950110153001")));
    }

    [Fact]
    public void CreateGs1DataBarExpandedStacked_ProducesTheExpectedRows()
    {
        // 7 segments in rows of 4: the second row is read from the right
        var barcode = Barcode.CreateGs1DataBarExpandedStacked(4, new Gs1Element("01", "09501101530003"), new Gs1Element("17", "250101"), new Gs1Element("10", "ABC123"));

        Assert.Equal(BarcodeType.Gs1DataBarExpandedStacked, barcode.Type);
        Assert.True(barcode.IsStacked);
        Assert.Equal(
            [
                ("010100110011111000101111111100001011100001110110010100000011011010001011111000000110011010000001001101", 34),
                ("000011001100000111010000000010100100011110001001101011111100100101110100000101010001100101111110110000", 1),
                ("000001010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010000", 1),
                ("000011000101001111100001000000001011100100110111110110011011111000101001010000001010011101110100010000", 1),
                ("101100111010110000011100111111110100011011001000001001100100000111010100001111110001100010001011100010", 34),
                ("000011000101001111100001000000001011100100110111110110011011111000101001010000001010011101110100010000", 1),
                ("000001010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010000", 1),
                ("000010111101011110010100101010100101110011110100011001001100011111010100000000010000000000000000000000", 1),
                ("010001000010100001100011000000001010001100001011100110110011100000101011111111100110100000000000000000", 34),
            ],
            GetRows(barcode));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(20)]
    public void CreateGs1DataBarExpandedStacked_HasRowsOfTheRequestedSegments(int segmentsPerRow)
    {
        var elements = new[] { new Gs1Element("01", "09501101530003"), new Gs1Element("10", "ABCDEFGHIJ"), new Gs1Element("21", "0123456789") };
        var singleRow = Barcode.CreateGs1DataBarExpanded(elements);
        var barcode = Barcode.CreateGs1DataBarExpandedStacked(segmentsPerRow, elements);

        // A symbol character is 17 modules wide, a finder pattern 15, and a row has 2 guard modules on each side
        var symbolCharacters = Enumerable.Range(3, 20).Single(count => 4 + (17 * count) + (15 * ((count + 1) / 2)) == singleRow.Width);
        Assert.Equal(symbolCharacters <= segmentsPerRow ? singleRow.Width : (segmentsPerRow / 2 * 49) + 4, barcode.Width);
        Assert.Equal(singleRow.Text, barcode.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("095011015300")] // 12 digits
    [InlineData("09501101530004")] // invalid check digit
    [InlineData("0950110153000A")]
    public void CreateGs1DataBar_InvalidData_ThrowsArgumentException(string value)
    {
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateGs1DataBar(value));
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateGs1DataBarStacked(value));
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateGs1DataBarStackedOmnidirectional(value));
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateGs1DataBarLimited(value));
    }

    [Fact]
    public void CreateGs1DataBarLimited_GtinStartingWith2_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>("data", () => Barcode.CreateGs1DataBarLimited("2001234567890"));
    }

    [Fact]
    public void CreateGs1DataBarExpanded_InvalidElements_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>("elements", () => Barcode.CreateGs1DataBarExpanded());
        Assert.Throws<ArgumentException>("elements", () => Barcode.CreateGs1DataBarExpanded([null!]));

        // 70 digits fill the 21 data characters of a symbol, 71 do not fit
        Assert.Throws<ArgumentException>("elements", () => Barcode.CreateGs1DataBarExpanded(new Gs1Element("90", new string('1', 69))));
        Assert.Equal((22 * 17) + (11 * 15) + 4, Barcode.CreateGs1DataBarExpanded(new Gs1Element("90", new string('1', 68))).Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(22)]
    public void CreateGs1DataBarExpandedStacked_InvalidSegmentsPerRow_ThrowsArgumentOutOfRangeException(int segments)
    {
        Assert.Throws<ArgumentOutOfRangeException>("segmentsPerRow", () => Barcode.CreateGs1DataBarExpandedStacked(segments, new Gs1Element("10", "1")));
    }

    [Fact]
    public void StackedBarcode_RowsAreAsHighAsAModuleIsWide()
    {
        var barcode = Barcode.CreateGs1DataBarStacked("0950110153001");

        var svg = barcode.ToSvg(new BarcodeSvgOptions { ModuleWidth = 3, ModuleHeight = 50, QuietZoneModules = 1 });
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 156 39\">", svg, StringComparison.Ordinal);
        Assert.Contains("M6 0h3v15h-3z", svg, StringComparison.Ordinal); // the first bar of the 5-module top row, after a space

        using var image = Image.Load<Rgba32>(barcode.ToPng(new BarcodePngOptions { ModuleWidth = 3, ModuleHeight = 50, QuietZoneModules = 1 }));
        Assert.Equal((156, 39), (image.Width, image.Height));
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var module = x / 3 - 1;
                var expected = module >= 0 && module < barcode.Width && barcode[y / 3, module];
                Assert.Equal(expected, image.Frames[0][x, y].R == 0, message: $"({x}, {y})");
            }
        }
    }

    [Fact]
    public void LinearBarcode_IsNotStacked()
    {
        Assert.False(Barcode.CreateGs1DataBar("0950110153001").IsStacked);
        Assert.False(Barcode.CreateUpcE("0123456").IsStacked);
        Assert.Equal(1, Barcode.CreateGs1DataBarExpanded(new Gs1Element("10", "1")).Height);
    }

    // The distinct rows of a barcode and their heights in modules
    private static List<(string Modules, int Height)> GetRows(Barcode barcode)
    {
        var rows = new List<(string Modules, int Height)>();
        for (var row = 0; row < barcode.Height; row++)
        {
            var modules = new StringBuilder(barcode.Width);
            for (var column = 0; column < barcode.Width; column++)
            {
                modules.Append(barcode[row, column] ? '1' : '0');
            }

            if (rows.Count > 0 && rows[^1].Modules == modules.ToString())
            {
                rows[^1] = (rows[^1].Modules, rows[^1].Height + 1);
            }
            else
            {
                rows.Add((modules.ToString(), 1));
            }
        }

        return rows;
    }

    private static void AssertBarcodesEqual(Barcode expected, Barcode actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Width, actual.Width);

        for (var row = 0; row < expected.Height; row++)
        {
            for (var col = 0; col < expected.Width; col++)
            {
                Assert.Equal(expected[row, col], actual[row, col]);
            }
        }
    }

    // ───── Module patterns ─────
    //
    // Produced from this encoder's output and each confirmed to decode back to the documented
    // payload with an external reader (ZXing) while the tests were written. These symbologies had
    // no coverage beyond image snapshots, so a wrong parity or check digit would have rendered
    // cleanly and gone unnoticed.

    [Fact]
    public void CreateCode93_ProducesTheExpectedModules()
    {
        // Decodes as "ABC123".
        Assert.Equal(
            "1010111101101010001101001001101000101010010001010001001010000101011011001000010101010111101",
            BarcodeModulePattern.Render(Barcode.CreateCode93("ABC123")));
    }

    [Fact]
    public void CreateEan8_ProducesTheExpectedModules()
    {
        // Decodes as "55123457" - the encoder appends the check digit.
        Assert.Equal(
            "1010110001011000100110010010011010101000010101110010011101000100101",
            BarcodeModulePattern.Render(Barcode.CreateEan8("5512345")));
    }

    [Fact]
    public void CreateEan13_ProducesTheExpectedModules()
    {
        // Decodes as "4006381333931". The first digit is carried by the left-group parity rather
        // than by a symbol of its own, so a wrong parity row silently changes the leading digit.
        Assert.Equal(
            "10100011010100111010111101111010001001011001101010100001010000101000010111010010000101100110101",
            BarcodeModulePattern.Render(Barcode.CreateEan13("400638133393")));
    }

    [Fact]
    public void CreateUpcA_ProducesTheExpectedModules()
    {
        // Decodes as "036000291452".
        Assert.Equal(
            "10100011010111101010111100011010001101000110101010110110011101001100110101110010011101101100101",
            BarcodeModulePattern.Render(Barcode.CreateUpcA("03600029145")));
    }

    [Fact]
    public void CreateCodabar_ProducesTheExpectedModules()
    {
        // Decodes as "A40156B" when the reader is asked to keep the start and stop guards.
        Assert.Equal(
            "10110010010101101001010101001101010110010110101001010010101101001001011",
            BarcodeModulePattern.Render(Barcode.CreateCodabar("40156", startCharacter: 'A', stopCharacter: 'B')));
    }

    [Fact]
    public void CreateItf_ProducesTheExpectedModules()
    {
        // Decodes as "12345678".
        Assert.Equal(
            "101011101000101011100011101110100010100011101000111000101010001010111000111011101",
            BarcodeModulePattern.Render(Barcode.CreateItf("12345678")));
    }
}
