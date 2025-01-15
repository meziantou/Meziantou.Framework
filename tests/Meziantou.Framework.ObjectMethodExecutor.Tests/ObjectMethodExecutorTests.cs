using System.Runtime.CompilerServices;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class ObjectMethodExecutorTests
{
    [Fact]
    public void StaticSyncVoidTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("StaticSyncVoid"));
        var result = executor.Execute(target: null, [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public void SyncVoidTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncVoid"));
        var result = executor.Execute(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public void SyncInt32Test()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32"));
        var result = executor.Execute(new Test(), []);
        Assert.Equal(1, result);
    }

    [Fact]
    public void SyncInt32WithParamTest()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam"));
        var result = executor.Execute(new Test(), [12]);
        Assert.Equal(12, result);
    }

    [Fact]
    public async Task StaticAsyncTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("StaticAsyncTask"));
        var result = await executor.ExecuteAsync(null, [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task AsyncTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTask"));
        var result = await executor.ExecuteAsync(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task AsyncTaskInt32Tests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTaskInt32"));
        var result = await executor.ExecuteAsync(new Test(), []);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ValueTaskInt32Tests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("ValueTaskInt32"));
        var result = await executor.ExecuteAsync(new Test(), []);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task AsyncTaskInt32WithParamTests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTaskInt32WithParam"));
        var result = await executor.ExecuteAsync(new Test(), [12]);
        Assert.Equal(12, result);
    }

    [Fact]
    public async Task AsyncCustomAwaiter()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncCustomAwaiter"));
        var result = await executor.ExecuteAsync(new Test(), []);
        Assert.Null(result);
    }

    [Fact]
    public async Task AsyncValueTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncValueTask"));
        var result = await executor.ExecuteAsync(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task SyncVoidCalledAsyncTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncVoid"));
        var result = await executor.ExecuteAsync(new Test(), [validator]);

        Assert.Null(result);
        Assert.True(validator.HasBeenInvoked);
    }

    [Fact]
    public async Task FSharpAsync()
    {
        var executor = ObjectMethodExecutor.Create(typeof(FSharpTests.Say).GetMethod("get_int32"));
        var result = await executor.ExecuteAsync(new FSharpTests.Say(), []);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task FSharpAsync_Unit()
    {
        var executor = ObjectMethodExecutor.Create(typeof(FSharpTests.Say).GetMethod("get_dummyUnit"));
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

        public async ValueTask AsyncValueTask(Validator validator)
        {
            await Task.Delay(1);
            validator.Invoked();
        }

        public YieldAwaitable AsyncCustomAwaiter() => Task.Yield();
    }
#pragma warning restore CA1822
}