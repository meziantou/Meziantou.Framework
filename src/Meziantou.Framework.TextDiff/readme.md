# Meziantou.Framework.TextDiff

Compute text differences at line, word, character or grapheme level with configurable comparison options, detect
moved blocks, write, read and apply unified diffs, and merge two versions of a text.

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

Set `CompareOptions` (and optionally `Culture`) for a culture-aware comparison, such as one that ignores accents:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_CompareOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { CompareOptions = CompareOptions.IgnoreNonSpace };

// The accents are ignored
Assert.False(TextDiff.ComputeDiff("Caf\u00E9 cr\u00E8me\n", "Cafe creme\n", options).HasDifferences);
```
<!-- endref -->

`TextChunker.Graphemes` keeps each user-perceived character (an emoji, a letter and its combining accents) in one chunk:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_GraphemeChunks;format=md-fenced-code-block;dedent=true -->
```csharp
// "e" followed by a combining acute accent, an emoji (a surrogate pair), and "\r\n"
var chunks = TextChunker.Graphemes.Chunk("e\u0301\U0001F600!\r\n");

Assert.Equal(["e\u0301", "\U0001F600", "!", "\r\n"], chunks);
```
<!-- endref -->

## Detect moved blocks

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_DetectMoves;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { DetectMoves = true };

var result = TextDiff.ComputeDiff("a\nb\nc\nd\n", "a\nc\nd\nb\n", options);

// The deleted "b" and the inserted "b" share move 0
Assert.Equal(
    [
        new TextDiffEntry(TextDiffOperation.Equal, "a\n"),
        new TextDiffEntry(TextDiffOperation.Delete, "b\n", null, moveId: 0),
        new TextDiffEntry(TextDiffOperation.Equal, "c\n"),
        new TextDiffEntry(TextDiffOperation.Equal, "d\n"),
        new TextDiffEntry(TextDiffOperation.Insert, null, "b\n", moveId: 0),
        new TextDiffEntry(TextDiffOperation.Equal, ""),
    ],
    result.Entries);
```
<!-- endref -->

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

## Write, read and apply unified diffs

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_UnifiedDiffUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var oldText = "a\nb\nc\nd\ne\nf\ng\nh\n";
var newText = "a\nb\nC\nd\ne\nf\ng\nh\ni\n";

var diff = UnifiedDiff.Create(oldText, newText, new UnifiedDiffOptions
{
    OldFileName = "old.txt",
    NewFileName = "new.txt",
    ContextLines = 1,
});

Assert.Equal(
    "--- old.txt\n" +
    "+++ new.txt\n" +
    "@@ -2,3 +2,3 @@\n" +
    " b\n" +
    "-c\n" +
    "+C\n" +
    " d\n" +
    "@@ -8 +8,2 @@\n" +
    " h\n" +
    "+i\n",
    diff.ToString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_ApplyPatch;format=md-fenced-code-block;dedent=true -->
```csharp
var patch = UnifiedDiff.Create("one\ntwo\nthree\n", "one\n2\nthree\n").ToString();

// A line was added at the start since the patch was made: the hunk still applies, one line further down
var patched = UnifiedDiff.Apply("zero\none\ntwo\nthree\n", patch);

Assert.Equal("zero\none\n2\nthree\n", patched);
```
<!-- endref -->

`UnifiedDiff.Parse` reads every file of a patch, such as the output of `git diff`.

## Merge two versions of a text

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextMerge_Merge;format=md-fenced-code-block;dedent=true -->
```csharp
var baseText = "red\ngreen\nblue\n";
var oursText = "red\nGREEN\nblue\n";            // Changes a line
var theirsText = "red\ngreen\nblue\nyellow\n";   // Adds a line

var result = TextMerge.Merge(baseText, oursText, theirsText);

Assert.False(result.HasConflicts);
Assert.Equal("red\nGREEN\nblue\nyellow\n", result.Text);
```
<!-- endref -->

Conflicts are written between `<<<<<<<`, `=======` and `>>>>>>>` markers, and `Chunks` gives the text of each region
in the three versions.
