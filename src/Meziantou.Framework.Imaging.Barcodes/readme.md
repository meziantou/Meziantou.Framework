# Meziantou.Framework.Imaging.Barcodes

Renders the QR codes, Data Matrix, PDF417 and Aztec symbols, and barcodes of [`Meziantou.Framework.Barcodes`](https://www.nuget.org/packages/Meziantou.Framework.Barcodes) as [`Meziantou.Framework.Imaging`](https://www.nuget.org/packages/Meziantou.Framework.Imaging) images. The resulting `Image<Rgba32>` can be saved in any format the library supports (PNG, WebP, JPEG, GIF, AVIF, ...), resized, or drawn onto another image.

The image is the scene of the code (`CreateScene` of `Meziantou.Framework.Barcodes`), drawn with `Meziantou.Framework.Imaging.VectorGraphics`: it is the same drawing as the SVG of `ToSvg`, with the same options, logo and human-readable text. Use this package for PNG files and the other image formats, to draw a code on an image, or to read the codes of an image.

## Usage

The types are in the `Meziantou.Framework.Imaging.Barcodes` namespace. The examples also use the
`Meziantou.Framework.Barcodes` (QR codes, barcodes and their options), `Meziantou.Framework.VectorGraphics` (colors and
paints), `Meziantou.Framework.Imaging` (images) and `Meziantou.Framework.Imaging.Formats` (encoders) namespaces.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_QRCodeToImage;format=md-fenced-code-block;dedent=true -->
```csharp
var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.M);

// Default options: 10 pixels per module, a 4-module quiet zone, black on white
using var image = qrCode.ToImage();
Assert.Equal((330, 330), (image.Width, image.Height));

// Save to a file (image.Save("qrcode.webp", new WebPEncoder())) or to a stream
using var webp = new MemoryStream();
image.Save(webp, new WebPEncoder());

// Custom options, the same as the SVG renderer takes: any paint, and no background
using var customImage = qrCode.ToImage(new QRCodeRenderOptions
{
    ModuleSize = 4,
    QuietZoneModules = 2,
    DarkPaint = new SceneColor(0x00, 0x33, 0x66),
    LightPaint = null,
});
Assert.Equal((116, 116), (customImage.Width, customImage.Height));
Assert.Equal(Colors.Transparent, customImage.Frames[0][0, 0]);
Assert.Equal(new Rgba32(0x00, 0x33, 0x66), customImage.Frames[0][8, 8]);

// An indexed PNG stores the two colors in a palette
using var png = new MemoryStream();
customImage.Save(png, new PngEncoder { PixelLayout = PngPixelLayout.Indexed });
```
<!-- endref -->

Barcodes work the same way:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_BarcodeToImage;format=md-fenced-code-block;dedent=true -->
```csharp
var barcode = Barcode.CreateEan13("5901234123457");
using var image = barcode.ToImage(new BarcodeRenderOptions { ModuleWidth = 3, ModuleHeight = 100 });
Assert.Equal((345, 100), (image.Width, image.Height));
```
<!-- endref -->

## Drawing on a canvas

`DrawQRCode` and `DrawBarcode` draw on the `DrawingCanvas` of `Image.Draw`, as vector shapes: the transform, the clip and
the layers of the canvas apply, and any `ScenePaint` (color, gradient, pattern) paints the modules. `MeasureQRCode` and
`MeasureBarcode` give the size of the drawing beforehand, to lay out the page. With `SnapToPixels` (the default), the
module sizes and the position are rounded to whole pixels, so that every module has the same size and sharp edges.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_DrawQRCode;format=md-fenced-code-block;dedent=true -->
```csharp
using var ticket = new Image<Rgba32>(400, 200, Colors.White); // or Image.Load<Rgba32>("ticket.png")
var qrCode = QRCode.Create("TICKET-0042");
var options = new QRCodeRenderOptions { ModuleSize = 4, QuietZoneModules = 2 };

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
`BarcodeRenderOptions.Text` is set, in the bundled OCR-B font by default:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_DrawBarcode;format=md-fenced-code-block;dedent=true -->
```csharp
var barcode = Barcode.CreateEan13("590123412345");
var options = new BarcodeRenderOptions
{
    ModuleWidth = 2,
    ModuleHeight = 60,
    QuietZoneModules = 9,
    DarkPaint = new SceneColor(0x00, 0x33, 0x66),
    Text = new BarcodeTextOptions { Fonts = BarcodeFonts.OcrB, FontSize = 24 }, // prints the digits of barcode.Text in groups under the bars
};

var size = BarcodeImageRenderer.MeasureBarcode(barcode, options);
Assert.Equal(226, size.Width); // (95 modules + 2 x 9) x 2 pixels

using var label = new Image<Rgba32>(300, 140, Colors.White);
label.Draw(canvas => canvas.DrawBarcode(barcode, new PointD(20, 20), options));
Assert.Equal(new Rgba32(0x00, 0x33, 0x66), label.Frames[0][20 + 18, 50]); // the first guard bar
```
<!-- endref -->

## Reading codes from images

`ReadQRCodes` finds and decodes the QR codes, Micro QR codes and rMQR codes of an image, `ReadBarcodes2D` its Data Matrix, PDF417 and Aztec symbols, and `ReadBarcodes` its linear barcodes: generated, printed or scanned codes, which can be rotated, scaled or in mild perspective.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_ReadQRCodes;format=md-fenced-code-block;dedent=true -->
```csharp
using var image = QRCode.Create("https://www.meziantou.net").ToImage(); // or Image.Load("photo.png")

var results = image.ReadQRCodes();

var result = Assert.Single(results);
Assert.Equal("https://www.meziantou.net", result.Text);

// The corners of the symbol in the image: top-left, top-right, bottom-right, bottom-left
var topLeft = result.Corners[0];
Assert.True(topLeft.DistanceTo(new PointD(40, 40)) < 1); // after the quiet zone of 4 modules of 10 pixels
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_ReadBarcodes2D;format=md-fenced-code-block;dedent=true -->
```csharp
using var image = Barcode2D.CreateDataMatrix("https://www.meziantou.net").ToImage(); // or Image.Load("label.png")

var results = image.ReadBarcodes2D();

var result = Assert.Single(results);
Assert.Equal((Barcode2DType.DataMatrix, "https://www.meziantou.net"), (result.Result.Type, result.Text));
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_ReadBarcodes;format=md-fenced-code-block;dedent=true -->
```csharp
using var image = Barcode.CreateEan13("590123412345").ToImage(); // or Image.Load("product.jpg")

var results = image.ReadBarcodes();

var result = Assert.Single(results);
Assert.Equal((BarcodeType.Ean13, "5901234123457"), (result.Result.Type, result.Text));
```
<!-- endref -->

## Fonts

`BarcodeFonts.OcrA` and `BarcodeFonts.OcrB`, in `Meziantou.Framework.Barcodes`, are the two fonts of optical character recognition, the ones usually
printed under barcodes (OCR-B under EAN and UPC). They are generated from the character shapes of the standards, at their
Size I and with their nominal stroke width, by `tools/Meziantou.Framework.Imaging.Barcodes.FontGenerator`:

- OCR-A from the dimensioned drawings of ANSI X3.17-1981: the centerlines in fractions of the character height and
  width, painted with a round pen, with the sharp corners the drawings show. The 2 degree lean of the stems of some
  small letters is not reproduced.
- OCR-B from the 4:1 illustrations of ANSI X3.49-1975 (the American counterpart of ECMA-11 and ISO 1073-2): the
  centerlines are traced from the illustrations, so they follow them within a few hundredths of a millimeter.

Both are monospaced (an advance of 0.6 em, the 0.1 inch pitch of the standards), map the printable ASCII characters,
and are licensed with this package. OCR-A also maps the Hook, Chair and Fork (U+2440 to U+2442), which stand for the
Tilde, the Grave Accent and the Underline; OCR-B also maps the letters of its extended repertoire (Ä, Å, Æ, Ö, Ø, Ü, Ñ,
IJ, å, æ, ø, ij, ß) and §, ¤, £, ¥, ¨ and ´. The preferred shapes of the punctuation are drawn, not the alternate ones.

## Options

`ToImage`, `Measure*` and the canvas drawing take the options of every renderer of `Meziantou.Framework.Barcodes`
(`QRCodeRenderOptions`, `BarcodeRenderOptions`, `Barcode2DRenderOptions`), and the `SceneImageOptions` of the image
renderer, whose `Scale` draws more pixels per unit. The sizes are in canvas units: pixels, unless a transform or a scale
applies.
