---
title: Reading
description: Read QR codes, Data Matrix, PDF417, Aztec symbols and linear barcodes from images, modules or scan lines.
---

The packages read the codes back: `Meziantou.Framework.Imaging.Barcodes` finds the codes of an image, and
`Meziantou.Framework.Barcodes` decodes every symbology from its modules, or the linear barcodes from the widths of their
bars.

## Reading images

`Meziantou.Framework.Imaging.Barcodes` reads the codes of the images of
[Meziantou.Framework.Imaging](../Imaging/01-Introduction.md): any format it loads. Install it:

<InstallNuGetPackage Id="Meziantou.Framework.Imaging.Barcodes" />

[ReadQRCodes](cref:Meziantou.Framework.Imaging.Barcodes.QRCodeImageReader.ReadQRCodes) finds the QR codes, Micro QR codes
and rMQR codes of an image and decodes them. It gives each one once, with its
[Result](cref:Meziantou.Framework.Imaging.Barcodes.QRCodeImageResult.Result) and its
[Corners](cref:Meziantou.Framework.Imaging.Barcodes.QRCodeImageResult.Corners) in the image.

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

The reader is made for clean images: generated symbols, printed and scanned, or photographed straight on. They can be
rotated, mirrored, scaled, slightly blurred or compressed, or in mild perspective; they need their quiet zone and modules
of at least 2 pixels. Symbols printed light on a dark background are read when the image has none printed dark on light.
A transparent background reads as white.
[QRCodeReadingOptions](cref:Meziantou.Framework.Imaging.Barcodes.QRCodeReadingOptions) limits the
[Types](cref:Meziantou.Framework.Imaging.Barcodes.QRCodeReadingOptions.Types) to read and the
[MaximumResults](cref:Meziantou.Framework.Imaging.Barcodes.QRCodeReadingOptions.MaximumResults).

[ReadBarcodes2D](cref:Meziantou.Framework.Imaging.Barcodes.Barcode2DImageReader.ReadBarcodes2D) reads the Data Matrix,
PDF417 and Aztec symbols of an image the same way, with
[Barcode2DReadingOptions](cref:Meziantou.Framework.Imaging.Barcodes.Barcode2DReadingOptions). Data Matrix and Aztec
symbols can be in any orientation; the rows of a PDF417 symbol must be about horizontal or vertical.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_ReadBarcodes2D;format=md-fenced-code-block;dedent=true -->
```csharp
using var image = Barcode2D.CreateDataMatrix("https://www.meziantou.net").ToImage(); // or Image.Load("label.png")

var results = image.ReadBarcodes2D();

var result = Assert.Single(results);
Assert.Equal((Barcode2DType.DataMatrix, "https://www.meziantou.net"), (result.Result.Type, result.Text));
```
<!-- endref -->

[ReadBarcodes](cref:Meziantou.Framework.Imaging.Barcodes.BarcodeImageReader.ReadBarcodes) reads the linear barcodes, with
[BarcodeReadingOptions](cref:Meziantou.Framework.Imaging.Barcodes.BarcodeReadingOptions). Parallel scan lines cross the
image horizontally, vertically and at 30°, 60°, 120° and 150°: a barcode turned by another angle is read when a line
crosses all its bars. Code 39, Codabar and ITF, which have no check character, must be crossed by two lines that agree.
The text printed under the bars is not read: it is not needed.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs;region=ImagingBarcodes_ReadBarcodes;format=md-fenced-code-block;dedent=true -->
```csharp
using var image = Barcode.CreateEan13("590123412345").ToImage(); // or Image.Load("product.jpg")

var results = image.ReadBarcodes();

var result = Assert.Single(results);
Assert.Equal((BarcodeType.Ean13, "5901234123457"), (result.Result.Type, result.Text));
```
<!-- endref -->

## Reading modules

[QRCodeDecoder](cref:Meziantou.Framework.Barcodes.QRCodeDecoder) reads the data of a QR code from its modules: it reads
the format information, fixes the errors that the error correction can fix, and decodes the segments. It reads the three
kinds of QR codes: standard QR codes, Micro QR codes and rMQR codes.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs;region=QRCodeDecoder_Decode;format=md-fenced-code-block;dedent=true -->
```csharp
var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.M);

var result = QRCodeDecoder.Decode(qrCode);

Assert.Equal("https://www.meziantou.net", result.Text);
Assert.Equal((QRCodeType.Standard, 2, ErrorCorrectionLevel.M), (result.Type, result.Version, result.ErrorCorrectionLevel));
```
<!-- endref -->

The decoder works on modules, not on pixels. Pass the modules as a matrix of `bool`, `true` for dark, indexed by row then
column, without the quiet zone. The size of the matrix gives the kind of symbol: a square of 21 to 177 modules is a
standard QR code, a square of 11 to 17 modules a Micro QR code, and a rectangle an rMQR code.

