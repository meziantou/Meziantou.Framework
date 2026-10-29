---
title: Introduction
description: Call a method known only by its MethodInfo, synchronous or asynchronous, and get its result as an object.
---

`Meziantou.Framework.ObjectMethodExecutor` calls a method from its `MethodInfo`, like `MethodInfo.Invoke`, and handles
the asynchronous methods for you: `ExecuteAsync` awaits whatever the method returns, a `Task`, a `ValueTask`, an F#
`Async` or any type with a `GetAwaiter` method, and gives you the result as an `object`.

<!-- ref:../../tests/Meziantou.Framework.ObjectMethodExecutor.Tests/ObjectMethodExecutorTests.cs;region=ObjectMethodExecutor_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Synchronous method
var replaceMethod = typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;
var replaceExecutor = ObjectMethodExecutor.Create(replaceMethod);
Assert.Equal("Hello Universe", await InvokeAsync(replaceExecutor, "Hello World", ["World", "Universe"]));

// Asynchronous method
var readToEndMethod = typeof(StringReader).GetMethod(nameof(StringReader.ReadToEndAsync), Type.EmptyTypes)!;
var readToEndExecutor = ObjectMethodExecutor.Create(readToEndMethod);
using var reader = new StringReader("Hello");
Assert.Equal("Hello", await InvokeAsync(readToEndExecutor, reader, []));

static async Task<object?> InvokeAsync(ObjectMethodExecutor executor, object instance, object?[] parameters)
{
    if (executor.IsMethodAsync)
    {
        return await executor.ExecuteAsync(instance, parameters);
    }
    else
    {
        return executor.Execute(instance, parameters);
    }
}
```
<!-- endref -->

Use it in frameworks that call user code discovered at run time: command handlers, message handlers, plug-ins, test
runners... The executor compiles a delegate for the method once, so the calls that follow do not pay the cost of
reflection again.

## Next steps

- [Getting started](02-GettingStarted.md): create an executor and call a method.
- [Asynchronous methods](03-AsynchronousMethods.md): the awaitable types, `async void`, and when to use `Execute`.
- [Parameters](04-Parameters.md): pass the arguments and the default values.
- [Trimming and Native AOT](05-TrimmingAndNativeAot.md): what the executor requires at run time.
