using System.Diagnostics.CodeAnalysis;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

[SuppressMessage("Design", "MA0046:The second parameter must be of type 'System.EventArgs' or a derived type", Justification = "TEventArgs is constrained to EventArgs.")]
[SuppressMessage("Design", "MA0091:Sender parameter should be 'this' for instance events", Justification = "The tests intentionally verify the sender value captured by the assertion helpers.")]
public sealed class AssertRaiseTests
{
    [Fact]
    public void AssertRaiseGeneric_Success()
    {
        var source = new GenericEventSource<CustomEventArgs>();
        var sender = new object();
        var arguments = new CustomEventArgs("value");

        var result = AssertionsAssert.Raise<CustomEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(sender, arguments));

        AssertionsAssert.Same(sender, result.Sender);
        AssertionsAssert.Same(arguments, result.Arguments);
    }

    [Fact]
    public void AssertRaiseNonGeneric_Success()
    {
        var source = new NonGenericEventSource();
        var sender = new object();
        var arguments = EventArgs.Empty;

        var result = AssertionsAssert.Raise(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(sender, arguments));

        AssertionsAssert.Same(sender, result.Sender);
        AssertionsAssert.Same(arguments, result.Arguments);
    }

    [Fact]
    public void AssertRaise_FailsWhenNoEventIsRaised()
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
    public void AssertRaise_FailsWhenDerivedEventArgsIsRaised()
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
    public void AssertRaiseAnyGeneric_AllowsDerivedEventArgs()
    {
        var source = new GenericEventSource<BaseEventArgs>();
        var arguments = new DerivedEventArgs();

        var result = AssertionsAssert.RaiseAny<BaseEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(source, arguments));

        AssertionsAssert.Same(source, result.Sender);
        AssertionsAssert.Same(arguments, result.Arguments);
    }

    [Fact]
    public void AssertRaiseAnyNonGeneric_AllowsDerivedEventArgs()
    {
        var source = new NonGenericEventSource();
        var arguments = new CustomEventArgs("value");

        var result = AssertionsAssert.RaiseAny(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () => source.Raise(source, arguments));

        AssertionsAssert.Same(source, result.Sender);
        AssertionsAssert.Same(arguments, result.Arguments);
    }

    [Fact]
    public void AssertRaiseAny_FailsWhenNoEventIsRaised()
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
    public void AssertRaise_DetachesHandlerWhenActionThrows()
    {
        var source = new CountingEventSource();
        Action action = () => throw new InvalidOperationException("Failure");

        AssertionsAssert.Throws<InvalidOperationException>(() => AssertionsAssert.Raise(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            action));
        AssertionsAssert.Equal(1, source.AttachCount);
        AssertionsAssert.Equal(1, source.DetachCount);
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
