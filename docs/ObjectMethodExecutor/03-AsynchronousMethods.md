---
title: Asynchronous methods
description: Await the result of any awaitable method, call async void methods, and avoid the boxing of ExecuteAsync.
---

## Supported return types

A method is asynchronous ([IsMethodAsync](cref:Meziantou.Framework.ObjectMethodExecutor.IsMethodAsync) is `true`) when its return type follows the awaitable pattern: it has a
`GetAwaiter()` method that returns an awaiter with `IsCompleted`, `GetResult()` and `OnCompleted`. This covers:

- `Task` and `Task<T>`, `ValueTask` and `ValueTask<T>`;
- custom awaitable types, including those whose awaiter only implements `INotifyCompletion`;
- F# `Async<T>` (`FSharpAsync<T>`), which is started as a task. The executor finds the F# types at run time, so
  applications that do not use F# do not need FSharp.Core.

[ExecuteAsync](cref:Meziantou.Framework.ObjectMethodExecutor.ExecuteAsync) returns an [ObjectMethodExecutorAwaitable](cref:Meziantou.Framework.ObjectMethodExecutorAwaitable). Awaiting it gives the result of the method as an `object`,
or `null` when the awaitable has no result, such as a `Task` or an F# `Async<unit>`.

## Avoid the boxing of ExecuteAsync

[ExecuteAsync](cref:Meziantou.Framework.ObjectMethodExecutor.ExecuteAsync) works for every awaitable type, but it has to handle the awaitable, its awaiter and the result as `object`
values, which can allocate. When you know the return type of the method, call [Execute](cref:Meziantou.Framework.ObjectMethodExecutor.Execute) and await the result you get:

<!-- ref:../../tests/Meziantou.Framework.ObjectMethodExecutor.Tests/ObjectMethodExecutorTests.cs;region=ObjectMethodExecutor_KnownReturnType;format=md-fenced-code-block;dedent=true -->
```csharp
var executor = ObjectMethodExecutor.Create(typeof(Sample).GetMethod(nameof(Sample.AsyncMethod))!);

// Execute returns the Task<int> returned by the method
var task = (Task<int>)executor.Execute(new Sample { Offset = 10 }, [1, 2])!;
Assert.Equal(13, await task);
```
<!-- endref -->

## async void methods

An `async void` method returns nothing to await, so [IsMethodAsync](cref:Meziantou.Framework.ObjectMethodExecutor.IsMethodAsync) is `false`. [Execute](cref:Meziantou.Framework.ObjectMethodExecutor.Execute) starts it and returns `null`
without waiting for it to complete. [ExecuteAsync](cref:Meziantou.Framework.ObjectMethodExecutor.ExecuteAsync) throws an `InvalidOperationException` for such a method: use
[Execute](cref:Meziantou.Framework.ObjectMethodExecutor.Execute).

## Value types

For an instance method of a struct, pass the boxed value as the target. A method that changes the struct changes the
boxed instance, as `MethodInfo.Invoke` does.
