---
title: Character properties
description: Read the properties of a code point (category, classes, case mappings, emoji, block) and the full record of a character.
---

## Read a property

`UnicodeProperties` gives the properties that text processing needs. Every method takes a code point as an `int` and
never allocates: the value is read from a table.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Properties;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(UnicodeCategory.UppercaseLetter, UnicodeProperties.GetGeneralCategory('A'));
Assert.Equal(230, UnicodeProperties.GetCanonicalCombiningClass(0x0301));
Assert.Equal(UnicodeBidirectionalCategory.RightToLeft, UnicodeProperties.GetBidiClass(0x05D0));
Assert.True(UnicodeProperties.TryGetBidiMirroringGlyph('(', out var mirrored));
Assert.Equal(')', mirrored);
Assert.Equal(UnicodeLineBreakClass.Glue, UnicodeProperties.GetLineBreakClass(0x00A0));
Assert.Equal(UnicodeGraphemeClusterBreak.ZWJ, UnicodeProperties.GetGraphemeClusterBreak(0x200D));
Assert.Equal(UnicodeWordBreak.ExtendNumLet, UnicodeProperties.GetWordBreak('_'));
Assert.Equal(UnicodeEastAsianWidth.Wide, UnicodeProperties.GetEastAsianWidth(0x4E00));
Assert.Equal(UnicodeVerticalOrientation.TransformedRotated, UnicodeProperties.GetVerticalOrientation(0x300C));
Assert.Equal(UnicodeJoiningType.DualJoining, UnicodeProperties.GetJoiningType(0x0628));
Assert.Equal(UnicodeIndicSyllabicCategory.Virama, UnicodeProperties.GetIndicSyllabicCategory(0x094D));
Assert.True(UnicodeProperties.IsDefaultIgnorable(0x00AD));
Assert.True(UnicodeProperties.IsIdStart('a')); // UAX #31
Assert.True(UnicodeProperties.IsIdContinue('0'));
Assert.Equal('a', UnicodeProperties.ToSimpleLowercase('A'));
Assert.Equal(0x01C5, UnicodeProperties.ToSimpleTitlecase(0x01C6)); // 'ǆ' to 'ǅ'
Assert.Equal('k', UnicodeProperties.ToSimpleCaseFold(0x212A)); // the Kelvin sign
Assert.True(UnicodeProperties.HasBinaryProperty(0x00E9, UnicodeBinaryProperty.Alphabetic));
Assert.Equal([new UnicodeRange('0', '9'), new UnicodeRange('A', 'F'), new UnicodeRange('a', 'f')], UnicodeProperties.GetBinaryPropertyRanges(UnicodeBinaryProperty.ASCIIHexDigit));
```
<!-- endref -->

| Area | Methods |
| --- | --- |
| General | `GetGeneralCategory`, `GetCanonicalCombiningClass`, `IsDefaultIgnorable` |
| Segmentation | `GetGraphemeClusterBreak`, `GetWordBreak`, `GetSentenceBreak`, `GetLineBreakClass`, `GetIndicConjunctBreak` |
| Bidirectional text | `GetBidiClass`, `IsBidiMirrored`, `TryGetBidiMirroringGlyph`, `GetBidiPairedBracketType`, `TryGetBidiPairedBracket` |
| Layout and shaping | `GetEastAsianWidth`, `GetVerticalOrientation`, `GetJoiningType`, `GetJoiningGroup`, `GetIndicSyllabicCategory`, `GetIndicPositionalCategory` |
| Identifiers | `IsIdStart`, `IsIdContinue` (UAX #31) |
| Case | `ToSimpleLowercase`, `ToSimpleUppercase`, `ToSimpleTitlecase`, `ToSimpleCaseFold`, `GetLowercaseMapping`, `GetUppercaseMapping`, `GetTitlecaseMapping`, `IsCased`, `IsCaseIgnorable`, `IsSoftDotted` |
| Binary properties | `HasBinaryProperty`, `GetBinaryPropertyRanges` |

- `HasBinaryProperty` and `GetBinaryPropertyRanges` cover the binary properties of the Unicode Character Database
  (`Alphabetic`, `White_Space`, `Changes_When_Casefolded`, the emoji properties...), except the contributory `Other_*`
  properties.
- An unassigned code point has the default value the standard gives to its range, for instance `RightToLeft` in the
  Hebrew block. An integer that is not a code point has the default value of the property.
- The numeric values of the enumerations (`UnicodeScript`, `UnicodeLineBreakClass`, ...) never change: a later version
  of the standard only adds values. You can store them.

## Map the case of a code point

The simple mappings (`ToSimpleUppercase`...) map a code point to one code point. The full mappings can give several:
the German sharp s is `SS` in uppercase.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_CaseMappings;format=md-fenced-code-block;dedent=true -->
```csharp
Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];

