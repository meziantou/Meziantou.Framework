---
title: Options
description: Change the maximum length, the separator, the case and the allowed characters of a slug, or replace characters.
---

`Slug.Create(text, options)` takes a [SlugOptions](cref:Meziantou.Framework.SlugOptions). `null` uses the default options.

| Option | Default | Description |
| --- | --- | --- |
| [MaximumLength](cref:Meziantou.Framework.SlugOptions.MaximumLength) | `80` ([SlugOptions.DefaultMaximumLength](cref:Meziantou.Framework.SlugOptions.DefaultMaximumLength)) | Maximum length of the slug. `0` or a negative value means no limit |
| [Separator](cref:Meziantou.Framework.SlugOptions.Separator) | `"-"` ([SlugOptions.DefaultSeparator](cref:Meziantou.Framework.SlugOptions.DefaultSeparator)) | Text written in place of the characters that are not allowed. It cannot be `null`, but it can be empty |
| [CanEndWithSeparator](cref:Meziantou.Framework.SlugOptions.CanEndWithSeparator) | `false` | Keeps a separator at the end of the slug |
| [CasingTransformation](cref:Meziantou.Framework.CasingTransformation) | [PreserveCase](cref:Meziantou.Framework.CasingTransformation.PreserveCase) | [PreserveCase](cref:Meziantou.Framework.CasingTransformation.PreserveCase), [ToLowerCase](cref:Meziantou.Framework.CasingTransformation.ToLowerCase) or [ToUpperCase](cref:Meziantou.Framework.CasingTransformation.ToUpperCase) |
| [Culture](cref:Meziantou.Framework.SlugOptions.Culture) | `null` | Culture of the case transformation. `null` uses the invariant casing rules |
| [AllowedRanges](cref:Meziantou.Framework.SlugOptions.AllowedRanges) | `a`-`z`, `A`-`Z`, `0`-`9` | Unicode ranges of the characters kept in the slug |

## Limit the length

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_MaximumLength;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new SlugOptions
{
    MaximumLength = 10,
    CasingTransformation = CasingTransformation.ToLowerCase,
};

// "the-quick-" is cut to 10 characters, then the trailing separator is removed
Assert.Equal("the-quick", Slug.Create("The quick brown fox", options));

// The slug is cut between characters, not between words
options.MaximumLength = 12;
Assert.Equal("the-quick-br", Slug.Create("The quick brown fox", options));
```
<!-- endref -->

The limit applies to the returned slug and is never exceeded. The slug is cut between characters, so it never ends with
half of a surrogate pair, half of a multi-character separator, or a letter without its accents; it can therefore be a
little shorter than the limit. The slug is not cut between words.

## Change the separator

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_Separator;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new SlugOptions
{
    Separator = "_",
    CasingTransformation = CasingTransformation.ToUpperCase,
};
Assert.Equal("RELEASE_NOTES_V2", Slug.Create("Release notes: v2!", options));

options.CanEndWithSeparator = true;
Assert.Equal("RELEASE_NOTES_V2_", Slug.Create("Release notes: v2!", options));
```
<!-- endref -->

Use a separator made of characters that are not allowed. When the text itself contains an allowed separator character,
it is kept as content, so a trailing one stays even when [CanEndWithSeparator](cref:Meziantou.Framework.SlugOptions.CanEndWithSeparator) is `false`.

## Allow more characters

[AllowedRanges](cref:Meziantou.Framework.SlugOptions.AllowedRanges) is a list of `UnicodeRange` (from `System.Text.Unicode`). Add ranges to keep more characters:

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_AllowedRanges;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new SlugOptions { CasingTransformation = CasingTransformation.ToLowerCase };

// Keep Cyrillic letters in addition to a-z, A-Z and 0-9
options.AllowedRanges.Add(UnicodeRanges.Cyrillic);
Assert.Equal("привет-мир", Slug.Create("Привет, мир!", options));
```
<!-- endref -->

The list has no setter. To replace the default ranges, clear it, then add yours:

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_CustomOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new SlugOptions
{
    MaximumLength = 20,
    Separator = "-",
    CanEndWithSeparator = false,
    CasingTransformation = CasingTransformation.ToLowerCase,
};

// AllowedRanges is read-only, so change the list in place instead of assigning a new one.
// It already contains a-z, A-Z and 0-9, so clear it first to replace that set.
options.AllowedRanges.Clear();
options.AllowedRanges.Add(UnicodeRange.Create('a', 'z'));
options.AllowedRanges.Add(UnicodeRange.Create('A', 'Z'));
options.AllowedRanges.Add(UnicodeRange.Create('0', '9'));

var result = Slug.Create("This is a text", options);
Assert.Equal("this-is-a-text", result);
```
<!-- endref -->

An **empty** list allows every character instead of none: clearing it without adding a range returns the text
unfiltered, with its spaces, punctuation and diacritics, and only the case transformation applied.

## Replace characters

[SlugOptions](cref:Meziantou.Framework.SlugOptions) has two virtual methods that run for each character of the text:

- `IsAllowed(Rune)` tells whether the character is kept. The default implementation checks [AllowedRanges](cref:Meziantou.Framework.SlugOptions.AllowedRanges).
- `Replace(Rune)` returns the text written for a kept character. The default implementation applies
  [CasingTransformation](cref:Meziantou.Framework.CasingTransformation).

Override them to write a character differently:

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_CustomOptionsClass;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class AmpersandSlugOptions : SlugOptions
{
    // Keep '&' instead of turning it into a separator...
    public override bool IsAllowed(Rune character) => character.Value is '&' || base.IsAllowed(character);

    // ...and write it as "and"
    public override string Replace(Rune rune) => rune.Value is '&' ? "and" : base.Replace(rune);
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_CustomOptionsUsage;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Tom-and-Jerry", Slug.Create("Tom & Jerry", new AmpersandSlugOptions()));
```
<!-- endref -->

The text is decomposed before these methods run, so a letter and its accents are separate characters: `é` is `e`
followed by a combining accent. [IsAllowed](cref:Meziantou.Framework.SlugOptions.IsAllowed) receives the character before [Replace](cref:Meziantou.Framework.SlugOptions.Replace) and the case transformation. An
accent that is not allowed is dropped without writing a separator. A replacement is never cut by [MaximumLength](cref:Meziantou.Framework.SlugOptions.MaximumLength): it is
kept whole or dropped.
