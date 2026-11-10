---
title: Displaying a diff
description: Write a diff as a unified diff, or highlight the changed words in HTML.
---

[UnifiedDiff](cref:Meziantou.Framework.UnifiedDiff) writes a line diff in the unified format. For any other output, the entries are data: this page gives
the code of an HTML format, which you can copy and adapt to console colors or anything else.

## Write a unified diff

The unified format is the format of `diff -u` and `git diff`: the changed lines with a prefix (`-` for a deleted line,
`+` for an inserted one), surrounded by a few unchanged lines of context, in hunks that start with the line numbers in
both texts. [UnifiedDiff.Create](cref:Meziantou.Framework.UnifiedDiff.Create) compares two texts line by line and returns the diff; `ToString` writes it:

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

- [OldFileName](cref:Meziantou.Framework.UnifiedDiffOptions.OldFileName) and [NewFileName](cref:Meziantou.Framework.UnifiedDiffOptions.NewFileName) are written after `---` and `+++` (default `a` and `b`). For a patch that
  `git apply` reads, use `a/<path>` and `b/<path>`.
- [ContextLines](cref:Meziantou.Framework.UnifiedDiffOptions.ContextLines) is the number of unchanged lines around each change (default 3). Changes separated by at most
  twice the context share one hunk.
- A range of one line writes no line count (`@@ -8 +8,2 @@`), as `diff -u` does.
- A line without a line feed, at the end of a text, is followed by `\ No newline at end of file`.
- Lines are separated by `\n` only, as `diff` does: a `\r` before it is part of the line. The comparison is ordinal, so
  the diff applies to the old text exactly.
- Equal texts give no hunk, and `ToString` returns an empty string.

The result is a [UnifiedDiffFile](cref:Meziantou.Framework.UnifiedDiffFile): its hunks give the line numbers and the lines, and it applies to the old text.
See [Patches](10-Patches.md).

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

- A [Replace](cref:Meziantou.Framework.TextDiffHierarchyOperation.Replace) line is written from its children, the word diff of the old and the new line. Since `Append` calls
  itself for the children, it works with any number of levels.
- Consecutive deleted or inserted chunks share one element, so `lazy` and the space after it are one insertion.
- Every chunk is encoded with `WebUtility.HtmlEncode` (in `System.Net`): the texts can contain `<` or `&`.
- The line terminators are kept: show the result in a `<pre>` element, or in an element styled with
  `white-space: pre-wrap`.

The same structure writes other formats: replace the HTML elements with console colors, or with the `[-deleted-]` and
`{+inserted+}` markers of `git diff --word-diff`.
