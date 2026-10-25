# Meziantou.Framework.Imaging.Barcodes

Renders the QR codes and barcodes of [`Meziantou.Framework.Barcodes`](https://www.nuget.org/packages/Meziantou.Framework.Barcodes) as [`Meziantou.Framework.Imaging`](https://www.nuget.org/packages/Meziantou.Framework.Imaging) images. The resulting `Image<Rgba32>` can be saved in any format the library supports (PNG, WebP, JPEG, GIF, AVIF, ...), resized, or drawn onto another image.

`Meziantou.Framework.Barcodes` already writes PNG files on its own (`ToPng`), with no dependency. Use this package when you need another format or want to process the image further.

## Usage

The types are in the `Meziantou.Framework.Imaging.Barcodes` namespace. The examples also use the
`Meziantou.Framework.Barcodes` (QR codes and barcodes), `Meziantou.Framework.Imaging` (images) and
`Meziantou.Framework.Imaging.Formats` (encoders) namespaces.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_QRCodeToImage;format=md-fenced-code-block;dedent=true -->
```csharp
var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.M);

// Default options: 10 pixels per module, a 4-module quiet zone, black on white
using var image = qrCode.ToImage();
Assert.Equal((330, 330), (image.Width, image.Height));

// Save to a file (image.Save("qrcode.webp", new WebPEncoder())) or to a stream
using var webp = new MemoryStream();
image.Save(webp, new WebPEncoder());

// Custom options
using var customImage = qrCode.ToImage(new QRCodeImageOptions
{
    ModuleSize = 4,
    QuietZoneModules = 2,
    DarkColor = new Rgba32(0x00, 0x33, 0x66),
    LightColor = Colors.Transparent,
});
Assert.Equal((116, 116), (customImage.Width, customImage.Height));
Assert.Equal(Colors.Transparent, customImage.Frames[0][0, 0]);
Assert.Equal(new Rgba32(0x00, 0x33, 0x66), customImage.Frames[0][8, 8]);

// An indexed PNG stores the two colors in a palette, as ToPng does
using var png = new MemoryStream();
customImage.Save(png, new PngEncoder { PixelLayout = PngPixelLayout.Indexed });
```
<!-- endref -->

Barcodes work the same way:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_BarcodeToImage;format=md-fenced-code-block;dedent=true -->
```csharp
var barcode = Barcode.CreateEan13("5901234123457");
using var image = barcode.ToImage(new BarcodeImageOptions { ModuleWidth = 3, ModuleHeight = 100 });
Assert.Equal((345, 100), (image.Width, image.Height));
```
<!-- endref -->

Drawing a QR code onto an existing image:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_DrawOntoImage;format=md-fenced-code-block;dedent=true -->
```csharp
using var ticket = new Image<Rgba32>(400, 200, Colors.White); // or Image.Load<Rgba32>("ticket.png")
using var qrCodeImage = QRCode.Create("TICKET-0042").ToImage(new QRCodeImageOptions { ModuleSize = 4 });
ticket.Draw(canvas => canvas.DrawImage(qrCodeImage.Frames[0], new PointD(16, 16)));
Assert.Equal(Colors.Black, ticket.Frames[0][32, 32]); // the top-left finder pattern, after the 16-pixel quiet zone

// Save to a file (ticket.Save("ticket-with-qrcode.png")) or to a stream
using var png = new MemoryStream();
ticket.Save(png, new PngEncoder());
```
<!-- endref -->

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
