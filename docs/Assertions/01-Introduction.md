---
title: Introduction
description: An Assert class for .NET tests whose failure messages show where values differ, with analyzers and code fixes.
---

`Meziantou.Framework.Assertions` provides an `Assert` class for unit tests. Its assertions compare values, text,
collections, types, exceptions and events, and their failure messages show the expressions you wrote, the values, and
the item or character where they differ. The package also ships analyzers and code fixes that report weak or wrong
assertions as you type, such as `Assert.True(list.Contains(item))` or an assertion that is never awaited.

A failed assertion throws an `AssertionException`, which every test framework reports as a failure. The only part tied
to a test framework is `Assert.XunitSkip`, which skips an xUnit.v3 test.

## Not the Assert class of your test framework

`Meziantou.Framework.Assertions.Assert` is a class of its own, not the `Assert` class of your test framework. Many
method names are the same, but the rules are this library's, and one of them is easy to miss: **the expected value
always comes first**, then the actual value, for every assertion, including `Contains`, `StartsWith` and `EndsWith`:

- `Assert.Equal(expected, actual)`
- `Assert.Contains(expectedItemOrSubstring, actual)`
- `Assert.StartsWith(expectedPrefix, actual)`
- `Assert.Matches(pattern, actual)`
- `Assert.HasCount(expectedCount, actual)`

The analyzer reports a call where the constant is passed as the actual value (`MFAS0001`). The exceptions are the
assertions without an expected value: `Assert.Contains(collection, predicate)`, `Assert.All(collection, assertion)` and
`Assert.InRange(actual, low, high)` take the value under test first.

To use this `Assert` class when you write `Assert`, add a global alias. See
[Getting started](02-GettingStarted.md#use-it-as-the-assert-class).

## What you can do with it

- **Compare values**: numbers of different types, strings ignoring case or line endings, floating-point values with a
  tolerance, dates with a precision, collections item by item. See [Equality](03-Equality.md).
- **Compare objects member by member**, regardless of their types, including JSON and XML. See
  [Equivalence](04-Equivalence.md).
- **Check text**: substrings, prefixes, suffixes and regular expressions. See [Text](05-Text.md).
- **Check collections**: items, subsequences, predicates, counts, single items, per-item assertions, duplicates and
  sets, on lists, spans, lazy sequences and `IAsyncEnumerable<T>`. See [Collections](06-Collections.md).
- **Check nulls, types and ranges**, and get the value back with the right type. See
  [Nulls, types and ranges](07-NullsTypesAndRanges.md).
- **Check exceptions and events**, synchronous or asynchronous. See [Exceptions](08-Exceptions.md) and
  [Events](09-Events.md).
- **Read the failure messages** and control how much of a value they show. See
  [Failure messages](10-FailureMessages.md).
- **Write better assertions** with the help of the analyzers. See [Analyzer rules](11-AnalyzerRules.md).

## Namespace

`Assert`, `AssertionException`, `EquivalentOptions`, `FormatterOptions` and `RaisedEvent<TEventArgs>` are in the
[Meziantou.Framework.Assertions](cref:Meziantou.Framework.Assertions) namespace.

## Related libraries

- [Meziantou.Xunit](../Xunit/01-Introduction.md) runs or skips xUnit.v3 tests depending on the environment.
- [Snapshot testing](../SnapshotTesting/01-Introduction.md) and
  [inline snapshot testing](../InlineSnapshotTesting/01-Introduction.md) replace a long list of assertions on an
  object with a single comparison to a snapshot.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, set up the `Assert` alias and write your first
  assertions.
- [Analyzer rules](11-AnalyzerRules.md): the mistakes the analyzers catch.
