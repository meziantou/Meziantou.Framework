using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// See Assert.Raise.cs for the overload resolution priorities of the delegate overloads
public partial class Assert
{
    public static void DoesNotRaise(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        DoesNotRaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: false, nameof(DoesNotRaise), "event with exact EventArgs", message, actionExpression);
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotRaise(Action<EventHandler> attach, Action<EventHandler> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        DoesNotRaiseCore(await RecordEventsAsync(attach, detach, action).ConfigureAwait(false), allowDerivedTypes: false, nameof(DoesNotRaise), "event with exact EventArgs", message, actionExpression);
    }

    public static async Task DoesNotRaise(Action<EventHandler> attach, Action<EventHandler> detach, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        DoesNotRaiseCore(await RecordEventsAsync(attach, detach, ToTaskFunc(action)).ConfigureAwait(false), allowDerivedTypes: false, nameof(DoesNotRaise), "event with exact EventArgs", message, actionExpression);
    }

    public static void DoesNotRaise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        DoesNotRaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: false, nameof(DoesNotRaise), "event with exact " + typeof(TEventArgs).FullName, message, actionExpression);
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotRaise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        DoesNotRaiseCore(await RecordEventsAsync(attach, detach, action).ConfigureAwait(false), allowDerivedTypes: false, nameof(DoesNotRaise), "event with exact " + typeof(TEventArgs).FullName, message, actionExpression);
    }

    public static async Task DoesNotRaise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        DoesNotRaiseCore(await RecordEventsAsync(attach, detach, ToTaskFunc(action)).ConfigureAwait(false), allowDerivedTypes: false, nameof(DoesNotRaise), "event with exact " + typeof(TEventArgs).FullName, message, actionExpression);
    }

    public static void DoesNotRaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        DoesNotRaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: true, nameof(DoesNotRaiseAny), "event assignable to EventArgs", message, actionExpression);
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotRaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        DoesNotRaiseCore(await RecordEventsAsync(attach, detach, action).ConfigureAwait(false), allowDerivedTypes: true, nameof(DoesNotRaiseAny), "event assignable to EventArgs", message, actionExpression);
    }

    public static async Task DoesNotRaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        DoesNotRaiseCore(await RecordEventsAsync(attach, detach, ToTaskFunc(action)).ConfigureAwait(false), allowDerivedTypes: true, nameof(DoesNotRaiseAny), "event assignable to EventArgs", message, actionExpression);
    }

    public static void DoesNotRaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        DoesNotRaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: true, nameof(DoesNotRaiseAny), "event assignable to " + typeof(TEventArgs).FullName, message, actionExpression);
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotRaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        DoesNotRaiseCore(await RecordEventsAsync(attach, detach, action).ConfigureAwait(false), allowDerivedTypes: true, nameof(DoesNotRaiseAny), "event assignable to " + typeof(TEventArgs).FullName, message, actionExpression);
    }

    public static async Task DoesNotRaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        DoesNotRaiseCore(await RecordEventsAsync(attach, detach, ToTaskFunc(action)).ConfigureAwait(false), allowDerivedTypes: true, nameof(DoesNotRaiseAny), "event assignable to " + typeof(TEventArgs).FullName, message, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that no event is raised.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void NotRaisedAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        DoesNotRaiseAny(attach, detach, action, message, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that no event is raised by the asynchronous action.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task NotRaisedAnyAsync<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        await DoesNotRaiseAny(attach, detach, action, message, actionExpression).ConfigureAwait(false);
    }

    private static void DoesNotRaiseCore<TEventArgs>((object? Sender, TEventArgs? Arguments)[] events, bool allowDerivedTypes, string assertionName, string notExpectedText, string? message, string? actionExpression)
        where TEventArgs : EventArgs
    {
        foreach (var (_, arguments) in events)
        {
            if (IsExpectedEventArguments(typeof(TEventArgs), arguments, allowDerivedTypes))
                throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(assertionName, notExpectedText, actionExpression ?? "<action>", message)));
        }
    }
}
