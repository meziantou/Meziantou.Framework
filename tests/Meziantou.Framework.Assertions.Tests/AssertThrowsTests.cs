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

    [Fact]
    public async Task Throws_ValueTask_ExceptionThrownAfterAwait_Success()
    {
        var exception = new InvalidOperationException("Failure");
        var service = new ValueTaskService(exception);

        AssertionsAssert.Same(exception, await AssertionsAssert.Throws<InvalidOperationException>(() => service.SaveAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.Throws(typeof(InvalidOperationException), () => service.SaveAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.ThrowsAny<Exception>(() => service.SaveAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.ThrowsAny(typeof(Exception), () => service.SaveAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.Throws<InvalidOperationException>(service.SaveAsync));
    }

    [Fact]
    public async Task Throws_ValueTaskOfObject_ExceptionThrownAfterAwait_Success()
    {
        var exception = new InvalidOperationException("Failure");
        var service = new ValueTaskService(exception);

        AssertionsAssert.Same(exception, await AssertionsAssert.Throws<InvalidOperationException>(() => service.LoadAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.Throws(typeof(InvalidOperationException), () => service.LoadAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.ThrowsAny<Exception>(() => service.LoadAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.ThrowsAny(typeof(Exception), () => service.LoadAsync()));
    }

    [Fact]
    public async Task Throws_ValueTask_SynchronousException_Success()
    {
        var exception = new InvalidOperationException("Failure");
        Func<ValueTask> action = () => throw exception;

        AssertionsAssert.Same(exception, await AssertionsAssert.Throws<InvalidOperationException>(action));
    }

    [Fact]
    public async Task Throws_ValueTask_FailsWhenNoExceptionIsThrown()
    {
        var service = new ValueTaskService(exception: null);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Throws<InvalidOperationException>(() => service.SaveAsync()), """
            Assert.Throws() assertion failed.
            Expression: () => service.SaveAsync()
            Expected exception type: System.InvalidOperationException
            Actual exception type:   <null>
            Exception: <none>
            """);
    }

    [Fact]
    public async Task ThrowsAny_ValueTask_FailsWhenUnrelatedExceptionIsThrown()
    {
        var exception = new InvalidOperationException("Failure");
        var service = new ValueTaskService(exception);

        var assertionException = await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.ThrowsAny<ArgumentException>(() => service.SaveAsync()));

        AssertionsAssert.Equal("""
            Assert.ThrowsAny() assertion failed.
            Expression: () => service.SaveAsync()
            Expected exception type: System.ArgumentException
            Actual exception type:   System.InvalidOperationException
            Exception: Failure
            """, assertionException.Message);
        AssertionsAssert.Same(exception, assertionException.InnerException);
    }

    [Fact]
    public void Throws_XunitSkipIsNotWrapped()
    {
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.ThrowsAny<InvalidOperationException>(() => AssertionsAssert.XunitSkip("n/a")));
    }

    [Fact]
    public void ThrowsAny_XunitSkipAssignableToTheExpectedTypeIsNotReturned()
    {
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.ThrowsAny<Exception>(() => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.ThrowsAny(typeof(Exception), () => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.Throws<Exception>(() => AssertionsAssert.XunitSkip("n/a")));
    }

    [Fact]
    public async Task ThrowsAnyAsync_XunitSkipAssignableToTheExpectedTypeIsNotReturned()
    {
        await AssertionTestHelpers.ValidateXunitSkipAsync(() => AssertionsAssert.ThrowsAny<Exception>(async () =>
        {
            await Task.Yield();
            AssertionsAssert.XunitSkip("n/a");
        }));
    }

    [Fact]
    public void Throws_XunitSkipIsReturnedWhenItsExactTypeIsExpected()
    {
        var exception = AssertionsAssert.ThrowsAny<AssertionException>(() => AssertionsAssert.XunitSkip("n/a"));

        AssertionsAssert.Equal("$XunitDynamicSkip$n/a", exception.Message);
    }

    [Fact]
    public async Task ThrowsAsync_XunitSkipIsNotWrapped()
    {
        await AssertionTestHelpers.ValidateXunitSkipAsync(() => AssertionsAssert.Throws<InvalidOperationException>(async () =>
        {
            await Task.Yield();
            AssertionsAssert.XunitSkip("n/a");
        }));
    }

    [Fact]
    public async Task ThrowsParamName_Success()
    {
        var exception = CreateArgumentNullException("value");
        var service = new ValueTaskService(exception);
        Action action = () => { throw exception; };

        AssertionsAssert.Same(exception, AssertionsAssert.Throws<ArgumentNullException>("value", action));
        AssertionsAssert.Same(exception, AssertionsAssert.Throws<ArgumentNullException>("value", () => new ThrowingPropertySource(exception).Value));
        AssertionsAssert.Same(exception, await AssertionsAssert.Throws<ArgumentNullException>("value", () => ThrowAsync(exception)));
        AssertionsAssert.Same(exception, await AssertionsAssert.Throws<ArgumentNullException>("value", () => new ThrowingPropertySource(exception).ValueAsync));
        AssertionsAssert.Same(exception, await AssertionsAssert.Throws<ArgumentNullException>("value", () => service.SaveAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.Throws<ArgumentNullException>("value", () => service.LoadAsync()));
        AssertionsAssert.Same(exception, await AssertionsAssert.ThrowsAsync<ArgumentNullException>("value", () => ThrowAsync(exception)));
    }

    [Fact]
    public void ThrowsParamName_NullParamName_Success()
    {
        var exception = CreateArgumentException("Failure", paramName: null);
        Action action = () => { throw exception; };

        AssertionsAssert.Same(exception, AssertionsAssert.Throws<ArgumentException>(null, action));
    }

    [Fact]
    public async Task ThrowsParamName_FailsWhenTheParameterNameDiffers()
    {
        var exception = CreateArgumentNullException("other");
        Action action = () => { throw exception; };

        var assertionException = AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Throws<ArgumentNullException>("value", action));
        AssertionsAssert.Equal("""
            Assert.Throws() assertion failed.
            Expression: action
            Exception type: System.ArgumentNullException
            Expected parameter name: "value"
            Actual parameter name:   "other"
            Exception: Value cannot be null. (Parameter 'other')
            """, assertionException.Message);
        AssertionsAssert.Same(exception, assertionException.InnerException);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Throws<ArgumentException>("value", () => ThrowAsync(CreateArgumentException("Failure", paramName: null))), """
            Assert.Throws() assertion failed.
            Expression: () => ThrowAsync(CreateArgumentException("Failure", paramName: null))
            Exception type: System.ArgumentException
            Expected parameter name: "value"
            Actual parameter name:   <null>
            Exception: Failure
            """);
    }

    [Fact]
    public void ThrowsParamName_FailsWhenDerivedExceptionIsThrown()
    {
        Action action = () => { throw CreateArgumentNullException("value"); };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Throws<ArgumentException>("value", action), """
            Assert.Throws() assertion failed.
            Expression: action
            Expected exception type: System.ArgumentException
            Actual exception type:   System.ArgumentNullException
            Exception: Value cannot be null. (Parameter 'value')
            """);
    }

    [Fact]
    public void DoesNotThrow_Function_Success()
    {
        var source = new ValueSource(42);

        AssertionsAssert.DoesNotThrow(() => source.Value);
        AssertionsAssert.DoesNotThrow<InvalidOperationException>(() => source.Value);
        AssertionsAssert.DoesNotThrow(typeof(InvalidOperationException), () => source.Value);
        AssertionsAssert.DoesNotThrowAny<InvalidOperationException>(() => source.Value);
        AssertionsAssert.DoesNotThrowAny(typeof(InvalidOperationException), () => source.Value);
    }

    [Fact]
    public void DoesNotThrow_Function_FailsAndKeepsTheOriginalException()
    {
        var exception = new InvalidOperationException("Failure");
        var source = new ThrowingPropertySource(exception);

        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrow(() => source.Value), """
            Assert.DoesNotThrow() assertion failed.
            Expression: () => source.Value
            Not expected: exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrow<InvalidOperationException>(() => source.Value), """
            Assert.DoesNotThrow() assertion failed.
            Expression: () => source.Value
            Not expected: exception of type System.InvalidOperationException
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrow(typeof(InvalidOperationException), () => source.Value), """
            Assert.DoesNotThrow() assertion failed.
            Expression: () => source.Value
            Not expected: exception of type System.InvalidOperationException
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrowAny<Exception>(() => source.Value), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: () => source.Value
            Not expected: exception assignable to System.Exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrowAny(typeof(Exception), () => source.Value), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: () => source.Value
            Not expected: exception assignable to System.Exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        AssertionsAssert.Same(exception, AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.DoesNotThrow<ArgumentException>(() => source.Value)));
    }

    [Fact]
    public void DoesNotThrow_Success()
    {
        AssertionsAssert.DoesNotThrow(() => { });
        AssertionsAssert.DoesNotThrow<InvalidOperationException>(() => { });
        AssertionsAssert.DoesNotThrowAny<InvalidOperationException>(() => { });
    }

    [Fact]
    public void DoesNotThrow_FailsAndKeepsTheOriginalException()
    {
        var exception = new InvalidOperationException("Failure");
        Action action = () => { throw exception; };

        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrow(action), """
            Assert.DoesNotThrow() assertion failed.
            Expression: action
            Not expected: exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrow<InvalidOperationException>(action), """
            Assert.DoesNotThrow() assertion failed.
            Expression: action
            Not expected: exception of type System.InvalidOperationException
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrowAny<Exception>(action), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: action
            Not expected: exception assignable to System.Exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
    }

    [Fact]
    public async Task DoesNotThrow_Task_FailsAndKeepsTheOriginalException()
    {
        var exception = new InvalidOperationException("Failure");

        await ValidateDoesNotThrowAsync(exception, () => AssertionsAssert.DoesNotThrow(() => ThrowAsync(exception)), """
            Assert.DoesNotThrow() assertion failed.
            Expression: () => ThrowAsync(exception)
            Not expected: exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        await ValidateDoesNotThrowAsync(exception, () => AssertionsAssert.DoesNotThrow(typeof(InvalidOperationException), () => ThrowAsync(exception)), """
            Assert.DoesNotThrow() assertion failed.
            Expression: () => ThrowAsync(exception)
            Not expected: exception of type System.InvalidOperationException
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        await ValidateDoesNotThrowAsync(exception, () => AssertionsAssert.DoesNotThrowAny(typeof(Exception), () => ThrowAsync(exception)), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: () => ThrowAsync(exception)
            Not expected: exception assignable to System.Exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
    }

    [Fact]
    public async Task DoesNotThrow_ValueTask_Success()
    {
        var service = new ValueTaskService(exception: null);

        await AssertionsAssert.DoesNotThrow(() => service.SaveAsync());
        await AssertionsAssert.DoesNotThrow(service.SaveAsync);
        await AssertionsAssert.DoesNotThrow<InvalidOperationException>(() => service.SaveAsync());
        await AssertionsAssert.DoesNotThrowAny(typeof(Exception), () => service.SaveAsync());
    }

    [Fact]
    public async Task DoesNotThrow_ValueTask_FailsWhenExceptionIsThrownAfterAwait()
    {
        var exception = new InvalidOperationException("Failure");
        var service = new ValueTaskService(exception);

        await ValidateDoesNotThrowAsync(exception, () => AssertionsAssert.DoesNotThrow(() => service.SaveAsync()), """
            Assert.DoesNotThrow() assertion failed.
            Expression: () => service.SaveAsync()
            Not expected: exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        await ValidateDoesNotThrowAsync(exception, () => AssertionsAssert.DoesNotThrow<InvalidOperationException>(() => service.SaveAsync()), """
            Assert.DoesNotThrow() assertion failed.
            Expression: () => service.SaveAsync()
            Not expected: exception of type System.InvalidOperationException
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        await ValidateDoesNotThrowAsync(exception, () => AssertionsAssert.DoesNotThrow(typeof(InvalidOperationException), () => service.SaveAsync()), """
            Assert.DoesNotThrow() assertion failed.
            Expression: () => service.SaveAsync()
            Not expected: exception of type System.InvalidOperationException
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        await ValidateDoesNotThrowAsync(exception, () => AssertionsAssert.DoesNotThrowAny<Exception>(() => service.SaveAsync()), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: () => service.SaveAsync()
            Not expected: exception assignable to System.Exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
        await ValidateDoesNotThrowAsync(exception, () => AssertionsAssert.DoesNotThrowAny(typeof(Exception), () => service.SaveAsync()), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: () => service.SaveAsync()
            Not expected: exception assignable to System.Exception
            Exception: System.InvalidOperationException
            Exception message: Failure
            """);
    }

    [Fact]
    public void DoesNotThrow_ExactType_OtherExceptionsPropagateUnchanged()
    {
        var derivedException = CreateArgumentNullException("value");
        var unrelatedException = new InvalidOperationException("Failure");
        Action derivedAction = () => { throw derivedException; };
        Action unrelatedAction = () => { throw unrelatedException; };

        AssertionsAssert.Same(derivedException, AssertionsAssert.Throws<ArgumentNullException>(() => AssertionsAssert.DoesNotThrow<ArgumentException>(derivedAction)));
        AssertionsAssert.Same(derivedException, AssertionsAssert.Throws<ArgumentNullException>(() => AssertionsAssert.DoesNotThrow(typeof(ArgumentException), derivedAction)));
        AssertionsAssert.Same(unrelatedException, AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.DoesNotThrow<ArgumentException>(unrelatedAction)));
        AssertionsAssert.Same(unrelatedException, AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.DoesNotThrowAny<ArgumentException>(unrelatedAction)));
        AssertionsAssert.Same(unrelatedException, AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.DoesNotThrowAny(typeof(ArgumentException), unrelatedAction)));
    }

    [Fact]
    public void DoesNotThrowAny_DerivedExceptionFails()
    {
        var exception = CreateArgumentNullException("value");
        Action action = () => { throw exception; };

        ValidateDoesNotThrow(exception, () => AssertionsAssert.DoesNotThrowAny<ArgumentException>(action), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: action
            Not expected: exception assignable to System.ArgumentException
            Exception: System.ArgumentNullException
            Exception message: Value cannot be null. (Parameter 'value')
            """);
    }

    [Fact]
    public async Task DoesNotThrow_Task_ExactType_OtherExceptionsPropagateUnchanged()
    {
        var derivedException = CreateArgumentNullException("value");
        var unrelatedException = new InvalidOperationException("Failure");
        Func<Task> derivedAction = () => ThrowAsync(derivedException);
        Func<Task> unrelatedAction = () => ThrowAsync(unrelatedException);

        AssertionsAssert.Same(derivedException, await AssertionsAssert.Throws<ArgumentNullException>(() => AssertionsAssert.DoesNotThrow<ArgumentException>(derivedAction)));
        AssertionsAssert.Same(derivedException, await AssertionsAssert.Throws<ArgumentNullException>(() => AssertionsAssert.DoesNotThrow(typeof(ArgumentException), derivedAction)));
        AssertionsAssert.Same(unrelatedException, await AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.DoesNotThrow<ArgumentException>(unrelatedAction)));
        AssertionsAssert.Same(unrelatedException, await AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.DoesNotThrowAny<ArgumentException>(unrelatedAction)));
        await ValidateDoesNotThrowAsync(derivedException, () => AssertionsAssert.DoesNotThrowAny<ArgumentException>(derivedAction), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: derivedAction
            Not expected: exception assignable to System.ArgumentException
            Exception: System.ArgumentNullException
            Exception message: Value cannot be null. (Parameter 'value')
            """);
    }

    [Fact]
    public async Task DoesNotThrow_ValueTask_ExactType_OtherExceptionsPropagateUnchanged()
    {
        var derivedException = CreateArgumentNullException("value");
        var unrelatedException = new InvalidOperationException("Failure");
        var derivedService = new ValueTaskService(derivedException);
        var unrelatedService = new ValueTaskService(unrelatedException);
        Func<ValueTask> derivedAction = derivedService.SaveAsync;
        Func<ValueTask> unrelatedAction = unrelatedService.SaveAsync;

        AssertionsAssert.Same(derivedException, await AssertionsAssert.Throws<ArgumentNullException>(() => AssertionsAssert.DoesNotThrow<ArgumentException>(derivedAction)));
        AssertionsAssert.Same(derivedException, await AssertionsAssert.Throws<ArgumentNullException>(() => AssertionsAssert.DoesNotThrow(typeof(ArgumentException), derivedAction)));
        AssertionsAssert.Same(unrelatedException, await AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.DoesNotThrow<ArgumentException>(unrelatedAction)));
        AssertionsAssert.Same(unrelatedException, await AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.DoesNotThrowAny<ArgumentException>(unrelatedAction)));
        await ValidateDoesNotThrowAsync(derivedException, () => AssertionsAssert.DoesNotThrowAny<ArgumentException>(derivedAction), """
            Assert.DoesNotThrowAny() assertion failed.
            Expression: derivedAction
            Not expected: exception assignable to System.ArgumentException
            Exception: System.ArgumentNullException
            Exception message: Value cannot be null. (Parameter 'value')
            """);
    }

    [Fact]
    public async Task DoesNotThrow_XunitSkipIsNotWrapped()
    {
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.DoesNotThrow(() => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.DoesNotThrow<AssertionException>(() => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.DoesNotThrowAny<Exception>(() => AssertionsAssert.XunitSkip("n/a")));
        await AssertionTestHelpers.ValidateXunitSkipAsync(() => AssertionsAssert.DoesNotThrow(async () =>
        {
            await Task.Yield();
            AssertionsAssert.XunitSkip("n/a");
        }));
        await AssertionTestHelpers.ValidateXunitSkipAsync(() => AssertionsAssert.DoesNotThrowAny<Exception>(async () =>
        {
            await Task.Yield();
            AssertionsAssert.XunitSkip("n/a");
        }));
    }

    private static void ValidateDoesNotThrow(Exception expectedInnerException, Action action, string expectedMessage)
    {
        var exception = AssertionsAssert.Throws<AssertionException>(action);
        AssertionsAssert.Equal(expectedMessage, exception.Message);
        AssertionsAssert.Same(expectedInnerException, exception.InnerException);
    }

    private static async Task ValidateDoesNotThrowAsync(Exception expectedInnerException, Func<Task> action, string expectedMessage)
    {
        var exception = await AssertionsAssert.Throws<AssertionException>(action);
        AssertionsAssert.Equal(expectedMessage, exception.Message);
        AssertionsAssert.Same(expectedInnerException, exception.InnerException);
    }

    private static Task ThrowAsync(Exception exception)
    {
        return Task.FromException(exception);
    }

    private static ArgumentNullException CreateArgumentNullException(string paramName)
    {
        return new ArgumentNullException(paramName);
    }

    private static ArgumentException CreateArgumentException(string message, string? paramName)
    {
        return new ArgumentException(message, paramName);
    }

    private sealed class ValueSource(object? value)
    {
        public object? Value => value;
    }

    private sealed class ThrowingPropertySource(Exception exception)
    {
        public object? Value => throw exception;
        public Task<object?> ValueAsync => Task.FromException<object?>(exception);
    }

    // The exception is thrown after an await, so it is only observed when the returned ValueTask is awaited
    private sealed class ValueTaskService(Exception? exception)
    {
        public async ValueTask SaveAsync()
        {
            await Task.Yield();
            if (exception is not null)
                throw exception;
        }

        public async ValueTask<object?> LoadAsync()
        {
            await Task.Yield();
            if (exception is not null)
                throw exception;

            return null;
        }
    }
}
