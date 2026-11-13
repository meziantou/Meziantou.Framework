---
title: Reading
description: Read the data of QR codes, Data Matrix, PDF417 and Aztec symbols from their modules, with error correction.
---

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

## Reading modules

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

A symbol read in a mirror has its rows and columns swapped: the decoder reads it too, and sets
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
