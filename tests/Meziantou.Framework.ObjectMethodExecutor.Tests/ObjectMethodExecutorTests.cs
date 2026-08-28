using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Tests;

public sealed class ObjectMethodExecutorTests
{
    [Fact]
    public void StaticSyncVoidTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("StaticSyncVoid")!);
        var result = executor.Execute(target: null, [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public void SyncVoidTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncVoid")!);
        var result = executor.Execute(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public void MutatingStructMethod_AppliesMutationToBoxedInstance()
    {
        var executor = ObjectMethodExecutor.Create(typeof(MutableCounter).GetMethod(nameof(MutableCounter.Increment))!);
        object target = new MutableCounter();

        executor.Execute(target, []);

        Assert.Equal(1, ((MutableCounter)target).Value);
    }

    [Fact]
    public async Task MutatingStructMethod_AppliesMutationToBoxedInstance_Async()
    {
        var executor = ObjectMethodExecutor.Create(typeof(MutableCounter).GetMethod(nameof(MutableCounter.IncrementAsync))!);
        object target = new MutableCounter();

        await executor.ExecuteAsync(target, []);

        Assert.Equal(1, ((MutableCounter)target).Value);
    }

    [Fact]
    public void MutatingStructMethod_MatchesMethodInfoInvoke()
    {
        var methodInfo = typeof(MutableCounter).GetMethod(nameof(MutableCounter.Increment))!;
        object viaInvoke = new MutableCounter();
        methodInfo.Invoke(viaInvoke, parameters: null);

        object viaExecutor = new MutableCounter();
        ObjectMethodExecutor.Create(methodInfo).Execute(viaExecutor, []);

        Assert.Equal(((MutableCounter)viaInvoke).Value, ((MutableCounter)viaExecutor).Value);
    }

    private struct MutableCounter
    {
        public int Value;

        public void Increment() => Value++;

        public Task IncrementAsync()
        {
            Value++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void SyncInt32Test()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32")!);
        var result = executor.Execute(new Test(), []);
        Assert.Equal(1, result);
    }

    [Fact]
    public void SyncInt32WithParamTest()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam")!);
        var result = executor.Execute(new Test(), [12]);
        Assert.Equal(12, result);
    }

    [Fact]
    public void GetDefaultValueForParameter_ReturnsSuppliedValues()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam")!, [42]);

        Assert.Equal(42, executor.GetDefaultValueForParameter(0));
    }

    [Fact]
    public void GetDefaultValueForParameter_ThrowsWhenNoDefaultsSupplied()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam")!);

        Assert.Throws<InvalidOperationException>(() => executor.GetDefaultValueForParameter(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void GetDefaultValueForParameter_ThrowsWhenIndexOutOfRange(int index)
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam")!, [42]);

        Assert.Throws<ArgumentOutOfRangeException>(() => executor.GetDefaultValueForParameter(index));
    }

    [Fact]
    public void Create_ThrowsWhenDefaultValueCountDoesNotMatchParameterCount()
    {
        var methodInfo = typeof(Test).GetMethod("SyncInt32WithParam")!;

        Assert.Throws<ArgumentException>(() => ObjectMethodExecutor.Create(methodInfo, [1, 2]));
    }

    [Fact]
    public async Task StaticAsyncTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("StaticAsyncTask")!);
        var result = await executor.ExecuteAsync(null, [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task AsyncTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTask")!);
        var result = await executor.ExecuteAsync(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public void AsyncVoid_IsNotReportedAsAsync()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncVoid")!);

        Assert.False(executor.IsMethodAsync);
    }

    [Fact]
    public void AsyncVoid_ExecuteAsyncThrows()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncVoid")!);

        var exception = Assert.Throws<InvalidOperationException>(() => executor.ExecuteAsync(new Test(), [new Validator()]));
        Assert.Contains("async void", exception.Message);
    }

    [Fact]
    public void AsyncVoid_ExecuteStartsTheMethod()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncVoid")!);

        var result = executor.Execute(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task PlainVoid_IsStillExecutableAsync()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncVoid")!);

        await executor.ExecuteAsync(new Test(), [validator]);

        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task AsyncTaskInt32Tests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTaskInt32")!);
        var result = await executor.ExecuteAsync(new Test(), []);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ValueTaskInt32Tests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("ValueTaskInt32")!);
        var result = await executor.ExecuteAsync(new Test(), []);
        Assert.Equal(1, result);
    }

    [Fact]
    public void Execute_ThrowsWhenTooFewParameters()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam")!);

        var exception = Assert.Throws<ArgumentException>(() => executor.Execute(new Test(), []));
        Assert.Contains("SyncInt32WithParam", exception.Message);
    }

    [Fact]
    public void Execute_ThrowsWhenTooManyParameters()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam")!);

        Assert.Throws<ArgumentException>(() => executor.Execute(new Test(), [1, 2]));
    }

    [Fact]
    public void Execute_ThrowsWhenParametersIsNullAndMethodTakesParameters()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam")!);

        Assert.Throws<ArgumentException>(() => executor.Execute(new Test(), parameters: null));
    }

    [Fact]
    public void Execute_AllowsNullParametersForParameterlessMethod()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32")!);

        Assert.Equal(1, executor.Execute(new Test(), parameters: null));
    }

    [Fact]
    public void ExecuteAsync_ThrowsWhenParameterCountIsWrong()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTaskInt32WithParam")!);

        Assert.Throws<ArgumentException>(() => executor.ExecuteAsync(new Test(), []));
    }

    [Fact]
    public async Task ExecuteAsync_AllowsNullParametersForParameterlessMethod()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTaskInt32")!);

        Assert.Equal(1, await executor.ExecuteAsync(new Test(), parameters: null));
    }

    [Fact]
    public async Task AsyncTaskInt32WithParamTests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTaskInt32WithParam")!);
        var result = await executor.ExecuteAsync(new Test(), [12]);
        Assert.Equal(12, result);
    }

    [Fact]
    public async Task AsyncCustomAwaiter()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncCustomAwaiter")!);
        var result = await executor.ExecuteAsync(new Test(), []);
        Assert.Null(result);
    }

    [Fact]
    public async Task AwaiterImplementingOnlyINotifyCompletion()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("NotifyOnlyAwaitable")!);

        Assert.True(executor.IsMethodAsync);
        Assert.Equal(42, await executor.ExecuteAsync(new Test(), []));
    }

    private sealed class NotifyOnlyAwaitable
    {
        public NotifyOnlyAwaiter GetAwaiter() => new();

        internal sealed class NotifyOnlyAwaiter : INotifyCompletion
        {
            public bool IsCompleted => false;

            public int GetResult() => 42;

            public void OnCompleted(Action continuation) => ThreadPool.QueueUserWorkItem(_ => continuation());
        }
    }

    [Fact]
    public async Task AsyncValueTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncValueTask")!);
        var result = await executor.ExecuteAsync(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task SyncVoidCalledAsyncTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncVoid")!);
        var result = await executor.ExecuteAsync(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task FSharpAsync()
    {
        var executor = ObjectMethodExecutor.Create(typeof(FSharpTests.Say).GetMethod("get_int32")!);
        var result = await executor.ExecuteAsync(new FSharpTests.Say(), []);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task FSharpAsync_Unit()
    {
        var executor = ObjectMethodExecutor.Create(typeof(FSharpTests.Say).GetMethod("get_dummyUnit")!);
        var result = await executor.ExecuteAsync(new FSharpTests.Say(), []);
        Assert.Null(result);
    }

    private sealed class Validator
    {
        public bool HasBeenInvoked { get; private set; }

        public void Invoked() => HasBeenInvoked = true;
    }

#pragma warning disable CA1822 // Mark members as static
    private sealed class Test
    {
        public static void StaticSyncVoid(Validator validator)
        {
            validator.Invoked();
        }

        public void SyncVoid(Validator validator)
        {
            validator.Invoked();
        }

        public int SyncInt32() => 1;

        public int SyncInt32WithParam(int i) => i;

        public static async Task StaticAsyncTask(Validator validator)
        {
            await Task.Delay(1);
            validator.Invoked();
        }

        public async Task AsyncTask(Validator validator)
        {
            await Task.Delay(1);
            validator.Invoked();
        }

        public Task<int> AsyncTaskInt32() => Task.FromResult(1);

        public async Task<int> AsyncTaskInt32WithParam(int i)
        {
            await Task.Delay(1);
            return i;
        }

        public ValueTask<int> ValueTaskInt32() => ValueTask.FromResult(1);

        public NotifyOnlyAwaitable NotifyOnlyAwaitable() => new();

        public async ValueTask AsyncValueTask(Validator validator)
        {
            await Task.Delay(1);
            validator.Invoked();
        }

        public YieldAwaitable AsyncCustomAwaiter() => Task.Yield();

#pragma warning disable MA0155 // Do not use async void methods - this is the shape under test
        public async void AsyncVoid(Validator validator)
        {
            validator.Invoked();
            await Task.Delay(1);
        }
#pragma warning restore MA0155
    }
#pragma warning restore CA1822
}
