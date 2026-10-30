---
title: Equality
description: Compare values, strings, floating-point numbers, dates and collections, with or without order, and compare references.
---

## Compare values

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualTests.cs;region=Assert_EqualValues;format=md-fenced-code-block;dedent=true -->
```csharp
long total = 42;
Assert.Equal(42, total);                     // numbers of different types are compared by value

var greeting = "Hello";
Assert.Equal("hello", greeting, ignoreCase: true);

var text = "first line\r\nsecond line";
Assert.Equal("first line\nsecond line", text, ignoreLineEndingDifferences: true);

Assert.NotEqual("Goodbye", greeting);
```
<!-- endref -->

- Numbers are compared by value, whatever their types: `42` equals `42L`, `42u` and `42m`.
- Other values are compared with the equality of their type (`Equals`).
- Strings are compared ordinally. `ignoreCase: true` ignores the case, and `ignoreLineEndingDifferences: true` treats
  `\r\n`, `\r` and `\n` as the same line ending, so a text read from a file checked out on Windows matches a literal
  written with `\n`.
- [NotEqual](cref:Meziantou.Framework.Assertions.Assert.NotEqual) accepts the same arguments and options as [Equal](cref:Meziantou.Framework.Assertions.Assert.Equal), and fails exactly when [Equal](cref:Meziantou.Framework.Assertions.Assert.Equal) would pass.

To choose how two values are compared, pass an `IEqualityComparer<T>`:
`Assert.Equal("abc", code, StringComparer.OrdinalIgnoreCase)`.

## Compare collections

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualTests.cs;region=Assert_EqualCollections;format=md-fenced-code-block;dedent=true -->
```csharp
List<int> numbers = [1, 2, 3];
Assert.Equal(new[] { 1, 2, 3 }, numbers);    // item by item, whatever the collection types

// Nested collections are compared by content too
var matrix = new List<int[]> { new[] { 1, 2 }, new[] { 3 } };
Assert.Equal(new[] { new[] { 1, 2 }, new[] { 3 } }, matrix);

// Pass a comparer to choose how items are compared
var tags = new[] { "Admin", "User" };
Assert.Equal(new[] { "admin", "user" }, tags, StringComparer.OrdinalIgnoreCase);

// The order matters, unless you use EqualUnordered
Assert.NotEqual(new[] { 3, 2, 1 }, numbers);
Assert.EqualUnordered(new[] { 3, 2, 1 }, numbers);
```
<!-- endref -->

- Collections are compared item by item, in order, whatever their types: an array equals a `List<T>` with the same
  items. Collections nested in items, key/value pairs and tuples are compared by content too.
- Two `null` collections are equal. Multidimensional arrays must also have the same dimensions.
- A comparer passed to [Equal](cref:Meziantou.Framework.Assertions.Assert.Equal) compares the items.
- The failure message gives the index of the first difference. See
  [Failure messages](10-FailureMessages.md#collections).

[EqualUnordered](cref:Meziantou.Framework.Assertions.Assert.EqualUnordered) compares two sequences regardless of the order of their items. Without a comparer, items are compared
like [Equal](cref:Meziantou.Framework.Assertions.Assert.Equal) compares two values, so nested collections are compared by content and `1` equals `1L`. Two `null`
sequences are equal. [NotEqualUnordered](cref:Meziantou.Framework.Assertions.Assert.NotEqualUnordered) is its exact complement: it fails when both sequences are `null`.

Spans and memory (`ReadOnlySpan<T>`, `ReadOnlyMemory<T>`) are compared the same way, and so is an
`IAsyncEnumerable<T>`, whose assertions return a task to await. See
[Collections](06-Collections.md#async-sequences).

## Compare approximately

Floating-point results and timestamps rarely match exactly. Compare them with a tolerance, a number of decimal places
or a precision:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualTests.cs;region=Assert_EqualApproximately;format=md-fenced-code-block;dedent=true -->
```csharp
var sum = 0.1 + 0.2;
Assert.NotEqual(0.3, sum);                       // 0.30000000000000004
Assert.Equal(0.3, sum, tolerance: 1e-9);         // |expected - actual| <= tolerance
Assert.Equal(3.14, Math.PI, precision: 2);       // both values rounded to 2 decimal places

var expected = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
var actual = expected.AddMilliseconds(300);
Assert.Equal(expected, actual, TimeSpan.FromSeconds(1));
```
<!-- endref -->

| Overload | Passes when |
| --- | --- |
| `Equal(expected, actual, tolerance)` | The values are equal, or differ by at most `tolerance`. For `double`, `float`, `decimal`, `Half` and other `IFloatingPoint<T>` types |
| `Equal(expected, actual, precision)` | Both values are equal once rounded to `precision` decimal places. For `double`, `float` and `decimal` |
| `Equal(expected, actual, precision, rounding)` | The same, with a `MidpointRounding` mode |
| `Equal(expected, actual, TimeSpan precision)` | The two `DateTime` or `DateTimeOffset` values are at most `precision` apart |

Without a `MidpointRounding`, values are rounded like `Math.Round`: to the nearest even digit, so `2.5` rounds to `2`.
[NotEqual](cref:Meziantou.Framework.Assertions.Assert.NotEqual) has the same overloads.

## Compare references

[Same](cref:Meziantou.Framework.Assertions.Assert.Same) passes when both arguments are the same instance, and [NotSame](cref:Meziantou.Framework.Assertions.Assert.NotSame) when they are not. Use them to check that a
method returns the instance it was given, or that a copy is a new object:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertSameTests.cs;region=Assert_Same;format=md-fenced-code-block;dedent=true -->
```csharp
var original = new List<int> { 1, 2 };
IReadOnlyList<int> view = original;
var copy = new List<int>(original);

Assert.Same(original, view);                // the same instance
Assert.NotSame(original, copy);             // another instance...
Assert.Equal(original, copy);               // ...with the same content
```
<!-- endref -->

Value types are boxed on each call, so [Same](cref:Meziantou.Framework.Assertions.Assert.Same) and [NotSame](cref:Meziantou.Framework.Assertions.Assert.NotSame) are meaningless for them: the analyzer reports it as an
error (`MFAS0010`, `MFAS0011`).

## Compare objects member by member

[Equal](cref:Meziantou.Framework.Assertions.Assert.Equal) uses the equality of the type: two instances of a class that does not override `Equals` are equal only when
they are the same instance. To compare their public properties and fields instead, use [Equivalent](cref:Meziantou.Framework.Assertions.Assert.Equivalent). See
[Equivalence](04-Equivalence.md).
