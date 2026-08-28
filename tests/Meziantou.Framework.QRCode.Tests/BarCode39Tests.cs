using System.Buffers.Binary;
using System.Xml.Linq;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Tests;

public class BarCode39Tests
{
    [Fact]
    public void Create_ReturnsExpectedMetadata()
    {
        var barcode = Barcode.CreateCode39("A");

        Assert.Equal(BarcodeType.Code39, barcode.Type);
        Assert.Equal(1, barcode.Height);
        Assert.Equal(38, barcode.Width);
        Assert.True(barcode[0, 0]);
        Assert.True(barcode[0, barcode.Width - 1]);
    }

    [Fact]
    public void Create_WithChecksum_AppendsMod43Character()
    {
        var withoutChecksum = Barcode.CreateCode39("A");
        var withChecksum = Barcode.CreateCode39("A", includeChecksum: true);

        Assert.Equal(38, withoutChecksum.Width);
        Assert.Equal(51, withChecksum.Width);
    }

    [Fact]
    public void Create_UnsupportedCharacter_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateCode39("abc"));
    }

    [Fact]
    public void Create_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Barcode.CreateCode39(data: null!));
    }

    [Fact]
    public void Create_EmptyData_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Barcode.CreateCode39(""));
    }

    [Fact]
    public void ToSvg_Code39_DefaultOptions_Snapshot()
    {
        var barcode = Barcode.CreateCode39("A");
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_Code39_CustomColors_Snapshot()
    {
        var barcode = Barcode.CreateCode39("A");
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
    public void ToSvg_Code39_ComplexPayloadWithChecksum_Snapshot()
    {
        var barcode = Barcode.CreateCode39("ABC-123/+%", includeChecksum: true);
        var svg = barcode.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_DefaultOptions_UsesHorizontalQuietZoneOnly()
    {
        var barcode = Barcode.CreateCode39("A");
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
        var barcode = Barcode.CreateCode39("A");

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToSvg(new BarcodeSvgOptions { ModuleWidth = moduleWidth }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToSvg_InvalidModuleHeight_ThrowsArgumentOutOfRangeException(int moduleHeight)
    {
        var barcode = Barcode.CreateCode39("A");

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToSvg(new BarcodeSvgOptions { ModuleHeight = moduleHeight }));
    }

    [Fact]
    public void ToPng_Code39_DefaultOptions_Snapshot()
    {
        var barcode = Barcode.CreateCode39("A");
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_Code39_ComplexPayloadWithChecksum_Snapshot()
    {
        var barcode = Barcode.CreateCode39("ABC-123/+%", includeChecksum: true);
        var png = barcode.ToPng();

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Fact]
    public void ToPng_DefaultOptions_UsesHorizontalQuietZoneOnly()
    {
        var barcode = Barcode.CreateCode39("A");
        var png = barcode.ToPng();

        var (width, height) = GetPngSize(png);
        Assert.Equal((barcode.Width + (2 * 10)) * 2, width);
        Assert.Equal(barcode.Height * 80, height);
    }

    [Fact]
    public void ToPng_Code39_CustomColors_Snapshot()
    {
        var barcode = Barcode.CreateCode39("A");
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

    [Fact]
    public void ToPng_Code39_TransparentColors_Snapshot()
    {
        var barcode = Barcode.CreateCode39("A");
        var png = barcode.ToPng(new BarcodePngOptions
        {
            ModuleWidth = 1,
            ModuleHeight = 1,
            QuietZoneModules = 0,
            DarkColor = Color.Transparent,
            LightColor = Color.FromArgb(0x80, 0xff, 0xff, 0x00),
        });

        Snapshot.Validate(png, SnapshotType.Png);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToPng_InvalidModuleWidth_ThrowsArgumentOutOfRangeException(int moduleWidth)
    {
        var barcode = Barcode.CreateCode39("A");

        Assert.Throws<ArgumentOutOfRangeException>(() => barcode.ToPng(new BarcodePngOptions { ModuleWidth = moduleWidth }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToPng_InvalidModuleHeight_ThrowsArgumentOutOfRangeException(int moduleHeight)
    {
        var barcode = Barcode.CreateCode39("A");

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
    // The expected patterns were produced from this encoder's output and each one was confirmed
    // to decode back to the documented payload with an external reader (ZXing) while the tests
    // were written. Pinning the modules keeps a change that still renders a plausible-looking
    // barcode, but an unreadable one, from passing.

    [Theory]
    [InlineData("ABC-123", "10010110110101101010010110101101001011011011010010101001010110110110100101011010110010101101101100101010100101101101")]
    [InlineData("A", "10010110110101101010010110100101101101")]
    public void CreateCode39_ProducesTheExpectedModules(string data, string expected)
    {
        Assert.Equal(expected, BarcodeModulePattern.Render(Barcode.CreateCode39(data)));
    }

    [Fact]
    public void CreateCode39_WithChecksum_ProducesTheExpectedModules()
    {
        // Decodes as "ABC-123" once the reader validates and strips the Mod 43 check digit.
        var barcode = Barcode.CreateCode39("ABC-123", includeChecksum: true);

        Assert.Equal(
            "100101101101011010100101101011010010110110110100101010010101101101101001010110101100101011011011001010101100110101010100101101101",
            BarcodeModulePattern.Render(barcode));
    }
}
