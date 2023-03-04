# Meziantou.Framework.ObjectMethodExecutor

Allow to execute synchronous and asynchronous methods using reflection. `ExecuteAsync` support all methods returning an abject with a compatible `GetAwaiter` method. 

````c#
var methodInfo = typeof(Sample).GetMethod("AsyncMethod");
var executor = ObjectMethodExecutor.Create(methodInfo);

var instance = new Sample();
var params = new object?[] { param1, param2 };
if (executor.IsMethodAsync)
{
    // You can call ExecuteAsync even if the underlying method is not async.
    // It will wrap the result into a Task.
    var result = await executor.ExecuteAsync(instance, params);
}
else
{
    var result = executor.Execute(instance, params);
}
````
