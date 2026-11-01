---
title: Linear barcodes
description: Create Code 39, Code 93, Code 128, GS1-128, EAN-8, EAN-13, UPC-A, Codabar and ITF barcodes, with their check digits and add-ons.
---

## Create a barcode

Each symbology has its own method on [Barcode](cref:Meziantou.Framework.Barcodes.Barcode). The result has the [Type](cref:Meziantou.Framework.Barcodes.Barcode.Type), the [Width](cref:Meziantou.Framework.Barcodes.Barcode.Width) in modules, the [Height](cref:Meziantou.Framework.Barcodes.Barcode.Height) (1: a
linear barcode is one row of modules) and the [Text](cref:Meziantou.Framework.Barcodes.Barcode.Text) printed under the bars:

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCodeAdditionalTypesTests.cs;region=Barcode_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var code39 = Barcode.CreateCode39("ABC-123", includeChecksum: true);
var code93 = Barcode.CreateCode93("ABC123");
var code128 = Barcode.CreateCode128("SKU-123456");
var ean8 = Barcode.CreateEan8("5512345");
var ean13 = Barcode.CreateEan13("400638133393");
var upcA = Barcode.CreateUpcA("03600029145", extension: "12");
var codabar = Barcode.CreateCodabar("40156", startCharacter: 'A', stopCharacter: 'B');
var itf = Barcode.CreateItf("12345678");

// Type, width in modules and height (a linear barcode is one module high)
Assert.Equal((BarcodeType.Code39, 129, 1), (code39.Type, code39.Width, code39.Height));
Assert.Equal((BarcodeType.Code93, 91, 1), (code93.Type, code93.Width, code93.Height));
Assert.Equal((BarcodeType.Code128, 123, 1), (code128.Type, code128.Width, code128.Height));
Assert.Equal((BarcodeType.Ean8, 67, 1), (ean8.Type, ean8.Width, ean8.Height));
Assert.Equal((BarcodeType.Ean13, 95, 1), (ean13.Type, ean13.Width, ean13.Height));
Assert.Equal((BarcodeType.UpcA, 124, 1), (upcA.Type, upcA.Width, upcA.Height));
Assert.Equal((BarcodeType.Codabar, 71, 1), (codabar.Type, codabar.Width, codabar.Height));
Assert.Equal((BarcodeType.Itf, 81, 1), (itf.Type, itf.Width, itf.Height));

// The human-readable text, with the characters the barcode adds (check digits, add-on, Codabar guards)
Assert.Equal("ABC-123W", code39.Text);
Assert.Equal("55123457", ean8.Text);
Assert.Equal("4006381333931", ean13.Text);
Assert.Equal("036000291452 12", upcA.Text);
Assert.Equal("A40156B", codabar.Text);
```
<!-- endref -->

## Choose the symbology

The symbology is usually imposed by what reads the barcode: a retail checkout reads EAN and UPC, a warehouse or a
shipping label often uses Code 128.

| Symbology | Method | Data |
| --- | --- | --- |
| Code 39 | [CreateCode39](cref:Meziantou.Framework.Barcodes.Barcode.CreateCode39) | Uppercase letters, digits and `-. $/+%`; an optional Mod 43 check character (`includeChecksum`) |
| Code 93 | [CreateCode93](cref:Meziantou.Framework.Barcodes.Barcode.CreateCode93) | Any ASCII character: the characters outside the base set are encoded with the extended encoding |
| Code 128 | [CreateCode128](cref:Meziantou.Framework.Barcodes.Barcode.CreateCode128) | Any ASCII character, control characters included; the method switches between code sets A, B and C |
| GS1-128 | [CreateGs1128](cref:Meziantou.Framework.Barcodes.Barcode.CreateGs1128) | GS1 element strings: application identifiers and their data |
| EAN-8 | [CreateEan8](cref:Meziantou.Framework.Barcodes.Barcode.CreateEan8) | 7 digits, or 8 with the check digit |
| EAN-13 | [CreateEan13](cref:Meziantou.Framework.Barcodes.Barcode.CreateEan13) | 12 digits, or 13 with the check digit |
| UPC-A | [CreateUpcA](cref:Meziantou.Framework.Barcodes.Barcode.CreateUpcA) | 11 digits, or 12 with the check digit |
| Codabar | [CreateCodabar](cref:Meziantou.Framework.Barcodes.Barcode.CreateCodabar) | Digits and `-$:/.+`, between a start and a stop character (`A`, `B`, `C` or `D`) |
| ITF (Interleaved 2 of 5) | [CreateItf](cref:Meziantou.Framework.Barcodes.Barcode.CreateItf) | An even number of digits |

For free text, prefer Code 128: it encodes letters of both cases and is compact, especially for digits.

## Code 128 code sets

Code 128 has three code sets: A holds the uppercase letters, the digits, the punctuation and the ASCII control
characters (`\t`, `\r`, `\n`, ...); B holds the same printable characters and the lowercase letters; C holds pairs of
digits, which halves the width of numbers. You pass the text, and
[CreateCode128](cref:Meziantou.Framework.Barcodes.Barcode.CreateCode128) chooses the code sets: C for runs of 4 digits or
more, A for control characters, B for lowercase letters, and a single-character shift when the other set is only needed
for one character. Control characters let a scanner that types the data into a form move between fields or submit it:

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCode128Tests.cs;region=Barcode_Code128ControlCharacters;format=md-fenced-code-block;dedent=true -->
```csharp
// A Tab between the fields and an Enter at the end, for a scanner that types the data into a form
var barcode = Barcode.CreateCode128("SKU-123456\tQTY-5\r");
Assert.Equal("SKU-123456\tQTY-5\r", barcode.Text);
```
<!-- endref -->

Characters outside ASCII (`é`, `€`) throw an `ArgumentException`.

## GS1-128

GS1-128 is the Code 128 of logistics labels: it holds GS1 element strings, each an application identifier (AI) that
says what the data is, followed by the data: `01` for a GTIN, `17` for an expiry date, `10` for a batch number, and so
on. Pass the elements to [CreateGs1128](cref:Meziantou.Framework.Barcodes.Barcode.CreateGs1128) as
[Gs1Element](cref:Meziantou.Framework.Barcodes.Gs1Element) instances:

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCode128Tests.cs;region=Barcode_Gs1128;format=md-fenced-code-block;dedent=true -->
```csharp
// A GTIN, an expiry date and a batch number
var barcode = Barcode.CreateGs1128(
    new Gs1Element("01", "09521234543213"),
    new Gs1Element("17", "250101"),
    new Gs1Element("10", "ABC123"));

