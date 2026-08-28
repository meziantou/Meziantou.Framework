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
```

This package also exposes Unicode character metadata from the Unicode data table:

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
