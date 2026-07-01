using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertThrowsTests
{
    [Fact]
    public void ThrowsGeneric_Success()
    {
        var exception = new InvalidOperationException("Failure");
        Action action = () => { throw exception; };

        var result = AssertionsAssert.Throws<InvalidOperationException>(action);

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public void ThrowsType_Success()
    {
        var exception = new InvalidOperationException("Failure");
        Action action = () => { throw exception; };

        var result = AssertionsAssert.Throws(typeof(InvalidOperationException), action);

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public void ThrowsGeneric_PropertyGetter_Success()
    {
        var exception = new InvalidOperationException("Failure");
        var source = new ThrowingPropertySource(exception);

        var result = AssertionsAssert.Throws<InvalidOperationException>(() => source.Value);

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public void Throws_FailsWhenNoExceptionIsThrown()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Throws<InvalidOperationException>(() => { }), """
            Assert.Throws() assertion failed.
            Expression: () => { }
            Expected exception type: System.InvalidOperationException
            Actual exception type:   <null>
            Exception: <none>
            """);
    }

    [Fact]
    public void Throws_FailsWhenDerivedExceptionIsThrown()
    {
        Action action = () => { throw new InvalidOperationException("Failure"); };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Throws<Exception>(action), """
            Assert.Throws() assertion failed.
            Expression: action
            Expected exception type: System.Exception
            Actual exception type:   System.InvalidOperationException
            Exception: Failure
            """);
    }

    [Fact]
    public void ThrowsAnyGeneric_Success()
    {
        var exception = new InvalidOperationException("Failure");
        Action action = () => { throw exception; };

        var result = AssertionsAssert.ThrowsAny<Exception>(action);

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public void ThrowsAnyType_Success()
    {
        var exception = new InvalidOperationException("Failure");
        Action action = () => { throw exception; };

        var result = AssertionsAssert.ThrowsAny(typeof(Exception), action);

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public void ThrowsAnyGeneric_PropertyGetter_Success()
    {
        var exception = new InvalidOperationException("Failure");
        var source = new ThrowingPropertySource(exception);

        var result = AssertionsAssert.ThrowsAny<Exception>(() => source.Value);

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public void ThrowsAny_FailsWhenNoExceptionIsThrown()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.ThrowsAny<InvalidOperationException>(() => { }), """
            Assert.ThrowsAny() assertion failed.
            Expression: () => { }
            Expected exception type: System.InvalidOperationException
            Actual exception type:   <null>
            Exception: <none>
            """);
    }

    [Fact]
    public void ThrowsAny_FailsWhenUnrelatedExceptionIsThrown()
    {
        Action action = () => { throw new InvalidOperationException("Failure"); };

        AssertionTestHelpers.Validate(() => AssertionsAssert.ThrowsAny<ArgumentException>(action), """
            Assert.ThrowsAny() assertion failed.
            Expression: action
            Expected exception type: System.ArgumentException
            Actual exception type:   System.InvalidOperationException
            Exception: Failure
            """);
    }

    [Fact]
    public async Task ThrowsAsyncGeneric_Success()
    {
        var exception = new InvalidOperationException("Failure");

        var result = await AssertionsAssert.ThrowsAsync<InvalidOperationException>(() => ThrowAsync(exception));

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public async Task ThrowsAsyncType_Success()
    {
        var exception = new InvalidOperationException("Failure");

        var result = await AssertionsAssert.ThrowsAsync(typeof(InvalidOperationException), () => ThrowAsync(exception));

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public async Task ThrowsAsyncGeneric_TaskOfObject_Success()
    {
        var exception = new InvalidOperationException("Failure");
        var source = new ThrowingPropertySource(exception);

        var result = await AssertionsAssert.ThrowsAsync<InvalidOperationException>(() => source.ValueAsync);

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public async Task ThrowsAsync_FailsWhenNoExceptionIsThrown()
    {
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.ThrowsAsync<InvalidOperationException>(() => Task.CompletedTask), """
            Assert.Throws() assertion failed.
            Expression: () => Task.CompletedTask
            Expected exception type: System.InvalidOperationException
            Actual exception type:   <null>
            Exception: <none>
            """);
    }

    [Fact]
    public async Task ThrowsAnyAsyncGeneric_Success()
    {
        var exception = new InvalidOperationException("Failure");

        var result = await AssertionsAssert.ThrowsAnyAsync<Exception>(() => ThrowAsync(exception));

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public async Task ThrowsAnyAsyncType_Success()
    {
        var exception = new InvalidOperationException("Failure");

        var result = await AssertionsAssert.ThrowsAnyAsync(typeof(Exception), () => ThrowAsync(exception));

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public async Task ThrowsAnyAsyncGeneric_TaskOfObject_Success()
    {
        var exception = new InvalidOperationException("Failure");
        var source = new ThrowingPropertySource(exception);

        var result = await AssertionsAssert.ThrowsAnyAsync<Exception>(() => source.ValueAsync);

        AssertionsAssert.Same(exception, result);
    }

    [Fact]
    public async Task ThrowsAnyAsync_FailsWhenUnrelatedExceptionIsThrown()
    {
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.ThrowsAnyAsync<ArgumentException>(() => ThrowAsync(new InvalidOperationException("Failure"))), """
            Assert.ThrowsAny() assertion failed.
            Expression: () => ThrowAsync(new InvalidOperationException("Failure"))
            Expected exception type: System.ArgumentException
            Actual exception type:   System.InvalidOperationException
            Exception: Failure
            """);
    }

    private static Task ThrowAsync(Exception exception)
    {
        return Task.FromException(exception);
    }

    private sealed class ThrowingPropertySource(Exception exception)
    {
        public object? Value => throw exception;
        public Task<object?> ValueAsync => Task.FromException<object?>(exception);
    }
}