// The human-readable interpretation printed under the bars: the parentheses are not encoded
Assert.Equal("(01)09521234543213(17)250101(10)ABC123", barcode.Text);
Assert.Equal(BarcodeType.Gs1128, barcode.Type);
```
<!-- endref -->

The parentheses are only in the human-readable interpretation, the [Text](cref:Meziantou.Framework.Barcodes.Barcode.Text)
printed under the bars: they are not encoded. What is encoded is a FNC1 function character, which tells a reader that the
symbol is GS1-128, then the application identifiers and their data. A reader cannot know where the data of an
identifier without a predefined length (such as the batch number `10`) ends, so a FNC1 separator follows it when other
elements come after it. A reader returns the separators as the ASCII group separator character (U+001D). Put
the elements of variable length last to save separators.

[CreateCode128](cref:Meziantou.Framework.Barcodes.Barcode.CreateCode128) never adds FNC1: passing it `(01)09521234543213`
creates an ordinary Code 128 symbol with the parentheses in its data, which a GS1 application does not read as a GTIN.

[Gs1Element](cref:Meziantou.Framework.Barcodes.Gs1Element) checks the syntax every element follows: an application
identifier of 2 to 4 digits; data from the GS1 character set 82 (letters, digits and `!"%&'()*+,-./:;<=>?_`, no
space); and digits of the right length for the identifiers whose data has a predefined length (`00` to `04`, `11` to
`20`, `31nn` to `36nn`, `41n`). It does not check the data against the GS1 dictionary of application identifiers, nor
check digits: validate the data your application builds. A GS1-128 symbol holds at most 48 characters, application
identifiers and separators included.

## Check digits and add-ons

EAN-8, EAN-13 and UPC-A end with a check digit. Pass the data without it and the method computes it, or with it and the
method verifies it. The three take an optional add-on of 2 or 5 digits (`extension`), such as the price add-on printed
next to the barcode of a book:

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCodeAdditionalTypesTests.cs;region=Barcode_Ean13WithAddOn;format=md-fenced-code-block;dedent=true -->
```csharp
// A book: its ISBN without the check digit, and a 5-digit add-on
var barcode = Barcode.CreateEan13("978020137962", extension: "52499");
Assert.Equal("9780201379624 52499", barcode.Text); // the check digit is added

var png = barcode.ToPng(new BarcodePngOptions { ModuleHeight = 60 });
```
<!-- endref -->

![An EAN-13 barcode followed by a 5-digit add-on](images/Ean13WithAddOn.png)

[Text](cref:Meziantou.Framework.Barcodes.Barcode.Text) is what is printed under the bars: it includes the characters the barcode adds (the check digit, then a space
and the add-on; the Mod 43 check character of Code 39 when it is included; the start and stop characters of Codabar),
and not the start and stop characters of Code 39 and Code 93.

## Invalid data

Data a symbology cannot encode throws an `ArgumentException` whose `ParamName` is the parameter at fault:

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCodeAdditionalTypesTests.cs;region=Barcode_InvalidData;format=md-fenced-code-block;dedent=true -->
```csharp
// A check digit that does not match the data
Assert.Throws<ArgumentException>("data", () => Barcode.CreateEan13("4006381333932"));

// A character the symbology cannot encode
Assert.Throws<ArgumentException>("data", () => Barcode.CreateCode39("abc"));

// ITF encodes pairs of digits
Assert.Throws<ArgumentException>("data", () => Barcode.CreateItf("12345"));
```
<!-- endref -->

Validate input from users before creating the barcode, or catch the exception to report it.

## Read the modules

Like a QR code, a barcode is a grid of modules, with a single row: the indexer tells whether a module is a bar. Use it
to render the barcode your own way:

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/BarCodeAdditionalTypesTests.cs;region=Barcode_Modules;format=md-fenced-code-block;dedent=true -->
```csharp
var barcode = Barcode.CreateEan8("5512345");

// One row of modules: true for a bar
var bars = new StringBuilder();
for (var column = 0; column < barcode.Width; column++)
{
    bars.Append(barcode[0, column] ? '|' : ' ');
}

Assert.StartsWith("| |", bars.ToString()); // the start guard
```
<!-- endref -->

The grid does not include the quiet zone, the light margin on each side that scanners need.
