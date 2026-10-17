using System.Collections.ObjectModel;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An event whose lifetime can be extended by promises (https://w3c.github.io/ServiceWorker/#extendableevent-interface):
/// the lifecycle events (<c>install</c>, <c>activate</c>) and the functional events of the service workers.
/// </summary>
[IdlInterface("ExtendableEvent", "https://w3c.github.io/ServiceWorker/#extendableevent")]
public class ExtendableEvent : Event
{
    private int _pendingPromiseCount;
    private TaskCompletionSource? _notActive;
    private DomEventLoop? _eventLoop;

    /// <summary>Creates an extendable event.</summary>
    [IdlMember("constructor", "https://w3c.github.io/ServiceWorker/#dom-extendableevent-extendableevent")]
    public ExtendableEvent(string type, ExtendableEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
    }

    /// <summary>
    /// Whether the event is active (https://w3c.github.io/ServiceWorker/#extendableevent-active): it is being dispatched, or
    /// one of its extend lifetime promises is pending.
    /// </summary>
    internal bool IsActive => _pendingPromiseCount > 0 || DispatchFlag;

    /// <summary>Whether one of the extend lifetime promises was rejected.</summary>
    internal bool HasRejectedLifetimePromise { get; private set; }

    /// <summary>The service worker whose set of extended events contains the event, or null.</summary>
    internal ServiceWorkerRecord? ExtendedWorker { get; set; }

    /// <summary>A task completed when the event is no longer active (after its dispatch).</summary>
    internal Task WhenNotActive
    {
        get
        {
            // The continuations run synchronously, on the thread of the service worker (see ServiceWorkerWait)
            _notActive ??= new TaskCompletionSource();
            if (!IsActive)
            {
                _notActive.TrySetResult();
            }

            return _notActive.Task;
        }
    }

    /// <summary>Extends the lifetime of the event until the promise settles (https://w3c.github.io/ServiceWorker/#dom-extendableevent-waituntil).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the event is not trusted or not active.</exception>
    [IdlMember("waitUntil", "https://w3c.github.io/ServiceWorker/#dom-extendableevent-waituntil")]
    public void WaitUntil(Task<object?> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        AddLifetimePromise(f);
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#add-lifetime-promise</summary>
    internal void AddLifetimePromise(Task promise)
    {
        if (!IsTrusted)
            throw DomException.InvalidState("The event is not trusted");

        if (!IsActive)
            throw DomException.InvalidState("The event is no longer active");

        _eventLoop ??= GlobalScope.Current?.EventLoop;
        _pendingPromiseCount++;

        // The count is decremented by a microtask queued by the reaction to the promise (the promise of a task converted by the
        // bindings is settled by a reaction, on the thread of the event loop)
        _ = promise.ContinueWith(
            completed =>
            {
                if (!completed.IsCompletedSuccessfully)
                {
                    HasRejectedLifetimePromise = true;
                }

                if (_eventLoop is { } eventLoop)
                {
                    eventLoop.QueueMicrotask(OnLifetimePromiseSettled);
                }
                else
                {
                    OnLifetimePromiseSettled();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnLifetimePromiseSettled()
    {
        _pendingPromiseCount--;
        if (_pendingPromiseCount == 0)
        {
            OnNoLongerActive();
        }
    }

    /// <summary>Called when the dispatch of the event ends.</summary>
    internal void OnDispatched()
    {
        if (!IsActive)
        {
            OnNoLongerActive();
        }
    }

    private void OnNoLongerActive()
    {
        _notActive?.TrySetResult();
        if (ExtendedWorker is { } worker)
        {
            ExtendedWorker = null;
            Interlocked.Decrement(ref worker.ActiveExtendedEventCount);

            // https://w3c.github.io/ServiceWorker/#dom-extendableevent-waituntil: the registration may be cleared or activated
            var registration = worker.Registration;
            registration.Registry.TryClearOrActivateInParallel(registration);
        }
    }
}

/// <summary>The <c>install</c> event of a service worker (https://w3c.github.io/ServiceWorker/#installevent).</summary>
[IdlInterface("InstallEvent", "https://w3c.github.io/ServiceWorker/#installevent")]
public sealed class InstallEvent : ExtendableEvent
{
    /// <summary>Creates an install event.</summary>
    [IdlMember("constructor", "https://w3c.github.io/ServiceWorker/#dom-installevent-installevent")]
    public InstallEvent(string type, ExtendableEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
    }
}

/// <summary>
/// The <c>fetch</c> event of a service worker (https://w3c.github.io/ServiceWorker/#fetchevent): a request of a client of the
/// service worker, which the service worker can answer with <see cref="RespondWith"/>. The request and the response are
/// <c>Request</c> and <c>Response</c> objects of the script engine (the Fetch API is implemented in JavaScript).
/// </summary>
[IdlInterface("FetchEvent", "https://w3c.github.io/ServiceWorker/#fetchevent")]
public sealed class FetchEvent : ExtendableEvent
{
    private TaskCompletionSource? _responded;

    /// <summary>Creates a fetch event.</summary>
    [IdlMember("constructor", "https://w3c.github.io/ServiceWorker/#dom-fetchevent-fetchevent")]
    public FetchEvent(string type, FetchEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Request = eventInitDict.Request;
        PreloadResponse = eventInitDict.PreloadResponse ?? Task.FromResult<object?>(null);
        ClientId = eventInitDict.ClientId;
        ResultingClientId = eventInitDict.ResultingClientId;
        ReplacesClientId = eventInitDict.ReplacesClientId;
        Handled = eventInitDict.Handled ?? new TaskCompletionSource().Task;
        PromiseSlots.Register(PreloadResponse);
        PromiseSlots.Register(Handled);
    }

    /// <summary>Gets the request: a <c>Request</c> object of the script engine.</summary>
    [IdlMember("request", "https://w3c.github.io/ServiceWorker/#dom-fetchevent-request")]
    public object Request { get; }

    /// <summary>Gets the promise of the response of the navigation preload (a <c>Response</c> object of the script engine, or undefined).</summary>
    [IdlMember("preloadResponse", "https://w3c.github.io/ServiceWorker/#dom-fetchevent-preloadresponse")]
    public Task<object?> PreloadResponse { get; }

    /// <summary>Gets the id of the client of the request, or an empty string.</summary>
    [IdlMember("clientId", "https://w3c.github.io/ServiceWorker/#dom-fetchevent-clientid")]
    public string ClientId { get; }

    /// <summary>Gets the id of the client created by a navigation, or an empty string.</summary>
    [IdlMember("resultingClientId", "https://w3c.github.io/ServiceWorker/#dom-fetchevent-resultingclientid")]
    public string ResultingClientId { get; }

    /// <summary>Gets the id of the client replaced by a navigation, or an empty string.</summary>
    [IdlMember("replacesClientId", "https://w3c.github.io/ServiceWorker/#dom-fetchevent-replacesclientid")]
    public string ReplacesClientId { get; }

    /// <summary>Gets the promise fulfilled once the event was handled.</summary>
    [IdlMember("handled", "https://w3c.github.io/ServiceWorker/#dom-fetchevent-handled")]
    public Task Handled { get; }

    /// <summary>https://w3c.github.io/ServiceWorker/#fetchevent-respond-with-entered-flag</summary>
    internal bool RespondWithEntered { get; private set; }

    /// <summary>https://w3c.github.io/ServiceWorker/#fetchevent-respond-with-error-flag</summary>
    internal bool RespondWithError { get; private set; }

    /// <summary>https://w3c.github.io/ServiceWorker/#fetchevent-potential-response</summary>
    internal FetchResponse? PotentialResponse { get; private set; }

    /// <summary>A task completed when the wait to respond flag is unset (https://w3c.github.io/ServiceWorker/#wait-to-respond-flag).</summary>
    internal Task WhenResponded => _responded?.Task ?? Task.CompletedTask;

    /// <summary>
    /// Answers the request with a response (https://w3c.github.io/ServiceWorker/#dom-fetchevent-respondwith): the promise
    /// must be fulfilled with a <c>Response</c> object, else the request is a network error.
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the event is not being dispatched, or when the method was already called.</exception>
    [IdlMember("respondWith", "https://w3c.github.io/ServiceWorker/#dom-fetchevent-respondwith")]
    public void RespondWith(Task<object?> r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (!DispatchFlag)
            throw DomException.InvalidState("respondWith() must be called while the event is dispatched");

        if (RespondWithEntered)
            throw DomException.InvalidState("respondWith() was already called");

        AddLifetimePromise(r);
        StopPropagationFlag = true;
        StopImmediatePropagationFlag = true;
        RespondWithEntered = true;
        var responded = _responded = new TaskCompletionSource();
        var global = GlobalScope.Current;
        _ = r.ContinueWith(
            completed =>
            {
                if (!completed.IsCompletedSuccessfully || global?.ScriptEngine?.GetServiceWorkerFetchOperations(global) is not { } operations)
                {
                    RespondWithError = true;
                    responded.TrySetResult();
                    return;
                }

                // The body of the response is read (the bodies are not streamed): a response whose body is disturbed or locked
                // is an error
                operations.ReadResponse(completed.Result!, response =>
                {
                    if (response is null)
                    {
                        RespondWithError = true;
                    }
                    else
                    {
                        PotentialResponse = response;
                    }

                    responded.TrySetResult();
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

/// <summary>The <c>message</c> event of a service worker (https://w3c.github.io/ServiceWorker/#extendablemessageevent).</summary>
[IdlInterface("ExtendableMessageEvent", "https://w3c.github.io/ServiceWorker/#extendablemessageevent")]
public sealed class ExtendableMessageEvent : ExtendableEvent
{
    /// <summary>Creates a message event.</summary>
    [IdlMember("constructor", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageevent-extendablemessageevent")]
    public ExtendableMessageEvent(string type, ExtendableMessageEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        // The default value of data is null (a missing member is converted to a .NET null)
        Data = eventInitDict?.Data ?? NullValue.Instance;
        Origin = eventInitDict?.Origin ?? "";
        LastEventId = eventInitDict?.LastEventId ?? "";
        Source = eventInitDict?.Source;
        Ports = new ReadOnlyCollection<MessagePort>([.. eventInitDict?.Ports ?? []]);
    }

    /// <summary>Gets the data of the message.</summary>
    [IdlMember("data", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageevent-data")]
    public object? Data { get; }

    /// <summary>Gets the origin of the sender.</summary>
    [IdlMember("origin", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageevent-origin")]
    public string Origin { get; }

    /// <summary>Gets the last event ID.</summary>
    [IdlMember("lastEventId", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageevent-lasteventid")]
    public string LastEventId { get; }

    /// <summary>Gets the sender: a <see cref="Client"/>, a <see cref="ServiceWorker"/> or a <see cref="MessagePort"/>, or null.</summary>
    [IdlMember("source", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageevent-source")]
    public object? Source { get; }

    /// <summary>Gets the ports sent with the message.</summary>
    [IdlMember("ports", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageevent-ports")]
    public IReadOnlyList<MessagePort> Ports { get; }
}
