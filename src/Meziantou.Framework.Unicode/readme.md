# Meziantou.Framework.Unicode

This package provides Unicode helpers for normalizing confusable characters using the Unicode confusables table.

```csharp
using Meziantou.Framework;

var input = "раураl"; // Uses Cyrillic letters that look like Latin
var normalized = Unicode.ReplaceConfusablesCharacters(input);

Console.WriteLine(normalized); // "paypal"
```

## Detecting confusable strings

To decide whether two strings look alike, use the [UTS #39](https://unicode.org/reports/tr39/)
skeleton algorithm rather than comparing the output of `ReplaceConfusablesCharacters`:

```csharp
Unicode.AreConfusable("paypal", "раураl"); // true  (Cyrillic look-alikes)
Unicode.AreConfusable("café", "café");     // true  (composed vs decomposed)
Unicode.AreConfusable("paypal", "example");     // false
```

`Unicode.GetConfusableSkeleton(string)` exposes the key itself, so it can be stored or indexed
to find collisions across a set of names:

```csharp
var skeleton = Unicode.GetConfusableSkeleton(userName);
```

> The skeleton is a comparison key, not displayable text. Do not show it to users or store it
> in place of the original string.

> These two methods require Unicode normalization and therefore ICU. In
> globalization-invariant mode (`InvariantGlobalization=true`) `string.Normalize` silently does
> nothing, so they throw `PlatformNotSupportedException` rather than return a result that would
> quietly fail to detect spoofing.

## Detecting mixed-script text

A homograph attack usually shows up as a string that mixes writing systems. `IsMixedScript`
implements the [UTS #39](https://unicode.org/reports/tr39/) resolved-script-set rules:

```csharp
Unicode.IsMixedScript("paypal");   // false - all Latin
Unicode.IsMixedScript("раураl");   // true  - Cyrillic letters with a Latin "l"
Unicode.IsMixedScript("日本語です"); // false - Japanese legitimately mixes Han and Hiragana
Unicode.IsMixedScript("user123");  // false - digits match any script
```

Script data is also exposed directly:

```csharp
UnicodeScripts.GetScript(new Rune('A'));      // UnicodeScript.Latin
UnicodeScripts.GetScript(new Rune(0x0410));   // UnicodeScript.Cyrillic
UnicodeScripts.GetIso15924Code(UnicodeScript.Latin); // "Latn"

// Script_Extensions: the scripts a shared character is used with
Span<UnicodeScript> scripts = stackalloc UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
var count = UnicodeScripts.GetScriptExtensions(0x30FC, scripts); // Hiragana, Katakana
UnicodeScripts.HasScriptExtension(0x30FC, UnicodeScript.Katakana); // true
```

## Character properties

`UnicodeProperties` gives the properties of a code point that text processing needs: segmentation, bidirectional
ordering, shaping and case. Every method takes an `int`, never throws and never allocates: the value is read from a
table indexed by the code point.

```csharp
UnicodeProperties.GetGeneralCategory('A');           // UppercaseLetter
UnicodeProperties.GetCanonicalCombiningClass(0x0301); // 230
UnicodeProperties.GetBidiClass(0x05D0);              // RightToLeft
UnicodeProperties.TryGetBidiMirroringGlyph('(', out var mirrored); // true, ')'
UnicodeProperties.GetLineBreakClass(0x00A0);         // Glue
UnicodeProperties.GetGraphemeClusterBreak(0x200D);   // ZWJ
UnicodeProperties.GetWordBreak('_');                 // ExtendNumLet
UnicodeProperties.GetEastAsianWidth(0x4E00);         // Wide
UnicodeProperties.GetJoiningType(0x0628);            // DualJoining
UnicodeProperties.GetIndicSyllabicCategory(0x094D);  // Virama
UnicodeProperties.IsDefaultIgnorable(0x00AD);        // true
UnicodeProperties.ToSimpleLowercase('A');            // 'a'
```

- The values are those of one version of the Unicode Standard, `UnicodeProperties.UnicodeVersion` (18.0.0), whatever
  the version the runtime implements. The results are the same on every platform and every framework version, and
  do not need ICU.
- An unassigned code point has the default value the standard gives to its range, for instance `RightToLeft` in the
  Hebrew block. An integer that is not a code point has the default value of the property.
- The numeric values of the enumerations (`UnicodeScript`, `UnicodeLineBreakClass`, ...) never change: a later
  version of the standard only appends values.

## Character information

`Unicode.GetCharacterInfo` gives the full record of a character, including its name. It is meant for inspection
tools: prefer `UnicodeProperties` to read a property of many code points.

```csharp
var info = Unicode.GetCharacterInfo(new Rune('A'));
if (info is not null)
{
	Console.WriteLine(info.Value.Name); // "LATIN CAPITAL LETTER A"
	Console.WriteLine(info.Value.Category); // UppercaseLetter
	Console.WriteLine(info.Value.BidiCategory); // LeftToRight
	Console.WriteLine(info.Value.Block); // BasicLatin
}
```
