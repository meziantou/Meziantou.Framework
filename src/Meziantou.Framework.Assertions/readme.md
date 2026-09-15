# Meziantou.Framework.Assertions

Assertion helpers for .NET tests.

## Assertion methods

`Assert` provides the following assertion methods:

- `True`: Asserts a condition is true.
- `False`: Asserts a condition is false.
- `Null`: Asserts a value is null.
- `NotNull`: Asserts a value is not null and returns it with its non-nullable type (`Assert.NotNull(repository.Find(id)).Name`).
- `Same`: Asserts two references point to the same instance.
- `NotSame`: Asserts two references are different instances.
- `Equal`: Asserts two values are equal. Collections are compared item by item, including collections nested in items, key/value pairs and tuples, and two null collections are equal. Multidimensional arrays must also have the same dimensions. Overloads accept an `IEqualityComparer<T>`, a tolerance, a number of decimal places (`precision`, with an optional `MidpointRounding`) for `double`, `float` and `decimal`, a `TimeSpan` precision for `DateTime` and `DateTimeOffset`, and `ignoreCase`/`ignoreLineEndingDifferences` for strings.
- `NotEqual`: Asserts two values are not equal. It accepts the same options as `Equal` and fails exactly when `Equal` would pass.
- `EqualUnordered`: Asserts two sequences contain the same values regardless of order. Without a comparer, items are compared like `Equal` compares two values, so nested collections are compared by content and `1` equals `1L`. Two `null` sequences are equal.
- `NotEqualUnordered`: Asserts two sequences differ when compared without ordering. It is the exact complement of `EqualUnordered`, so it fails when both sequences are `null`.
- `Equivalent`: Asserts two objects are equivalent by recursively comparing their public properties and fields. Collections are compared item by item, dictionaries by key, sets regardless of order, and multi-dimensional arrays by their dimensions, then their items. Scalars, strings, dates, numbers, `Uri`, `Version`, `IPAddress`, `CultureInfo` and reflection objects such as `Type` are compared by value, and other .NET types that define their own equality (for example `Encoding` or `XName`) are compared with it. `StringBuilder` is compared by content, `FileSystemInfo` by full name, `Regex` by pattern and options, and `XNode` with `XNode.DeepEquals`. `JsonElement` and `JsonNode` are compared by JSON content: objects as dictionaries of their properties, arrays as collections, strings as strings, and other values with `DeepEquals`. A property getter that throws is compared by the type of the exception. `EquivalentOptions` can ignore the order of collections, the case of member names, and the case of strings (including dictionary keys, JSON strings and property names, and `StringBuilder` content).
- `NotEquivalent`: Asserts two objects are not equivalent, using the same rules and options as `Equivalent`.
- `IsType`: Asserts a value is exactly of the specified type and returns it. A boxed `T` is considered to be exactly of type `Nullable<T>`, as boxing a `Nullable<T>` produces a boxed `T`. The value is known to be non-null after the assertion.
- `IsNotType`: Asserts a value is not exactly of the specified type.
- `IsAssignableTo`: Asserts a value is assignable to the specified type and returns it. It matches `value is T`, including COM objects and `IDynamicInterfaceCastable` implementations. The value is known to be non-null after the assertion.
- `IsNotAssignableTo`: Asserts a value is not assignable to the specified type (`value is not T`).
- `InRange`: Asserts a value is within the inclusive range. Without a custom comparer, a NaN or a null `Nullable<T>`, whether the value or a bound, is never in range, like `low <= value && value <= high`.
- `NotInRange`: Asserts a value is outside the inclusive range.
- `StartsWith`: Asserts text or sequence starts with an expected prefix.
- `EndsWith`: Asserts text or sequence ends with an expected suffix.
- `DoesNotStartWith`: Asserts text or sequence does not start with a prefix.
- `DoesNotEndWith`: Asserts text or sequence does not end with a suffix.
- `Contains`: Asserts a value, key, predicate, or subsequence is present. When the comparer is omitted and the collection is an `ICollection<T>` (for instance a case-insensitive `HashSet<string>`), the collection decides, like `Enumerable.Contains`. A dictionary lookup without an explicit comparer is final. A subsequence search stops at the first match, so it works on endless sequences.
- `DoesNotContain`: Asserts a value, key, predicate, or subsequence is not present. The failure message shows the index where it was found.
- `Matches`: Asserts text matches a regular expression. A pattern string uses the default match timeout, like `Regex.IsMatch(input, pattern)`.
- `DoesNotMatch`: Asserts text does not match a regular expression.
- `All`: Asserts all items in a collection satisfy an assertion or a predicate. A lazy sequence is read once, and only the items a failure message can show are kept in memory.
- `DoesNotAll`: Asserts at least one item in a collection does not satisfy a predicate. Like `!collection.All(predicate)`, an empty collection fails.
- `Collection`: Asserts collection items match a list of inspectors. A lazy sequence is read at most one item past the inspectors.
- `Distinct`: Asserts all items in a sequence are distinct.
- `NotDistinct`: Asserts a sequence contains duplicate items.
- `Empty`: Asserts a collection or sequence is empty. A null array, string or collection fails, as it does for `NotEmpty`, `Single`, `Distinct`, `NotDistinct`, `All`, `DoesNotAll`, `Collection` and the count assertions.
- `NotEmpty`: Asserts a collection or sequence is not empty.
- `Single`: Asserts a sequence has exactly one item and returns it.
- `HasCount`: Asserts a collection has an exact count. Like the other count assertions, it reads a lazy sequence only as far as the comparison requires, and reports the count of a longer sequence as a lower bound (`at least N`).
- `DoesNotHaveCount`: Asserts a collection count differs from the specified count.
- `HasCountGreaterThan`: Asserts a collection count is greater than a value.
- `HasCountGreaterThanOrEqual`: Asserts a collection count is greater than or equal to a value.
- `HasCountLessThan`: Asserts a collection count is less than a value.
- `HasCountLessThanOrEqual`: Asserts a collection count is less than or equal to a value.
- `Subset`: `Assert.Subset(expectedSuperset, actual)` asserts `actual` is a subset of `expectedSuperset`, like xunit's `Assert.Subset` and `actual.IsSubsetOf(expectedSuperset)`. A `null` `actual` fails. Without a comparer, a set `actual` compares items with its own comparer; this applies to all the set assertions.
- `ProperSubset`: `Assert.ProperSubset(expectedSuperset, actual)` asserts `actual` is a proper subset of `expectedSuperset`, like xunit's `Assert.ProperSubset` and `actual.IsProperSubsetOf(expectedSuperset)`. A `null` `actual` fails.
- `NotProperSubset`: Asserts `actual` is not a proper subset of `expectedSuperset`. A `null` `actual` succeeds.
- `Superset`: `Assert.Superset(expectedSubset, actual)` asserts `actual` is a superset of `expectedSubset`, like xunit's `Assert.Superset` and `actual.IsSupersetOf(expectedSubset)`. A `null` `actual` fails.
- `ProperSuperset`: `Assert.ProperSuperset(expectedSubset, actual)` asserts `actual` is a proper superset of `expectedSubset`, like xunit's `Assert.ProperSuperset` and `actual.IsProperSupersetOf(expectedSubset)`. A `null` `actual` fails.
- `NotProperSuperset`: Asserts `actual` is not a proper superset of `expectedSubset`. A `null` `actual` succeeds.
- `Throws`: Asserts a specific exception type is thrown and returns it. Delegates returning a `Task`, a `ValueTask` or a `ValueTask<object?>` are awaited, and the assertion must then be awaited. Wrap a method returning another `ValueTask<T>` or any other awaitable in an `async` lambda (`async () => await GetAsync()`); `MFAS0057` reports a delegate whose awaitable would not be awaited; to check an exception thrown synchronously by an asynchronous method, discard its result explicitly (`() => _ = GetAsync()`). `Throws<T>(paramName, action)` also checks the `ParamName` of an `ArgumentException`. An xunit dynamic skip propagates unchanged unless its exact exception type is expected.
- `ThrowsAny`: Asserts an exception assignable to the specified type is thrown and returns it.
- `ThrowsAsync`: Asserts a specific exception type is thrown by an async delegate, optionally with the expected parameter name.
- `ThrowsAnyAsync`: Asserts an assignable exception type is thrown by an async delegate.
- `DoesNotThrow`: Asserts a delegate completes without throwing. Delegates returning a `Task` or a `ValueTask` are awaited (wrap a method returning a `ValueTask<T>` in an `async` lambda, as reported by `MFAS0057`). A function such as `() => sut.Value` is accepted and its value is discarded. The unexpected exception is available as the `InnerException` of the assertion failure. `DoesNotThrow<T>` only fails for an exception exactly of type `T`, and any other exception propagates unchanged.
- `DoesNotThrowAny`: Asserts a delegate completes without throwing an exception assignable to the specified type.
- `Raise`: Asserts a specific event is raised and captures event data. When the event is raised several times, the first raised event whose arguments match is returned. Null event arguments match any event args type. Delegates returning a `Task` or a `ValueTask` are awaited before the handler is detached, and the assertion must then be awaited. The xunit names `Raises`, `RaisesAny`, `RaisesAsync`, `RaisesAnyAsync`, `NotRaisedAny` and `NotRaisedAnyAsync` are also available.
- `RaiseAny`: Asserts an event with a compatible event args type is raised.
- `DoesNotRaise`: Asserts a specific event is not raised. It fails if any raised event matches, including an event raised with null arguments.
- `DoesNotRaiseAny`: Asserts no compatible event is raised.
- `Fail`: Fails the test with a custom message.
- `XunitSkip`: Skips the running xunit test with the specified reason. Only xunit v3 supports dynamically skipping a test; xunit v2 reports it as a failure. Assertions that run user code, such as `All`, `Collection` or `DoesNotThrow`, let the skip request propagate unchanged.

