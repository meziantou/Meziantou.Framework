---
title: Failure messages
description: Read a failure message, add your own message, limit how much of a value is shown, and fail or skip a test explicitly.
---

## Read a failure message

A failed assertion throws an [AssertionException](cref:Meziantou.Framework.Assertions.AssertionException) whose message names the assertion, then shows the expressions of the
arguments, as written in the test, and their values. The character or the item where the values differ is underlined:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertionFormatterTests.cs;region=Assert_FailureMessage;format=md-fenced-code-block;dedent=true -->
```csharp
var expected = "Hello, World!";
var actual = "Hello, world!";

var exception = Assert.Throws<AssertionException>(() => Assert.Equal(expected, actual, message: "greeting"));
Assert.Equal("""
    Assert.Equal() assertion failed.
    Message: greeting
    Expected expression: expected
    Actual expression:   actual
    Index of first difference: 7
    Expected: "Hello, W̲orld!"
    Actual:   "Hello, w̲orld!"
    """, exception.Message);
```
<!-- endref -->

- The `message` argument, when you pass one, is written on the `Message:` line.
- The expressions are captured by the compiler with `[CallerArgumentExpression]`: you do not need a message to know
  which assertion failed.
- Strings and characters are quoted. Line breaks and invisible characters are escaped, including those outside the
  Basic Multilingual Plane such as `\U000E007F`, so two strings that look the same show where they differ.
- Other values are written with their `ToString()`, as is, except that line breaks and invisible characters are
  escaped too.
- When two unequal values are written the same way, for instance `0.1f` and `0.1`, the message also shows their
  types, or states that they differ but are formatted identically.

## Collections

For collections, the message gives the index of the first difference and underlines the item:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertionFormatterTests.cs;region=Assert_CollectionFailureMessage;format=md-fenced-code-block;dedent=true -->
```csharp
int[] expected = [1, 2, 3];
List<int> actual = [1, 5, 3];

var exception = Assert.Throws<AssertionException>(() => Assert.Equal(expected, actual));
Assert.Equal("""
    Assert.Equal() assertion failed: Item at index 1 differs.
    Expected expression: expected
    Actual expression:   actual
    Index of first difference: 1
    Expected: [1, 2̲, 3]
    Actual:   [1, 5̲, 3]
    """, exception.Message);
```
<!-- endref -->

## Limit how much of a value is shown

[FormatterOptions](cref:Meziantou.Framework.Assertions.FormatterOptions) controls how much of a long collection or string the messages write:

| Option | Default | Effect |
| --- | --- | --- |
| [MaxFormattedItems](cref:Meziantou.Framework.Assertions.FormatterOptions.MaxFormattedItems) | 20 | The number of items of a collection written before it is truncated with `...` |
| [PrefixItemCount](cref:Meziantou.Framework.Assertions.FormatterOptions.PrefixItemCount) | 6 | When the highlighted item is past [MaxFormattedItems](cref:Meziantou.Framework.Assertions.FormatterOptions.MaxFormattedItems), the number of items kept from the start of the collection |
| [HighlightedContextItemCount](cref:Meziantou.Framework.Assertions.FormatterOptions.HighlightedContextItemCount) | 4 | When the highlighted item is past [MaxFormattedItems](cref:Meziantou.Framework.Assertions.FormatterOptions.MaxFormattedItems), the number of items written on each side of it |
| [SuffixItemCount](cref:Meziantou.Framework.Assertions.FormatterOptions.SuffixItemCount) | 0 | The minimum number of items written after a highlighted item within the first [MaxFormattedItems](cref:Meziantou.Framework.Assertions.FormatterOptions.MaxFormattedItems) items |
| [MaxFormattedStringLength](cref:Meziantou.Framework.Assertions.FormatterOptions.MaxFormattedStringLength) | 10,000 | The number of characters of a string written. A longer string is written as a window around the highlighted character, with `...` on each truncated side and the total length |

[Assert.FormatterOptions](cref:Meziantou.Framework.Assertions.Assert.FormatterOptions) sets the options for the whole process. Tests usually run in parallel, so to change the
options for one test, use [Assert.UseFormatterOptions](cref:Meziantou.Framework.Assertions.Assert.UseFormatterOptions): the options apply to the current asynchronous flow, including
the tasks it starts, until the scope is disposed. Scopes can be nested.

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertionFormatterTests.cs;region=Assert_UseFormatterOptions;format=md-fenced-code-block;dedent=true -->
```csharp
int[] expected = [1, 2, 3];
int[] actual = [1, 2, 3];

using (Assert.UseFormatterOptions(new FormatterOptions { MaxFormattedItems = 100 }))
{
    Assert.Equal(expected, actual);
}
```
<!-- endref -->

## Stack traces

The stack trace of an [AssertionException](cref:Meziantou.Framework.Assertions.AssertionException) starts at the method that called the assertion, not inside the assertion
library, so the first line points to the failing assertion of your test. The runtime cannot hide the frames of
asynchronous methods: the stack trace of an awaited assertion still contains them.

## Fail or skip a test explicitly

- `Assert.Fail(message)` fails the test with your message, for instance in a branch the test must not reach. The
  analyzer suggests it for `Assert.True(false)` and `Assert.False(true)` (`MFAS0050`).
- `Assert.XunitSkip(reason)` skips the running xUnit test, with the reason, when the test discovers at run time that it
  cannot run. Only xUnit.v3 supports dynamically skipped tests: xUnit v2 reports them as failures. Assertions that run
  your code, such as [All](cref:Meziantou.Framework.Assertions.Assert.All), [Collection](cref:Meziantou.Framework.Assertions.Assert.Collection) or [DoesNotThrow](cref:Meziantou.Framework.Assertions.Assert.DoesNotThrow), let the skip request through unchanged.

To skip a test depending on the operating system, the globalization mode or the CI server before it starts, use the
attributes of [Meziantou.Xunit](../Xunit/01-Introduction.md).
