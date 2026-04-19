using System.Xml.Linq;
using Meziantou.Framework.SnapshotTesting;
using Xunit;

namespace Meziantou.Framework.Tests;

public class QRCodeSvgRendererTests
{
    [Fact]
    public void ToSvg_DefaultOptions()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg();

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_DefaultOptions_UsesModuleSize10_QuietZone4()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg();

        // Default: ModuleSize=10, QuietZoneModules=4 -> (21 + 8) * 10 = 290
        Assert.Contains("viewBox=\"0 0 290 290\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#000000\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#ffffff\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSvg_SmallQRCode_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_MicroQR_Snapshot()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var options = new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 };
        var svg = qr.ToSvg(options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_RMQR_Snapshot()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var options = new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 };
        var svg = qr.ToSvg(options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_CustomModuleSize()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 5, QuietZoneModules = 0 });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_CustomColors()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions
        {
            DarkColor = "#ff0000",
            LightColor = "#00ff00",
            QuietZoneModules = 0,
            ModuleSize = 1,
        });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_QuietZone()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 4 });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_FullSnapshot()
    {
        var qr = QRCode.Create("HELLO", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 });

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void ToSvg_LongPayload_ProducesValidSvg()
    {
        var data = """
            067786869696809yhjkhghgkghkjfgtyrfturtfjrftuyrgfjhftyurdftyjfjghftyurfuytfftkiftifif56565685756586586565658655685878j74564567456454534345456475478545456jk7879797@hhhhhhhhhhhhhhhhhhhhhhhhhhhhhrffffffffffffffffffffffffffffffhhhhhhhhhhhhjbjgbhgkjgkhghjghjgkjgkgkgkkhgbnadssdfsghjkghhhhhhhhhhhhhhhhhhhhhjjjjjjjjjjjjjjjgyuyghgjghjgjkgkhkkjhkljhgjlghlk067786869696809yhjkhghgkghkjfgtyrfturtfjrftuyrgfjhftycfffhsdjfhdskjksfsdfsdddddddddddsddddddddddddddddjjjjjjjjjjjjjjjjjjjjjjjjjjjjjkurdftyjfjghftyurfuytfftkiftifif56565685756586586565658655685878j74564567456454534345456475478545456jk7879797@hhhhhhhhhhhhhhhhhhhhhhhhhhhhhrfffffffffffffffffffffffffffffffffffffffghhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhhjbjgbhgkjgkhghjghjgkjgkgkgkkhgbnadssdfsghjkghhhhhhhhhhhhhhhhhhhhhjjjjjjjjjjjjjjjgyuyghgjghjgjkgkhkkjhkljhgjlghlk
            """;
        var qr = QRCode.Create(data, ErrorCorrectionLevel.M);
        var svg = qr.ToSvg();
        var document = XDocument.Parse(svg);

        Assert.Equal("svg", document.Root!.Name.LocalName);
    }

    [Fact]
    public void ToSvg_ModuleSizeZero_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 0 }));
    }

    [Fact]
    public void ToSvg_NegativeQuietZone_ThrowsArgumentOutOfRangeException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentOutOfRangeException>(() => qr.ToSvg(new QRCodeSvgOptions { QuietZoneModules = -1 }));
    }

    [Fact]
    public void ToSvg_NullDarkColor_ThrowsArgumentNullException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentNullException>(() => qr.ToSvg(new QRCodeSvgOptions { DarkColor = null! }));
    }

    [Fact]
    public void ToSvg_NullLightColor_ThrowsArgumentNullException()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        Assert.Throws<ArgumentNullException>(() => qr.ToSvg(new QRCodeSvgOptions { LightColor = null! }));
    }
}
