---
title: Strings
description: Compare strings, remove prefixes, suffixes and diacritics, split lines without allocating, and escape Markdown.
---

## Compare strings

<!-- ref:../../tests/Meziantou.Framework.Tests/StringExtensionsTests.cs;region=StringExtensions_Comparisons;format=md-fenced-code-block;dedent=true -->
```csharp
// Ordinal comparisons, with or without case
Assert.True("Content-Type".EqualsIgnoreCase("content-type"));
Assert.False("Content-Type".EqualsOrdinal("content-type"));
Assert.True("Content-Type: text/html".ContainsIgnoreCase("TEXT/HTML"));

// EqualsIgnoreCase and EqualsOrdinal accept null values
string? missing = null;
Assert.False(missing.EqualsIgnoreCase("value"));
Assert.True(missing.EqualsOrdinal(null));
```
<!-- endref -->

`EqualsIgnoreCase` and `ContainsIgnoreCase` use `StringComparison.OrdinalIgnoreCase`, and `EqualsOrdinal` uses
`StringComparison.Ordinal`. The comparison is the same whatever the current culture, which is what identifiers, file
extensions, HTTP headers or configuration keys need.

## Remove a prefix or a suffix

<!-- ref:../../tests/Meziantou.Framework.Tests/StringExtensionsTests.cs;region=StringExtensions_RemovePrefixAndSuffix;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Home", "HomeController".RemoveSuffix("Controller", StringComparison.Ordinal));
Assert.Equal("Repository", "IRepository".RemovePrefix("I", StringComparison.Ordinal));

// Nothing is removed when the string does not start or end with the value
Assert.Equal("Repository", "Repository".RemoveSuffix("Controller", StringComparison.Ordinal));

// Choose the comparison, for instance to ignore case
Assert.Equal("Home", "HomeCONTROLLER".RemoveSuffix("Controller", StringComparison.OrdinalIgnoreCase));
```
<!-- endref -->

`RemovePrefix` and `RemoveSuffix` remove one occurrence, only when the string starts or ends with it. The overloads
without a `StringComparison` use an ordinal comparison. With a culture-sensitive comparison, the part removed is the
part that matched, which can be longer or shorter than the value you pass.

## Turn empty strings into null

<!-- ref:../../tests/Meziantou.Framework.Tests/StringExtensionsTests.cs;region=StringExtensions_Nullify;format=md-fenced-code-block;dedent=true -->
```csharp
// Turn empty (and optionally whitespace-only) strings into null
Assert.Null("".Nullify(trim: false));
Assert.Null("   ".Nullify(trim: true));
Assert.Equal("   ", "   ".Nullify(trim: false));

// With trim: true, the returned value is trimmed
Assert.Equal("value", "  value  ".Nullify(trim: true));
```
<!-- endref -->

`Nullify` returns `null` for `null` and empty strings. With `trim: true`, it trims the string first, so a string that
contains only white space also gives `null`, and any other string is returned trimmed.

## Remove diacritics

<!-- ref:../../tests/Meziantou.Framework.Tests/StringExtensionsTests.cs;region=StringExtensions_RemoveDiacritics;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Creme brulee a l'Ile-de-France", "Crème brûlée à l'Île-de-France".RemoveDiacritics());
```
<!-- endref -->

`RemoveDiacritics` removes the accents and other combining marks, for instance to build a URL slug or to search without
accents. It also works when the application runs in invariant globalization mode.

## Replace several characters

<!-- ref:../../tests/Meziantou.Framework.Tests/StringExtensionsTests.cs;region=StringExtensions_ReplaceAny;format=md-fenced-code-block;dedent=true -->
```csharp
var invalidCharacters = SearchValues.Create(@"/\:*?");
Assert.Equal("reports_2024_q1_draft_", @"reports/2024\q1:draft?".ReplaceAny(invalidCharacters, '_'));
```
<!-- endref -->

`ReplaceAny` replaces every character of a `SearchValues<char>` with the same character, and returns the original
string when there is nothing to replace. Create the `SearchValues<char>` once, in a `static readonly` field, when you
call it often.

## Split lines

