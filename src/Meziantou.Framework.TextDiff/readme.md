# Meziantou.Framework.TextDiff

Compute text differences at line, word, or character level with configurable comparison options.

Install the NuGet package `Meziantou.Framework.TextDiff` ([NuGet](https://www.nuget.org/packages/Meziantou.Framework.TextDiff/))

````xml
<PackageReference Include="Meziantou.Framework.TextDiff" Version="1.0.0" />
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
````

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