var length = UnicodeProperties.GetUppercaseMapping('ß', mapping);
Assert.Equal<int>(['S', 'S'], mapping[..length]);

length = UnicodeProperties.GetTitlecaseMapping(0xFB01, mapping); // the "ﬁ" ligature
Assert.Equal<int>(['F', 'i'], mapping[..length]);

length = UnicodeProperties.GetLowercaseMapping(0x0130, mapping); // 'İ'
Assert.Equal<int>(['i', 0x0307], mapping[..length]);
```
<!-- endref -->

The full mappings of a code point are the mappings of `SpecialCasing.txt` that apply everywhere, else the simple
mappings. The mappings that depend on the language (Turkish, Lithuanian) or on the context (the final sigma of Greek)
need the text around the code point: convert a whole text with `UnicodeCaseMapping`, described in
[Case conversion](05-CaseConversion.md). `ToSimpleCaseFold` is the simple case folding of `CaseFolding.txt`, to compare
texts without case.

## Blocks and emoji

`UnicodeBlocks` has a property per block of the standard, and `UnicodeEmoji` the emoji properties of
[UTS #51](https://www.unicode.org/reports/tr51/):

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_BlocksAndEmoji;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(UnicodeBlocks.BasicLatin.Contains('A'));
Assert.Equal(new UnicodeRange(0x0000, 0x007F), UnicodeBlocks.BasicLatin.Range);

Assert.True(UnicodeEmoji.IsEmoji(0x1F600));
Assert.True(UnicodeEmoji.HasEmojiPresentation(0x1F600));
Assert.False(UnicodeEmoji.HasEmojiPresentation(0x2764)); // the heart is shown as text unless followed by U+FE0F

// The emoji that are sequences of code points, such as flags
var flags = UnicodeEmoji.GetSequences(UnicodeEmojiSequenceProperty.RgiEmojiFlagSequence);
Assert.Contains("\U0001F1EB\U0001F1F7", flags);
```
<!-- endref -->

- A block is a range of code points with a name. It says where a character was allocated, not what it is: to know the
  writing system of a character, use its script (see [Scripts](04-Scripts.md)).
- `UnicodeEmoji.GetSequences` lists the emoji that are sequences of code points (flags, keycaps, people with a skin
  tone, sequences joined with U+200D), from the properties of strings of UTS #51.
- An emoji is often more than one code point: to count or cut emoji, use grapheme clusters (see
  [Text segmentation](07-TextSegmentation.md)).

## Get the full record of a character

`Unicode.GetCharacterInfo` gives everything the Unicode Character Database says about a character, including its name.
It is meant for inspection tools, such as a character map; to read one property of many code points, prefer
`UnicodeProperties`.

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_GetCharacterInfo;format=md-fenced-code-block;dedent=true -->
```csharp
var info = Unicode.GetCharacterInfo(new Rune('A'));

Assert.NotNull(info);
Assert.Equal("LATIN CAPITAL LETTER A", info.Value.Name);
Assert.Equal(UnicodeCategory.UppercaseLetter, info.Value.Category);
Assert.Equal(UnicodeBidirectionalCategory.LeftToRight, info.Value.BidiCategory);
Assert.Equal("Basic Latin", info.Value.Block.Name);
```
<!-- endref -->

`GetCharacterInfo` returns `null` for a code point the Unicode data does not list, and `TryGetCharacterInfo` returns
`false`. `Unicode.AllCharacters` lists the records of the Unicode data.