<!-- ref:../../tests/Meziantou.Framework.Tests/StringExtensionsTests.cs;region=StringExtensions_SplitLines;format=md-fenced-code-block;dedent=true -->
```csharp
var lines = new List<string>();
var separators = new List<string>();
foreach (var (line, separator) in "first\r\nsecond\nthird".SplitLines())
{
    lines.Add(line.ToString());
    separators.Add(separator.ToString());
}

Assert.Equal(["first", "second", "third"], lines);
Assert.Equal(["\r\n", "\n", ""], separators);
```
<!-- endref -->

`SplitLines` enumerates the lines of a `string` or a `ReadOnlySpan<char>` without allocating: each line is a
`ReadOnlySpan<char>`, with the line break that ended it (empty for the last line). Call `ToString` on a line only when
you need to keep it. A text that ends with a line break does not produce an empty last line.

`\r\n` is one line break. Choose the other characters that end a line with `LineBreakMode`:

<!-- ref:../../tests/Meziantou.Framework.Tests/StringExtensionsTests.cs;region=StringExtensions_SplitLinesMode;format=md-fenced-code-block;dedent=true -->
```csharp
var unicodeLines = new List<string>();
foreach (var line in "a\u2028b".SplitLines())
{
    unicodeLines.Add(line.Line.ToString());
}

var standardLines = new List<string>();
foreach (var line in "a\u2028b".SplitLines(LineBreakMode.Standard))
{
    standardLines.Add(line.Line.ToString());
}

// By default, Unicode line separators such as U+2028 also break lines
Assert.Equal(["a", "b"], unicodeLines);

// LineBreakMode.Standard only breaks lines on \r and \n
Assert.Equal(["a\u2028b"], standardLines);
```
<!-- endref -->

| `LineBreakMode` | Line breaks |
| --- | --- |
| `Standard` | `\r`, `\n` |
| `Unicode` (default) | `\r`, `\n`, `\u0085` (next line), ` ` (line separator), ` ` (paragraph separator) |
| `UnicodeWithLegacyControls` | The Unicode ones, plus `\v` (vertical tab) and `\f` (form feed) |

## StringBuilder helpers

<!-- ref:../../tests/Meziantou.Framework.Tests/StringBuilderExtensionsTests.cs;region=StringBuilderExtensions_StartsWithAndTrim;format=md-fenced-code-block;dedent=true -->
```csharp
var sb = new StringBuilder();
foreach (var item in new[] { "apple", "banana", "cherry" })
{
    sb.Append(item).Append(',');
}

// Check and remove the trailing separator without converting the builder to a string
Assert.True(sb.EndsWith(','));
sb.TrimEnd(',');
Assert.Equal("apple,banana,cherry", sb.ToString());
Assert.True(sb.StartsWith("apple"));
```
<!-- endref -->

`StartsWith` and `EndsWith` check the content of a `StringBuilder` without creating a string, and `TrimStart`,
`TrimEnd` and `Trim` remove a repeated character at its ends. To append values formatted with the invariant culture,
see [Formatting and cultures](04-FormattingAndCultures.md#append-invariant-values).

## Escape Markdown

<!-- ref:../../tests/Meziantou.Framework.Tests/MarkdownBuilderTests.cs;region=MarkdownBuilder_Escape;format=md-fenced-code-block;dedent=true -->
```csharp
// Escape the text so that Markdown renders it as is
// Every ASCII punctuation character is escaped
Assert.Equal(@"\*Note\* \[docs\]\!", MarkdownBuilder.Escape("*Note* [docs]!"));

// Create a code span that can contain backticks
Assert.Equal("`dotnet build`", MarkdownBuilder.CreateCodeSpan("dotnet build"));
Assert.Equal("``a`b``", MarkdownBuilder.CreateCodeSpan("a`b"));
Assert.Equal("`` `code` ``", MarkdownBuilder.CreateCodeSpan("`code`"));
```
<!-- endref -->

- `MarkdownBuilder.Escape` adds a backslash before every ASCII punctuation character, so the text is displayed as is
  instead of being interpreted as Markdown.
- `MarkdownBuilder.CreateCodeSpan` creates an inline code span. It uses more backticks than the longest run of backticks
  in the content, and adds spaces when the content starts or ends with a backtick.
