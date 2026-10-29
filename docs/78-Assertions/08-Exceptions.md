---
title: Exceptions
description: Check that synchronous or asynchronous code throws an exception of a given type, or does not throw.
---

## Check that code throws

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertThrowsTests.cs;region=Assert_Throws;format=md-fenced-code-block;dedent=true -->
```csharp
// The exact type, returned so you can check it
var exception = Assert.Throws<FormatException>(() => int.Parse("abc", CultureInfo.InvariantCulture));
Assert.Contains("abc", exception.Message);

static void Greet(string? name) => ArgumentException.ThrowIfNullOrEmpty(name);

// An ArgumentException, or a derived type, and its ParamName
Assert.Throws<ArgumentException>("name", () => Greet(""));
Assert.Throws<ArgumentNullException>("name", () => Greet(null));

// Throws expects the exact type, ThrowsAny accepts a derived type
Assert.ThrowsAny<ArgumentException>(() => Greet(null));

Assert.DoesNotThrow(() => int.Parse("42", CultureInfo.InvariantCulture));
```
<!-- endref -->

| Assertion | Passes when the delegate throws |
| --- | --- |
| `Throws<T>(action)` | an exception exactly of type `T` |
| `ThrowsAny<T>(action)` | an exception of type `T` or a derived type |
| `Throws<T>(paramName, action)` | an `ArgumentException` exactly of type `T`, whose `ParamName` is `paramName` |

- The assertion returns the exception, so the test can check its message or its properties.
- A delegate that returns a value, such as `() => int.Parse(text)` or `() => sut.Value`, is accepted: the value is
  discarded.
- `Throws(typeof(T), action)` and `ThrowsAny(typeof(T), action)` take the type as an argument.
- A request to skip an xUnit test, thrown by `Assert.XunitSkip`, goes through the assertion unchanged, unless its exact
  exception type is the one expected.

## Asynchronous code

When the delegate returns a `Task` or a `ValueTask`, the assertion awaits it and returns a task: await the assertion.

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertThrowsTests.cs;region=Assert_ThrowsAsync;format=md-fenced-code-block;dedent=true -->
```csharp
static async Task<int> LoadAsync()
{
    await Task.Yield();
    throw new InvalidOperationException("Not connected");
}

// The delegate returns a Task: the assertion awaits it, and you await the assertion
var exception = await Assert.Throws<InvalidOperationException>(() => LoadAsync());
Assert.Equal("Not connected", exception.Message);

await Assert.DoesNotThrow(() => Task.CompletedTask);
```
<!-- endref -->

- Delegates that return a `Task`, a `Task<T>`, a `ValueTask` or a `ValueTask<object?>` are awaited.
- For a method that returns another `ValueTask<T>`, or another awaitable type, use an `async` lambda:
  `async () => await reader.ReadAsync()`. The analyzer reports a delegate whose result would not be awaited
  (`MFAS0057`), and an assertion whose task is never awaited (`MFAS0048`): such an assertion never runs.
- To check an exception thrown synchronously by an asynchronous method, before it returns its task, discard the task
  explicitly: `Assert.Throws<ArgumentNullException>(() => _ = sut.LoadAsync(null))`.
- `ThrowsAsync<T>` and `ThrowsAnyAsync<T>` are also available for a `Func<Task>`.

## Check that code does not throw

`Assert.DoesNotThrow(action)` passes when the delegate completes. When it throws, the assertion fails, and the
exception is available as the `InnerException` of the `AssertionException`. As for `Throws`, a delegate that returns a
`Task` or a `ValueTask` is awaited, and a delegate that returns a value is accepted.

- `DoesNotThrow<T>(action)` only fails for an exception exactly of type `T`: any other exception propagates unchanged
  and fails the test as usual.
- `DoesNotThrowAny<T>(action)` fails for an exception of type `T` or a derived type.

Most tests do not need `DoesNotThrow`: an exception thrown by the test fails it anyway. Use it to make the intent of a
test explicit, or to get an assertion failure with the expression of the delegate.
