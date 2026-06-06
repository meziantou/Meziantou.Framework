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
