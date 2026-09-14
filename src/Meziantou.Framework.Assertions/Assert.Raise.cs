using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<EventArgs> Raise(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: false, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<TEventArgs> Raise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: false, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<EventArgs> RaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: true, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<TEventArgs> RaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: true, message, actionExpression);
    }

    // Every raised event is recorded, so that the assertion is decided by all the events and not only by the first one.
    // The arguments are nullable because nothing prevents an event from being raised with null arguments.
    private static List<(object? Sender, EventArgs? Arguments)> RecordEvents(Action<EventHandler> attach, Action<EventHandler> detach, Action action)
    {
        var events = new List<(object? Sender, EventArgs? Arguments)>();
        void Handler(object? sender, EventArgs? arguments)
        {
            events.Add((sender, arguments));
        }

        attach(Handler);
        try
        {
            action();
        }
        finally
        {
            detach(Handler);
        }

        return events;
    }

    private static List<(object? Sender, TEventArgs? Arguments)> RecordEvents<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action)
        where TEventArgs : EventArgs
    {
        var events = new List<(object? Sender, TEventArgs? Arguments)>();
        void Handler(object? sender, TEventArgs? arguments)
        {
            events.Add((sender, arguments));
        }

        attach(Handler);
        try
        {
            action();
        }
        finally
        {
            detach(Handler);
        }

        return events;
    }

    private static RaisedEvent<TEventArgs> RaiseCore<TEventArgs>(List<(object? Sender, TEventArgs? Arguments)> events, bool allowDerivedTypes, string? message, string? actionExpression)
        where TEventArgs : EventArgs
    {
        foreach (var (sender, arguments) in events)
        {
            if (IsExpectedEventArguments(typeof(TEventArgs), arguments, allowDerivedTypes))
            {
                // Null arguments are only possible when the event is raised in violation of the nullable annotations of the handler
                return new RaisedEvent<TEventArgs>(sender, arguments!);
            }
        }

        // No event matches, so every raised event has non-null arguments of an unexpected type
        throw CreateRaiseException(typeof(TEventArgs), events.Count > 0 ? events[0].Arguments!.GetType() : null, allowDerivedTypes, message, actionExpression);
    }

    // An event raised with null arguments matches any expected type: the arguments have no runtime type to compare, and
    // null is a valid value for the declared event args type (this is also the behavior of xunit).
    private static bool IsExpectedEventArguments(Type expectedEventArgsType, EventArgs? arguments, bool allowDerivedTypes)
    {
        if (arguments is null)
            return true;

        var actualEventArgsType = arguments.GetType();
        if (allowDerivedTypes)
            return expectedEventArgsType.IsAssignableFrom(actualEventArgsType);

        return actualEventArgsType == expectedEventArgsType;
    }

    private static AssertionException CreateRaiseException(Type expectedEventArgsType, Type? actualEventArgsType, bool allowDerivedTypes, string? message, string? actionExpression)
    {
        return new AssertionException(ErrorFormatter.Format(new RaiseAssertionError(expectedEventArgsType, actualEventArgsType, allowDerivedTypes, actionExpression, message)));
    }
}
