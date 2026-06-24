using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertRaiseTests
{
    [Fact]
    public void RaiseGeneric_Success()
    {
        var source = new GenericEventSource<CustomEventArgs>();
        var sender = new object();
        var arguments = new CustomEventArgs("value");

        var result = AssertionsAssert.Raise<CustomEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(sender, arguments));

        global::Xunit.Assert.Same(sender, result.Sender);
        global::Xunit.Assert.Same(arguments, result.Arguments);
    }

    [Fact]
    public void RaiseNonGeneric_Success()
    {
        var source = new NonGenericEventSource();
        var sender = new object();
        var arguments = EventArgs.Empty;

        var result = AssertionsAssert.Raise(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(sender, arguments));

        global::Xunit.Assert.Same(sender, result.Sender);
        global::Xunit.Assert.Same(arguments, result.Arguments);
    }

    [Fact]
    public void Raise_FailsWhenNoEventIsRaised()
    {
        var source = new GenericEventSource<CustomEventArgs>();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Raise<CustomEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => { }), """
            Assert.Raise() assertion failed.
            Expression:               () => { }
            Expected event args type: Meziantou.Framework.Assertions.Tests.AssertRaiseTests+CustomEventArgs
            Actual event args type:   <null>
            """);
    }

    [Fact]
    public void Raise_FailsWhenDerivedEventArgsIsRaised()
    {
        var source = new GenericEventSource<BaseEventArgs>();
        var arguments = new DerivedEventArgs();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Raise<BaseEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(source, arguments)), """
            Assert.Raise() assertion failed.
            Expression:               () => source.Raise(source, arguments)
            Expected event args type: Meziantou.Framework.Assertions.Tests.AssertRaiseTests+BaseEventArgs
            Actual event args type:   Meziantou.Framework.Assertions.Tests.AssertRaiseTests+DerivedEventArgs
            """);
    }

    [Fact]
    public void RaiseAnyGeneric_AllowsDerivedEventArgs()
    {
        var source = new GenericEventSource<BaseEventArgs>();
        var arguments = new DerivedEventArgs();

        var result = AssertionsAssert.RaiseAny<BaseEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(source, arguments));

        global::Xunit.Assert.Same(source, result.Sender);
        global::Xunit.Assert.Same(arguments, result.Arguments);
    }

    [Fact]
    public void RaiseAnyNonGeneric_AllowsDerivedEventArgs()
    {
        var source = new NonGenericEventSource();
        var arguments = new CustomEventArgs("value");

        var result = AssertionsAssert.RaiseAny(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(source, arguments));

        global::Xunit.Assert.Same(source, result.Sender);
        global::Xunit.Assert.Same(arguments, result.Arguments);
    }

    [Fact]
    public void RaiseAny_FailsWhenNoEventIsRaised()
    {
        var source = new GenericEventSource<CustomEventArgs>();

        AssertionTestHelpers.Validate(() => AssertionsAssert.RaiseAny<CustomEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => { }), """
            Assert.RaiseAny() assertion failed.
            Expression:               () => { }
            Expected event args type: Meziantou.Framework.Assertions.Tests.AssertRaiseTests+CustomEventArgs
            Actual event args type:   <null>
            """);
    }

    [Fact]
    public void Raise_DetachesHandlerWhenActionThrows()
    {
        var source = new CountingEventSource();
        Action action = () => throw new InvalidOperationException("Failure");

        global::Xunit.Assert.Throws<InvalidOperationException>(() => AssertionsAssert.Raise(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            action));
        global::Xunit.Assert.Equal(1, source.AttachCount);
        global::Xunit.Assert.Equal(1, source.DetachCount);
    }

    [Fact]
    public void DoesNotRaise_Success()
    {
        var source = new GenericEventSource<CustomEventArgs>();

        AssertionsAssert.DoesNotRaise<CustomEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => { });
    }

    [Fact]
    public void DoesNotRaise_Fails()
    {
        var source = new GenericEventSource<CustomEventArgs>();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotRaise<CustomEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(source, new CustomEventArgs("value"))), """
            Assert.DoesNotRaise() assertion failed.
            Not expected: event with exact Meziantou.Framework.Assertions.Tests.AssertRaiseTests+CustomEventArgs
            Actual:       () => source.Raise(source, new CustomEventArgs("value"))
            """);
    }

    [Fact]
    public void DoesNotRaiseAny_Fails()
    {
        var source = new GenericEventSource<BaseEventArgs>();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotRaiseAny<BaseEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(source, new DerivedEventArgs())), """
            Assert.DoesNotRaiseAny() assertion failed.
            Not expected: event assignable to Meziantou.Framework.Assertions.Tests.AssertRaiseTests+BaseEventArgs
            Actual:       () => source.Raise(source, new DerivedEventArgs())
            """);
    }

    private sealed class GenericEventSource<TEventArgs>
        where TEventArgs : EventArgs
    {
        public event EventHandler<TEventArgs>? Raised;

        public void Raise(object? sender, TEventArgs arguments)
        {
            Raised?.Invoke(sender, arguments);
        }
    }

    private sealed class NonGenericEventSource
    {
        public event EventHandler? Raised;

        public void Raise(object? sender, EventArgs arguments)
        {
            Raised?.Invoke(sender, arguments);
        }
    }

    private sealed class CountingEventSource
    {
        public int AttachCount { get; private set; }
        public int DetachCount { get; private set; }

        public event EventHandler? Raised
        {
            add => AttachCount++;
            remove => DetachCount++;
        }
    }

    private class BaseEventArgs : EventArgs;

    private sealed class DerivedEventArgs : BaseEventArgs;

    private sealed class CustomEventArgs(string value) : EventArgs
    {
        public string Value { get; } = value;
    }
}
