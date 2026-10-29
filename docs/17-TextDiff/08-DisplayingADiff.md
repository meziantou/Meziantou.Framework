---
title: Displaying a diff
description: Write a diff as a unified diff, or highlight the changed words in HTML.
---

A diff is a list of entries; the library does not format it. This makes any output possible: a unified diff for a
patch, HTML for a web page, colors for a console. This page gives the code of two common formats, which you can copy and
adapt.

## Write a unified diff

The unified format is the format of `diff -u` and `git diff`: the changed lines with a prefix (`-` for a deleted line,
`+` for an inserted one), surrounded by a few unchanged lines of context, in hunks that start with the line numbers in
both texts. This method writes a line diff in that format (it needs `using System.Globalization;` and
`using System.Text;`):

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_UnifiedDiff;format=md-fenced-code-block;dedent=true -->
```csharp
public static string ToUnifiedDiff(string oldText, string newText, string oldName = "a", string newName = "b", int contextLines = 3)
{
    var result = TextDiff.ComputeDiff(oldText, newText);

    // One item per line, with the number of the line in each text
    var lines = new List<(char Prefix, string Text, int OldLine, int NewLine)>();
    var oldLine = 1;
    var newLine = 1;
    foreach (var entry in result.Entries)
    {
        if (entry.Text.Length is 0)
            continue; // The empty chunk after the last line terminator

        lines.Add(entry.Operation switch
        {
            TextDiffOperation.Equal => (' ', entry.Text, oldLine++, newLine++),
            TextDiffOperation.Delete => ('-', entry.Text, oldLine++, newLine),
            _ => ('+', entry.Text, oldLine, newLine++),
        });
    }

    var output = new StringBuilder();
    var index = 0;
    while (true)
    {
        var firstChange = lines.FindIndex(index, line => line.Prefix is not ' ');
        if (firstChange < 0)
            break;

        // Changes separated by at most 2 × contextLines unchanged lines share a hunk
        var lastChange = firstChange;
        while (true)
        {
            var nextChange = lines.FindIndex(lastChange + 1, line => line.Prefix is not ' ');
            if (nextChange < 0 || nextChange - lastChange - 1 > 2 * contextLines)
                break;

            lastChange = nextChange;
        }

        var start = Math.Max(index, firstChange - contextLines);
        var end = Math.Min(lines.Count, lastChange + contextLines + 1);
        var hunk = lines.GetRange(start, end - start);

        if (output.Length is 0)
        {
            output.Append("--- ").Append(oldName).Append('\n');
            output.Append("+++ ").Append(newName).Append('\n');
        }

        var oldRange = FormatRange(hunk[0].OldLine, hunk.Count(line => line.Prefix is not '+'));
        var newRange = FormatRange(hunk[0].NewLine, hunk.Count(line => line.Prefix is not '-'));
        output.Append("@@ -").Append(oldRange).Append(" +").Append(newRange).Append(" @@\n");
        foreach (var line in hunk)
        {
            output.Append(line.Prefix).Append(line.Text);
            if (line.Text[^1] is not '\n')
            {
                output.Append("\n\\ No newline at end of file\n");
            }
        }

        index = end;
    }

    return output.ToString();

    // An empty range gives the number of the line before it, and a count of 1 is omitted
    static string FormatRange(int start, int count) => count switch
    {
        0 => string.Create(CultureInfo.InvariantCulture, $"{start - 1},0"),
        1 => string.Create(CultureInfo.InvariantCulture, $"{start}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{start},{count}"),
    };
}
```
<!-- endref -->

Call it with the two texts, the names to write in the header, and the number of context lines:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_UnifiedDiffUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var oldText = "a\nb\nc\nd\ne\nf\ng\nh\n";
var newText = "a\nb\nC\nd\ne\nf\ng\nh\ni\n";

var patch = ToUnifiedDiff(oldText, newText, "old.txt", "new.txt", contextLines: 1);

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
    patch);
```
<!-- endref -->

- The method follows the [entries](03-ReadingTheResult.md) and counts the lines of each text, so each hunk knows where
  it starts in both texts.
- Changes separated by at most twice the context share one hunk, and a range of one line writes no line count
  (`@@ -8 +8,2 @@`), as `diff -u` does.
- A line without a line terminator, at the end of a text, is followed by `\ No newline at end of file`.
- It uses the default options. A patch is meant to be applied to the old text, so keep the default options when you
  write one: with `IgnoreCase`, for instance, a line whose casing changed would not appear in the patch.

## Highlight the changed words

A [hierarchical diff](06-HierarchicalDiffs.md) finds the changed lines, then the changed words inside them. This method
writes the diff as HTML, with the deleted words in `<del>` elements and the inserted words in `<ins>` elements:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_HighlightChangedWords;format=md-fenced-code-block;dedent=true -->
```csharp
public static string HighlightChangedWords(string oldText, string newText)
{
    var result = TextDiff.ComputeHierarchyDiff(oldText, newText, [TextChunker.Lines, TextChunker.Words]);

    var output = new StringBuilder();
    Append(result.Entries);
    return output.ToString();

    void Append(IReadOnlyList<TextDiffHierarchyEntry> entries)
    {
        var index = 0;
        while (index < entries.Count)
        {
            var entry = entries[index];
            switch (entry.Operation)
            {
                case TextDiffHierarchyOperation.Equal:
                    output.Append(WebUtility.HtmlEncode(entry.NewText));
                    index++;
                    break;

                case TextDiffHierarchyOperation.Replace:
                    Append(entry.Children); // The word diff of the two lines
                    index++;
                    break;

                default:
                    // Consecutive deleted (or inserted) chunks share one element
                    var tag = entry.Operation is TextDiffHierarchyOperation.Delete ? "del" : "ins";
                    output.Append('<').Append(tag).Append('>');
                    while (index < entries.Count && entries[index].Operation == entry.Operation)
                    {
                        output.Append(WebUtility.HtmlEncode(entries[index].OldText ?? entries[index].NewText));
                        index++;
                    }

                    output.Append("</").Append(tag).Append('>');
                    break;
            }
        }
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_HighlightChangedWordsUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var html = HighlightChangedWords(
    "The quick fox\njumps over\nthe dog\n",
    "The slow fox\njumps over\nthe lazy dog & cat\n");

Assert.Equal(
    "The <del>quick</del><ins>slow</ins> fox\n" +
    "jumps over\n" +
    "the <ins>lazy </ins>dog<ins> &amp; cat</ins>\n",
    html);
```
<!-- endref -->

- A `Replace` line is written from its children, the word diff of the old and the new line. Since `Append` calls
  itself for the children, it works with any number of levels.
- Consecutive deleted or inserted chunks share one element, so `lazy` and the space after it are one insertion.
- Every chunk is encoded with `WebUtility.HtmlEncode` (in `System.Net`): the texts can contain `<` or `&`.
- The line terminators are kept: show the result in a `<pre>` element, or in an element styled with
  `white-space: pre-wrap`.

The same structure writes other formats: replace the HTML elements with console colors, or with the `[-deleted-]` and
`{+inserted+}` markers of `git diff --word-diff`.
