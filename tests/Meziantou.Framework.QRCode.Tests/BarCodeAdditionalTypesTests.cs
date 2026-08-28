using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Tests;

public class BarCodeAdditionalTypesTests
{
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
        Assert.Equal(122, barcode.Width);
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