## Searching text and sequences

The text overloads of `Contains`, `DoesNotContain`, `StartsWith`, `EndsWith`, `DoesNotStartWith` and `DoesNotEndWith` compare ordinally by default. Pass `ignoreCase: true` for `OrdinalIgnoreCase`, or a `StringComparison` (as with xunit) for any other comparison. A message passed as the third argument (`Assert.DoesNotContain("secret", log, "must not leak")`) still compares the two strings as text. A `null` string or array fails these assertions instead of being treated as empty.

For these six assertions, `expected` is a subsequence (a prefix or a suffix) when its type is a sequence of the element type of `actual`, such as `int[]` in a `List<int>` or `object[]` in a `List<object>`; otherwise it is an item. Such a sequence also matches when `actual` contains it as an item, which is only possible when the element type can hold it (for example `object`). The same rule applies to non-generic `IEnumerable` arguments, where the item match is used only without a custom comparer. A `string` is compared as an item, except against a sequence of characters, where it is compared as text.

## Use as the default `Assert` class

You can override the `Assert` type used in your tests with a global alias:

```xml
<Using Include="Meziantou.Framework.Assertions.Assert" Alias="Assert" />
```

or:

```csharp
global using Assert = Meziantou.Framework.Assertions.Assert;
```

## Failure messages

