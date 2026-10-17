# Meziantou.Framework.QRCode.Imaging

Renders the QR codes and barcodes of [`Meziantou.Framework.QRCode`](https://www.nuget.org/packages/Meziantou.Framework.QRCode) as [`Meziantou.Framework.Imaging`](https://www.nuget.org/packages/Meziantou.Framework.Imaging) images. The resulting `Image<Rgba32>` can be saved in any format the library supports (PNG, WebP, JPEG, GIF, AVIF, ...), resized, or drawn onto another image.

`Meziantou.Framework.QRCode` already writes PNG files on its own (`ToPng`), with no dependency. Use this package when you need another format or want to process the image further.

## Usage

```csharp
using Meziantou.Framework;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;

var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.M);

// Default options: 10 pixels per module, a 4-module quiet zone, black on white
using var image = qrCode.ToImage();
image.Save("qrcode.webp", new WebPEncoder());

// Custom options
using var customImage = qrCode.ToImage(new QRCodeImageOptions
{
    ModuleSize = 4,
    QuietZoneModules = 2,
    DarkColor = new Rgba32(0x00, 0x33, 0x66),
    LightColor = Colors.Transparent,
});

// An indexed PNG stores the two colors in a palette, as ToPng does
customImage.Save("qrcode.png", new PngEncoder { PixelLayout = PngPixelLayout.Indexed });
```

Barcodes work the same way:

```csharp
var barcode = Barcode.CreateEan13("5901234123457");
using var image = barcode.ToImage(new BarcodeImageOptions { ModuleWidth = 3, ModuleHeight = 100 });
```

Drawing a QR code onto an existing image:

```csharp
using var ticket = Image.Load<Rgba32>("ticket.png");
using var qrCodeImage = QRCode.Create("TICKET-0042").ToImage(new QRCodeImageOptions { ModuleSize = 4 });
ticket.Draw(canvas => canvas.DrawImage(qrCodeImage.Frames[0], new PointD(16, 16)));
ticket.Save("ticket-with-qrcode.png");
```

## Options

| `QRCodeImageOptions` | Default | Description |
|----------------------|---------|-------------|
| `ModuleSize` | 10 | Size of each module, in pixels |
| `QuietZoneModules` | 4 | Number of quiet zone modules around the QR code |
| `DarkColor` | `Colors.Black` | Color of dark modules |
| `LightColor` | `Colors.White` | Color of light modules and of the quiet zone |

| `BarcodeImageOptions` | Default | Description |
|-----------------------|---------|-------------|
| `ModuleWidth` | 2 | Width of each module, in pixels |
| `ModuleHeight` | 80 | Height of each module, in pixels |
| `QuietZoneModules` | 10 | Number of quiet zone modules on the left and right sides |
| `DarkColor` | `Colors.Black` | Color of dark modules |
| `LightColor` | `Colors.White` | Color of light modules and of the quiet zone |
