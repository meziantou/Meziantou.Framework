# Meziantou.Framework.TextDiff

Compute text differences at line, word, or character level with configurable comparison options.

Install the NuGet package `Meziantou.Framework.TextDiff` ([NuGet](https://www.nuget.org/packages/Meziantou.Framework.TextDiff/))

````bash
dotnet package add Meziantou.Framework.TextDiff
````

## Basic usage

````csharp
var oldText = "line1\nline2\nline3";
var newText = "line1\nline2 updated\nline3";

var result = TextDiff.ComputeDiff(oldText, newText);

foreach (var entry in result.Entries)
{
    Console.WriteLine($"{entry.Operation}: {entry.Text}");
}
````

## Configure chunking and comparison

````csharp
var options = new TextDiffOptions
{
    Chunker = TextChunker.Words,        // Lines (default), Words, Characters, or custom chunker
    IgnoreCase = true,
    IgnoreWhitespace = true,
    IgnoreEndOfLine = true,
};

var result = TextDiff.ComputeDiff("Hello   world\r\n", "hello world\n", options);
// result.HasDifferences == false
````

`IgnoreWhitespace` trims the **edges** of each chunk; whitespace inside a chunk stays
significant. `Chunker = TextChunker.Words` is what makes the two texts above compare equal:
it puts each run of whitespace in its own chunk, which trimming then reduces to an empty
one. With the default `TextChunker.Lines`, `"Hello   world"` and `"hello world"` are a
single chunk each and still differ.

`IgnoreEndOfLine` normalizes line terminators *before* chunking, so the entries of the
result carry `\n` rather than the original `\r\n`.

## Compute a hierarchical diff with multiple chunking levels

````csharp
var result = TextDiff.ComputeHierarchyDiff(
    oldText: "line1\nhello world\nline3",
    newText: "line1\nhello brave world\nline3",
    chunkers: [TextChunker.Lines, TextChunker.Words, TextChunker.Characters]);

foreach (var entry in result.Entries)
{
    Console.WriteLine($"{entry.Operation}: old={entry.OldText} new={entry.NewText}");

    foreach (var child in entry.Children)
    {
        Console.WriteLine($"  {child.Operation}: old={child.OldText} new={child.NewText}");
    }
}
````

The hierarchy API lets you refine changed chunks progressively (`Lines -> Words -> Characters`, `Words -> Characters`, etc.).

## Choose a diff algorithm

````csharp
var options = new TextDiffOptions
{
    Algorithm = TextDiffAlgorithm.Patience,
};

var result = TextDiff.ComputeDiff(oldText, newText, options);
````

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