[QRCodeDecoder.TryDecode](cref:Meziantou.Framework.Barcodes.QRCodeDecoder.TryDecode) returns `false` when the modules are
not a QR code it can read; [QRCodeDecoder.Decode](cref:Meziantou.Framework.Barcodes.QRCodeDecoder.Decode) throws a
`FormatException` that tells why.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs;region=QRCodeDecoder_DecodeModules;format=md-fenced-code-block;dedent=true -->
```csharp
// The modules, true for dark, indexed by row then column, without quiet zone
var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.Q);
var modules = new bool[qrCode.Height, qrCode.Width];
for (var row = 0; row < qrCode.Height; row++)
{
    for (var column = 0; column < qrCode.Width; column++)
    {
        modules[row, column] = qrCode[row, column];
    }
}

// Damage the symbol: the error correction restores the data
for (var column = 9; column < 14; column++)
{
    modules[12, column] = !modules[12, column];
}

Assert.True(QRCodeDecoder.TryDecode(modules, out var result));
Assert.Equal("HELLO WORLD", result.Text);
Assert.True(result.CorrectedErrorCount > 0);
```
<!-- endref -->

A standard or Micro QR code read in a mirror has its rows and columns swapped: the decoder reads it too, and sets
[IsMirrored](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.IsMirrored).

## What the result holds

[QRCodeDecodeResult](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult) gives:

- [Text](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.Text): the data as text. The byte segments are decoded with
  the character set of the ECI designator that precedes them. Without one, they are decoded as UTF-8 when they are valid
  UTF-8, which is what [QRCode.Create](cref:Meziantou.Framework.Barcodes.QRCode.Create) writes, and as ISO/IEC 8859-1
  otherwise.
- [Segments](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.Segments): the segments of the data, with their
  [Mode](cref:Meziantou.Framework.Barcodes.QRCodeSegment.Mode), their
  [Text](cref:Meziantou.Framework.Barcodes.QRCodeSegment.Text), the raw
  [Bytes](cref:Meziantou.Framework.Barcodes.QRCodeSegment.Bytes) of a byte segment, and the
  [EciAssignmentNumber](cref:Meziantou.Framework.Barcodes.QRCodeSegment.EciAssignmentNumber) of an ECI designator. Read
  the bytes when the QR code holds binary data. Pass the segments to
  [QRCode.Create](cref:Meziantou.Framework.Barcodes.QRCode.Create) to encode the same data again.
- The parameters of the symbol: [Type](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.Type),
  [Version](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.Version),
  [ErrorCorrectionLevel](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.ErrorCorrectionLevel) and
  [MaskPattern](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.MaskPattern).
- [CorrectedErrorCount](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.CorrectedErrorCount): the number of
  codewords the error correction fixed. A high count means a damaged symbol. Micro QR version M1 only detects errors:
  the decoder rejects an M1 symbol with an error.

## Structured append

The symbols of a structured append sequence each hold a part of the data.
[StructuredAppend](cref:Meziantou.Framework.Barcodes.QRCodeDecodeResult.StructuredAppend) gives the place of a symbol in
its sequence, and [QRCodeDecoder.MergeStructuredAppend](cref:Meziantou.Framework.Barcodes.QRCodeDecoder.MergeStructuredAppend)
puts the data back together. It checks that every symbol of the sequence is there, once, and that the parity of the data
matches.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs;region=QRCodeDecoder_MergeStructuredAppend;format=md-fenced-code-block;dedent=true -->
```csharp
var text = "Structured append splits this text across several QR codes, and the reader puts it back together.";
var symbols = QRCode.CreateStructuredAppend(text, ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 2 });

// The symbols can be read in any order
var results = symbols.Reverse().Select(QRCodeDecoder.Decode).ToList();

Assert.Equal(text, QRCodeDecoder.MergeStructuredAppend(results));
```
<!-- endref -->

## Data Matrix, PDF417 and Aztec

[Barcode2DDecoder](cref:Meziantou.Framework.Barcodes.Barcode2DDecoder) reads Data Matrix (ECC 200), PDF417 and Aztec
symbols. Pass the modules and the type of the symbol: the decoder finds the orientation of a Data Matrix or Aztec symbol
from its finder pattern, mirrored included, and reads the rows of a PDF417 symbol from either side. A row of a PDF417
symbol can span several rows of the matrix.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/DataMatrixTests.cs;region=Barcode2DDecoder_Decode;format=md-fenced-code-block;dedent=true -->
```csharp
var dataMatrix = Barcode2D.CreateDataMatrix("https://www.meziantou.net");

var result = Barcode2DDecoder.Decode(dataMatrix);

Assert.Equal("https://www.meziantou.net", result.Text);
Assert.Equal(Barcode2DType.DataMatrix, result.Type);
```
<!-- endref -->