Failure messages show the values involved, and underline the item or character where they differ. Strings and characters are quoted, and line breaks and invisible characters (including invisible characters outside the Basic Multilingual Plane, such as `\U000E007F`) are escaped. The text returned by `ToString()` is written as is, except that line breaks and invisible characters are escaped too. When two unequal values are formatted identically (for instance `0.1f` and `0.1`), the message also shows their types, or states that they differ but are formatted identically.

`FormatterOptions` controls how much of a value is written:

- `MaxFormattedItems` (default 20): the number of items of a collection to write before truncating it with `...`.
- `PrefixItemCount` (default 6): when the highlighted item is past `MaxFormattedItems`, the number of items kept from the start of the collection.
- `HighlightedContextItemCount` (default 4): when the highlighted item is past `MaxFormattedItems`, the number of items written on each side of it.
- `SuffixItemCount` (default 0): the minimum number of items written after a highlighted item within the first `MaxFormattedItems` items.
- `MaxFormattedStringLength` (default 10,000): the number of characters of a string to write. A longer string is written as a window around the highlighted character, with `...` on each truncated side and the total length: `..."aaaab̲cccc"... (length: 50000000)`.

`Assert.FormatterOptions` sets the options for the whole process. Tests usually run in parallel, so to change the options for a single test, use a scope instead. The options apply to the current asynchronous flow only, until the scope is disposed:

