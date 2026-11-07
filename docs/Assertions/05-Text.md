---
title: Text
description: Check that a string contains a substring, starts or ends with a prefix or suffix, or matches a regular expression.
---

## Substrings, prefixes and suffixes

The substring, prefix or suffix you look for comes first, then the text under test:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertContainsTests.cs;region=Assert_Text;format=md-fenced-code-block;dedent=true -->
```csharp
var message = "Hello, World!";

Assert.Contains("World", message);                        // ordinal comparison by default
Assert.Contains("world", message, ignoreCase: true);      // OrdinalIgnoreCase
Assert.Contains("WORLD", message, StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("Goodbye", message);

Assert.StartsWith("Hello", message);
Assert.EndsWith('!', message);
Assert.DoesNotStartWith("World", message);
Assert.DoesNotEndWith("?", message);
```
<!-- endref -->

- [Contains](cref:Meziantou.Framework.Assertions.Assert.Contains), [DoesNotContain](cref:Meziantou.Framework.Assertions.Assert.DoesNotContain), [StartsWith](cref:Meziantou.Framework.Assertions.Assert.StartsWith), [EndsWith](cref:Meziantou.Framework.Assertions.Assert.EndsWith), [DoesNotStartWith](cref:Meziantou.Framework.Assertions.Assert.DoesNotStartWith) and [DoesNotEndWith](cref:Meziantou.Framework.Assertions.Assert.DoesNotEndWith) accept a `string`, a
  `char` or a `ReadOnlySpan<char>`.
- The comparison is ordinal by default. Pass `ignoreCase: true` for `StringComparison.OrdinalIgnoreCase`, or a
  `StringComparison` for any other comparison, such as `StringComparison.CurrentCulture`.
- A message passed as the third argument, `Assert.DoesNotContain("secret", log, "the log must not leak it")`, is the
  message of the assertion: the two strings are still compared as text.
- A `null` text fails the assertion instead of being treated as an empty string.
- When [DoesNotContain](cref:Meziantou.Framework.Assertions.Assert.DoesNotContain) fails, the message shows the index where the text was found.

To compare a whole string, use [Assert.Equal](cref:Meziantou.Framework.Assertions.Assert.Equal), which can also ignore the case and the line endings. See
[Equality](03-Equality.md#compare-values).

## Regular expressions

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertMatchTests.cs;region=Assert_Matches;format=md-fenced-code-block;dedent=true -->
```csharp
var version = "v2.10.3";

Assert.Matches(@"^v\d+\.\d+\.\d+$", version);   // the pattern comes first
Assert.DoesNotMatch("-preview", version);
```
<!-- endref -->

[Matches](cref:Meziantou.Framework.Assertions.Assert.Matches) and [DoesNotMatch](cref:Meziantou.Framework.Assertions.Assert.DoesNotMatch) take a pattern or a `Regex`. A pattern is evaluated with the default match timeout, like
`Regex.IsMatch(input, pattern)`. Pass a `Regex` to choose options or a timeout, or to reuse a
[generated regular expression](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-source-generators).
