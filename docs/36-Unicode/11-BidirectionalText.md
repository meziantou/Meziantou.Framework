---
title: Bidirectional text
description: Find the direction of each character of a paragraph that mixes left-to-right and right-to-left text, and the order to display it.
---

Arabic and Hebrew are written from right to left, and their numbers and the Latin words inside them from left to right.
A text is stored in the order it is read; displaying it needs the Unicode Bidirectional Algorithm
([UAX #9](https://www.unicode.org/reports/tr9/)), which `UnicodeBidi` implements in the two steps the annex defines:
the levels of a paragraph, then the display order of each line.

## Order a line for display

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Bidi;format=md-fenced-code-block;dedent=true -->
```csharp
var paragraph = "car \u05D0\u05D1\u05D2!";
Span<byte> levels = stackalloc byte[paragraph.Length];
var paragraphLevel = UnicodeBidi.ResolveLevels(paragraph, UnicodeBidiDirection.Auto, levels);

// For each line, once the paragraph is broken into lines:
UnicodeBidi.ResetLineLevels(paragraph, paragraphLevel, levels);
Span<int> visualToLogical = stackalloc int[paragraph.Length];
UnicodeBidi.GetVisualOrder(levels, visualToLogical);

Assert.Equal<int>([0, 1, 2, 3, 6, 5, 4, 7], visualToLogical);
```
<!-- endref -->

1. `ResolveLevels` gives the embedding level of each UTF-16 code unit of a paragraph (the two code units of a
   surrogate pair have the same level), and returns the level of the paragraph. A character whose level is odd is
   displayed from right to left. `UnicodeBidiDirection.Auto` takes the direction of the paragraph from its first strong
   character, left to right when it has none; `LeftToRight` and `RightToLeft` set it.
2. Break the paragraph into lines (see [Line breaking](08-LineBreaking.md)), then call `ResetLineLevels` for each line:
   it resets the levels of the separators and of the whitespace at the end of the line (rule L1).
3. `GetVisualOrder` gives, for each position on the screen from the left, the index of the character to display there.

## Details

- `UnicodeBidi.MayRequireBidi` is a fast check: when it returns `false`, every level is 0 and the display order is the
  order of the text. Skip the algorithm for such text.
- `GetParagraphLevel` gives the level of a paragraph without resolving the levels of its characters.
- `ResolveLevels` has an overload that takes its working storage from you (`GetWorkspaceLength` gives its length) and
  does not allocate.
- The algorithm orders characters; it does not mirror them. A right-to-left character such as a parenthesis is
  displayed mirrored: `UnicodeProperties.TryGetBidiMirroringGlyph` gives its mirrored character (rule L4).
- A text renderer also needs shaping: the letters of Arabic change their form with their neighbors. To draw text on
  images, the text layout of [Meziantou.Framework.Imaging](../01-Imaging/08-Text/04-TextLayout.md#right-to-left-and-mixed-directions)
  does all of it.
