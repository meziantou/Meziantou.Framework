using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// Overload resolution of the event assertions (Raise, RaiseAny, DoesNotRaise, DoesNotRaiseAny) follows the exception
// assertions (see Assert.Throws.cs): an async lambda binds to the Func<Task> overloads, which have a higher priority,
// and a lambda returning a ValueTask binds to the Func<ValueTask> overloads. The handler is detached only once the
// returned task completes, so the events raised after an await are observed.
public partial class Assert
{
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<EventArgs> Raise(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: false, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [OverloadResolutionPriority(1)]
    public static async Task<RaisedEvent<EventArgs>> Raise(Action<EventHandler> attach, Action<EventHandler> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(await RecordEventsAsync(attach, detach, action).ConfigureAwait(false), allowDerivedTypes: false, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static async Task<RaisedEvent<EventArgs>> Raise(Action<EventHandler> attach, Action<EventHandler> detach, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(await RecordEventsAsync(attach, detach, ToTaskFunc(action)).ConfigureAwait(false), allowDerivedTypes: false, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<TEventArgs> Raise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: false, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [OverloadResolutionPriority(1)]
    public static async Task<RaisedEvent<TEventArgs>> Raise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(await RecordEventsAsync(attach, detach, action).ConfigureAwait(false), allowDerivedTypes: false, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static async Task<RaisedEvent<TEventArgs>> Raise<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(await RecordEventsAsync(attach, detach, ToTaskFunc(action)).ConfigureAwait(false), allowDerivedTypes: false, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<EventArgs> RaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: true, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [OverloadResolutionPriority(1)]
    public static async Task<RaisedEvent<EventArgs>> RaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(await RecordEventsAsync(attach, detach, action).ConfigureAwait(false), allowDerivedTypes: true, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static async Task<RaisedEvent<EventArgs>> RaiseAny(Action<EventHandler> attach, Action<EventHandler> detach, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseCore(await RecordEventsAsync(attach, detach, ToTaskFunc(action)).ConfigureAwait(false), allowDerivedTypes: true, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static RaisedEvent<TEventArgs> RaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(RecordEvents(attach, detach, action), allowDerivedTypes: true, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [OverloadResolutionPriority(1)]
    public static async Task<RaisedEvent<TEventArgs>> RaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(await RecordEventsAsync(attach, detach, action).ConfigureAwait(false), allowDerivedTypes: true, message, actionExpression);
    }

    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    public static async Task<RaisedEvent<TEventArgs>> RaiseAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseCore(await RecordEventsAsync(attach, detach, ToTaskFunc(action)).ConfigureAwait(false), allowDerivedTypes: true, message, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that an event with exactly the specified event args type is raised.</summary>
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static RaisedEvent<TEventArgs> Raises<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return Raise(attach, detach, action, message, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that an event with exactly the specified event args type is raised by the asynchronous action.</summary>
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<RaisedEvent<TEventArgs>> RaisesAsync<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return await Raise(attach, detach, action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that an event is raised.</summary>
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static RaisedEvent<EventArgs> RaisesAny(Action<EventHandler> attach, Action<EventHandler> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return RaiseAny(attach, detach, action, message, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that an event with an event args type assignable to the specified type is raised.</summary>
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static RaisedEvent<TEventArgs> RaisesAny<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return RaiseAny(attach, detach, action, message, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that an event is raised by the asynchronous action.</summary>
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<RaisedEvent<EventArgs>> RaisesAnyAsync(Action<EventHandler> attach, Action<EventHandler> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await RaiseAny(attach, detach, action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that an event with an event args type assignable to the specified type is raised by the asynchronous action.</summary>
    [SuppressMessage("Design", "CA1030:Use events where appropriate")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<RaisedEvent<TEventArgs>> RaisesAnyAsync<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where TEventArgs : EventArgs
    {
        return await RaiseAny(attach, detach, action, message, actionExpression).ConfigureAwait(false);
    }

    private static Func<Task> ToTaskFunc(Func<ValueTask> action)
    {
        return async () => await action().ConfigureAwait(false);
    }

    // Every raised event is recorded, so that the assertion is decided by all the events and not only by the first one.
    private static (object? Sender, TEventArgs? Arguments)[] RecordEvents<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Action action)
        where TEventArgs : EventArgs
    {
        var recorder = new RaisedEventRecorder<TEventArgs>();
        EventHandler<TEventArgs> handler = recorder.Record;
        attach(handler);
        try
        {
            action();
        }
        finally
        {
            detach(handler);
        }

        return recorder.GetEvents();
    }

    private static (object? Sender, EventArgs? Arguments)[] RecordEvents(Action<EventHandler> attach, Action<EventHandler> detach, Action action)
    {
        var recorder = new RaisedEventRecorder<EventArgs>();
        EventHandler handler = recorder.Record;
        attach(handler);
        try
        {
            action();
        }
        finally
        {
            detach(handler);
        }

        return recorder.GetEvents();
    }

    private static async Task<(object? Sender, TEventArgs? Arguments)[]> RecordEventsAsync<TEventArgs>(Action<EventHandler<TEventArgs>> attach, Action<EventHandler<TEventArgs>> detach, Func<Task> action)
        where TEventArgs : EventArgs
    {
        var recorder = new RaisedEventRecorder<TEventArgs>();
        EventHandler<TEventArgs> handler = recorder.Record;
        attach(handler);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            detach(handler);
        }

        return recorder.GetEvents();
    }

    private static async Task<(object? Sender, EventArgs? Arguments)[]> RecordEventsAsync(Action<EventHandler> attach, Action<EventHandler> detach, Func<Task> action)
    {
        var recorder = new RaisedEventRecorder<EventArgs>();
        EventHandler handler = recorder.Record;
        attach(handler);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            detach(handler);
        }

        return recorder.GetEvents();
    }

    private static RaisedEvent<TEventArgs> RaiseCore<TEventArgs>((object? Sender, TEventArgs? Arguments)[] events, bool allowDerivedTypes, string? message, string? actionExpression)
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
        throw CreateRaiseException(typeof(TEventArgs), events.Length > 0 ? events[0].Arguments!.GetType() : null, allowDerivedTypes, message, actionExpression);
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
