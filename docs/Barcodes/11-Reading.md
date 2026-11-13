---
title: Reading
description: Read the data of QR codes, Micro QR codes and rMQR codes from their modules, with error correction and structured append.
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
