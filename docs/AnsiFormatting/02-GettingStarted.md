---
title: Getting started
description: Install the package, then detect and remove the ANSI escape sequences of a text.
---

## Install the package

Add the `Meziantou.Framework.AnsiFormatting` package to your project:

```bash
dotnet add package Meziantou.Framework.AnsiFormatting
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`. They check
their results with assertions such as `Assert.Equal`, which show what each call returns.

## Remove the escape sequences

`RemoveAnsiSequences` returns the text without its escape sequences, and `ContainsAnsiSequences` tells whether there
is any:

<!-- ref:../../tests/Meziantou.Framework.AnsiFormatting.Tests/AnsiTextProcessorTests.cs;region=AnsiTextProcessor_RemoveAnsiSequences;format=md-fenced-code-block;dedent=true -->
```csharp
var input = "\x1b[1;31mError:\x1b[0m Something went wrong";
var cleanText = AnsiTextProcessor.RemoveAnsiSequences(input);
var containsAnsi = AnsiTextProcessor.ContainsAnsiSequences(input);

Assert.Equal("Error: Something went wrong", cleanText);
Assert.True(containsAnsi);
```
<!-- endref -->

The output of a tool often mixes colors, cursor sequences and hyperlinks. They are all removed, and the text of a
hyperlink is kept:

<!-- ref:../../tests/Meziantou.Framework.AnsiFormatting.Tests/AnsiTextProcessorTests.cs;region=AnsiTextProcessor_CleanProcessOutput;format=md-fenced-code-block;dedent=true -->
```csharp
// Output of a command-line tool, with colors, a cursor sequence and a hyperlink
var output = "\u001b[32m✔\u001b[0m Build succeeded\u001b[K\n" +
             "See \u001b]8;;https://example.com/build/42\u001b\\the report\u001b]8;;\u001b\\";

if (AnsiTextProcessor.ContainsAnsiSequences(output))
{
    output = AnsiTextProcessor.RemoveAnsiSequences(output);
}

Assert.Equal("✔ Build succeeded\nSee the report", output);
```
<!-- endref -->

- Both methods accept a `string` or a `ReadOnlySpan<char>`.
- `RemoveAnsiSequences(string)` returns the same string instance when there is nothing to remove, so calling it on text
  without sequences does not allocate. The `ContainsAnsiSequences` check in the example is only useful when you need to
  know whether the text was styled.

## Text that is kept

A sequence is removed only when it is complete:

<!-- ref:../../tests/Meziantou.Framework.AnsiFormatting.Tests/AnsiTextProcessorTests.cs;region=AnsiTextProcessor_KeptSequences;format=md-fenced-code-block;dedent=true -->
```csharp
// A sequence cut before its terminator is kept, so no text is lost
Assert.Equal("Done\u001b[1;3", AnsiTextProcessor.RemoveAnsiSequences("Done\u001b[1;3"));
Assert.False(AnsiTextProcessor.ContainsAnsiSequences("Done\u001b[1;3"));

// An escape character that does not start a CSI or OSC sequence is kept
Assert.Equal("a\u001bb", AnsiTextProcessor.RemoveAnsiSequences("a\u001bb"));
```
<!-- endref -->

- A sequence that never reaches its terminator, such as text cut in the middle of a sequence, is kept with the rest of
  the text, so nothing is silently lost.
- An escape character that is not followed by `[` or `]` is kept.
- `ContainsAnsiSequences` returns `true` exactly for the texts that `RemoveAnsiSequences` changes.

When you read the output of a process piece by piece, a sequence can be split between two pieces. Join the pieces, or
at least the lines, before you remove the sequences.

## Next steps

- [Reading styles](03-ReadingStyles.md): get the text and the style of each part of it.
