using System.Runtime.CompilerServices;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class ObjectMethodExecutorTests
{
    [Fact]
    public void StaticSyncVoidTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("StaticSyncVoid")!);
        var result = executor.Execute(target: null, new object[] { validator });

        result.Should().BeNull();
        validator.HasBeenInvoked.Should().BeTrue();
    }

    [Fact]
    public void SyncVoidTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncVoid")!);
        var result = executor.Execute(new Test(), new object[] { validator });

        result.Should().BeNull();
        validator.HasBeenInvoked.Should().BeTrue();
    }

    [Fact]
    public void SyncInt32Test()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32")!);
        var result = executor.Execute(new Test(), Array.Empty<object>());

        result.Should().Be(1);
    }

    [Fact]
    public void SyncInt32WithParamTest()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncInt32WithParam")!);
        var result = executor.Execute(new Test(), new object[] { 12 });

        result.Should().Be(12);
    }

    [Fact]
    public async Task StaticAsyncTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("StaticAsyncTask")!);
        var result = await executor.ExecuteAsync(null, new object[] { validator });

        result.Should().BeNull();
        validator.HasBeenInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task AsyncTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTask")!);
        var result = await executor.ExecuteAsync(new Test(), new object[] { validator });

        result.Should().BeNull();
        validator.HasBeenInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task AsyncTaskInt32Tests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTaskInt32")!);
        var result = await executor.ExecuteAsync(new Test(), Array.Empty<object>());

        result.Should().Be(1);
    }

    [Fact]
    public async Task ValueTaskInt32Tests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("ValueTaskInt32")!);
        var result = await executor.ExecuteAsync(new Test(), Array.Empty<object>());

        result.Should().Be(1);
    }

    [Fact]
    public async Task AsyncTaskInt32WithParamTests()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncTaskInt32WithParam")!);
        var result = await executor.ExecuteAsync(new Test(), new object[] { 12 });

        result.Should().Be(12);
    }

    [Fact]
    public async Task AsyncCustomAwaiter()
    {
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncCustomAwaiter")!);
        var result = await executor.ExecuteAsync(new Test(), Array.Empty<object>());

        result.Should().Be(null);
    }

    [Fact]
    public async Task AsyncValueTaskTests()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("AsyncValueTask")!);
        var result = await executor.ExecuteAsync(new Test(), new object[] { validator });

        result.Should().BeNull();
        validator.HasBeenInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task SyncVoidCalledAsyncTest()
    {
        var validator = new Validator();
        var executor = ObjectMethodExecutor.Create(typeof(Test).GetMethod("SyncVoid")!);
        var result = await executor.ExecuteAsync(new Test(), new object[] { validator });

        result.Should().BeNull();
        validator.HasBeenInvoked.Should().BeTrue();
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