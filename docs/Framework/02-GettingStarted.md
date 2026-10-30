---
title: Getting started
description: Install the package and use a few of its most common helpers.
---

## Install the package

Add the `Meziantou.Framework` package to a project:

```bash
dotnet add package Meziantou.Framework
```

The examples of this documentation omit the `using` directives. Most of them need `using Meziantou.Framework;`, and
the collections need `using Meziantou.Framework.Collections;`.

## Compare and clean strings

The string extensions make the comparison explicit, and handle `null`:

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

See [Strings](03-Strings.md) for the other string helpers.

## Format values for machines

`ToStringInvariant` formats numbers and dates with the invariant culture, for file names, logs, protocols and anything
that must not depend on the culture of the user:

<!-- ref:../../tests/Meziantou.Framework.Tests/CultureInfoUtilitiesTests.cs;region=FormattableExtensions_ToStringInvariant;format=md-fenced-code-block;dedent=true -->
```csharp
// Numbers, dates and any IFormattable, formatted with the invariant culture
Assert.Equal("1234.5", 1234.5.ToStringInvariant());
Assert.Equal("1,234.50", 1234.5m.ToStringInvariant("N2"));
Assert.Equal("2024-03-15", new DateOnly(2024, 3, 15).ToStringInvariant("yyyy-MM-dd"));
Assert.Equal("00:30:00", TimeSpan.FromMinutes(30).ToStringInvariant());
```
<!-- endref -->

See [Formatting and cultures](04-FormattingAndCultures.md).

## Work with sequences

<!-- ref:../../tests/Meziantou.Framework.Tests/EnumerableTests.cs;region=EnumerableExtensions_Filtering;format=md-fenced-code-block;dedent=true -->
```csharp
string?[] values = ["a", null, "", " ", "b"];

// Remove null values, and get a sequence of non-nullable items
IEnumerable<string> notNull = values.WhereNotNull();
Assert.Equal(["a", "", " ", "b"], notNull);
Assert.Equal(["a", " ", "b"], values.WhereNotNullOrEmpty());
Assert.Equal(["a", "b"], values.WhereNotNullOrWhiteSpace());

// Treat a null sequence as an empty one
IEnumerable<int>? missing = null;
Assert.Empty(missing.EmptyIfNull());
```
<!-- endref -->

See [Sequences](07-Sequences.md).

## Keep the last items

`CircularBuffer<T>` keeps a fixed number of items, and can overwrite the oldest one when it is full:

<!-- ref:../../tests/Meziantou.Framework.Tests/Collections/CircularBufferTests.cs;region=CircularBuffer_AllowOverwrite;format=md-fenced-code-block;dedent=true -->
```csharp
var buffer = new CircularBuffer<int>(3) { AllowOverwrite = true };
buffer.AddLast(1);
buffer.AddLast(2);
buffer.AddLast(3);
Assert.Equal([1, 2, 3], buffer);

buffer.AddLast(4); // overwrites 1
Assert.Equal([2, 3, 4], buffer);
```
<!-- endref -->

See [Collections](06-Collections/01-Overview.md) for the other collections.

## Next steps

Browse the topics of the [introduction](01-Introduction.md#what-you-can-do-with-it): each page stands on its own.