```csharp
using (Assert.UseFormatterOptions(new FormatterOptions { MaxFormattedItems = 100 }))
{
    Assert.Equal(expected, actual);
}
```

The stack trace of an `AssertionException` starts at the method that called the assertion, not inside the assertion library. The runtime cannot hide the frames of asynchronous methods, so the stack trace of an awaited assertion still contains them.

## Analyzer rules

The package ships analyzers and code fixes to help write clearer assertions.

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFAS0001` | Assertions | Pass the expected value before the actual value | Warning | ✔️ |
| `MFAS0002` | Assertions | Use Assert.Same instead of Assert.ReferenceEquals | Error | ✔️ |
| `MFAS0003` | Assertions | Do not use Assert.IsType with static or abstract types | Error | ✔️ |
| `MFAS0004` | Assertions | Use Assert.Empty or Assert.NotEmpty for zero count checks | Warning | ✔️ |
| `MFAS0005` | Assertions | Use specialized count assertions | Warning | ✔️ |
| `MFAS0006` | Assertions | Use Assert.Null for null comparisons | Warning | ✔️ |
| `MFAS0007` | Assertions | Use Assert.NotNull for null comparisons | Warning | ✔️ |
| `MFAS0008` | Assertions | Do not use Assert.Null with value types | Error | ✔️ |
| `MFAS0009` | Assertions | Do not use Assert.NotNull with value types | Error | ✔️ |
| `MFAS0010` | Assertions | Do not use Assert.Same with value types | Error | ✔️ |
| `MFAS0011` | Assertions | Do not use Assert.NotSame with value types | Error | ✔️ |
| `MFAS0012` | Assertions | Use Assert.IsAssignableTo for type pattern checks | Warning | ✔️ |
| `MFAS0013` | Assertions | Use Assert.IsNotAssignableTo for type pattern checks | Warning | ✔️ |
| `MFAS0014` | Assertions | Use Assert.Matches instead of Assert.True(Regex.IsMatch(...)) | Warning | ✔️ |
| `MFAS0015` | Assertions | Use Assert.DoesNotMatch instead of Assert.False(Regex.IsMatch(...)) | Warning | ✔️ |
| `MFAS0016` | Assertions | Use Assert.Contains instead of Assert.True(string.Contains(...)) | Warning | ✔️ |
| `MFAS0017` | Assertions | Use Assert.DoesNotContain instead of Assert.False(string.Contains(...)) | Warning | ✔️ |
| `MFAS0018` | Assertions | Use Assert.StartsWith instead of Assert.True(string.StartsWith(...)) | Warning | ✔️ |
| `MFAS0019` | Assertions | Use Assert.DoesNotStartWith instead of Assert.False(string.StartsWith(...)) | Warning | ✔️ |
| `MFAS0020` | Assertions | Use Assert.EndsWith instead of Assert.True(string.EndsWith(...)) | Warning | ✔️ |
| `MFAS0021` | Assertions | Use Assert.DoesNotEndWith instead of Assert.False(string.EndsWith(...)) | Warning | ✔️ |
| `MFAS0022` | Assertions | Use Assert.Contains instead of Assert.True(collection.Contains(...)) | Warning | ✔️ |
| `MFAS0023` | Assertions | Use Assert.DoesNotContain instead of Assert.False(collection.Contains(...)) | Warning | ✔️ |
| `MFAS0024` | Assertions | Use Assert.Contains instead of Assert.True(collection.Any(...)) | Warning | ✔️ |
| `MFAS0025` | Assertions | Use Assert.DoesNotContain instead of Assert.False(collection.Any(...)) | Warning | ✔️ |
| `MFAS0026` | Assertions | Use Assert.All instead of Assert.True(collection.All(...)) | Warning | ✔️ |
| `MFAS0027` | Assertions | Use Assert.DoesNotAll instead of Assert.False(collection.All(...)) | Warning | ✔️ |
| `MFAS0028` | Assertions | Use Assert.NotEmpty instead of Assert.True(collection.Any()) | Warning | ✔️ |
| `MFAS0029` | Assertions | Use Assert.Empty instead of Assert.False(collection.Any()) | Warning | ✔️ |
| `MFAS0030` | Assertions | Use Assert.Empty or Assert.NotEmpty instead of a count assertion | Warning | ✔️ |
| `MFAS0031` | Assertions | Use Assert.Equal instead of Assert.True(a == b) | Warning | ✔️ |
| `MFAS0032` | Assertions | Use Assert.NotEqual instead of Assert.True(a != b) | Warning | ✔️ |
| `MFAS0033` | Assertions | Use Assert.Equal instead of Assert.True(a.Equals(b)) | Warning | ✔️ |
| `MFAS0034` | Assertions | Use Assert.NotEqual instead of Assert.False(a.Equals(b)) | Warning | ✔️ |
| `MFAS0035` | Assertions | Use Assert.Equal instead of Assert.True(actual.SequenceEqual(expected)) | Warning | ✔️ |
| `MFAS0036` | Assertions | Use Assert.NotEqual instead of Assert.False(actual.SequenceEqual(expected)) | Warning | ✔️ |
| `MFAS0037` | Assertions | Use Assert.Same instead of Assert.True(ReferenceEquals(a, b)) | Warning | ✔️ |
| `MFAS0038` | Assertions | Use Assert.NotSame instead of Assert.False(ReferenceEquals(a, b)) | Warning | ✔️ |
| `MFAS0039` | Assertions | Use Assert.InRange instead of Assert.True(low <= x && x <= high) | Warning | ✔️ |
| `MFAS0040` | Assertions | Use Assert.NotInRange instead of Assert.False(low <= x && x <= high) | Warning | ✔️ |
| `MFAS0041` | Assertions | Use Assert.ProperSubset instead of Assert.True(set.IsProperSubsetOf(other)) | Warning | ✔️ |
| `MFAS0042` | Assertions | Use Assert.NotProperSubset instead of Assert.False(set.IsProperSubsetOf(other)) | Warning | ✔️ |
| `MFAS0043` | Assertions | Use Assert.ProperSuperset instead of Assert.True(set.IsProperSupersetOf(other)) | Warning | ✔️ |
| `MFAS0044` | Assertions | Use Assert.NotProperSuperset instead of Assert.False(set.IsProperSupersetOf(other)) | Warning | ✔️ |
| `MFAS0045` | Assertions | Use Assert.False instead of Assert.True(!condition) | Warning | ✔️ |
| `MFAS0046` | Assertions | Use Assert.IsType instead of Assert.True(x.GetType() == typeof(T)) | Warning | ✔️ |
| `MFAS0047` | Assertions | Use Assert.IsNotType instead of Assert.False(x.GetType() == typeof(T)) | Warning | ✔️ |
| `MFAS0048` | Assertions | Await assertions that return a Task | Error | ✔️ |
| `MFAS0049` | Assertions | Assertion always produces the same result | Error | ✔️ |
| `MFAS0050` | Assertions | Use Assert.Fail instead of an assertion that always fails | Warning | ✔️ |
| `MFAS0051` | Assertions | Use Assert.Contains instead of Assert.NotEmpty(collection.Where(...)) | Warning | ✔️ |
| `MFAS0052` | Assertions | Use Assert.DoesNotContain instead of Assert.Empty(collection.Where(...)) | Warning | ✔️ |
| `MFAS0053` | Assertions | Use Assert.Single with a predicate instead of Assert.Single(collection.Where(...)) | Warning | ✔️ |
| `MFAS0054` | Assertions | Use Assert.Contains with the expected value instead of an equality predicate | Warning | ✔️ |
| `MFAS0055` | Assertions | Use Assert.DoesNotContain with the expected value instead of an equality predicate | Warning | ✔️ |
| `MFAS0056` | Assertions | Use the value returned by the assertion instead of re-deriving it | Info | ✔️ |
| `MFAS0057` | Assertions | Do not pass an awaitable delegate to an assertion that does not await it | Error | ✔️ |
<!-- analyzer-rules -->
