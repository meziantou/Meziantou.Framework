---
title: Chunking
description: Compare texts line by line, word by word, character by character, or with your own chunks.
---

A diff compares chunks: it splits both texts into chunks, then matches the chunks that are equal. The chunks decide how
precise the result is. Set the chunker in [TextDiffOptions.Chunker](cref:Meziantou.Framework.TextDiffOptions.Chunker):

| Chunker | Chunks | Use it for |
| --- | --- | --- |
| [TextChunker.Lines](cref:Meziantou.Framework.TextChunker.Lines) (default) | One line, with its line terminator | Source code, configuration files, logs |
| [TextChunker.Words](cref:Meziantou.Framework.TextChunker.Words) | Runs of non-whitespace and runs of whitespace | Prose, one-line values |
| [TextChunker.Characters](cref:Meziantou.Framework.TextChunker.Characters) | One `char` | Short strings, such as identifiers or names |
| Your own [TextChunker](cref:Meziantou.Framework.TextChunker) | Whatever you choose: sentences, CSV fields, tokens | Texts with their own structure |

## Lines

[TextChunker.Lines](cref:Meziantou.Framework.TextChunker.Lines) cuts after each line terminator (`\n`, `\r\n`, `\r`, and the Unicode separators `\u0085`, ` `
and ` `) and keeps the terminator at the end of its chunk:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_LineChunks;format=md-fenced-code-block;dedent=true -->
```csharp
var chunks = TextChunker.Lines.Chunk("first\r\nsecond\n");

// Each line keeps its terminator, and the text ends with an empty chunk
Assert.Equal(["first\r\n", "second\n", ""], chunks);
```
<!-- endref -->

A text that ends with a line terminator gives a final empty chunk, and an empty text gives one empty chunk. This is what
lets the chunks concatenate back to the text. Two consequences:

- A line that differs only by its terminator (`\r\n` in one text, `\n` in the other) is a changed line. Use
  [`IgnoreEndOfLine`](05-ComparisonOptions.md#line-terminators) to ignore it.
- The last line of a text that does not end with a line terminator differs from the same line followed by one.

## Words

[TextChunker.Words](cref:Meziantou.Framework.TextChunker.Words) alternates between runs of non-whitespace and runs of whitespace, so the whitespace is kept in its
own chunks:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_WordDiff;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { Chunker = TextChunker.Words };

var result = TextDiff.ComputeDiff("the quick fox", "the slow fox", options);

Assert.Equal(
    [
        new TextDiffEntry(TextDiffOperation.Equal, "the"),
        new TextDiffEntry(TextDiffOperation.Equal, " "),
        new TextDiffEntry(TextDiffOperation.Delete, "quick"),
        new TextDiffEntry(TextDiffOperation.Insert, "slow"),
        new TextDiffEntry(TextDiffOperation.Equal, " "),
        new TextDiffEntry(TextDiffOperation.Equal, "fox"),
    ],
    result.Entries);
```
<!-- endref -->

A word is everything between two runs of whitespace: punctuation stays attached to its word, so `world.` is one chunk.
Line terminators are whitespace, so a word chunker also cuts at the end of each line.

## Characters

[TextChunker.Characters](cref:Meziantou.Framework.TextChunker.Characters) gives one chunk per `char`. Chunks are UTF-16 code units, not what the user sees as a
character: an emoji or another character outside the Basic Multilingual Plane is two chunks, and a letter followed by a
combining accent is two chunks too.

Every character is a chunk, so a character diff of a long text compares many chunks. To find the changed characters of
a long text, use a [hierarchical diff](06-HierarchicalDiffs.md): it compares characters only inside the lines or the
words that changed.

## Write your own chunker

Derive from [TextChunker](cref:Meziantou.Framework.TextChunker) and override [Chunk](cref:Meziantou.Framework.TextChunker.Chunk). This chunker cuts after each sentence:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_CustomChunker;format=md-fenced-code-block;dedent=true -->
```csharp
// Splits after each '.' and the spaces that follow it, so the chunks concatenate back to the text
private sealed class SentenceChunker : TextChunker
{
    public override IEnumerable<string> Chunk(ReadOnlySpan<char> value)
    {
        var sentences = new List<string>();
        while (!value.IsEmpty)
        {
            var end = value.IndexOf('.') + 1;
            if (end is 0)
            {
                end = value.Length; // The last sentence has no '.'
            }

            while (end < value.Length && value[end] == ' ')
            {
                end++;
            }

            sentences.Add(value[..end].ToString());
            value = value[end..];
        }

        return sentences;
    }
}
```
<!-- endref -->

Set it in the options like a built-in chunker:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_CustomChunkerUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TextDiffOptions { Chunker = new SentenceChunker() };

var result = TextDiff.ComputeDiff("Hello world. Goodbye world.", "Hello world. Hi world.", options);

Assert.Equal(
    [
        new TextDiffEntry(TextDiffOperation.Equal, "Hello world. "),
        new TextDiffEntry(TextDiffOperation.Delete, "Goodbye world."),
        new TextDiffEntry(TextDiffOperation.Insert, "Hi world."),
    ],
    result.Entries);
```
<!-- endref -->

- **Keep every character.** The chunks must concatenate back to the text exactly; otherwise the entries cannot rebuild
  the texts.
- **Put the separators in the chunks.** Above, the spaces after a period belong to the sentence before them. Where you
  put them changes what compares equal: with [IgnoreWhitespace](cref:Meziantou.Framework.TextDiffOptions.IgnoreWhitespace), the whitespace at the edges of a chunk is ignored.
- [Chunk](cref:Meziantou.Framework.TextChunker.Chunk) receives a `ReadOnlySpan<char>`, so collect the chunks in a list and return it.
- A derived class that does not override [Chunk](cref:Meziantou.Framework.TextChunker.Chunk) splits into lines.
