---
title: Reading styles
description: Parse a text with ANSI escape sequences into plain text and runs of styles, with their colors.
---

## Parse the text

`ParseTextWithAnsiStyles` returns an `AnsiText`: the text without its escape sequences, and the runs of text that share
the same style:

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

- `Text` is the text without the escape sequences.
- `Runs` is a list of `AnsiTextRun`. Each run has a `Start` (inclusive) and an `End` (exclusive) index in `Text`, and an
  `AnsiStyle`. The runs follow each other, without gap or overlap, and cover the whole text. A run starts each time the
  style changes; an empty text has no run.
- The text that has no style is a run whose style is `AnsiStyle.None`.
- `ParseTextWithAnsiStyles` throws an `ArgumentNullException` for `null`.

## Styles

`AnsiStyle` is a record with `Bold`, `Italic`, `Underline` and `Inverse`, and a `Foreground` and a `Background` color,
`null` for the default color of the terminal. Two styles are equal when all their values are equal.

The SGR codes it reads are:

| Code | Effect |
| --- | --- |
| `0`, or no code | Resets every attribute and color |
| `1`, `22` | Bold on, off |
| `3`, `23` | Italic on, off |
| `4`, `24` | Underline on, off |
| `7`, `27` | Inverse on, off |
| `30`-`37`, `90`-`97` | Foreground color: indexes 0-7 and 8-15 |
| `40`-`47`, `100`-`107` | Background color: indexes 0-7 and 8-15 |
| `38;5;n`, `48;5;n` | Foreground, background color: index `n` of the 256-color palette |
| `38;2;r;g;b`, `48;2;r;g;b` | Foreground, background color: 24-bit RGB color |
| `39`, `49` | Default foreground, background color |

The extended colors are also read in their colon form, such as `38:5:208` or `38:2::255:128:0`. Codes that are not in
the table, such as blinking or faint text, are ignored, and a parameter that cannot be read does not change the style.

## Colors

`AnsiColor` is a record. Its `Kind` tells how to read it:

- `AnsiColorKind.Indexed`: `IndexedValue` is an index in the 256-color palette. Indexes 0-15 are the standard colors
  (black, red, green, yellow, blue, magenta, cyan, white, then their bright variants), whose exact RGB values depend on
  the terminal theme.
- `AnsiColorKind.Rgb`: `Red`, `Green` and `Blue` are the components of a 24-bit color.

`AnsiColor.FromIndexed(index)` and `AnsiColor.FromRgb(red, green, blue)` create colors to compare with:

<!-- ref:../../tests/Meziantou.Framework.AnsiFormatting.Tests/AnsiTextProcessorTests.cs;region=AnsiTextProcessor_Colors;format=md-fenced-code-block;dedent=true -->
```csharp
var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles(
    "\u001b[31mred\u001b[0m \u001b[38;2;255;128;0morange\u001b[0m \u001b[1;97;44mwhite on blue\u001b[0m");

Assert.Equal("red orange white on blue", parsed.Text);
Assert.HasCount(5, parsed.Runs);

// 30-37 and 90-97 are the 16 standard colors: indexes 0-7 and 8-15
var red = parsed.Runs[0];
Assert.Equal("red", parsed.Text[red.Start..red.End]);
Assert.Equal(AnsiTextProcessor.AnsiColor.FromIndexed(1), red.Style.Foreground);

// The space between two styled words is a run without style
Assert.Equal(AnsiTextProcessor.AnsiStyle.None, parsed.Runs[1].Style);

// 38;2;r;g;b is a 24-bit color
var orange = parsed.Runs[2];
Assert.Equal(AnsiTextProcessor.AnsiColor.FromRgb(255, 128, 0), orange.Style.Foreground);
Assert.Equal(AnsiTextProcessor.AnsiColorKind.Rgb, orange.Style.Foreground?.Kind);

var whiteOnBlue = parsed.Runs[4].Style;
Assert.True(whiteOnBlue.Bold);
Assert.Equal(AnsiTextProcessor.AnsiColor.FromIndexed(15), whiteOnBlue.Foreground);
Assert.Equal(AnsiTextProcessor.AnsiColor.FromIndexed(4), whiteOnBlue.Background);
```
<!-- endref -->

## Convert to HTML

The runs map directly to HTML elements. This function writes bold, underlined and RGB-colored text; extend it with the
other attributes and a palette for the indexed colors:

<!-- ref:../../tests/Meziantou.Framework.AnsiFormatting.Tests/AnsiTextProcessorTests.cs;region=AnsiTextProcessor_ToHtml;format=md-fenced-code-block;dedent=true -->
```csharp
static string ToHtml(string ansiText)
{
    var parsed = AnsiTextProcessor.ParseTextWithAnsiStyles(ansiText);
    var html = new StringBuilder();
    foreach (var run in parsed.Runs)
    {
        var text = WebUtility.HtmlEncode(parsed.Text[run.Start..run.End]);
        var style = run.Style;
        if (style.Bold)
            text = "<strong>" + text + "</strong>";

        if (style.Underline)
            text = "<u>" + text + "</u>";

        if (style.Foreground is { Kind: AnsiTextProcessor.AnsiColorKind.Rgb } color)
            text = "<span style=\"color:#" + Convert.ToHexStringLower([color.Red, color.Green, color.Blue]) + "\">" + text + "</span>";

        html.Append(text);
    }

    return html.ToString();
}

Assert.Equal(
    "<strong>Error:</strong> <span style=\"color:#ff8000\">disk &lt;C:&gt; is full</span>",
    ToHtml("\u001b[1mError:\u001b[0m \u001b[38;2;255;128;0mdisk <C:> is full\u001b[0m"));
```
<!-- endref -->

Encode the text of each run, as `WebUtility.HtmlEncode` does here: the output of a process can contain `<` and `&`.
