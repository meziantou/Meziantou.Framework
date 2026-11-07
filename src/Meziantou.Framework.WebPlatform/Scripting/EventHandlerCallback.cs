using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The value of an event handler (https://html.spec.whatwg.org/multipage/webappapis.html#event-handlers) that is a
/// callback of a script engine: an <c>EventHandler</c>, <c>OnErrorEventHandler</c> or <c>OnBeforeUnloadEventHandler</c>
/// callback function. The library invokes it with the event handler processing algorithm and processes its return value
/// (e.g. <c>return false</c> cancels the event).
/// </summary>
public abstract class EventHandlerCallback
{
    private Action<Event>? _delegate;

    /// <summary>
    /// Invokes the callback (https://webidl.spec.whatwg.org/#invoke-a-callback-function) with <paramref name="thisValue"/>
    /// as the <c>this</c> value. An exception thrown by the callback is reported by the engine to the global object of the
    /// realm of the callback, and the callback returns null.
    /// </summary>
    /// <param name="thisValue">The current target of the event.</param>
    /// <param name="arguments">
    /// The event, or, for the special error event handling of the <c>onerror</c> handler of a window, the message, the file
    /// name, the line number, the column number and the error of the <see cref="ErrorEvent"/>.
    /// </param>
    /// <returns>
    /// The return value: a <see cref="bool"/> when the callback returns a JavaScript boolean, null when it returns
    /// <c>undefined</c> or <c>null</c>, a <see cref="string"/> when the callback is an <c>OnBeforeUnloadEventHandler</c>
    /// (the result is converted to a <c>DOMString?</c>), or any other value.
    /// </returns>
    protected internal abstract object? Invoke(EventTarget thisValue, object?[] arguments);

    /// <summary>
    /// Gets the .NET delegate of the callback, returned by the event handler properties (e.g. <c>OnClick</c>): it runs the
    /// event handler processing algorithm. Setting an event handler property to this delegate sets the event handler to
    /// this callback.
    /// </summary>
    internal Action<Event> Delegate => _delegate ??= InvokeEventHandler;

    private void InvokeEventHandler(Event @event) => EventHandlerProcessing.Invoke(this, @event);

    /// <summary>Returns the callback of a delegate returned by <see cref="Delegate"/>, or wraps a .NET delegate.</summary>
    internal static EventHandlerCallback? FromDelegate(Action<Event>? handler) => handler switch
    {
        null => null,
        { Target: EventHandlerCallback callback } when ReferenceEquals(callback._delegate, handler) => callback,
        _ => new DelegateEventHandlerCallback(handler),
    };

    /// <summary>Returns the .NET delegate of a callback: the delegate set from .NET, or <see cref="Delegate"/>.</summary>
    internal static Action<Event>? ToDelegate(EventHandlerCallback? callback) => callback switch
    {
        null => null,
        DelegateEventHandlerCallback delegateCallback => delegateCallback.Handler,
        _ => callback.Delegate,
    };
}
