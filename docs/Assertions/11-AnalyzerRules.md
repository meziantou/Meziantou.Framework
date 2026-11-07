---
title: Analyzer rules
description: The analyzers of the package report assertions that cannot work and suggest clearer ones, with code fixes.
---

The package ships Roslyn analyzers and code fixes. They run in the IDE and during the build as soon as the package is
referenced: nothing else to install. They report two kinds of problems:

- **assertions that cannot work**, reported as errors: an assertion that is never awaited, that always passes, or that
  compares a value type by reference;
- **assertions that could be clearer**, reported as warnings: a generic `Assert.True(...)` where a specialized
  assertion would write a better failure message, or arguments in the wrong order.

Most rules come with a code fix: apply it from the light bulb of your IDE, or fix all the occurrences of a rule at once.

## Assertions that cannot work

| Id | Reports |
| --- | --- |
| `MFAS0048` | An assertion that returns a `Task`, such as an assertion on an `IAsyncEnumerable<T>` or [Throws](cref:Meziantou.Framework.Assertions.Assert.Throws) with an asynchronous delegate, that is not awaited. It never runs |
| `MFAS0057` | A delegate that returns an awaitable the assertion does not await, such as a `ValueTask<T>`. Wrap the call in an `async` lambda |
| `MFAS0049` | An assertion that always produces the same result, such as `Assert.True(true)` or a value compared with itself |
| `MFAS0002` | `Assert.ReferenceEquals` instead of [Assert.Same](cref:Meziantou.Framework.Assertions.Assert.Same) |
| `MFAS0003` | [Assert.IsType](cref:Meziantou.Framework.Assertions.Assert.IsType) with a static or abstract type: no value is exactly of such a type |
| `MFAS0008`, `MFAS0009` | [Assert.Null](cref:Meziantou.Framework.Assertions.Assert.Null) or [Assert.NotNull](cref:Meziantou.Framework.Assertions.Assert.NotNull) with a non-nullable value type, which is never `null` |
| `MFAS0010`, `MFAS0011` | [Assert.Same](cref:Meziantou.Framework.Assertions.Assert.Same) or [Assert.NotSame](cref:Meziantou.Framework.Assertions.Assert.NotSame) with a value type, which is boxed into a new object on each call |

## Argument order

`MFAS0001` reports a call whose actual value is a constant while its expected value is not, such as
`Assert.Equal(result, 42)`. The failure message would label the values the wrong way round. The code fix swaps them:
`Assert.Equal(42, result)`.

## Specialized assertions

A specialized assertion writes a better failure message than [Assert.True](cref:Meziantou.Framework.Assertions.Assert.True) or [Assert.Equal](cref:Meziantou.Framework.Assertions.Assert.Equal) on a derived value: it
shows the collection, the text or the range, not only `false`.

| Instead of | Write | Id |
| --- | --- | --- |
| `Assert.True(text.Contains("x"))` | `Assert.Contains("x", text)` | `MFAS0016` |
| `Assert.True(text.StartsWith("x"))` | `Assert.StartsWith("x", text)` | `MFAS0018` |
| `Assert.True(text.EndsWith("x"))` | `Assert.EndsWith("x", text)` | `MFAS0020` |
| `Assert.True(Regex.IsMatch(text, pattern))` | `Assert.Matches(pattern, text)` | `MFAS0014` |
| `Assert.True(list.Contains(item))` | `Assert.Contains(item, list)` | `MFAS0022` |
| `Assert.True(list.Any(x => x > 0))` | `Assert.Contains(list, x => x > 0)` | `MFAS0024` |
| `Assert.True(list.All(x => x > 0))` | `Assert.All(list, x => x > 0)` | `MFAS0026` |
| `Assert.True(list.Any())` | `Assert.NotEmpty(list)` | `MFAS0028` |
| `Assert.NotEmpty(list.Where(x => x > 0))` | `Assert.Contains(list, x => x > 0)` | `MFAS0051` |
| `Assert.Single(list.Where(x => x > 0))` | `Assert.Single(list, x => x > 0)` | `MFAS0053` |
| `Assert.Contains(list, x => x == 42)` | `Assert.Contains(42, list)` | `MFAS0054` |
| `Assert.Equal(3, list.Count)` | `Assert.HasCount(3, list)` | `MFAS0005` |
| `Assert.Equal(0, list.Count)`, `Assert.True(list.Count > 0)` | `Assert.Empty(list)`, `Assert.NotEmpty(list)` | `MFAS0004` |
| `Assert.HasCount(0, list)` | `Assert.Empty(list)` | `MFAS0030` |
| `Assert.True(value == 42)` | `Assert.Equal(42, value)` | `MFAS0031` |
| `Assert.True(value.Equals(other))` | `Assert.Equal(value, other)` | `MFAS0033` |
| `Assert.True(actual.SequenceEqual(expected))` | `Assert.Equal(expected, actual)` | `MFAS0035` |
| `Assert.True(ReferenceEquals(a, b))` | `Assert.Same(a, b)` | `MFAS0037` |
| `Assert.True(value == null)` | `Assert.Null(value)` | `MFAS0006` |
| `Assert.True(value is string)` | `Assert.IsAssignableTo<string>(value)` | `MFAS0012` |
| `Assert.True(value.GetType() == typeof(List<int>))` | `Assert.IsType<List<int>>(value)` | `MFAS0046` |
| `Assert.True(0 <= value && value <= 10)` | `Assert.InRange(value, 0, 10)` | `MFAS0039` |
| `Assert.True(set.IsProperSubsetOf(other))` | `Assert.ProperSubset(other, set)` | `MFAS0041` |
| `Assert.True(!condition)` | `Assert.False(condition)` | `MFAS0045` |
| `Assert.True(false)` | [Assert.Fail()](cref:Meziantou.Framework.Assertions.Assert.Fail) | `MFAS0050` |

