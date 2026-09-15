using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotRaise(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        if (!ContainsExpectedEvent(RecordEvents(attach, detach, action), allowDerivedTypes: false))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotRaise), "event with exact EventArgs", actionExpression ?? "<action>", message)));
    }

    public static void DoesNotRaise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        if (!ContainsExpectedEvent(RecordEvents(attach, detach, action), allowDerivedTypes: false))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotRaise), "event with exact " + typeof(TEventArgs).FullName, actionExpression ?? "<action>", message)));
    }

    public static void DoesNotRaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        if (!ContainsExpectedEvent(RecordEvents(attach, detach, action), allowDerivedTypes: true))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotRaiseAny), "event assignable to EventArgs", actionExpression ?? "<action>", message)));
    }

    public static void DoesNotRaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        if (!ContainsExpectedEvent(RecordEvents(attach, detach, action), allowDerivedTypes: true))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotRaiseAny), "event assignable to " + typeof(TEventArgs).FullName, actionExpression ?? "<action>", message)));
    }

    private static bool ContainsExpectedEvent<TEventArgs>(List<(object? Sender, TEventArgs? Arguments)> events, bool allowDerivedTypes)
        where TEventArgs : EventArgs
    {
        foreach (var (_, arguments) in events)
        {
            if (IsExpectedEventArguments(typeof(TEventArgs), arguments, allowDerivedTypes))
                return true;
        }

        return false;
    }
}
#pragma warning restore CA1030
