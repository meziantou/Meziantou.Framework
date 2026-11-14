---
title: Terminal output
description: Print QR codes, two-dimensional barcodes and linear barcodes in a terminal with Unicode block characters, for command-line tools.
---

A command-line tool can show a QR code in the terminal, for instance to set up an authenticator app or to open a URL
on a phone. The console renderer draws the code with the Unicode block characters `█`, `▀` and `▄`, two rows of
modules per line of text:

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeConsoleRendererTests.cs;region=QRCode_ToConsoleString;format=md-fenced-code-block;dedent=true -->
```csharp
var qrCode = QRCode.Create("HELLO WORLD", ErrorCorrectionLevel.H);
var microQr = QRCode.CreateMicroQR("12345", ErrorCorrectionLevel.L);

qrCode.WriteToConsole();
var text = qrCode.ToConsoleString();

var microText = microQr.ToConsoleString(new ConsoleRenderOptions
{
    QuietZoneModules = 1,
    ModuleWidth = 2,
    ModuleHeight = 2, // 2x bigger output that can look more square on many terminals
});

Assert.Equal("""

      ██████████████  ██  ██
      ██          ██  ████
      ██  ██████  ██  ██
      ██  ██████  ██
      ██  ██████  ██  ██████
      ██          ██    ████
      ██████████████  ██
                        ████
      ████    ██████    ████
        ██  ██      ████
      ████████          ████

    """, microText, ignoreLineEndingDifferences: true);
```
<!-- endref -->

- [WriteToConsole](cref:Meziantou.Framework.Barcodes.QRCodeConsoleRenderer.WriteToConsole) writes to `Console.Out`, [WriteTo](cref:Meziantou.Framework.Barcodes.QRCodeConsoleRenderer.WriteTo) to any `TextWriter`, and [ToConsoleString](cref:Meziantou.Framework.Barcodes.QRCodeConsoleRenderer.ToConsoleString) returns the text.
- The blocks are drawn in the color of the text, for the dark modules. On a terminal with a dark background, set
  [InvertColors](cref:Meziantou.Framework.Barcodes.ConsoleRenderOptions.InvertColors): the blocks then draw the light modules and the quiet zone, so that the code is dark on light.
- [QuietZoneModules](cref:Meziantou.Framework.Barcodes.ConsoleRenderOptions.QuietZoneModules) is 2 by default. With the default sizes, a module is one character wide and half a line high,
  which is about square in most terminals. [ModuleWidth](cref:Meziantou.Framework.Barcodes.ConsoleRenderOptions.ModuleWidth) repeats each module in characters and [ModuleHeight](cref:Meziantou.Framework.Barcodes.ConsoleRenderOptions.ModuleHeight) in half
  lines: increase both together to get a larger code that stays square.
- The terminal must use a font that has the block characters, and display the output in UTF-8.
- The output must fit in a string: options that make it larger throw an `ArgumentOutOfRangeException`.

## Linear and two-dimensional barcodes

The same methods write linear barcodes ([Barcode](cref:Meziantou.Framework.Barcodes.Barcode)) and Data Matrix, PDF417 and Aztec symbols
([Barcode2D](cref:Meziantou.Framework.Barcodes.Barcode2D)), with the same [ConsoleRenderOptions](cref:Meziantou.Framework.Barcodes.ConsoleRenderOptions):

<!-- ref:../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeConsoleRendererTests.cs;region=Barcode_ToConsoleString;format=md-fenced-code-block;dedent=true -->
```csharp
var barcode = Barcode.CreateEan8("9638507");
var text = barcode.ToConsoleString(new ConsoleRenderOptions
{
    QuietZoneModules = 2,
    BarHeight = 2, // lines of text
    ShowText = true, // the human-readable text, centered under the bars
});

var dataMatrix = Barcode2D.CreateDataMatrix("123456");
var dataMatrixText = dataMatrix.ToConsoleString();
```
<!-- endref -->

- A two-dimensional symbol is drawn as a QR code: two rows of modules per line, with a quiet zone of 2 modules.
- The bars of a linear barcode are columns of `█`, [BarHeight](cref:Meziantou.Framework.Barcodes.ConsoleRenderOptions.BarHeight) lines high (4 by default). Its
  quiet zone is on the left and on the right only, 10 modules by default as scanners expect. The rows of a stacked GS1
  DataBar symbol are drawn as the rows of a two-dimensional symbol.
- [ShowText](cref:Meziantou.Framework.Barcodes.ConsoleRenderOptions.ShowText) writes the human-readable text ([Barcode.Text](cref:Meziantou.Framework.Barcodes.Barcode.Text)) centered under the bars, on its
  own line, in the font of the terminal.
- A module of one character is narrow for a phone camera: set `ModuleWidth` to 2 or more for a barcode to scan from
  the screen.
