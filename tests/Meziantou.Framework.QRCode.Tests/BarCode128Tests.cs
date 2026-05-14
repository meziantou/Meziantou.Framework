using System.Buffers.Binary;
using System.Globalization;
using System.Xml.Linq;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Tests;

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
            DarkColor = "#ff0000",
            LightColor = "#00ff00",
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
    public void ToPng_Code128_InvertedColors_Snapshot()
    {
        var barcode = Barcode.CreateCode128(NumericData);
        var png = barcode.ToPng(new BarcodePngOptions
        {
            ModuleWidth = 1,
            ModuleHeight = 1,
            QuietZoneModules = 0,
            InvertColors = true,
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
}
