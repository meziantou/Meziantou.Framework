---
title: Getting started
description: Install the package, compute a line diff between two texts and read its entries.
---

## Install the package

Add the `Meziantou.Framework.TextDiff` package to a project:

```bash
dotnet add package Meziantou.Framework.TextDiff
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Compare two texts

[TextDiff.ComputeDiff](cref:Meziantou.Framework.TextDiff.ComputeDiff) compares two texts line by line:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_BasicUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var oldText = "line1\nline2\nline3";
var newText = "line1\nline2 updated\nline3";

var result = TextDiff.ComputeDiff(oldText, newText);

Assert.True(result.HasDifferences);
Assert.Equal(
    [
        new TextDiffEntry(TextDiffOperation.Equal, "line1\n"),
        new TextDiffEntry(TextDiffOperation.Delete, "line2\n"),
        new TextDiffEntry(TextDiffOperation.Insert, "line2 updated\n"),
        new TextDiffEntry(TextDiffOperation.Equal, "line3"),
    ],
    result.Entries);
```
<!-- endref -->

- [HasDifferences](cref:Meziantou.Framework.TextDiffResult.HasDifferences) tells whether the texts differ.
- [Entries](cref:Meziantou.Framework.TextDiffResult.Entries) holds every line of both texts, in order, with what happened to it. The second line changed: a change is
  the deletion of the old line followed by the insertion of the new one.
- Each line keeps its line terminator (`\n` here), so the entries concatenate back to the texts. The last line of the
  texts has none.

## Compare words or characters

The comparison works on chunks, which are lines by default. To find the changed words or characters, set [Chunker](cref:Meziantou.Framework.TextDiffOptions.Chunker) to
[TextChunker.Words](cref:Meziantou.Framework.TextChunker.Words) or [TextChunker.Characters](cref:Meziantou.Framework.TextChunker.Characters); the other options ignore the casing, the whitespace and the line
terminators:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_ChunkingAndComparison;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions
{
    Chunker = TextChunker.Words,        // Lines (default), Words, Characters, or custom chunker
    IgnoreCase = true,
    IgnoreWhitespace = true,
    IgnoreEndOfLine = true,
};

var result = TextDiff.ComputeDiff("Hello   world\r\n", "hello world\n", options);
Assert.False(result.HasDifferences);
```
<!-- endref -->

See [Chunking](04-Chunking.md) and [Comparison options](05-ComparisonOptions.md).

## Next steps

- [Reading the result](03-ReadingTheResult.md): the entries, and how to rebuild either text.
- [Hierarchical diffs](06-HierarchicalDiffs.md): find the changed lines, then the changed words inside them.
- [Algorithms](07-Algorithms.md): when to use another algorithm than Myers.
- [Displaying a diff](08-DisplayingADiff.md): write the result as a unified diff or as HTML.
