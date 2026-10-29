---
title: Parameters
description: Pass the arguments of the method, and keep default values with the executor.
---

## Pass the arguments

`Execute` and `ExecuteAsync` take the arguments as an `object?[]`, with exactly one entry per parameter of the method, in
order. A wrong number of arguments throws an `ArgumentException`. For a method without parameters, `null` and an empty
array are both accepted.

The executor does not apply the default values of optional parameters: pass a value for each parameter.

## Keep default values

`Create(methodInfo, parameterDefaultValues)` stores one default value per parameter with the executor, and
`GetDefaultValueForParameter(index)` returns it. Use them to build the arguments when the caller has no value for some
parameters, for instance a parameter that a framework did not bind:

<!-- ref:../../tests/Meziantou.Framework.ObjectMethodExecutor.Tests/ObjectMethodExecutorTests.cs;region=ObjectMethodExecutor_DefaultValues;format=md-fenced-code-block;dedent=true -->
```csharp
var methodInfo = typeof(Sample).GetMethod(nameof(Sample.AsyncMethod))!;
var executor = ObjectMethodExecutor.Create(methodInfo, parameterDefaultValues: [0, 100]);

// The caller has a value for the first parameter only
var parameters = new object?[] { 5, executor.GetDefaultValueForParameter(1) };
Assert.Equal(105, await executor.ExecuteAsync(new Sample(), parameters));
```
<!-- endref -->

The executor never reads these values: they are only stored for the caller. `Create` throws an `ArgumentException` when
the array does not have one entry per parameter, and `GetDefaultValueForParameter` throws an
`InvalidOperationException` when the executor was created without default values.
