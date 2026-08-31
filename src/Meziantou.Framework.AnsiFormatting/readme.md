# Meziantou.Framework.AnsiFormatting

`Meziantou.Framework.AnsiFormatting` provides helpers to detect, remove, and parse ANSI escape sequences.

## Remove ANSI sequences

```c#
using Meziantou.Framework;

var input = "\x1b[1;31mError:\x1b[0m Something went wrong";
var cleanText = AnsiTextProcessor.RemoveAnsiSequences(input);
var containsAnsi = AnsiTextProcessor.ContainsAnsiSequences(input);
```

## Parse text with styles

```c#
using Meziantou.Framework;

var text = "\x1b[1;38;5;208mWarning\x1b[0m and \x1b[4;34mInfo\x1b[0m";
var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles(text);

Console.WriteLine(parsed.Text); // Warning and Info

foreach (var run in parsed.Runs)
{
    Console.WriteLine($"{run.Start}-{run.End}: Bold={run.Style.Bold}, Underline={run.Style.Underline}");
}
```

## Supported sequences

`AnsiTextProcessor` recognizes two families of escape sequences:

- **CSI** (`ESC [` ... terminated by a byte in `0x40`-`0x7E`) covers SGR styling such as `ESC[1;31m`, as well as cursor movement and erase sequences.
- **OSC** (`ESC ]` ... terminated by `BEL` or by `ST`, written `ESC \`) covers hyperlinks (`OSC 8`) and window titles (`OSC 0` and `OSC 2`).

Only SGR sequences (those ending with `m`) produce styles. Every other recognized sequence is removed from the text without contributing any styling.

A sequence that never reaches its terminator is left in the text unchanged, so a value that was cut off mid-sequence is not silently truncated. `ContainsAnsiSequences` reports `true` for exactly the inputs that `RemoveAnsiSequences` would change.
