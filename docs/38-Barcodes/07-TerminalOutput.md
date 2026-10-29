---
title: Terminal output
description: Print a QR code in a terminal with Unicode block characters, for command-line tools.
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

var microText = microQr.ToConsoleString(new QRCodeConsoleOptions
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

- `WriteToConsole` writes to `Console.Out`, `WriteTo` to any `TextWriter`, and `ToConsoleString` returns the text.
- The blocks are drawn in the color of the text, for the dark modules. On a terminal with a dark background, set
  `InvertColors`: the blocks then draw the light modules and the quiet zone, so that the code is dark on light.
- `QuietZoneModules` is 2 by default. With the default sizes, a module is one character wide and half a line high,
  which is about square in most terminals. `ModuleWidth` repeats each module in characters and `ModuleHeight` in half
  lines: increase both together to get a larger code that stays square.
- The terminal must use a font that has the block characters, and display the output in UTF-8.

The console renderer draws QR codes only, not linear barcodes.
