using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotRaise(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        EventArgs? arguments = null;
        EventHandler handler = (_, eventArguments) =>
        {
            arguments ??= eventArguments;
        };

        attach(handler);
        try
        {
            action();
        }
        finally
        {
            detach(handler);
        }

        if (arguments is null || arguments.GetType() != typeof(EventArgs))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotRaise), "event with exact EventArgs", actionExpression ?? "<action>", message)));
    }

    public static void DoesNotRaise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        TEventArgs? arguments = null;
        EventHandler<TEventArgs> handler = (_, eventArguments) =>
        {
            arguments ??= eventArguments;
        };

        attach(handler);
        try
        {
            action();
        }
        finally
        {
            detach(handler);
        }

        if (arguments is null || arguments.GetType() != typeof(TEventArgs))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotRaise), "event with exact " + typeof(TEventArgs).FullName, actionExpression ?? "<action>", message)));
    }

    public static void DoesNotRaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        EventArgs? arguments = null;
        EventHandler handler = (_, eventArguments) =>
        {
            arguments ??= eventArguments;
        };

        attach(handler);
        try
        {
            action();
        }
        finally
        {
            detach(handler);
        }

        if (arguments is null || !typeof(EventArgs).IsAssignableFrom(arguments.GetType()))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotRaiseAny), "event assignable to EventArgs", actionExpression ?? "<action>", message)));
    }

    public static void DoesNotRaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        TEventArgs? arguments = null;
        EventHandler<TEventArgs> handler = (_, eventArguments) =>
        {
            arguments ??= eventArguments;
        };

        attach(handler);
        try
        {
            action();
        }
        finally
        {
            detach(handler);
        }

        if (arguments is null || !typeof(TEventArgs).IsAssignableFrom(arguments.GetType()))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotRaiseAny), "event assignable to " + typeof(TEventArgs).FullName, actionExpression ?? "<action>", message)));
    }
}
#pragma warning restore CA1030
