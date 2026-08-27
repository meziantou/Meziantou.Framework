# Meziantou.Framework.Unicode

This package provides Unicode helpers for normalizing characters that look like other characters, using the Unicode confusables table.

```csharp
using Meziantou.Framework;

var input = "раураl"; // Uses Cyrillic letters that look like Latin
var normalized = Unicode.ReplaceConfusablesCharacters(input);

Console.WriteLine(normalized); // "paypal"
```

Characters that are already ASCII are never replaced, so ordinary text passes through untouched:

```csharp
Unicode.ReplaceConfusablesCharacters("Item 1 of 10"); // "Item 1 of 10"
```

> The result is displayable text, not a comparison key. This is deliberately **not** the
> `skeleton` algorithm from [UTS #39](https://unicode.org/reports/tr39/): the input is not
> normalized and the mapping is applied in a single pass, so it is not sufficient on its own
> to decide whether two strings are confusable.

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
