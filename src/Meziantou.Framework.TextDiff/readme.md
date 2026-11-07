# Meziantou.Framework.TextDiff

Compute text differences at line, word, or character level with configurable comparison options.

Install the NuGet package `Meziantou.Framework.TextDiff` ([NuGet](https://www.nuget.org/packages/Meziantou.Framework.TextDiff/))

````bash
dotnet package add Meziantou.Framework.TextDiff
````

## Basic usage

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

## Rebuild either side of the diff

`Text` is the chunk from the old text (from the new text for an `Insert`). When a comparison
option such as `IgnoreCase` makes two *different* chunks compare equal, `OldText` and
`NewText` give the exact chunk from each side, so both texts can be rebuilt exactly:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_RebuildSides;format=md-fenced-code-block;dedent=true -->
```csharp
var result = TextDiff.ComputeDiff("Hello\nWorld\n", "hello\nWORLD\nagain\n", new TextDiffOptions { IgnoreCase = true });

var oldText = string.Concat(result.Entries
    .Where(e => e.Operation is TextDiffOperation.Equal or TextDiffOperation.Delete)
    .Select(e => e.OldText));

var newText = string.Concat(result.Entries
    .Where(e => e.Operation is TextDiffOperation.Equal or TextDiffOperation.Insert)
    .Select(e => e.NewText));

Assert.Equal("Hello\nWorld\n", oldText);
Assert.Equal("hello\nWORLD\nagain\n", newText);
```
<!-- endref -->

## Configure chunking and comparison

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

`IgnoreWhitespace` trims the **edges** of each chunk; whitespace inside a chunk stays
significant. `Chunker = TextChunker.Words` is what makes the two texts above compare equal:
it puts each run of whitespace in its own chunk, which trimming then reduces to an empty
one. With the default `TextChunker.Lines`, `"Hello   world"` and `"hello world"` are a
single chunk each and still differ.

`IgnoreEndOfLine` normalizes line terminators *before* chunking, so the entries of the
result carry `\n` rather than the original `\r\n`.

## Compute a hierarchical diff with multiple chunking levels

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_HierarchyDiff;format=md-fenced-code-block;dedent=true -->
```csharp
var result = TextDiff.ComputeHierarchyDiff(
    oldText: "line1\nhello world\nline3",
    newText: "line1\nhello brave world\nline3",
    chunkers: [TextChunker.Lines, TextChunker.Words, TextChunker.Characters]);

// Only the second line changed. The Words chunker refines it into words, without going further
// down to characters since every word is either equal or inserted.
Assert.HasCount(3, result.Entries);
var changedLine = result.Entries[1];
Assert.Equal(TextDiffHierarchyOperation.Replace, changedLine.Operation);
Assert.Equal("hello world\n", changedLine.OldText);
Assert.Equal("hello brave world\n", changedLine.NewText);

var insertedWords = changedLine.Children
    .Where(child => child.Operation is TextDiffHierarchyOperation.Insert)
    .Select(child => child.NewText)
    .ToArray();
Assert.Equal(["brave", " "], insertedWords);
```
<!-- endref -->

The hierarchy API lets you refine changed chunks progressively (`Lines -> Words -> Characters`, `Words -> Characters`, etc.).

## Choose a diff algorithm

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_Algorithm;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions
{
    Algorithm = TextDiffAlgorithm.Patience,
};

var result = TextDiff.ComputeDiff("A\nB\nA\nC", "A\nA\nB\nC", options);

Assert.Equal(
    [
        new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
        new TextDiffEntry(TextDiffOperation.Delete, "B\n"),
        new TextDiffEntry(TextDiffOperation.Equal, "A\n"),
        new TextDiffEntry(TextDiffOperation.Insert, "B\n"),
        new TextDiffEntry(TextDiffOperation.Equal, "C"),
    ],
    result.Entries);
```
<!-- endref -->

Available algorithms:

- `TextDiffAlgorithm.Myers` (default): best default when you want a high-quality, shortest-edit-script style diff.
- `TextDiffAlgorithm.Patience`: best for human-readable output in reviews, even if edits are not always minimal.
- `TextDiffAlgorithm.Histogram`: good practical choice for large or repetitive texts when performance is important.
- `TextDiffAlgorithm.HuntSzymanski`: useful for large inputs with relatively sparse matches.

### Cost

All four algorithms trim the common prefix and suffix first, so the cost below is driven by the
part that actually differs — two revisions of the same file are cheap whatever you pick.

| Algorithm | Cost | Degrades when |
|---|---|---|
| `Myers` | `O(D²)`, `D` = edit distance | the two texts have little in common: 50 000 fully different lines take seconds |
| `Patience` | anchor search, `Myers` on unanchored regions | no chunk is unique, so it falls back to `Myers` |
| `Histogram` | anchor search, `Myers` on unanchored regions | every shared chunk is very frequent, so it falls back to `Myers` |
| `HuntSzymanski` | `O(r log n)`, `r` = matching pairs | many duplicate chunks, which makes `r` quadratic |

There is no work limit: a diff of two large, unrelated texts runs to completion however long it
takes. Character-level diffs reach these sizes quickly, since every character is a chunk — prefer
`ComputeHierarchyDiff` so the character diff only runs on chunks that already changed.
