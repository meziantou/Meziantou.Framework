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

## Drawing on a canvas

`DrawQRCode` and `DrawBarcode` draw on the `DrawingCanvas` of `Image.Draw`, as vector shapes: the transform, the clip and
the layers of the canvas apply, and any `Brush` (solid, gradient, image, pattern) paints the modules. `MeasureQRCode` and
`MeasureBarcode` give the size of the drawing beforehand, to lay out the page. With `SnapToPixels` (the default), the
module sizes and the position are rounded to whole pixels, so that every module has the same size and sharp edges.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_DrawQRCode;format=md-fenced-code-block;dedent=true -->
```csharp
using var ticket = new Image<Rgba32>(400, 200, Colors.White); // or Image.Load<Rgba32>("ticket.png")
var qrCode = QRCode.Create("TICKET-0042");
var options = new QRCodeDrawingOptions { ModuleSize = 4, QuietZoneModules = 2 };

// The size of the drawing, quiet zone included, to lay out the page before drawing
var size = QRCodeImageRenderer.MeasureQRCode(qrCode, options);
Assert.Equal((100, 100), (size.Width, size.Height)); // (21 modules + 2 x 2) x 4 pixels

// Drawn as a vector shape: the transform and the clip of the canvas apply, any brush works
ticket.Draw(canvas => canvas.DrawQRCode(qrCode, new PointD(400 - 16 - size.Width, 16), options));
Assert.Equal(Colors.Black, ticket.Frames[0][284 + 8, 16 + 8]); // the top-left finder pattern, after the 8-pixel quiet zone

// Save to a file (ticket.Save("ticket-with-qrcode.png")) or to a stream
using var png = new MemoryStream();
ticket.Save(png, new PngEncoder());
```
<!-- endref -->

`DrawBarcode` draws the bars, and the human-readable text (`Barcode.Text`, with its check digit) under them when
`BarcodeDrawingOptions.Font` is set:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_DrawBarcode;format=md-fenced-code-block;dedent=true -->
```csharp
using var label = new Image<Rgba32>(300, 120, Colors.White);
var barcode = Barcode.CreateEan13("590123412345");
var options = new BarcodeDrawingOptions
{
    ModuleWidth = 2,
    ModuleHeight = 60,
    QuietZoneModules = 9,
    DarkBrush = new SolidBrush(new Rgba32(0x00, 0x33, 0x66)),
};

label.Draw(canvas =>
{
    var bounds = canvas.DrawBarcode(barcode, new PointD(20, 20), options);
    Assert.Equal(new RectangleD(20, 20, 226, 60), bounds); // (95 modules + 2 x 9) x 2 pixels wide
});
Assert.Equal(new Rgba32(0x00, 0x33, 0x66), label.Frames[0][20 + 18, 50]); // the first guard bar
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

| `QRCodeDrawingOptions` | Default | Description |
|------------------------|---------|-------------|
| `ModuleSize` | 10 | Size of each module, in canvas units |
| `QuietZoneModules` | 4 | Number of quiet zone modules around the QR code |
| `DarkBrush` | black | Brush of dark modules, in canvas coordinates |
| `LightBrush` | white | Brush of light modules and of the quiet zone; `null` keeps the background |
| `SnapToPixels` | `true` | Round the module size and the position to whole units |

| `BarcodeDrawingOptions` | Default | Description |
|-------------------------|---------|-------------|
| `ModuleWidth` | 2 | Width of each module, in canvas units |
| `ModuleHeight` | 80 | Height of the bars, in canvas units |
| `QuietZoneModules` | 10 | Number of quiet zone modules on the left and right sides |
| `DarkBrush` | black | Brush of the bars, in canvas coordinates |
| `LightBrush` | white | Brush of the spaces, of the quiet zone and of the text background; `null` keeps the background |
| `SnapToPixels` | `true` | Round the module sizes and the position to whole units |
| `Font` | `null` | Font of the human-readable text under the bars; `null` draws no text |
| `FontSize` | 14 | Size of the text, in units per em |
| `TextBrush` | `null` | Brush of the text; `null` uses `DarkBrush` |
| `TextSpacing` | 2 | Space between the bars and the text |
