---
title: Getting started
description: Install the package, create an executor from a MethodInfo, and call the method.
---

## Install the package

<InstallNuGetPackage Id="Meziantou.Framework.ObjectMethodExecutor" />

[ObjectMethodExecutor](cref:Meziantou.Framework.ObjectMethodExecutor) is in the [Meziantou.Framework](cref:Meziantou.Framework.ObjectMethodExecutor!N:Meziantou.Framework) namespace. The examples of this documentation omit the `using`
directives, and check their results with assertions such as `Assert.Equal`.

## Call a method

Given a class:

<!-- ref:../../tests/Meziantou.Framework.ObjectMethodExecutor.Tests/ObjectMethodExecutorTests.cs;region=ObjectMethodExecutor_Sample;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Sample
{
    public int Offset { get; init; }

    public async Task<int> AsyncMethod(int a, int b)
    {
        await Task.Yield();
        return Offset + a + b;
    }
}
```
<!-- endref -->

Create an executor from the `MethodInfo` of the method, then call it with the instance and the arguments:

<!-- ref:../../tests/Meziantou.Framework.ObjectMethodExecutor.Tests/ObjectMethodExecutorTests.cs;region=ObjectMethodExecutor_Execute;format=md-fenced-code-block;dedent=true -->
```csharp
var methodInfo = typeof(Sample).GetMethod(nameof(Sample.AsyncMethod))!;
var executor = ObjectMethodExecutor.Create(methodInfo);

var instance = new Sample { Offset = 10 };
var parameters = new object?[] { 1, 2 };
if (executor.IsMethodAsync)
{
    // You can call ExecuteAsync even if the underlying method is not async.
    // It returns an awaitable that completes with the return value of the method.
    var result = await executor.ExecuteAsync(instance, parameters);
    Assert.Equal(13, result);
}
else
{
    var result = executor.Execute(instance, parameters);
    Assert.Equal(13, result);
}
```
<!-- endref -->

- [ObjectMethodExecutor.Create](cref:Meziantou.Framework.ObjectMethodExecutor.Create) inspects the method and compiles a delegate to call it. Create the executor once and
  reuse it: the creation is the expensive part.
- [IsMethodAsync](cref:Meziantou.Framework.ObjectMethodExecutor.IsMethodAsync) is `true` when the method returns an awaitable type.
- [Execute](cref:Meziantou.Framework.ObjectMethodExecutor.Execute) calls the method and returns its return value, or `null` for a `void` method.
- [ExecuteAsync](cref:Meziantou.Framework.ObjectMethodExecutor.ExecuteAsync) returns an awaitable that completes with the result of the method once it is awaited.
- The target is the instance; pass `null` for a static method.

You can call [ExecuteAsync](cref:Meziantou.Framework.ObjectMethodExecutor.ExecuteAsync) for every method, even a synchronous one: the awaitable then completes with the return value.

## Next steps

- [Asynchronous methods](03-AsynchronousMethods.md): the supported awaitable types.
- [Parameters](04-Parameters.md): the arguments and their default values.
