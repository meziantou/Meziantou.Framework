---
title: Patches
description: Read unified diffs, such as the output of git diff, and apply them to a text.
---

[UnifiedDiff](cref:Meziantou.Framework.UnifiedDiff) reads and applies diffs in the unified format, the format of `diff -u` and `git diff`. To write one,
see [Write a unified diff](09-DisplayingADiff.md#write-a-unified-diff).

## Apply a patch

[UnifiedDiff.Apply](cref:Meziantou.Framework.UnifiedDiff.Apply) applies a patch of one file to a text and returns the patched text:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_ApplyPatch;format=md-fenced-code-block;dedent=true -->
```csharp
var patch = UnifiedDiff.Create("one\ntwo\nthree\n", "one\n2\nthree\n").ToString();

// A line was added at the start since the patch was made: the hunk still applies, one line further down
var patched = UnifiedDiff.Apply("zero\none\ntwo\nthree\n", patch);

Assert.Equal("zero\none\n2\nthree\n", patched);
```
<!-- endref -->

- The context and deleted lines of a hunk must match the text exactly, line terminators included.
- When a hunk does not match at the line its header gives, it is applied at the closest line where it matches, as
  `patch` does when lines were added or removed elsewhere. The hunks apply in order, each after the previous one.
- `\ No newline at end of file` is honored both ways: the hunk matches only a text whose last line has no line feed, and
  writes one without it.

When a hunk matches nowhere, [Apply](cref:Meziantou.Framework.UnifiedDiff.Apply) throws a [UnifiedDiffApplyException](cref:Meziantou.Framework.UnifiedDiffApplyException) whose [HunkIndex](cref:Meziantou.Framework.UnifiedDiffApplyException.HunkIndex) is the
failing hunk; the text is not changed:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_ApplyPatchConflict;format=md-fenced-code-block;dedent=true -->
```csharp
var patch = UnifiedDiff.Create("one\ntwo\nthree\n", "one\n2\nthree\n").ToString();

// The line the hunk deletes changed: the hunk no longer matches
var exception = Assert.Throws<UnifiedDiffApplyException>(() => UnifiedDiff.Apply("one\nTWO\nthree\n", patch));

Assert.Equal(0, exception.HunkIndex);
```
<!-- endref -->

## Read a patch

[UnifiedDiff.Parse](cref:Meziantou.Framework.UnifiedDiff.Parse) reads every file of a patch. The lines outside the files, such as the `diff --git` and `index` lines of
git, are skipped:

<!-- ref:../../tests/Meziantou.Framework.TextDiff.Tests/TextDiffTests.cs;region=TextDiff_ParsePatch;format=md-fenced-code-block;dedent=true -->
```csharp
var patch =
    "diff --git a/greeting.txt b/greeting.txt\n" +
    "index 3b18e51..a042389 100644\n" +
    "--- a/greeting.txt\n" +
    "+++ b/greeting.txt\n" +
    "@@ -1,2 +1,2 @@ Greetings\n" +
    " Hello\n" +
    "-World\n" +
    "+There\n";

var files = UnifiedDiff.Parse(patch);

var file = files[0];
Assert.Equal("a/greeting.txt", file.OldFileName);
Assert.Equal("b/greeting.txt", file.NewFileName);

var hunk = file.Hunks[0];
Assert.Equal(1, hunk.OldStart);
Assert.Equal(2, hunk.OldLineCount);
Assert.Equal("Greetings", hunk.SectionHeading);
Assert.Equal([TextDiffOperation.Equal, TextDiffOperation.Delete, TextDiffOperation.Insert], hunk.Lines.Select(line => line.Operation));
Assert.Equal(["Hello\n", "World\n", "There\n"], hunk.Lines.Select(line => line.Text));

Assert.Equal("Hello\nThere\n", file.Apply("Hello\nWorld\n"));
```
<!-- endref -->

- A file starts at a `---` line followed by a `+++` line. The names exclude the timestamp `diff` writes after a tab.
  Match them against your files yourself: the `a/` and `b/` prefixes of git are kept.
- [UnifiedDiffFile.Apply](cref:Meziantou.Framework.UnifiedDiffFile.Apply) applies one file of a multi-file patch; [UnifiedDiff.Apply](cref:Meziantou.Framework.UnifiedDiff.Apply) throws an `ArgumentException` when the
  patch does not hold exactly one file.
- [OldStart](cref:Meziantou.Framework.UnifiedDiffHunk.OldStart) and [NewStart](cref:Meziantou.Framework.UnifiedDiffHunk.NewStart) are 1-based. For an empty range, they give the line before it, as in the `@@` line.
- Each [UnifiedDiffLine](cref:Meziantou.Framework.UnifiedDiffLine) has an [Operation](cref:Meziantou.Framework.UnifiedDiffLine.Operation) ([Equal](cref:Meziantou.Framework.TextDiffOperation.Equal) for context) and a [Text](cref:Meziantou.Framework.UnifiedDiffLine.Text) that ends with `\n`, unless it is
  marked `\ No newline at end of file`.
- `ToString` writes a file or a hunk back in the unified format.
- A malformed hunk throws a `FormatException` that gives the line number: an invalid `@@` line, a line with an unknown
  prefix, or fewer lines than the header gives. A patch with CRLF line terminators is read: the `\r` belongs to the
  lines of the hunks, as in the files it patches.