Each rule that rewrites `Assert.True(...)` has a twin for the negated form, `Assert.False(...)`, which suggests the
negative assertion: [DoesNotContain](cref:Meziantou.Framework.Assertions.Assert.DoesNotContain), [DoesNotStartWith](cref:Meziantou.Framework.Assertions.Assert.DoesNotStartWith), [DoesNotEndWith](cref:Meziantou.Framework.Assertions.Assert.DoesNotEndWith), [DoesNotMatch](cref:Meziantou.Framework.Assertions.Assert.DoesNotMatch), [DoesNotAll](cref:Meziantou.Framework.Assertions.Assert.DoesNotAll), [Empty](cref:Meziantou.Framework.Assertions.Assert.Empty),
[NotEqual](cref:Meziantou.Framework.Assertions.Assert.NotEqual), [NotSame](cref:Meziantou.Framework.Assertions.Assert.NotSame), [NotNull](cref:Meziantou.Framework.Assertions.Assert.NotNull), [IsNotAssignableTo](cref:Meziantou.Framework.Assertions.Assert.IsNotAssignableTo), [IsNotType](cref:Meziantou.Framework.Assertions.Assert.IsNotType), [NotInRange](cref:Meziantou.Framework.Assertions.Assert.NotInRange), [NotProperSubset](cref:Meziantou.Framework.Assertions.Assert.NotProperSubset) or
[NotProperSuperset](cref:Meziantou.Framework.Assertions.Assert.NotProperSuperset) (`MFAS0007`, `MFAS0013`, `MFAS0015`, `MFAS0017`, `MFAS0019`, `MFAS0021`, `MFAS0023`, `MFAS0025`,
`MFAS0027`, `MFAS0029`, `MFAS0032`, `MFAS0034`, `MFAS0036`, `MFAS0038`, `MFAS0040`, `MFAS0042`, `MFAS0044`,
`MFAS0047`). The same goes for
`Assert.Empty(list.Where(...))` (`MFAS0052`), `Assert.DoesNotContain(list, x => x == 42)` (`MFAS0055`), and
`Assert.True(set.IsProperSupersetOf(other))` (`MFAS0043`).

## Use the returned value

`MFAS0056`, reported as a suggestion, points to code that derives again a value the assertion returned, such as
`collection[0]` or `collection.First()` after `Assert.Single(collection)`. Its code fix stores the returned value in a
variable and uses it. See [Getting started](02-GettingStarted.md#use-the-values-the-assertions-return).

## Change the severity of a rule

Set the severity of a rule in the `.editorconfig` file of your test projects, like any other analyzer rule:

```editorconfig
[*.cs]
dotnet_diagnostic.MFAS0005.severity = suggestion
dotnet_diagnostic.MFAS0056.severity = none
```

The severity can be `error`, `warning`, `suggestion`, `silent` or `none`.
