using System.Xml.Linq;
using Meziantou.Framework.InlineSnapshotTesting;
using Xunit;

namespace Meziantou.Framework.Tests;

public class QRCodeSvgRendererTests
{
    [Fact]
    public void ToSvg_DefaultOptions()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg();

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 290 290"><rect width="290" height="290" fill="#ffffff"/><path fill="#000000" d="M40 40h70v10h-70zM130 40h10v10h-10zM150 40h20v10h-20zM180 40h70v10h-70zM40 50h10v10h-10zM100 50h10v10h-10zM120 50h20v10h-20zM150 50h10v10h-10zM180 50h10v10h-10zM240 50h10v10h-10zM40 60h10v10h-10zM60 60h30v10h-30zM100 60h10v10h-10zM120 60h20v10h-20zM160 60h10v10h-10zM180 60h10v10h-10zM200 60h30v10h-30zM240 60h10v10h-10zM40 70h10v10h-10zM60 70h30v10h-30zM100 70h10v10h-10zM130 70h10v10h-10zM150 70h10v10h-10zM180 70h10v10h-10zM200 70h30v10h-30zM240 70h10v10h-10zM40 80h10v10h-10zM60 80h30v10h-30zM100 80h10v10h-10zM120 80h10v10h-10zM160 80h10v10h-10zM180 80h10v10h-10zM200 80h30v10h-30zM240 80h10v10h-10zM40 90h10v10h-10zM100 90h10v10h-10zM120 90h10v10h-10zM150 90h20v10h-20zM180 90h10v10h-10zM240 90h10v10h-10zM40 100h70v10h-70zM120 100h10v10h-10zM140 100h10v10h-10zM160 100h10v10h-10zM180 100h70v10h-70zM120 110h50v10h-50zM40 120h20v10h-20zM70 120h10v10h-10zM100 120h20v10h-20zM130 120h20v10h-20zM180 120h30v10h-30zM220 120h20v10h-20zM50 130h10v10h-10zM70 130h30v10h-30zM120 130h30v10h-30zM180 130h10v10h-10zM210 130h20v10h-20zM240 130h10v10h-10zM40 140h40v10h-40zM90 140h40v10h-40zM140 140h10v10h-10zM160 140h20v10h-20zM240 140h10v10h-10zM40 150h20v10h-20zM70 150h30v10h-30zM130 150h10v10h-10zM150 150h10v10h-10zM230 150h10v10h-10zM50 160h10v10h-10zM70 160h10v10h-10zM100 160h10v10h-10zM130 160h10v10h-10zM160 160h10v10h-10zM180 160h10v10h-10zM200 160h30v10h-30zM240 160h10v10h-10zM120 170h10v10h-10zM140 170h20v10h-20zM190 170h30v10h-30zM230 170h10v10h-10zM40 180h70v10h-70zM120 180h20v10h-20zM170 180h10v10h-10zM190 180h10v10h-10zM210 180h10v10h-10zM230 180h10v10h-10zM40 190h10v10h-10zM100 190h10v10h-10zM140 190h40v10h-40zM190 190h30v10h-30zM230 190h20v10h-20zM40 200h10v10h-10zM60 200h30v10h-30zM100 200h10v10h-10zM130 200h30v10h-30zM180 200h30v10h-30zM220 200h30v10h-30zM40 210h10v10h-10zM60 210h30v10h-30zM100 210h10v10h-10zM120 210h40v10h-40zM210 210h40v10h-40zM40 220h10v10h-10zM60 220h30v10h-30zM100 220h10v10h-10zM140 220h10v10h-10zM160 220h10v10h-10zM200 220h10v10h-10zM240 220h10v10h-10zM40 230h10v10h-10zM100 230h10v10h-10zM120 230h30v10h-30zM170 230h20v10h-20zM200 230h10v10h-10zM220 230h10v10h-10zM240 230h10v10h-10zM40 240h70v10h-70zM120 240h20v10h-20zM150 240h20v10h-20zM200 240h30v10h-30z"/></svg>""");
    }

    [Fact]
    public void ToSvg_DefaultOptions_UsesModuleSize10_QuietZone4()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg();

        // Default: ModuleSize=10, QuietZoneModules=4 → (21 + 8) * 10 = 290
        Assert.Contains("viewBox=\"0 0 290 290\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#000000\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#ffffff\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSvg_SmallQRCode_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 });

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 21 21"><rect width="21" height="21" fill="#ffffff"/><path fill="#000000" d="M0 0h7v1h-7zM9 0h1v1h-1zM11 0h2v1h-2zM14 0h7v1h-7zM0 1h1v1h-1zM6 1h1v1h-1zM9 1h3v1h-3zM14 1h1v1h-1zM20 1h1v1h-1zM0 2h1v1h-1zM2 2h3v1h-3zM6 2h1v1h-1zM8 2h2v1h-2zM11 2h2v1h-2zM14 2h1v1h-1zM16 2h3v1h-3zM20 2h1v1h-1zM0 3h1v1h-1zM2 3h3v1h-3zM6 3h1v1h-1zM9 3h1v1h-1zM11 3h1v1h-1zM14 3h1v1h-1zM16 3h3v1h-3zM20 3h1v1h-1zM0 4h1v1h-1zM2 4h3v1h-3zM6 4h1v1h-1zM10 4h1v1h-1zM12 4h1v1h-1zM14 4h1v1h-1zM16 4h3v1h-3zM20 4h1v1h-1zM0 5h1v1h-1zM6 5h1v1h-1zM12 5h1v1h-1zM14 5h1v1h-1zM20 5h1v1h-1zM0 6h7v1h-7zM8 6h1v1h-1zM10 6h1v1h-1zM12 6h1v1h-1zM14 6h7v1h-7zM8 7h2v1h-2zM11 7h2v1h-2zM0 8h3v1h-3zM4 8h8v1h-8zM13 8h2v1h-2zM18 8h1v1h-1zM1 9h3v1h-3zM7 9h1v1h-1zM10 9h1v1h-1zM14 9h1v1h-1zM18 9h2v1h-2zM3 10h4v1h-4zM10 10h1v1h-1zM12 10h1v1h-1zM16 10h1v1h-1zM20 10h1v1h-1zM1 11h1v1h-1zM5 11h1v1h-1zM7 11h4v1h-4zM14 11h1v1h-1zM18 11h2v1h-2zM0 12h3v1h-3zM4 12h3v1h-3zM8 12h2v1h-2zM12 12h1v1h-1zM14 12h1v1h-1zM16 12h1v1h-1zM18 12h3v1h-3zM8 13h2v1h-2zM11 13h1v1h-1zM13 13h1v1h-1zM15 13h1v1h-1zM17 13h1v1h-1zM0 14h7v1h-7zM8 14h1v1h-1zM10 14h2v1h-2zM13 14h3v1h-3zM17 14h2v1h-2zM0 15h1v1h-1zM6 15h1v1h-1zM8 15h6v1h-6zM15 15h3v1h-3zM19 15h1v1h-1zM0 16h1v1h-1zM2 16h3v1h-3zM6 16h1v1h-1zM8 16h1v1h-1zM11 16h1v1h-1zM13 16h3v1h-3zM17 16h2v1h-2zM20 16h1v1h-1zM0 17h1v1h-1zM2 17h3v1h-3zM6 17h1v1h-1zM9 17h1v1h-1zM14 17h1v1h-1zM18 17h2v1h-2zM0 18h1v1h-1zM2 18h3v1h-3zM6 18h1v1h-1zM8 18h3v1h-3zM12 18h1v1h-1zM16 18h1v1h-1zM20 18h1v1h-1zM0 19h1v1h-1zM6 19h1v1h-1zM8 19h1v1h-1zM10 19h1v1h-1zM14 19h1v1h-1zM18 19h3v1h-3zM0 20h7v1h-7zM8 20h3v1h-3zM12 20h1v1h-1zM14 20h1v1h-1zM16 20h1v1h-1zM18 20h1v1h-1zM20 20h1v1h-1z"/></svg>""");
    }

    [Fact]
    public void ToSvg_MicroQR_Snapshot()
    {
        var qr = QRCode.CreateMicroQR("123", ErrorCorrectionLevel.L);
        var options = new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 };
        var svg = qr.ToSvg(options);

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 11 11"><rect width="11" height="11" fill="#ffffff"/><path fill="#000000" d="M0 0h7v1h-7zM8 0h1v1h-1zM10 0h1v1h-1zM0 1h1v1h-1zM6 1h1v1h-1zM0 2h1v1h-1zM2 2h3v1h-3zM6 2h1v1h-1zM8 2h1v1h-1zM0 3h1v1h-1zM2 3h3v1h-3zM6 3h1v1h-1zM10 3h1v1h-1zM0 4h1v1h-1zM2 4h3v1h-3zM6 4h1v1h-1zM8 4h3v1h-3zM0 5h1v1h-1zM6 5h1v1h-1zM9 5h2v1h-2zM0 6h7v1h-7zM8 6h1v1h-1zM8 7h3v1h-3zM0 8h2v1h-2zM4 8h3v1h-3zM9 8h2v1h-2zM1 9h1v1h-1zM3 9h2v1h-2zM6 9h1v1h-1zM8 9h1v1h-1zM0 10h4v1h-4zM5 10h4v1h-4z"/></svg>""");
    }

    [Fact]
    public void ToSvg_RMQR_Snapshot()
    {
        var qr = QRCode.CreateRMQR("AB", ErrorCorrectionLevel.M);
        var options = new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 };
        var svg = qr.ToSvg(options);

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 27 11"><rect width="27" height="11" fill="#ffffff"/><path fill="#000000" d="M0 0h7v1h-7zM8 0h1v1h-1zM10 0h1v1h-1zM12 0h1v1h-1zM14 0h1v1h-1zM16 0h1v1h-1zM18 0h1v1h-1zM20 0h1v1h-1zM22 0h1v1h-1zM24 0h3v1h-3zM0 1h1v1h-1zM6 1h1v1h-1zM8 1h3v1h-3zM12 1h1v1h-1zM14 1h2v1h-2zM20 1h1v1h-1zM22 1h1v1h-1zM24 1h3v1h-3zM0 2h1v1h-1zM2 2h3v1h-3zM6 2h1v1h-1zM9 2h4v1h-4zM15 2h2v1h-2zM19 2h1v1h-1zM21 2h1v1h-1zM25 2h2v1h-2zM0 3h1v1h-1zM2 3h3v1h-3zM6 3h1v1h-1zM10 3h1v1h-1zM12 3h2v1h-2zM16 3h4v1h-4zM21 3h3v1h-3zM0 4h1v1h-1zM2 4h3v1h-3zM6 4h1v1h-1zM11 4h1v1h-1zM14 4h1v1h-1zM18 4h2v1h-2zM22 4h3v1h-3zM26 4h1v1h-1zM0 5h1v1h-1zM6 5h1v1h-1zM9 5h2v1h-2zM12 5h4v1h-4zM17 5h2v1h-2zM20 5h3v1h-3zM24 5h2v1h-2zM0 6h7v1h-7zM8 6h3v1h-3zM12 6h2v1h-2zM15 6h1v1h-1zM17 6h1v1h-1zM21 6h2v1h-2zM24 6h1v1h-1zM26 6h1v1h-1zM8 7h1v1h-1zM11 7h1v1h-1zM13 7h3v1h-3zM20 7h1v1h-1zM22 7h1v1h-1zM26 7h1v1h-1zM0 8h10v1h-10zM11 8h1v1h-1zM13 8h1v1h-1zM16 8h2v1h-2zM19 8h1v1h-1zM21 8h2v1h-2zM24 8h1v1h-1zM26 8h1v1h-1zM0 9h4v1h-4zM5 9h1v1h-1zM9 9h4v1h-4zM14 9h1v1h-1zM18 9h1v1h-1zM20 9h1v1h-1zM22 9h1v1h-1zM26 9h1v1h-1zM0 10h3v1h-3zM4 10h1v1h-1zM6 10h1v1h-1zM8 10h1v1h-1zM10 10h1v1h-1zM12 10h1v1h-1zM14 10h1v1h-1zM16 10h1v1h-1zM18 10h1v1h-1zM20 10h1v1h-1zM22 10h1v1h-1zM24 10h1v1h-1zM26 10h1v1h-1z"/></svg>""");
    }

    [Fact]
    public void ToSvg_CustomModuleSize()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 5, QuietZoneModules = 0 });

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 105 105"><rect width="105" height="105" fill="#ffffff"/><path fill="#000000" d="M0 0h35v5h-35zM45 0h5v5h-5zM55 0h10v5h-10zM70 0h35v5h-35zM0 5h5v5h-5zM30 5h5v5h-5zM45 5h15v5h-15zM70 5h5v5h-5zM100 5h5v5h-5zM0 10h5v5h-5zM10 10h15v5h-15zM30 10h5v5h-5zM40 10h10v5h-10zM55 10h10v5h-10zM70 10h5v5h-5zM80 10h15v5h-15zM100 10h5v5h-5zM0 15h5v5h-5zM10 15h15v5h-15zM30 15h5v5h-5zM45 15h5v5h-5zM55 15h5v5h-5zM70 15h5v5h-5zM80 15h15v5h-15zM100 15h5v5h-5zM0 20h5v5h-5zM10 20h15v5h-15zM30 20h5v5h-5zM50 20h5v5h-5zM60 20h5v5h-5zM70 20h5v5h-5zM80 20h15v5h-15zM100 20h5v5h-5zM0 25h5v5h-5zM30 25h5v5h-5zM60 25h5v5h-5zM70 25h5v5h-5zM100 25h5v5h-5zM0 30h35v5h-35zM40 30h5v5h-5zM50 30h5v5h-5zM60 30h5v5h-5zM70 30h35v5h-35zM40 35h10v5h-10zM55 35h10v5h-10zM0 40h15v5h-15zM20 40h40v5h-40zM65 40h10v5h-10zM90 40h5v5h-5zM5 45h15v5h-15zM35 45h5v5h-5zM50 45h5v5h-5zM70 45h5v5h-5zM90 45h10v5h-10zM15 50h20v5h-20zM50 50h5v5h-5zM60 50h5v5h-5zM80 50h5v5h-5zM100 50h5v5h-5zM5 55h5v5h-5zM25 55h5v5h-5zM35 55h20v5h-20zM70 55h5v5h-5zM90 55h10v5h-10zM0 60h15v5h-15zM20 60h15v5h-15zM40 60h10v5h-10zM60 60h5v5h-5zM70 60h5v5h-5zM80 60h5v5h-5zM90 60h15v5h-15zM40 65h10v5h-10zM55 65h5v5h-5zM65 65h5v5h-5zM75 65h5v5h-5zM85 65h5v5h-5zM0 70h35v5h-35zM40 70h5v5h-5zM50 70h10v5h-10zM65 70h15v5h-15zM85 70h10v5h-10zM0 75h5v5h-5zM30 75h5v5h-5zM40 75h30v5h-30zM75 75h15v5h-15zM95 75h5v5h-5zM0 80h5v5h-5zM10 80h15v5h-15zM30 80h5v5h-5zM40 80h5v5h-5zM55 80h5v5h-5zM65 80h15v5h-15zM85 80h10v5h-10zM100 80h5v5h-5zM0 85h5v5h-5zM10 85h15v5h-15zM30 85h5v5h-5zM45 85h5v5h-5zM70 85h5v5h-5zM90 85h10v5h-10zM0 90h5v5h-5zM10 90h15v5h-15zM30 90h5v5h-5zM40 90h15v5h-15zM60 90h5v5h-5zM80 90h5v5h-5zM100 90h5v5h-5zM0 95h5v5h-5zM30 95h5v5h-5zM40 95h5v5h-5zM50 95h5v5h-5zM70 95h5v5h-5zM90 95h15v5h-15zM0 100h35v5h-35zM40 100h15v5h-15zM60 100h5v5h-5zM70 100h5v5h-5zM80 100h5v5h-5zM90 100h5v5h-5zM100 100h5v5h-5z"/></svg>""");
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

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 21 21"><rect width="21" height="21" fill="#00ff00"/><path fill="#ff0000" d="M0 0h7v1h-7zM9 0h1v1h-1zM11 0h2v1h-2zM14 0h7v1h-7zM0 1h1v1h-1zM6 1h1v1h-1zM9 1h3v1h-3zM14 1h1v1h-1zM20 1h1v1h-1zM0 2h1v1h-1zM2 2h3v1h-3zM6 2h1v1h-1zM8 2h2v1h-2zM11 2h2v1h-2zM14 2h1v1h-1zM16 2h3v1h-3zM20 2h1v1h-1zM0 3h1v1h-1zM2 3h3v1h-3zM6 3h1v1h-1zM9 3h1v1h-1zM11 3h1v1h-1zM14 3h1v1h-1zM16 3h3v1h-3zM20 3h1v1h-1zM0 4h1v1h-1zM2 4h3v1h-3zM6 4h1v1h-1zM10 4h1v1h-1zM12 4h1v1h-1zM14 4h1v1h-1zM16 4h3v1h-3zM20 4h1v1h-1zM0 5h1v1h-1zM6 5h1v1h-1zM12 5h1v1h-1zM14 5h1v1h-1zM20 5h1v1h-1zM0 6h7v1h-7zM8 6h1v1h-1zM10 6h1v1h-1zM12 6h1v1h-1zM14 6h7v1h-7zM8 7h2v1h-2zM11 7h2v1h-2zM0 8h3v1h-3zM4 8h8v1h-8zM13 8h2v1h-2zM18 8h1v1h-1zM1 9h3v1h-3zM7 9h1v1h-1zM10 9h1v1h-1zM14 9h1v1h-1zM18 9h2v1h-2zM3 10h4v1h-4zM10 10h1v1h-1zM12 10h1v1h-1zM16 10h1v1h-1zM20 10h1v1h-1zM1 11h1v1h-1zM5 11h1v1h-1zM7 11h4v1h-4zM14 11h1v1h-1zM18 11h2v1h-2zM0 12h3v1h-3zM4 12h3v1h-3zM8 12h2v1h-2zM12 12h1v1h-1zM14 12h1v1h-1zM16 12h1v1h-1zM18 12h3v1h-3zM8 13h2v1h-2zM11 13h1v1h-1zM13 13h1v1h-1zM15 13h1v1h-1zM17 13h1v1h-1zM0 14h7v1h-7zM8 14h1v1h-1zM10 14h2v1h-2zM13 14h3v1h-3zM17 14h2v1h-2zM0 15h1v1h-1zM6 15h1v1h-1zM8 15h6v1h-6zM15 15h3v1h-3zM19 15h1v1h-1zM0 16h1v1h-1zM2 16h3v1h-3zM6 16h1v1h-1zM8 16h1v1h-1zM11 16h1v1h-1zM13 16h3v1h-3zM17 16h2v1h-2zM20 16h1v1h-1zM0 17h1v1h-1zM2 17h3v1h-3zM6 17h1v1h-1zM9 17h1v1h-1zM14 17h1v1h-1zM18 17h2v1h-2zM0 18h1v1h-1zM2 18h3v1h-3zM6 18h1v1h-1zM8 18h3v1h-3zM12 18h1v1h-1zM16 18h1v1h-1zM20 18h1v1h-1zM0 19h1v1h-1zM6 19h1v1h-1zM8 19h1v1h-1zM10 19h1v1h-1zM14 19h1v1h-1zM18 19h3v1h-3zM0 20h7v1h-7zM8 20h3v1h-3zM12 20h1v1h-1zM14 20h1v1h-1zM16 20h1v1h-1zM18 20h1v1h-1zM20 20h1v1h-1z"/></svg>""");
    }

    [Fact]
    public void ToSvg_QuietZone()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 4 });

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 29 29"><rect width="29" height="29" fill="#ffffff"/><path fill="#000000" d="M4 4h7v1h-7zM13 4h1v1h-1zM15 4h2v1h-2zM18 4h7v1h-7zM4 5h1v1h-1zM10 5h1v1h-1zM13 5h3v1h-3zM18 5h1v1h-1zM24 5h1v1h-1zM4 6h1v1h-1zM6 6h3v1h-3zM10 6h1v1h-1zM12 6h2v1h-2zM15 6h2v1h-2zM18 6h1v1h-1zM20 6h3v1h-3zM24 6h1v1h-1zM4 7h1v1h-1zM6 7h3v1h-3zM10 7h1v1h-1zM13 7h1v1h-1zM15 7h1v1h-1zM18 7h1v1h-1zM20 7h3v1h-3zM24 7h1v1h-1zM4 8h1v1h-1zM6 8h3v1h-3zM10 8h1v1h-1zM14 8h1v1h-1zM16 8h1v1h-1zM18 8h1v1h-1zM20 8h3v1h-3zM24 8h1v1h-1zM4 9h1v1h-1zM10 9h1v1h-1zM16 9h1v1h-1zM18 9h1v1h-1zM24 9h1v1h-1zM4 10h7v1h-7zM12 10h1v1h-1zM14 10h1v1h-1zM16 10h1v1h-1zM18 10h7v1h-7zM12 11h2v1h-2zM15 11h2v1h-2zM4 12h3v1h-3zM8 12h8v1h-8zM17 12h2v1h-2zM22 12h1v1h-1zM5 13h3v1h-3zM11 13h1v1h-1zM14 13h1v1h-1zM18 13h1v1h-1zM22 13h2v1h-2zM7 14h4v1h-4zM14 14h1v1h-1zM16 14h1v1h-1zM20 14h1v1h-1zM24 14h1v1h-1zM5 15h1v1h-1zM9 15h1v1h-1zM11 15h4v1h-4zM18 15h1v1h-1zM22 15h2v1h-2zM4 16h3v1h-3zM8 16h3v1h-3zM12 16h2v1h-2zM16 16h1v1h-1zM18 16h1v1h-1zM20 16h1v1h-1zM22 16h3v1h-3zM12 17h2v1h-2zM15 17h1v1h-1zM17 17h1v1h-1zM19 17h1v1h-1zM21 17h1v1h-1zM4 18h7v1h-7zM12 18h1v1h-1zM14 18h2v1h-2zM17 18h3v1h-3zM21 18h2v1h-2zM4 19h1v1h-1zM10 19h1v1h-1zM12 19h6v1h-6zM19 19h3v1h-3zM23 19h1v1h-1zM4 20h1v1h-1zM6 20h3v1h-3zM10 20h1v1h-1zM12 20h1v1h-1zM15 20h1v1h-1zM17 20h3v1h-3zM21 20h2v1h-2zM24 20h1v1h-1zM4 21h1v1h-1zM6 21h3v1h-3zM10 21h1v1h-1zM13 21h1v1h-1zM18 21h1v1h-1zM22 21h2v1h-2zM4 22h1v1h-1zM6 22h3v1h-3zM10 22h1v1h-1zM12 22h3v1h-3zM16 22h1v1h-1zM20 22h1v1h-1zM24 22h1v1h-1zM4 23h1v1h-1zM10 23h1v1h-1zM12 23h1v1h-1zM14 23h1v1h-1zM18 23h1v1h-1zM22 23h3v1h-3zM4 24h7v1h-7zM12 24h3v1h-3zM16 24h1v1h-1zM18 24h1v1h-1zM20 24h1v1h-1zM22 24h1v1h-1zM24 24h1v1h-1z"/></svg>""");
    }

    [Fact]
    public void ToSvg_FullSnapshot()
    {
        var qr = QRCode.Create("HELLO", ErrorCorrectionLevel.L);
        var svg = qr.ToSvg(new QRCodeSvgOptions { ModuleSize = 1, QuietZoneModules = 0 });

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 21 21"><rect width="21" height="21" fill="#ffffff"/><path fill="#000000" d="M0 0h7v1h-7zM9 0h1v1h-1zM12 0h1v1h-1zM14 0h7v1h-7zM0 1h1v1h-1zM6 1h1v1h-1zM8 1h1v1h-1zM11 1h1v1h-1zM14 1h1v1h-1zM20 1h1v1h-1zM0 2h1v1h-1zM2 2h3v1h-3zM6 2h1v1h-1zM9 2h1v1h-1zM14 2h1v1h-1zM16 2h3v1h-3zM20 2h1v1h-1zM0 3h1v1h-1zM2 3h3v1h-3zM6 3h1v1h-1zM8 3h1v1h-1zM11 3h1v1h-1zM14 3h1v1h-1zM16 3h3v1h-3zM20 3h1v1h-1zM0 4h1v1h-1zM2 4h3v1h-3zM6 4h1v1h-1zM10 4h3v1h-3zM14 4h1v1h-1zM16 4h3v1h-3zM20 4h1v1h-1zM0 5h1v1h-1zM6 5h1v1h-1zM8 5h3v1h-3zM12 5h1v1h-1zM14 5h1v1h-1zM20 5h1v1h-1zM0 6h7v1h-7zM8 6h1v1h-1zM10 6h1v1h-1zM12 6h1v1h-1zM14 6h7v1h-7zM10 7h3v1h-3zM0 8h5v1h-5zM6 8h4v1h-4zM12 8h2v1h-2zM15 8h1v1h-1zM17 8h1v1h-1zM19 8h1v1h-1zM0 9h1v1h-1zM2 9h1v1h-1zM5 9h1v1h-1zM12 9h1v1h-1zM15 9h1v1h-1zM17 9h4v1h-4zM6 10h1v1h-1zM8 10h4v1h-4zM13 10h1v1h-1zM16 10h2v1h-2zM20 10h1v1h-1zM1 11h5v1h-5zM7 11h1v1h-1zM10 11h1v1h-1zM15 11h2v1h-2zM1 12h2v1h-2zM5 12h2v1h-2zM8 12h4v1h-4zM13 12h1v1h-1zM16 12h1v1h-1zM18 12h1v1h-1zM8 13h2v1h-2zM11 13h4v1h-4zM17 13h1v1h-1zM19 13h2v1h-2zM0 14h7v1h-7zM8 14h1v1h-1zM10 14h1v1h-1zM12 14h1v1h-1zM14 14h2v1h-2zM18 14h1v1h-1zM20 14h1v1h-1zM0 15h1v1h-1zM6 15h1v1h-1zM9 15h6v1h-6zM17 15h1v1h-1zM20 15h1v1h-1zM0 16h1v1h-1zM2 16h3v1h-3zM6 16h1v1h-1zM8 16h1v1h-1zM12 16h1v1h-1zM15 16h1v1h-1zM18 16h1v1h-1zM0 17h1v1h-1zM2 17h3v1h-3zM6 17h1v1h-1zM8 17h1v1h-1zM10 17h1v1h-1zM12 17h1v1h-1zM15 17h1v1h-1zM18 17h1v1h-1zM0 18h1v1h-1zM2 18h3v1h-3zM6 18h1v1h-1zM8 18h1v1h-1zM11 18h1v1h-1zM13 18h1v1h-1zM16 18h1v1h-1zM18 18h1v1h-1zM0 19h1v1h-1zM6 19h1v1h-1zM8 19h1v1h-1zM10 19h1v1h-1zM15 19h2v1h-2zM18 19h1v1h-1zM20 19h1v1h-1zM0 20h7v1h-7zM8 20h1v1h-1zM11 20h1v1h-1zM13 20h1v1h-1zM16 20h1v1h-1zM18 20h1v1h-1z"/></svg>""");
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
