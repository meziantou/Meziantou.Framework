using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1030 // Assertion methods intentionally use the xUnit-compatible Raise terminology.
partial class Assert
{
    public static void DoesNotRaise(Action<EventHandler> attach, Action<EventHandler> detach, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        SucceedWhenAssertionFails(() => Raise(attach, detach, action, actionExpression), () => CreateNegativeTextAssertion(nameof(DoesNotRaise), "event with exact EventArgs", actionExpression ?? "<action>"));
    }

    public static void DoesNotRaise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        SucceedWhenAssertionFails(() => Raise(attach, detach, action, actionExpression), () => CreateNegativeTextAssertion(nameof(DoesNotRaise), "event with exact " + typeof(TEventArgs).FullName, actionExpression ?? "<action>"));
    }

    public static void DoesNotRaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        SucceedWhenAssertionFails(() => RaiseAny(attach, detach, action, actionExpression), () => CreateNegativeTextAssertion(nameof(DoesNotRaiseAny), "event assignable to EventArgs", actionExpression ?? "<action>"));
    }

    public static void DoesNotRaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        SucceedWhenAssertionFails(() => RaiseAny(attach, detach, action, actionExpression), () => CreateNegativeTextAssertion(nameof(DoesNotRaiseAny), "event assignable to " + typeof(TEventArgs).FullName, actionExpression ?? "<action>"));
    }
}
#pragma warning restore CA1030
