using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<EventArgs> Raise(Action<EventHandler> attach, Action<EventHandler> detach, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(attach, detach, action, allowDerivedTypes: false, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<TEventArgs> Raise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(attach, detach, action, allowDerivedTypes: false, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<EventArgs> RaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(attach, detach, action, allowDerivedTypes: true, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<TEventArgs> RaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(attach, detach, action, allowDerivedTypes: true, actionExpression);
    }

    private static RaisedEvent<EventArgs> RaiseCore(Action<EventHandler> attach, Action<EventHandler> detach, Action action, bool allowDerivedTypes, string? actionExpression)
    {
        EventArgs? arguments = null;
        object? sender = null;
        void Handler(object? eventSender, EventArgs eventArguments)
        {
            sender ??= eventSender;
            arguments ??= eventArguments;
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

        if (arguments is not null)
        {
            var actualEventArgsType = arguments.GetType();
            if (IsExpectedEventArgsType(typeof(EventArgs), actualEventArgsType, allowDerivedTypes))
                return new RaisedEvent<EventArgs>(sender, arguments);

            throw CreateRaiseException(typeof(EventArgs), actualEventArgsType, allowDerivedTypes, actionExpression);
        }

        throw CreateRaiseException(typeof(EventArgs), actualEventArgsType: null, allowDerivedTypes, actionExpression);
    }

    private static RaisedEvent<TEventArgs> RaiseCore<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, bool allowDerivedTypes, string? actionExpression)
        where TEventArgs : EventArgs
    {
        TEventArgs? arguments = null;
        object? sender = null;
        void Handler(object? eventSender, TEventArgs eventArguments)
        {
            sender ??= eventSender;
            arguments ??= eventArguments;
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

        if (arguments is not null)
        {
            var actualEventArgsType = arguments.GetType();
            if (IsExpectedEventArgsType(typeof(TEventArgs), actualEventArgsType, allowDerivedTypes))
                return new RaisedEvent<TEventArgs>(sender, arguments);

            throw CreateRaiseException(typeof(TEventArgs), actualEventArgsType, allowDerivedTypes, actionExpression);
        }

        throw CreateRaiseException(typeof(TEventArgs), actualEventArgsType: null, allowDerivedTypes, actionExpression);
    }

    private static bool IsExpectedEventArgsType(Type expectedEventArgsType, Type actualEventArgsType, bool allowDerivedTypes)
    {
        if (allowDerivedTypes)
            return expectedEventArgsType.IsAssignableFrom(actualEventArgsType);

        return actualEventArgsType == expectedEventArgsType;
    }

    private static AssertionException CreateRaiseException(Type expectedEventArgsType, Type? actualEventArgsType, bool allowDerivedTypes, string? actionExpression)
    {
        return new AssertionException(ErrorFormatter.Format(new RaiseAssertionError(expectedEventArgsType, actualEventArgsType, allowDerivedTypes, actionExpression)));
    }
}
