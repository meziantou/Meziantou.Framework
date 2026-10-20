# Meziantou.Framework.AnsiFormatting

`Meziantou.Framework.AnsiFormatting` provides helpers to detect, remove, and parse ANSI escape sequences. `AnsiTextProcessor` is in the `Meziantou.Framework` namespace.

## Remove ANSI sequences

<!-- ref:../../tests/Meziantou.Framework.AnsiFormatting.Tests/AnsiTextProcessorTests.cs;region=AnsiTextProcessor_RemoveAnsiSequences;format=md-fenced-code-block;dedent=true -->
```csharp
var input = "\x1b[1;31mError:\x1b[0m Something went wrong";
var cleanText = AnsiTextProcessor.RemoveAnsiSequences(input);
var containsAnsi = AnsiTextProcessor.ContainsAnsiSequences(input);

Assert.Equal("Error: Something went wrong", cleanText);
Assert.True(containsAnsi);
```
<!-- endref -->

## Parse text with styles

<!-- ref:../../tests/Meziantou.Framework.AnsiFormatting.Tests/AnsiTextProcessorTests.cs;region=AnsiTextProcessor_ParseTextWithAnsiStyles;format=md-fenced-code-block;dedent=true -->
```csharp
var text = "\x1b[1;38;5;208mWarning\x1b[0m and \x1b[4;34mInfo\x1b[0m";
var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles(text);

Assert.Equal("Warning and Info", parsed.Text);

var runs = parsed.Runs.Select(run => $"{run.Start}-{run.End}: Bold={run.Style.Bold}, Underline={run.Style.Underline}");
Assert.Equal(
    [
        "0-7: Bold=True, Underline=False",
        "7-12: Bold=False, Underline=False",
        "12-16: Bold=False, Underline=True",
    ],
    runs);
```
<!-- endref -->

## Supported sequences

`AnsiTextProcessor` recognizes two families of escape sequences:

- **CSI** (`ESC [` ... terminated by a byte in `0x40`-`0x7E`) covers SGR styling such as `ESC[1;31m`, as well as cursor movement and erase sequences.
- **OSC** (`ESC ]` ... terminated by `BEL` or by `ST`, written `ESC \`) covers hyperlinks (`OSC 8`) and window titles (`OSC 0` and `OSC 2`).

Only SGR sequences (those ending with `m`) produce styles. Every other recognized sequence is removed from the text without contributing any styling.

A sequence that never reaches its terminator is left in the text unchanged, so a value that was cut off mid-sequence is not silently truncated. `ContainsAnsiSequences` reports `true` for exactly the inputs that `RemoveAnsiSequences` would change.
