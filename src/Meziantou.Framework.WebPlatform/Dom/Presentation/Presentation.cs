using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a presentation connection (https://w3c.github.io/presentation-api/#dom-presentationconnectionstate).</summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionstate"/>
[IdlInterface("PresentationConnectionState", "https://w3c.github.io/presentation-api/#dom-presentationconnectionstate")]
public enum PresentationConnectionState
{
    /// <summary>The connection is being established.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionstate-connecting"/>
    [IdlMember("connecting", "https://w3c.github.io/presentation-api/#dom-presentationconnectionstate-connecting")]
    Connecting,

    /// <summary>The connection is established: the messages can be sent.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionstate-connected"/>
    [IdlMember("connected", "https://w3c.github.io/presentation-api/#dom-presentationconnectionstate-connected")]
    Connected,

    /// <summary>The connection is closed: the presentation continues, and <c>reconnect()</c> can connect it again.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionstate-closed"/>
    [IdlMember("closed", "https://w3c.github.io/presentation-api/#dom-presentationconnectionstate-closed")]
    Closed,

    /// <summary>The presentation is terminated.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionstate-terminated"/>
    [IdlMember("terminated", "https://w3c.github.io/presentation-api/#dom-presentationconnectionstate-terminated")]
    Terminated,
}

/// <summary>The reason why a presentation connection is closed (https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason).</summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason"/>
[IdlInterface("PresentationConnectionCloseReason", "https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason")]
public enum PresentationConnectionCloseReason
{
    /// <summary>An error occurred (the message describes it).</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason-error"/>
    [IdlMember("error", "https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason-error")]
    Error,

    /// <summary>One of the sides called <c>close()</c>.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason-closed"/>
    [IdlMember("closed", "https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason-closed")]
    Closed,

    /// <summary>The document of the other side was unloaded.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason-wentaway"/>
    [IdlMember("wentaway", "https://w3c.github.io/presentation-api/#dom-presentationconnectionclosereason-wentaway")]
    WentAway,
}

/// <summary>
/// The Presentation API of a window (https://w3c.github.io/presentation-api/#interface-presentation, <c>navigator.presentation</c>):
/// the default presentation request of the controlling side, and the receiver of a receiving browsing context.
/// </summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentation"/>
[IdlInterface("Presentation", "https://w3c.github.io/presentation-api/#dom-presentation")]
public sealed class Presentation
{
    private readonly GlobalPresentation _state;

    internal Presentation(GlobalPresentation state) => _state = state;

    /// <summary>
    /// Gets or sets the default presentation request (https://w3c.github.io/presentation-api/#dom-presentation-defaultrequest),
    /// that the browser could start from its own user interface. The library has no such user interface: the value is kept,
    /// and not used.
    /// </summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentation-defaultrequest"/>
    [IdlMember("defaultRequest", "https://w3c.github.io/presentation-api/#dom-presentation-defaultrequest")]
    public PresentationRequest? DefaultRequest { get; set; }

    /// <summary>
    /// Gets the receiver of the presentation, when the window is the top-level window of a receiving browsing context
    /// (<see cref="PresentationOptions.Receiver"/>), or null (https://w3c.github.io/presentation-api/#dom-presentation-receiver).
    /// </summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentation-receiver"/>
    [IdlMember("receiver", "https://w3c.github.io/presentation-api/#dom-presentation-receiver")]
    public PresentationReceiver? Receiver => _state.GetReceiver();
}

/// <summary>
/// A request to present one of several URLs on a presentation display (https://w3c.github.io/presentation-api/#interface-presentationrequest):
/// <see cref="Start"/> starts a presentation on a display of the <see cref="PresentationProvider"/> of the browsing context
/// (<see cref="BrowsingContextOptions.Presentation"/>), <see cref="Reconnect"/> connects to a presentation started before, and
/// <see cref="GetAvailability"/> tells whether a display can present the URLs.
/// </summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationrequest"/>
[IdlInterface("PresentationRequest", "https://w3c.github.io/presentation-api/#dom-presentationrequest")]
public sealed class PresentationRequest : EventTarget
{
    // The top-level browsing contexts in which a start() is pending (only one at a time, https://w3c.github.io/presentation-api/#dom-presentationrequest-start)
    private static readonly ConditionalWeakTable<BrowsingContext, StrongBox<int>> PendingStarts = [];

    private readonly Window _window;
    private readonly GlobalPresentation _state;
    private PresentationAvailability? _availability;

    /// <summary>Creates a request with one presentation URL.</summary>
    /// <param name="url">The presentation URL, relative to the base URL of the document.</param>
    /// <exception cref="DomException">See <see cref="PresentationRequest(IReadOnlyList{string})"/>.</exception>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationrequest-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/presentation-api/#dom-presentationrequest-constructor")]
    public PresentationRequest(string url)
        : this([url ?? throw new ArgumentNullException(nameof(url))])
    {
    }

    /// <summary>
    /// Creates a request with several presentation URLs (https://w3c.github.io/presentation-api/#constructing-a-presentationrequest):
    /// the URLs are parsed relative to the base URL of the document, and the URLs whose scheme is not supported (the library
    /// supports <c>http</c> and <c>https</c>) are ignored.
    /// </summary>
    /// <param name="urls">The presentation URLs, in the order of preference.</param>
    /// <exception cref="DomException">
    /// A <c>SecurityError</c> when the document is sandboxed without <c>allow-presentation</c> or a URL is not potentially
    /// trustworthy, a <c>NotSupportedError</c> when there is no URL or no URL of a supported scheme, a <c>SyntaxError</c> when
    /// a URL is not valid.
    /// </exception>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationrequest-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/presentation-api/#dom-presentationrequest-constructor")]
    public PresentationRequest(IReadOnlyList<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        _window = GlobalScope.Current as Window ?? throw new InvalidOperationException("A presentation request can only be created in a window");
        _state = _window.GetPresentationState();
        if ((_window.Document.ActiveSandboxingFlags & SandboxingFlags.Presentation) != SandboxingFlags.None)
            throw new DomException("Failed to construct 'PresentationRequest': The document is sandboxed and lacks the 'allow-presentation' flag.", "SecurityError");

        if (urls.Count == 0)
            throw new DomException("Failed to construct 'PresentationRequest': An empty sequence of URLs is not supported.", "NotSupportedError");

        List<string> presentationUrls = [];
        foreach (var url in urls)
        {
            var record = UrlParser.Parse(url, _window.ApiBaseUrl) ?? throw new DomException($"Failed to construct 'PresentationRequest': '{url}' is not a valid URL.", "SyntaxError");
            if (record.Scheme is "http" or "https")
            {
                if (!ReferrerPolicies.IsPotentiallyTrustworthy(record))
                    throw new DomException($"Failed to construct 'PresentationRequest': Presentation of an insecure document [{record.Serialize()}] is prohibited from a secure context.", "SecurityError");

                presentationUrls.Add(record.Serialize());
            }
        }

        if (presentationUrls.Count == 0)
            throw new DomException("Failed to construct 'PresentationRequest': An array of only unsupported URLs is not supported.", "NotSupportedError");

        Urls = presentationUrls;
    }

    internal override GlobalScope? RelevantGlobal => _window;

    /// <summary>Gets the presentation request URLs: the absolute URLs of the supported schemes, in the order of the page.</summary>
    [NotInIdl("The presentation request URLs of the request, for the .NET code")]
    public IReadOnlyList<string> Urls { get; }

    /// <summary>Gets or sets the event handler of the <c>connectionavailable</c> events (<see cref="PresentationConnectionAvailableEvent"/>).</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationrequest-onconnectionavailable"/>
    [IdlMember("onconnectionavailable", "https://w3c.github.io/presentation-api/#dom-presentationrequest-onconnectionavailable")]
    public Action<Event>? OnConnectionAvailable
    {
        get => GetEventHandler("connectionavailable");
        set => SetEventHandler("connectionavailable", value);
    }

    /// <summary>
    /// Starts a presentation (https://w3c.github.io/presentation-api/#dom-presentationrequest-start): the user chooses a display
    /// with <see cref="PresentationProvider.RequestDisplay"/>, which starts the presentation with
    /// <see cref="PresentationDisplay.StartAsync"/>. The task returns a <c>connecting</c> connection, which gets a
    /// <c>connect</c> event when it is established. The task fails with an <c>InvalidAccessError</c> <see cref="DomException"/>
    /// without transient activation, an <c>OperationError</c> when another <c>start()</c> of the page is pending, a
    /// <c>NotFoundError</c> when no display can present the URLs, and a <c>NotAllowedError</c> when the user does not choose a
    /// display.
    /// </summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationrequest-start"/>
    [IdlMember("start", "https://w3c.github.io/presentation-api/#dom-presentationrequest-start")]
    public Task<PresentationConnection> Start()
    {
        if (!_window.HasTransientActivation)
            return Task.FromException<PresentationConnection>(new DomException("Failed to execute 'start' on 'PresentationRequest': PresentationRequest::start() requires user gesture.", "InvalidAccessError"));

        var top = _window.BrowsingContext?.Top;
        if (top is null)
            return Task.FromException<PresentationConnection>(DomException.InvalidState("Failed to execute 'start' on 'PresentationRequest': The document is not fully active."));

        var pending = PendingStarts.GetOrCreateValue(top);
        lock (pending)
        {
            if (pending.Value > 0)
                return Task.FromException<PresentationConnection>(new DomException("Failed to execute 'start' on 'PresentationRequest': A presentation is already being started.", "OperationError"));

            pending.Value++;
        }

        var result = new TaskCompletionSource<PresentationConnection>();
        _ = result.Task.ContinueWith(
            _ =>
            {
                lock (pending)
                {
                    pending.Value--;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var provider = _state.Provider;
        var displays = provider?.Displays.Where(display => Urls.Any(display.CanPresent)).ToArray() ?? [];
        if (provider is null || displays.Length == 0)
        {
            _window.QueueGlobalTask(() => result.TrySetException(DomException.NotFound("Failed to execute 'start' on 'PresentationRequest': No presentation display is available for the presentation URLs.")));
            return result.Task;
        }

        Task<PresentationDisplay?> selection;
        try
        {
            selection = provider.RequestDisplay(new PresentationDisplayRequest(CallbackContext.For(_window), Urls, displays));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            selection = Task.FromException<PresentationDisplay?>(exception);
        }

        _ = selection.ContinueWith(
            task => _window.QueueGlobalTask(() =>
            {
                var display = task.IsCompletedSuccessfully ? task.Result : null;
                if (display is null || !displays.Contains(display))
                {
                    result.TrySetException(DomException.NotAllowed("Failed to execute 'start' on 'PresentationRequest': The user did not choose a presentation display."));
                    return;
                }

                StartPresentationConnection(provider, display, result);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>
    /// Connects to a presentation started before (https://w3c.github.io/presentation-api/#dom-presentationrequest-reconnect):
    /// a connection of the window with this identifier and one of the URLs of the request is returned (and connected again
    /// when it is closed); otherwise a new connection is established to the presentation of another window
    /// (<see cref="PresentationSession.ConnectAsync"/>). The task fails with a <c>NotFoundError</c> <see cref="DomException"/>
    /// when there is no such presentation.
    /// </summary>
    /// <param name="presentationId">The presentation identifier (<c>PresentationConnection.id</c>).</param>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationrequest-reconnect"/>
    [IdlMember("reconnect", "https://w3c.github.io/presentation-api/#dom-presentationrequest-reconnect")]
    public Task<PresentationConnection> Reconnect(string presentationId)
    {
        ArgumentNullException.ThrowIfNull(presentationId);
        var result = new TaskCompletionSource<PresentationConnection>();
        _window.QueueGlobalTask(() =>
        {
            var provider = _state.Provider;
            var controlled = provider?.GetControlledPresentations() ?? [];
            bool Matches(PresentationConnection connection)
                => connection.State != PresentationConnectionState.Terminated && connection.Id == presentationId && Urls.Contains(connection.Url, StringComparer.Ordinal);

            var existing = controlled.FirstOrDefault(connection => connection.Global == _window && Matches(connection));
            if (existing is not null)
            {
                result.TrySetResult(existing);
                if (existing.State is PresentationConnectionState.Connecting or PresentationConnectionState.Connected)
                    return;

                existing.Reconnect();
                return;
            }

            var other = controlled.FirstOrDefault(connection => connection.Global != _window && connection.Session is not null && Matches(connection));
            if (other?.Session is not { } session)
            {
                result.TrySetException(DomException.NotFound("Failed to execute 'reconnect' on 'PresentationRequest': No presentation of the presentation URLs with this identifier is found."));
                return;
            }

            var connection = new PresentationConnection(_state, presentationId, other.Url, isReceiver: false);
            provider!.AddControlledPresentation(connection);
            result.TrySetResult(connection);
            _window.QueueGlobalTask(() => FireEvent(new PresentationConnectionAvailableEvent(connection)));
            connection.Establish(session);
        });
        return result.Task;
    }

    /// <summary>
    /// Returns whether a display of the <see cref="PresentationProvider"/> can present the URLs
    /// (https://w3c.github.io/presentation-api/#dom-presentationrequest-getavailability): the same
    /// <see cref="PresentationAvailability"/> for the request, which gets a <c>change</c> event when the displays change.
    /// </summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationrequest-getavailability"/>
    [IdlMember("getAvailability", "https://w3c.github.io/presentation-api/#dom-presentationrequest-getavailability")]
    public Task<PresentationAvailability> GetAvailability()
    {
        var result = new TaskCompletionSource<PresentationAvailability>();
        _window.QueueGlobalTask(() =>
        {
            if (_availability is null)
            {
                _availability = new PresentationAvailability(_window, Urls, _state.IsAvailable(Urls));
                _state.AddAvailability(_availability);
            }

            result.TrySetResult(_availability);
        });
        return result.Task;
    }

    // https://w3c.github.io/presentation-api/#dfn-start-a-presentation-connection
    private void StartPresentationConnection(PresentationProvider provider, PresentationDisplay display, TaskCompletionSource<PresentationConnection> result)
    {
        var id = Guid.NewGuid().ToString("N");
        var url = Urls.First(display.CanPresent);
        var connection = new PresentationConnection(_state, id, url, isReceiver: false);
        provider.AddControlledPresentation(connection);
        result.TrySetResult(connection);
        _window.QueueGlobalTask(() => FireEvent(new PresentationConnectionAvailableEvent(connection)));

        Task<PresentationSession> start;
        try
        {
            start = display.StartAsync(new PresentationStartRequest(CallbackContext.For(_window), url, id), _state.CancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            start = Task.FromException<PresentationSession>(exception);
        }

        _ = start.ContinueWith(
            task => _window.QueueGlobalTask(() =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    connection.StartClosing(PresentationConnectionCloseReason.Error, "The presentation could not be started.");
                    return;
                }

                connection.Establish(task.Result);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

/// <summary>
/// Whether a presentation display can present the URLs of a <see cref="PresentationRequest"/>
/// (https://w3c.github.io/presentation-api/#interface-presentationavailability): <see cref="Value"/> changes, with a
/// <c>change</c> event, when the displays of the <see cref="PresentationProvider"/> change.
/// </summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationavailability"/>
[IdlInterface("PresentationAvailability", "https://w3c.github.io/presentation-api/#dom-presentationavailability")]
public sealed class PresentationAvailability : EventTarget
{
    private readonly Window _window;

    internal PresentationAvailability(Window window, IReadOnlyList<string> urls, bool value)
    {
        _window = window;
        Urls = urls;
        Value = value;
    }

    internal override GlobalScope? RelevantGlobal => _window;

    internal IReadOnlyList<string> Urls { get; }

    /// <summary>Gets whether a display can present one of the URLs.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationavailability-value"/>
    [IdlMember("value", "https://w3c.github.io/presentation-api/#dom-presentationavailability-value")]
    public bool Value { get; private set; }

    /// <summary>Gets or sets the event handler of the <c>change</c> events.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationavailability-onchange"/>
    [IdlMember("onchange", "https://w3c.github.io/presentation-api/#dom-presentationavailability-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    internal void Update(bool value)
    {
        if (value == Value)
            return;

        Value = value;
        FireEvent(new Event("change"));
    }
}

/// <summary>The initialization dictionary of a <see cref="PresentationConnectionAvailableEvent"/> (https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableeventinit).</summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableeventinit"/>
[IdlInterface("PresentationConnectionAvailableEventInit", "https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableeventinit")]
public sealed class PresentationConnectionAvailableEventInit : EventInit
{
    /// <summary>Gets the connection.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableeventinit-connection"/>
    [IdlMember("connection", "https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableeventinit-connection")]
    public required PresentationConnection Connection { get; init; }
}

/// <summary>
/// The event fired at a <see cref="PresentationRequest"/>, or at the <see cref="PresentationConnectionList"/> of a receiver,
/// when a connection is available (<c>connectionavailable</c>, https://w3c.github.io/presentation-api/#interface-presentationconnectionavailableevent).
/// </summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableevent"/>
[IdlInterface("PresentationConnectionAvailableEvent", "https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableevent")]
public sealed class PresentationConnectionAvailableEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableevent-constructor")]
    public PresentationConnectionAvailableEvent(string type, PresentationConnectionAvailableEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Connection = eventInitDict.Connection;
    }

    internal PresentationConnectionAvailableEvent(PresentationConnection connection)
        : base("connectionavailable")
    {
        Connection = connection;
    }

    /// <summary>Gets the connection.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableevent-connection"/>
    [IdlMember("connection", "https://w3c.github.io/presentation-api/#dom-presentationconnectionavailableevent-connection")]
    public PresentationConnection Connection { get; }
}

/// <summary>The initialization dictionary of a <see cref="PresentationConnectionCloseEvent"/> (https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseeventinit).</summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseeventinit"/>
[IdlInterface("PresentationConnectionCloseEventInit", "https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseeventinit")]
public sealed class PresentationConnectionCloseEventInit : EventInit
{
    /// <summary>Gets the reason.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseeventinit-reason"/>
    [IdlMember("reason", "https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseeventinit-reason")]
    public required PresentationConnectionCloseReason Reason { get; init; }

    /// <summary>Gets the message.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseeventinit-message"/>
    [IdlMember("message", "https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseeventinit-message")]
    public string Message { get; init; } = "";
}

/// <summary>The event fired at a <see cref="PresentationConnection"/> when it is closed (<c>close</c>, https://w3c.github.io/presentation-api/#interface-presentationconnectioncloseevent).</summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseevent"/>
[IdlInterface("PresentationConnectionCloseEvent", "https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseevent")]
public sealed class PresentationConnectionCloseEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseevent-constructor")]
    public PresentationConnectionCloseEvent(string type, PresentationConnectionCloseEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Reason = eventInitDict.Reason;
        Message = eventInitDict.Message;
    }

    internal PresentationConnectionCloseEvent(PresentationConnectionCloseReason reason, string message)
        : base("close")
    {
        Reason = reason;
        Message = message;
    }

    /// <summary>Gets the reason.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseevent-reason"/>
    [IdlMember("reason", "https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseevent-reason")]
    public PresentationConnectionCloseReason Reason { get; }

    /// <summary>Gets the message: a description of the error for the <c>error</c> reason, else empty.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseevent-message"/>
    [IdlMember("message", "https://w3c.github.io/presentation-api/#dom-presentationconnectioncloseevent-message")]
    public string Message { get; }
}

/// <summary>
/// A connection between a controlling window and a receiving browsing context (https://w3c.github.io/presentation-api/#interface-presentationconnection):
/// the messages go through the <see cref="PresentationChannel"/> that <see cref="PresentationSession.ConnectAsync"/> (on the
/// controlling side) or <see cref="PresentationReceivingContext.Connect"/> (on the receiving side) gives.
/// </summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection"/>
[IdlInterface("PresentationConnection", "https://w3c.github.io/presentation-api/#dom-presentationconnection")]
public sealed class PresentationConnection : EventTarget
{
    private readonly GlobalPresentation _state;
    private readonly bool _isReceiver;
    private PresentationChannel? _channel;
    private bool _closeTaskQueued;
    private bool _destroyed;

    internal PresentationConnection(GlobalPresentation state, string id, string url, bool isReceiver)
    {
        _state = state;
        _isReceiver = isReceiver;
        Id = id;
        Url = url;
        state.AddConnection(this);
    }

    internal Window Global => _state.Global;

    internal override GlobalScope? RelevantGlobal => Global;

    /// <summary>The presentation of a connection of the controlling side, once the display started it.</summary>
    internal PresentationSession? Session { get; private set; }

    internal bool IsGlobalDestroyed => _destroyed;

    /// <summary>Gets the presentation identifier.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-id"/>
    [IdlMember("id", "https://w3c.github.io/presentation-api/#dom-presentationconnection-id")]
    public string Id { get; }

    /// <summary>Gets the presentation URL.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-url"/>
    [IdlMember("url", "https://w3c.github.io/presentation-api/#dom-presentationconnection-url")]
    public string Url { get; }

    /// <summary>Gets the state of the connection.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-state"/>
    [IdlMember("state", "https://w3c.github.io/presentation-api/#dom-presentationconnection-state")]
    public PresentationConnectionState State { get; private set; } = PresentationConnectionState.Connecting;

    /// <summary>Gets or sets how the binary messages are exposed: as a <see cref="byte"/> array (an <c>ArrayBuffer</c>, the default) or as a <see cref="Blob"/>.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-binarytype"/>
    [IdlMember("binaryType", "https://w3c.github.io/presentation-api/#dom-presentationconnection-binarytype")]
    public BinaryType BinaryType { get; set; } = BinaryType.ArrayBuffer;

    /// <summary>Gets or sets the event handler of the <c>connect</c> events.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-onconnect"/>
    [IdlMember("onconnect", "https://w3c.github.io/presentation-api/#dom-presentationconnection-onconnect")]
    public Action<Event>? OnConnect
    {
        get => GetEventHandler("connect");
        set => SetEventHandler("connect", value);
    }

    /// <summary>Gets or sets the event handler of the <c>close</c> events (<see cref="PresentationConnectionCloseEvent"/>).</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-onclose"/>
    [IdlMember("onclose", "https://w3c.github.io/presentation-api/#dom-presentationconnection-onclose")]
    public Action<Event>? OnClose
    {
        get => GetEventHandler("close");
        set => SetEventHandler("close", value);
    }

    /// <summary>Gets or sets the event handler of the <c>terminate</c> events.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-onterminate"/>
    [IdlMember("onterminate", "https://w3c.github.io/presentation-api/#dom-presentationconnection-onterminate")]
    public Action<Event>? OnTerminate
    {
        get => GetEventHandler("terminate");
        set => SetEventHandler("terminate", value);
    }

    /// <summary>Gets or sets the event handler of the <c>message</c> events (<see cref="MessageEvent"/>).</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-onmessage"/>
    [IdlMember("onmessage", "https://w3c.github.io/presentation-api/#dom-presentationconnection-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>
    /// Closes the connection (https://w3c.github.io/presentation-api/#dom-presentationconnection-close): the other side is
    /// signaled with <see cref="PresentationChannel.Close"/>, and the connection gets a <c>close</c> event. The presentation
    /// continues.
    /// </summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-close"/>
    [IdlMember("close", "https://w3c.github.io/presentation-api/#dom-presentationconnection-close")]
    public void Close() => StartClosing(PresentationConnectionCloseReason.Closed, "");

    /// <summary>
    /// Terminates the presentation (https://w3c.github.io/presentation-api/#dom-presentationconnection-terminate): on the
    /// controlling side, the connections of the controlling windows to the presentation get a <c>terminate</c> event, and
    /// <see cref="PresentationSession.Terminate"/> asks the receiving side to end it; on the receiving side, the connections
    /// of the controllers are terminated, and <see cref="PresentationReceivingContext.Terminate"/> ends the presentation.
    /// </summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-terminate"/>
    [IdlMember("terminate", "https://w3c.github.io/presentation-api/#dom-presentationconnection-terminate")]
    public void Terminate()
    {
        if (State is not (PresentationConnectionState.Connected or PresentationConnectionState.Connecting))
            return;

        if (_isReceiver)
        {
            _state.TerminateAsReceiver();
            return;
        }

        foreach (var connection in _state.Provider?.GetControlledPresentations() ?? [this])
        {
            if (connection.Id == Id && connection.State is PresentationConnectionState.Connected or PresentationConnectionState.Connecting)
            {
                connection.QueueTermination();
            }
        }

        try
        {
            Session?.Terminate();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The connections are terminated anyway
        }
    }

    /// <summary>
    /// Sends a message to the other side (https://w3c.github.io/presentation-api/#dom-presentationconnection-send): a text
    /// message for a string, a binary message for a <see cref="Blob"/> or bytes (a <see cref="byte"/> array, a
    /// <see cref="ReadOnlyMemory{T}"/> of bytes, or a buffer source of the script engine). When the channel fails, the
    /// connection is closed with the <c>error</c> reason.
    /// </summary>
    /// <param name="data">The message.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the connection is not connected.</exception>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnection-send"/>
    [IdlMember("send", "https://w3c.github.io/presentation-api/#dom-presentationconnection-send")]
    public void Send(object data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (State != PresentationConnectionState.Connected || _channel is not { } channel)
            throw DomException.InvalidState($"Failed to execute 'send' on 'PresentationConnection': Presentation connection is in '{GetStateName(State)}' state.");

        try
        {
            switch (data)
            {
                case string text:
                    channel.Send(InfraString.ToScalarValueString(text));
                    break;
                case Blob blob:
                    channel.Send(blob.Data.ToArray());
                    break;
                case byte[] buffer:
                    channel.Send(buffer.ToArray());
                    break;
                case ReadOnlyMemory<byte> memory:
                    channel.Send(memory.ToArray());
                    break;
                case JsBufferSource bufferSource:
                    channel.Send(bufferSource.Bytes.ToArray());
                    break;
                default:
                    channel.Send(InfraString.ToScalarValueString(data.ToString() ?? ""));
                    break;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            StartClosing(PresentationConnectionCloseReason.Error, "The message could not be sent: " + exception.Message);
        }
    }

    private static string GetStateName(PresentationConnectionState state) => state switch
    {
        PresentationConnectionState.Connecting => "connecting",
        PresentationConnectionState.Connected => "connected",
        PresentationConnectionState.Closed => "closed",
        _ => "terminated",
    };

    /// <summary>A closed connection of the window is connected again by <c>reconnect()</c>.</summary>
    internal void Reconnect()
    {
        State = PresentationConnectionState.Connecting;
        if (Session is { } session)
        {
            Establish(session);
        }
        else
        {
            StartClosing(PresentationConnectionCloseReason.Error, "The presentation was not started.");
        }
    }

    // https://w3c.github.io/presentation-api/#dfn-establish-a-presentation-connection
    internal void Establish(PresentationSession session)
    {
        Session = session;
        session.AddConnection(this);
        if (State != PresentationConnectionState.Connecting)
            return;

        Task<PresentationChannel> connect;
        try
        {
            connect = session.ConnectAsync(_state.CancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            connect = Task.FromException<PresentationChannel>(exception);
        }

        _ = connect.ContinueWith(
            task => Global.QueueGlobalTask(() =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    StartClosing(PresentationConnectionCloseReason.Error, "The presentation connection could not be established.");
                    return;
                }

                if (State != PresentationConnectionState.Connecting)
                {
                    task.Result.Close(PresentationConnectionCloseReason.Closed, "");
                    return;
                }

                SetConnected(task.Result);
                FireEvent(new Event("connect"));
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>The connection uses a channel, and is connected.</summary>
    internal void SetConnected(PresentationChannel channel)
    {
        _channel = channel;
        _closeTaskQueued = false;
        State = PresentationConnectionState.Connected;
        channel.Attach(this);
    }

    // https://w3c.github.io/presentation-api/#dfn-start-closing-a-presentation-connection
    internal void StartClosing(PresentationConnectionCloseReason reason, string message)
    {
        if (State is not (PresentationConnectionState.Connecting or PresentationConnectionState.Connected))
            return;

        State = PresentationConnectionState.Closed;
        var channel = _channel;
        _channel = null;
        try
        {
            channel?.Close(reason, message);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The connection is closed anyway
        }

        if (reason != PresentationConnectionCloseReason.WentAway)
        {
            RunCloseSteps(reason, message);
        }
    }

    /// <summary>A message of the other side. Called from any thread.</summary>
    internal void OnChannelMessage(object message) => Global.QueueGlobalTask(() =>
    {
        // https://w3c.github.io/presentation-api/#receiving-a-message-through-presentationconnection
        if (State != PresentationConnectionState.Connected)
            return;

        var data = message is byte[] bytes && BinaryType == BinaryType.Blob ? new Blob([bytes]) : message;
        FireEvent(new MessageEvent("message", new MessageEventInit { Data = data }));
    });

    /// <summary>The other side closed the connection. Called from any thread.</summary>
    internal void OnClosedByOtherSide(PresentationConnectionCloseReason reason, string message) => Global.QueueGlobalTask(() =>
    {
        if (State is PresentationConnectionState.Terminated)
            return;

        _channel = null;
        RunCloseSteps(reason, message);
    });

    /// <summary>The host terminated the presentation. Called from any thread.</summary>
    internal void OnTerminatedByHost() => QueueTermination();

    /// <summary>The connection is terminated in a task of its window.</summary>
    internal void QueueTermination() => Global.QueueGlobalTask(() =>
    {
        if (State is not (PresentationConnectionState.Connected or PresentationConnectionState.Connecting))
            return;

        State = PresentationConnectionState.Terminated;
        _channel = null;
        FireEvent(new Event("terminate"));
    });

    /// <summary>The window is destroyed: the other side is told that the document went away.</summary>
    internal void OnDestroyed()
    {
        StartClosing(PresentationConnectionCloseReason.WentAway, "");
        _destroyed = true;
    }

    // https://w3c.github.io/presentation-api/#dfn-close-a-presentation-connection
    private void RunCloseSteps(PresentationConnectionCloseReason reason, string message)
    {
        if (_closeTaskQueued)
            return;

        _closeTaskQueued = true;
        Global.QueueGlobalTask(() =>
        {
            if (State is not (PresentationConnectionState.Connecting or PresentationConnectionState.Connected or PresentationConnectionState.Closed))
                return;

            State = PresentationConnectionState.Closed;
            if (_isReceiver)
            {
                _state.RemoveController(this);
            }

            FireEvent(new PresentationConnectionCloseEvent(reason, message));
        });
    }
}

/// <summary>The receiver of a receiving browsing context (https://w3c.github.io/presentation-api/#interface-presentationreceiver): the connections of the controllers.</summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationreceiver"/>
[IdlInterface("PresentationReceiver", "https://w3c.github.io/presentation-api/#dom-presentationreceiver")]
public sealed class PresentationReceiver
{
    private readonly GlobalPresentation _state;

    internal PresentationReceiver(GlobalPresentation state) => _state = state;

    /// <summary>
    /// Gets the list of the connections of the controllers (https://w3c.github.io/presentation-api/#dom-presentationreceiver-connectionlist):
    /// the task completes when the first controller is connected.
    /// </summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationreceiver-connectionlist"/>
    [IdlMember("connectionList", "https://w3c.github.io/presentation-api/#dom-presentationreceiver-connectionlist")]
    public Task<PresentationConnectionList> ConnectionList => _state.GetConnectionList();
}

/// <summary>
/// The connections of the controllers of a receiving browsing context (https://w3c.github.io/presentation-api/#interface-presentationconnectionlist):
/// a <c>connectionavailable</c> event is fired when a new controller connects.
/// </summary>
/// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionlist"/>
[IdlInterface("PresentationConnectionList", "https://w3c.github.io/presentation-api/#dom-presentationconnectionlist")]
public sealed class PresentationConnectionList : EventTarget
{
    private readonly GlobalPresentation _state;

    internal PresentationConnectionList(GlobalPresentation state) => _state = state;

    internal override GlobalScope? RelevantGlobal => _state.Global;

    /// <summary>Gets the connections that are not terminated.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionlist-connections"/>
    [IdlMember("connections", "https://w3c.github.io/presentation-api/#dom-presentationconnectionlist-connections")]
    public IReadOnlyList<PresentationConnection> Connections => _state.Controllers.Where(static connection => connection.State != PresentationConnectionState.Terminated).ToArray();

    /// <summary>Gets or sets the event handler of the <c>connectionavailable</c> events (<see cref="PresentationConnectionAvailableEvent"/>).</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-presentationconnectionlist-onconnectionavailable"/>
    [IdlMember("onconnectionavailable", "https://w3c.github.io/presentation-api/#dom-presentationconnectionlist-onconnectionavailable")]
    public Action<Event>? OnConnectionAvailable
    {
        get => GetEventHandler("connectionavailable");
        set => SetEventHandler("connectionavailable", value);
    }
}

/// <summary>
/// The presentation state of a window: its <see cref="Presentation"/> object, the availability objects of its requests, its
/// connections, and the receiver of a receiving browsing context.
/// </summary>
#pragma warning disable CA1001 // The cancellation source is canceled when the window is destroyed, and the host may still observe its token: it is not disposed (it allocates no handle)
internal sealed class GlobalPresentation
#pragma warning restore CA1001
{
    private readonly List<WeakReference<PresentationAvailability>> _availabilities = [];
    private readonly List<PresentationConnection> _connections = [];
    private readonly List<PresentationConnection> _controllers = [];
    private readonly CancellationTokenSource _destroyed = new();
    private PresentationReceiver? _receiver;
    private PresentationConnectionList? _monitor;
    private TaskCompletionSource<PresentationConnectionList>? _connectionListPromise;

    public GlobalPresentation(Window global)
    {
        Global = global;
        Presentation = new Presentation(this);
        Provider?.Register(this);
    }

    public Window Global { get; }

    public Presentation Presentation { get; }

    public PresentationProvider? Provider => Global.Options.Presentation?.Provider;

    public CancellationToken CancellationToken => _destroyed.Token;

    /// <summary>The set of presentation controllers of the receiving browsing context.</summary>
    public IReadOnlyList<PresentationConnection> Controllers => _controllers;

    public bool IsAvailable(IReadOnlyList<string> urls) => Provider?.Displays.Any(display => urls.Any(display.CanPresent)) ?? false;

    public void AddAvailability(PresentationAvailability availability)
    {
        _availabilities.RemoveAll(static reference => !reference.TryGetTarget(out _));
        _availabilities.Add(new WeakReference<PresentationAvailability>(availability));
    }

    public void AddConnection(PresentationConnection connection) => _connections.Add(connection);

    /// <summary>The displays of the provider changed: the availability objects are updated. Called from any thread.</summary>
    public void OnDisplaysChanged() => Global.QueueGlobalTask(() =>
    {
        foreach (var reference in _availabilities.ToArray())
        {
            if (reference.TryGetTarget(out var availability))
            {
                availability.Update(IsAvailable(availability.Urls));
            }
        }
    });

    /// <summary>The receiver of the window, when it is the top-level window of a receiving browsing context.</summary>
    public PresentationReceiver? GetReceiver()
    {
        if (Global.Options.Presentation?.Receiver is not { } context || Global.BrowsingContext is not { Parent: null, IsAuxiliary: false })
            return null;

        if (_receiver is null)
        {
            _receiver = new PresentationReceiver(this);
            context.Register(this);
        }

        return _receiver;
    }

    public Task<PresentationConnectionList> GetConnectionList()
    {
        if (_connectionListPromise is null)
        {
            _connectionListPromise = new TaskCompletionSource<PresentationConnectionList>();
            if (_monitor is not null)
            {
                _connectionListPromise.TrySetResult(_monitor);
            }
        }

        return _connectionListPromise.Task;
    }

    // https://w3c.github.io/presentation-api/#monitoring-incoming-presentation-connections. Called from any thread.
    public void OnIncomingConnection(PresentationChannel channel) => Global.QueueGlobalTask(() =>
    {
        if (Global.Options.Presentation?.Receiver is not { } context || _destroyed.IsCancellationRequested)
        {
            channel.Close(PresentationConnectionCloseReason.WentAway, "");
            return;
        }

        var connection = new PresentationConnection(this, context.Id, context.Url, isReceiver: true);
        connection.SetConnected(channel);
        _controllers.Add(connection);
        if (_monitor is null)
        {
            _monitor = new PresentationConnectionList(this);
            _connectionListPromise?.TrySetResult(_monitor);
            return;
        }

        _monitor.FireEvent(new PresentationConnectionAvailableEvent(connection));
    });

    public void RemoveController(PresentationConnection connection) => _controllers.Remove(connection);

    // https://w3c.github.io/presentation-api/#terminating-a-presentation-in-a-receiving-browsing-context
    public void TerminateAsReceiver()
    {
        foreach (var connection in _controllers.ToArray())
        {
            connection.QueueTermination();
        }

        try
        {
            Global.Options.Presentation?.Receiver?.Terminate();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The connections are terminated anyway
        }
    }

    /// <summary>The host terminated the presentation of the receiving browsing context. Called from any thread.</summary>
    public void OnReceiverTerminated() => Global.QueueGlobalTask(() =>
    {
        foreach (var connection in _controllers.ToArray())
        {
            connection.QueueTermination();
        }
    });

    /// <summary>The window is destroyed: the other side of its connections is told that the document went away.</summary>
    public void OnDestroyed()
    {
        _destroyed.Cancel();
        foreach (var connection in _connections.ToArray())
        {
            connection.OnDestroyed();
        }
    }
}

// https://w3c.github.io/presentation-api/#extensions-to-the-navigator-interface
public sealed partial class Navigator
{
    /// <summary>Gets the Presentation API of the window.</summary>
    /// <seealso href="https://w3c.github.io/presentation-api/#dom-navigator-presentation"/>
    [IdlMember("presentation", "https://w3c.github.io/presentation-api/#dom-navigator-presentation")]
    public Presentation Presentation => _window.GetPresentationState().Presentation;
}

public sealed partial class Window
{
    private GlobalPresentation? _presentationState;

    /// <summary>The presentation state of the window, created by the first access to <c>navigator.presentation</c>.</summary>
    internal GlobalPresentation GetPresentationState() => _presentationState ??= new GlobalPresentation(this);

    /// <summary>The window is unloaded: its presentation connections are closed.</summary>
    internal void ClosePresentationConnections() => _presentationState?.OnDestroyed();
}
