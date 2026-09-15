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
            Expression: () => { }
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
            Expression: () => source.Raise(source, arguments)
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
            Expression: () => { }
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

    [Fact]
    public void AssertRaise_EventRaisedWithNullArguments_Success()
    {
        var genericSource = new GenericEventSource<CustomEventArgs>();
        var nonGenericSource = new NonGenericEventSource();

        var genericResult = AssertionsAssert.Raise<CustomEventArgs>(
            handler => genericSource.Raised += handler,
            handler => genericSource.Raised -= handler,
            () => genericSource.Raise(genericSource, null!));
        var nonGenericResult = AssertionsAssert.Raise(
            handler => nonGenericSource.Raised += handler,
            handler => nonGenericSource.Raised -= handler,
            () => nonGenericSource.Raise(nonGenericSource, null!));
        var genericAnyResult = AssertionsAssert.RaiseAny<CustomEventArgs>(
            handler => genericSource.Raised += handler,
            handler => genericSource.Raised -= handler,
            () => genericSource.Raise(genericSource, null!));
        var nonGenericAnyResult = AssertionsAssert.RaiseAny(
            handler => nonGenericSource.Raised += handler,
            handler => nonGenericSource.Raised -= handler,
            () => nonGenericSource.Raise(nonGenericSource, null!));

        AssertionsAssert.Same(genericSource, genericResult.Sender);
        AssertionsAssert.Null(genericResult.Arguments);
        AssertionsAssert.Same(nonGenericSource, nonGenericResult.Sender);
        AssertionsAssert.Null(nonGenericResult.Arguments);
        AssertionsAssert.Same(genericSource, genericAnyResult.Sender);
        AssertionsAssert.Null(genericAnyResult.Arguments);
        AssertionsAssert.Same(nonGenericSource, nonGenericAnyResult.Sender);
        AssertionsAssert.Null(nonGenericAnyResult.Arguments);
    }

    [Fact]
    public void AssertRaise_ReturnsTheFirstMatchingEvent()
    {
        var source = new GenericEventSource<BaseEventArgs>();
        var firstSender = new object();
        var secondSender = new object();
        var thirdSender = new object();
        var derivedArguments = new DerivedEventArgs();
        var firstBaseArguments = new BaseEventArgs();
        var secondBaseArguments = new BaseEventArgs();

        var result = AssertionsAssert.Raise<BaseEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () =>
            {
                source.Raise(firstSender, derivedArguments);
                source.Raise(secondSender, firstBaseArguments);
                source.Raise(thirdSender, secondBaseArguments);
            });

        AssertionsAssert.Same(secondSender, result.Sender);
        AssertionsAssert.Same(firstBaseArguments, result.Arguments);
    }

    [Fact]
    public void AssertRaise_ReturnsTheSenderOfTheMatchingEvent()
    {
        var source = new NonGenericEventSource();
        var firstArguments = new CustomEventArgs("first");
        var secondSender = new object();

        var result = AssertionsAssert.RaiseAny(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () =>
            {
                source.Raise(sender: null, firstArguments);
                source.Raise(secondSender, new CustomEventArgs("second"));
            });

        AssertionsAssert.Null(result.Sender);
        AssertionsAssert.Same(firstArguments, result.Arguments);
    }

    [Fact]
    public void AssertRaise_FailsWhenNoRaisedEventMatches()
    {
        var source = new NonGenericEventSource();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Raise(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () =>
            {
                source.Raise(source, new CustomEventArgs("first"));
                source.Raise(source, new DerivedEventArgs());
            }), """
            Assert.Raise() assertion failed.
            Expression: () =>
                        {
                            source.Raise(source, new CustomEventArgs("first"));
                            source.Raise(source, new DerivedEventArgs());
                        }
            Expected event args type: System.EventArgs
            Actual event args type:   Meziantou.Framework.Assertions.Tests.AssertRaiseTests+CustomEventArgs
            """);
    }

    [Fact]
    public void DoesNotRaise_FailsWhenEventIsRaisedWithNullArguments()
    {
        var genericSource = new GenericEventSource<CustomEventArgs>();
        var nonGenericSource = new NonGenericEventSource();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotRaise<CustomEventArgs>(
            handler => genericSource.Raised += handler,
            handler => genericSource.Raised -= handler,
            () => genericSource.Raise(genericSource, null!)), """
            Assert.DoesNotRaise() assertion failed.
            Not expected: event with exact Meziantou.Framework.Assertions.Tests.AssertRaiseTests+CustomEventArgs
            Actual:       () => genericSource.Raise(genericSource, null!)
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotRaise(
            handler => nonGenericSource.Raised += handler,
            handler => nonGenericSource.Raised -= handler,
            () => nonGenericSource.Raise(nonGenericSource, null!)), """
            Assert.DoesNotRaise() assertion failed.
            Not expected: event with exact EventArgs
            Actual:       () => nonGenericSource.Raise(nonGenericSource, null!)
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotRaiseAny<CustomEventArgs>(
            handler => genericSource.Raised += handler,
            handler => genericSource.Raised -= handler,
            () => genericSource.Raise(genericSource, null!)), """
            Assert.DoesNotRaiseAny() assertion failed.
            Not expected: event assignable to Meziantou.Framework.Assertions.Tests.AssertRaiseTests+CustomEventArgs
            Actual:       () => genericSource.Raise(genericSource, null!)
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotRaiseAny(
            handler => nonGenericSource.Raised += handler,
            handler => nonGenericSource.Raised -= handler,
            () => nonGenericSource.Raise(nonGenericSource, null!)), """
            Assert.DoesNotRaiseAny() assertion failed.
            Not expected: event assignable to EventArgs
            Actual:       () => nonGenericSource.Raise(nonGenericSource, null!)
            """);
    }

    [Fact]
    public void DoesNotRaise_FailsWhenALaterEventMatches()
    {
        var source = new GenericEventSource<BaseEventArgs>();

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotRaise<BaseEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () =>
            {
                source.Raise(source, new DerivedEventArgs());
                source.Raise(source, new BaseEventArgs());
            }), """
            Assert.DoesNotRaise() assertion failed.
            Not expected: event with exact Meziantou.Framework.Assertions.Tests.AssertRaiseTests+BaseEventArgs
            Actual:       () =>
                        {
                            source.Raise(source, new DerivedEventArgs());
                            source.Raise(source, new BaseEventArgs());
                        }
            """);
    }

    [Fact]
    public void DoesNotRaise_SucceedsWhenNoRaisedEventMatches()
    {
        var source = new GenericEventSource<BaseEventArgs>();

        AssertionsAssert.DoesNotRaise<BaseEventArgs>(
            handler => source.Raised += handler,
            handler => source.Raised -= handler,
            () =>
            {
                source.Raise(source, new DerivedEventArgs());
                source.Raise(source, new DerivedEventArgs());
            });
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
