namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Presentation API (https://w3c.github.io/presentation-api/, <see cref="BrowsingContextOptions.Presentation"/>).</summary>
public sealed class PresentationOptions
{
    /// <summary>
    /// Gets or sets the presentation displays of the controlling user agent: a <see cref="PresentationProvider"/> to which the
    /// application adds the <see cref="PresentationDisplay"/> objects that start the presentations. A provider can be shared by
    /// several browsing contexts. The default is null: there is no presentation display.
    /// </summary>
    public PresentationProvider? Provider { get; init; }

    /// <summary>
    /// Gets or sets the presentation that the top-level browsing contexts present, when they are receiving browsing contexts
    /// (https://w3c.github.io/presentation-api/#dfn-receiving-browsing-context): <c>navigator.presentation.receiver</c> gives
    /// the connections of the controllers that <see cref="PresentationReceivingContext.Connect"/> adds. The default is null:
    /// the browsing contexts are not receiving browsing contexts.
    /// </summary>
    /// <remarks>
    /// The application creates the receiving browsing context with its own <see cref="BrowsingContextOptions"/> (an empty
    /// cookie store and storage, the permissions denied) and navigates it to <see cref="PresentationReceivingContext.Url"/>,
    /// as https://w3c.github.io/presentation-api/#creating-a-receiving-browsing-context describes.
    /// </remarks>
    public PresentationReceivingContext? Receiver { get; init; }
}

/// <summary>
/// The presentation displays of a controlling user agent, used by the windows of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/> (<see cref="PresentationOptions.Provider"/>): the displays are available to the
/// presentation requests whose URLs they can present (<c>PresentationAvailability</c>), and
/// <see cref="RequestDisplay"/> plays the role of the display chooser of <c>PresentationRequest.start()</c>. The provider
/// also keeps the presentations that the windows started (the set of controlled presentations), which
/// <c>PresentationRequest.reconnect()</c> searches.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class PresentationProvider
{
    private readonly Lock _lock = new();
    private readonly List<PresentationDisplay> _displays = [];
    private readonly List<WeakReference<GlobalPresentation>> _globals = [];
    private readonly List<PresentationConnection> _controlledPresentations = [];

    /// <summary>Gets the available displays, in the order they were added.</summary>
    public IReadOnlyList<PresentationDisplay> Displays
    {
        get
        {
            lock (_lock)
            {
                return [.. _displays];
            }
        }
    }

    /// <summary>Adds a display: the <c>PresentationAvailability</c> objects whose URLs it can present become available. Can be called from any thread.</summary>
    /// <param name="display">The display.</param>
    public void AddDisplay(PresentationDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        lock (_lock)
        {
            if (_displays.Contains(display))
                return;

            _displays.Add(display);
        }

        OnDisplaysChanged();
    }

    /// <summary>Removes a display. The presentations that it shows are not terminated. Can be called from any thread.</summary>
    /// <param name="display">The display.</param>
    /// <returns>Whether the display was available.</returns>
    public bool RemoveDisplay(PresentationDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        lock (_lock)
        {
            if (!_displays.Remove(display))
                return false;
        }

        OnDisplaysChanged();
        return true;
    }

    /// <summary>
    /// Asks the user to choose a display for <c>PresentationRequest.start()</c> (https://w3c.github.io/presentation-api/#selecting-a-presentation-display):
    /// <see cref="PresentationDisplayRequest.Displays"/> are the available displays that can present one of the URLs of the
    /// request. Returns one of them, or null when the user denies the permission (the request fails with a
    /// <c>NotAllowedError</c>). The default implementation returns null.
    /// </summary>
    /// <param name="request">The request of the window.</param>
    public virtual Task<PresentationDisplay?> RequestDisplay(PresentationDisplayRequest request) => Task.FromResult<PresentationDisplay?>(null);

    /// <summary>Registers the presentation state of a window, which then gets the changes of the displays.</summary>
    internal void Register(GlobalPresentation global)
    {
        lock (_lock)
        {
            _globals.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _globals.Add(new WeakReference<GlobalPresentation>(global));
        }
    }

    /// <summary>Adds a connection to the set of controlled presentations (https://w3c.github.io/presentation-api/#dfn-set-of-controlled-presentations).</summary>
    internal void AddControlledPresentation(PresentationConnection connection)
    {
        lock (_lock)
        {
            _controlledPresentations.Add(connection);
        }
    }

    /// <summary>The connections of the set of controlled presentations.</summary>
    internal PresentationConnection[] GetControlledPresentations()
    {
        lock (_lock)
        {
            _controlledPresentations.RemoveAll(static connection => connection.State == PresentationConnectionState.Terminated || connection.IsGlobalDestroyed);
            return [.. _controlledPresentations];
        }
    }

    private void OnDisplaysChanged()
    {
        List<GlobalPresentation> globals = [];
        lock (_lock)
        {
            foreach (var reference in _globals)
            {
                if (reference.TryGetTarget(out var global))
                {
                    globals.Add(global);
                }
            }
        }

        foreach (var global in globals)
        {
            global.OnDisplaysChanged();
        }
    }
}

/// <summary>A request of <c>PresentationRequest.start()</c>, given to <see cref="PresentationProvider.RequestDisplay"/>.</summary>
public sealed class PresentationDisplayRequest
{
    internal PresentationDisplayRequest(CallbackContext context, IReadOnlyList<string> urls, IReadOnlyList<PresentationDisplay> displays)
    {
        Context = context;
        Urls = urls;
        Displays = displays;
    }

    /// <summary>Gets the context of the window that starts the presentation.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the presentation request URLs, in the order of the page.</summary>
    public IReadOnlyList<string> Urls { get; }

    /// <summary>Gets the displays to choose from: the available displays that can present one of the URLs.</summary>
    public IReadOnlyList<PresentationDisplay> Displays { get; }
}

/// <summary>
/// A presentation display (https://w3c.github.io/presentation-api/#dfn-presentation-display) of a
/// <see cref="PresentationProvider"/>: the URLs it can present, and <see cref="StartAsync"/>, which creates the receiving
/// browsing context of a new presentation.
/// </summary>
/// <remarks>The members can be called from any thread.</remarks>
public abstract class PresentationDisplay
{
    /// <summary>Creates a display.</summary>
    /// <param name="name">The name of the display, for the display chooser.</param>
    protected PresentationDisplay(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets the name of the display.</summary>
    public string Name { get; }

    /// <summary>
    /// Returns whether the display can present a presentation request URL (an available presentation display for the URL,
    /// https://w3c.github.io/presentation-api/#dfn-available-presentation-display). The default implementation returns true.
    /// </summary>
    /// <param name="url">The absolute URL.</param>
    public virtual bool CanPresent(string url) => true;

    /// <summary>
    /// Starts a presentation on the display (https://w3c.github.io/presentation-api/#dfn-start-a-presentation-connection):
    /// creates the receiving browsing context of <see cref="PresentationStartRequest.Url"/> (see
    /// <see cref="PresentationOptions.Receiver"/>). The connection of the controlling window is then established with
    /// <see cref="PresentationSession.ConnectAsync"/>. When the task fails, the connection is closed with the <c>error</c> reason.
    /// </summary>
    /// <param name="request">The URL and the identifier of the presentation.</param>
    /// <param name="cancellationToken">A token that is canceled when the controlling window is destroyed.</param>
    public abstract Task<PresentationSession> StartAsync(PresentationStartRequest request, CancellationToken cancellationToken);
}

/// <summary>The presentation that a <see cref="PresentationDisplay"/> starts (<see cref="PresentationDisplay.StartAsync"/>).</summary>
public sealed class PresentationStartRequest
{
    internal PresentationStartRequest(CallbackContext context, string url, string id)
    {
        Context = context;
        Url = url;
        Id = id;
    }

    /// <summary>Gets the context of the controlling window.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the presentation URL: the first URL of the request that the display can present.</summary>
    public string Url { get; }

    /// <summary>Gets the presentation identifier (a UUID without dashes).</summary>
    public string Id { get; }
}

/// <summary>
/// A presentation that a <see cref="PresentationDisplay"/> shows: <see cref="ConnectAsync"/> establishes the connections of
/// the controlling windows (one for <c>start()</c>, one for each <c>reconnect()</c> of another window), and
/// <see cref="Terminate"/> and <see cref="OnTerminated"/> end the presentation.
/// </summary>
/// <remarks>The members can be called from any thread.</remarks>
public abstract class PresentationSession
{
    private readonly Lock _lock = new();
    private readonly List<PresentationConnection> _connections = [];
    private bool _terminated;

    /// <summary>Creates a presentation.</summary>
    /// <param name="url">The presentation URL.</param>
    /// <param name="id">The presentation identifier.</param>
    protected PresentationSession(string url, string id)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(id);
        Url = url;
        Id = id;
    }

    /// <summary>Gets the presentation URL.</summary>
    public string Url { get; }

    /// <summary>Gets the presentation identifier.</summary>
    public string Id { get; }

    /// <summary>Gets whether the presentation is terminated.</summary>
    public bool IsTerminated
    {
        get
        {
            lock (_lock)
            {
                return _terminated;
            }
        }
    }

    /// <summary>
    /// Establishes a connection of a controlling window to the receiving browsing context (https://w3c.github.io/presentation-api/#dfn-establish-a-presentation-connection):
    /// returns the channel of the controlling side, whose other end is given to <see cref="PresentationReceivingContext.Connect"/>
    /// (<see cref="PresentationChannel.CreatePair"/> creates two channels connected in memory). When the task fails, the
    /// connection is closed with the <c>error</c> reason.
    /// </summary>
    /// <param name="cancellationToken">A token that is canceled when the controlling window is destroyed.</param>
    public abstract Task<PresentationChannel> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Terminates the presentation at the request of a controlling window (<c>PresentationConnection.terminate()</c>,
    /// https://w3c.github.io/presentation-api/#terminating-a-presentation-in-a-controlling-browsing-context): the
    /// implementation closes the receiving browsing context. The connections of the controlling windows are already
    /// terminated. The default implementation calls <see cref="OnTerminated"/>.
    /// </summary>
    public virtual void Terminate() => OnTerminated();

    /// <summary>
    /// Reports that the presentation is terminated (the receiving browsing context terminated it, or it was closed,
    /// https://w3c.github.io/presentation-api/#handling-a-termination-confirmation-in-a-controlling-user-agent): the
    /// connections of the controlling windows that are connecting or connected become <c>terminated</c> and get a
    /// <c>terminate</c> event.
    /// </summary>
    public void OnTerminated()
    {
        PresentationConnection[] connections;
        lock (_lock)
        {
            _terminated = true;
            connections = [.. _connections];
        }

        foreach (var connection in connections)
        {
            connection.OnTerminatedByHost();
        }
    }

    internal void AddConnection(PresentationConnection connection)
    {
        lock (_lock)
        {
            _connections.RemoveAll(static connection => connection.State == PresentationConnectionState.Terminated);
            _connections.Add(connection);
        }
    }
}

/// <summary>
/// The receiving browsing context of a presentation (https://w3c.github.io/presentation-api/#dfn-receiving-browsing-context),
/// given to <see cref="PresentationOptions.Receiver"/>: <see cref="Connect"/> adds the connections of the controllers, which
/// the page gets with <c>navigator.presentation.receiver.connectionList</c>.
/// </summary>
/// <remarks>The members can be called from any thread.</remarks>
public class PresentationReceivingContext
{
    private readonly Lock _lock = new();
    private readonly List<PresentationChannel> _pendingChannels = [];
    private WeakReference<GlobalPresentation>? _global;

    /// <summary>Creates a receiving browsing context.</summary>
    /// <param name="url">The presentation URL.</param>
    /// <param name="id">The presentation identifier.</param>
    public PresentationReceivingContext(string url, string id)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(id);
        Url = url;
        Id = id;
    }

    /// <summary>Gets the presentation URL.</summary>
    public string Url { get; }

    /// <summary>Gets the presentation identifier.</summary>
    public string Id { get; }

    /// <summary>
    /// Adds the connection of a controller (https://w3c.github.io/presentation-api/#monitoring-incoming-presentation-connections):
    /// the page gets a connected <c>PresentationConnection</c> in <c>connectionList</c>, and a <c>connectionavailable</c>
    /// event for the connections after the first one. The connections that arrive before the page reads
    /// <c>navigator.presentation.receiver</c> wait for it.
    /// </summary>
    /// <param name="channel">The channel of the receiving side.</param>
    public void Connect(PresentationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        GlobalPresentation? global;
        lock (_lock)
        {
            if (_global is null || !_global.TryGetTarget(out global))
            {
                _pendingChannels.Add(channel);
                return;
            }
        }

        global.OnIncomingConnection(channel);
    }

    /// <summary>
    /// Terminates the presentation at the request of the page (<c>PresentationConnection.terminate()</c> in the receiving
    /// browsing context, https://w3c.github.io/presentation-api/#terminating-a-presentation-in-a-receiving-browsing-context):
    /// the implementation closes the receiving browsing context and calls <see cref="PresentationSession.OnTerminated"/> for the
    /// controllers. The default implementation calls <see cref="OnTerminated"/>.
    /// </summary>
    public virtual void Terminate() => OnTerminated();

    /// <summary>Reports that the presentation is terminated: the connections of the controllers become <c>terminated</c> and get a <c>terminate</c> event.</summary>
    public void OnTerminated()
    {
        GlobalPresentation? global = null;
        lock (_lock)
        {
            _pendingChannels.Clear();
            _global?.TryGetTarget(out global);
        }

        global?.OnReceiverTerminated();
    }

    /// <summary>The window of the receiving browsing context reads its receiver: it gets the connections that wait for it.</summary>
    internal void Register(GlobalPresentation global)
    {
        PresentationChannel[] pending;
        lock (_lock)
        {
            _global = new WeakReference<GlobalPresentation>(global);
            pending = [.. _pendingChannels];
            _pendingChannels.Clear();
        }

        foreach (var channel in pending)
        {
            global.OnIncomingConnection(channel);
        }
    }
}

/// <summary>
/// The message channel of one presentation connection, on one side (https://w3c.github.io/presentation-api/#sending-a-message-through-presentationconnection):
/// <see cref="Send(string)"/> and <see cref="Send(ReadOnlyMemory{byte})"/> transmit the messages of the page to the other
/// side, and the implementation gives the messages of the other side to <see cref="Receive(string)"/> and
/// <see cref="Receive(ReadOnlyMemory{byte})"/>. <see cref="CreatePair"/> creates two channels connected in memory.
/// </summary>
/// <remarks>The members can be called from any thread.</remarks>
public abstract class PresentationChannel
{
    private readonly Lock _lock = new();
    private readonly List<Action<PresentationConnection>> _pending = [];
    private PresentationConnection? _connection;

    /// <summary>Transmits a text message to the other side.</summary>
    /// <param name="message">The message.</param>
    public abstract void Send(string message);

    /// <summary>Transmits a binary message to the other side.</summary>
    /// <param name="data">The message.</param>
    public abstract void Send(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Signals the other side that the page closed the connection (https://w3c.github.io/presentation-api/#dfn-start-closing-a-presentation-connection):
    /// the implementation gives the reason and the message to <see cref="OnClosed"/> of the other end.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <param name="message">The message (empty unless the reason is <c>error</c>).</param>
    public abstract void Close(PresentationConnectionCloseReason reason, string message);

    /// <summary>Gives a text message of the other side to the page (a <c>message</c> event of the connection).</summary>
    /// <param name="message">The message.</param>
    public void Receive(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Notify(connection => connection.OnChannelMessage(message));
    }

    /// <summary>Gives a binary message of the other side to the page (a <c>message</c> event of the connection).</summary>
    /// <param name="data">The message.</param>
    public void Receive(ReadOnlyMemory<byte> data)
    {
        var bytes = data.ToArray();
        Notify(connection => connection.OnChannelMessage(bytes));
    }

    /// <summary>Reports that the other side closed the connection: the connection is closed with this reason and message (a <c>close</c> event).</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="message">The message.</param>
    public void OnClosed(PresentationConnectionCloseReason reason, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Notify(connection => connection.OnClosedByOtherSide(reason, message));
    }

    /// <summary>Creates two channels connected in memory: what one sends, the other receives.</summary>
    public static (PresentationChannel Controller, PresentationChannel Receiver) CreatePair()
    {
        var controller = new InMemoryChannel();
        var receiver = new InMemoryChannel();
        controller.Other = receiver;
        receiver.Other = controller;
        return (controller, receiver);
    }

    /// <summary>The connection that uses the channel: the notifications received before it are given to it.</summary>
    internal void Attach(PresentationConnection connection)
    {
        Action<PresentationConnection>[] pending;
        lock (_lock)
        {
            _connection = connection;
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (var action in pending)
        {
            action(connection);
        }
    }

    private void Notify(Action<PresentationConnection> action)
    {
        PresentationConnection? connection;
        lock (_lock)
        {
            connection = _connection;
            if (connection is null)
            {
                _pending.Add(action);
                return;
            }
        }

        action(connection);
    }

    private sealed class InMemoryChannel : PresentationChannel
    {
        public InMemoryChannel? Other { get; set; }

        public override void Send(string message) => Other?.Receive(message);

        public override void Send(ReadOnlyMemory<byte> data) => Other?.Receive(data);

        public override void Close(PresentationConnectionCloseReason reason, string message) => Other?.OnClosed(reason, message);
    }
}