[Barcode2DDecodeResult](cref:Meziantou.Framework.Barcodes.Barcode2DDecodeResult) gives the
[Text](cref:Meziantou.Framework.Barcodes.Barcode2DDecodeResult.Text), the raw
[Bytes](cref:Meziantou.Framework.Barcodes.Barcode2DDecodeResult.Bytes), the
[EciAssignmentNumber](cref:Meziantou.Framework.Barcodes.Barcode2DDecodeResult.EciAssignmentNumber) of the first ECI
designator, and the [CorrectedErrorCount](cref:Meziantou.Framework.Barcodes.Barcode2DDecodeResult.CorrectedErrorCount).
The bytes are decoded as for QR codes: with the character set of their ECI designator, or as UTF-8 when they are valid
UTF-8, and as ISO/IEC 8859-1 otherwise.

A Data Matrix symbol that starts with a FNC1 character holds GS1 element strings:
[IsGs1](cref:Meziantou.Framework.Barcodes.Barcode2DDecodeResult.IsGs1) is `true`, and the text is the human-readable
interpretation, each application identifier in parentheses. The bytes keep the element strings, with a GS character for
each FNC1 separator.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/DataMatrixTests.cs;region=Barcode2DDecoder_DecodeGs1;format=md-fenced-code-block;dedent=true -->
```csharp
var gs1 = Barcode2D.CreateGs1DataMatrix(
    new Gs1Element("01", "09521234543213"),
    new Gs1Element("10", "ABC123"),
    new Gs1Element("21", "XYZ"));

var result = Barcode2DDecoder.Decode(gs1);

// The human-readable interpretation, and the bytes with a GS character for each FNC1 separator
Assert.True(result.IsGs1);
Assert.Equal("(01)09521234543213(10)ABC123(21)XYZ", result.Text);
Assert.Equal("010952123454321310ABC123\u001D21XYZ"u8.ToArray(), result.Bytes.ToArray());
```
<!-- endref -->

## Linear barcodes

[BarcodeDecoder](cref:Meziantou.Framework.Barcodes.BarcodeDecoder) reads Code 39, Code 93, Code 128, GS1-128, EAN-8,
EAN-13, UPC-A, UPC-E (with their 2- and 5-digit add-ons), Codabar, ITF and all the GS1 DataBar symbols. Pass a
[Barcode](cref:Meziantou.Framework.Barcodes.Barcode) or its modules: the text is the one that
[Barcode.Text](cref:Meziantou.Framework.Barcodes.Barcode.Text) gives for a generated barcode. The check characters of
Code 39 and the start and stop characters of Codabar are part of it, the add-on follows a space, and GS1 data is its
human-readable interpretation.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCode128Tests.cs;region=BarcodeDecoder_Decode;format=md-fenced-code-block;dedent=true -->
```csharp
var barcode = Barcode.CreateCode128("SKU-123456");

var result = BarcodeDecoder.Decode(barcode);

Assert.Equal((BarcodeType.Code128, "SKU-123456"), (result.Type, result.Text));
```
<!-- endref -->

An EAN-13 symbol whose first digit is 0 has the same bars as a UPC-A symbol: the decoder reports it as
[BarcodeType.UpcA](cref:Meziantou.Framework.Barcodes.BarcodeType.UpcA), with 12 digits.

### Scan lines

[BarcodeDecoder.DecodeScanLines](cref:Meziantou.Framework.Barcodes.BarcodeDecoder.DecodeScanLines) reads the barcodes
that scan lines cross. A scan line is the widths of its light and dark runs, starting with a light run, in any unit,
such as the pixels of a row of an image. The decoder reads each line in both directions, measures the widths relative to
each other, corrects bars that are uniformly too wide or too narrow, and requires the quiet zones around a symbol. The
rows of a stacked GS1 DataBar symbol are put together from the lines that cross them.

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCode128Tests.cs;region=BarcodeDecoder_DecodeScanLines;format=md-fenced-code-block;dedent=true -->
```csharp
// A scan line: the widths of its light and dark runs, starting with a light run, here 3 pixels per module
var barcode = Barcode.CreateEan13("590123412345");
var runs = new List<int> { 30 };
for (var column = 0; column < barcode.Width; column++)
{
    if (barcode[0, column] != (runs.Count % 2 == 0))
    {
        runs.Add(0);
    }

    runs[^1] += 3;
}

// The quiet zone after the last bar
runs.Add(30);

var results = BarcodeDecoder.DecodeScanLines([runs.ToArray()]);

var result = Assert.Single(results);
Assert.Equal((BarcodeType.Ean13, "5901234123457"), (result.Type, result.Text));
Assert.Equal((30, 30 + (95 * 3)), (result.Start, result.End));
```
<!-- endref -->

A result gives the lines that cross the barcode
([FirstScanLine](cref:Meziantou.Framework.Barcodes.BarcodeDecodeResult.FirstScanLine) and
[LastScanLine](cref:Meziantou.Framework.Barcodes.BarcodeDecodeResult.LastScanLine)) and its position on them
([Start](cref:Meziantou.Framework.Barcodes.BarcodeDecodeResult.Start) and
[End](cref:Meziantou.Framework.Barcodes.BarcodeDecodeResult.End)). Code 39 without check character, Codabar and ITF
have no check character: a damaged line can read as another text, so require several lines to agree.
