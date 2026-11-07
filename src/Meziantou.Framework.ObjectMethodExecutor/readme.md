# Meziantou.Framework.ObjectMethodExecutor

Allow to execute synchronous and asynchronous methods using reflection. `ExecuteAsync` support all methods returning an abject with a compatible `GetAwaiter` method. 

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

Create an executor from the `MethodInfo` of the method, then call it:

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
