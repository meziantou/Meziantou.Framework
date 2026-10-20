using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A channel of two entangled message ports (https://html.spec.whatwg.org/multipage/web-messaging.html#message-channels):
/// a message posted to one port is delivered to the other one.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#message-channels"/>
[IdlInterface("MessageChannel", "https://html.spec.whatwg.org/multipage/web-messaging.html#message-channels")]
public sealed class MessageChannel
{
    /// <summary>https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messagechannel</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messagechannel"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messagechannel")]
    public MessageChannel()
    {
        Port1 = new MessagePort();
        Port2 = new MessagePort();
        MessagePort.Entangle(Port1, Port2);
    }

    /// <summary>Gets the first port of the channel, entangled with <see cref="Port2"/>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messagechannel-port1"/>
    [IdlMember("port1", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messagechannel-port1")]
    public MessagePort Port1 { get; }

    /// <summary>Gets the second port of the channel, entangled with <see cref="Port1"/>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messagechannel-port2"/>
    [IdlMember("port2", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messagechannel-port2")]
    public MessagePort Port2 { get; }
}

/// <summary>
/// A message port (https://html.spec.whatwg.org/multipage/web-messaging.html#message-ports): the messages posted to a port
/// are delivered to its entangled port, through the event loop of the relevant global object of the entangled port, once
/// its port message queue is enabled (by <see cref="Start"/> or by setting <see cref="OnMessage"/>). The entangled ports may
/// belong to different agents (e.g. a window and a worker): the ports can be used from the threads of their agents.
/// </summary>
/// <remarks>
/// The relevant global object of a port is the current global object when it is created (e.g. the window of the script
/// that creates the channel); the messages sent to a port without relevant global object are not delivered. The messages
/// are serialized by the script engine of the global object (structured clone); without script engine, only the .NET
/// primitive values and the serializable platform objects can be sent. A port can be transferred to another realm (the
/// transfer list of <see cref="PostMessage(object?, StructuredSerializeOptions?)"/>, <see cref="Worker.PostMessage(object?, StructuredSerializeOptions?)"/>...).
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#message-ports"/>
[IdlInterface("MessagePort", "https://html.spec.whatwg.org/multipage/web-messaging.html#message-ports")]
public sealed class MessagePort : EventTarget, IStructuredTransferable
{
    // The state of the entangled ports, shared by the agents
    private static readonly Lock StateLock = new();

    // The port message queue (https://html.spec.whatwg.org/multipage/web-messaging.html#port-message-queue)
    private readonly Queue<PortMessage> _messages = new();
    private bool _enabled;
    private bool _deliveryQueued;
    private bool _detached;
    private bool _closed;

    // The entanglement of the port with another port (https://html.spec.whatwg.org/multipage/web-messaging.html#entangle):
    // the two sides of a channel, whose holders change when a port is transferred
    private Entanglement? _entanglement;
    private int _side;

    /// <summary>The entangled port (the current holder of the other side of the channel), or null.</summary>
    private MessagePort? EntangledPortLocked => _entanglement?.Holders[1 - _side];

    // The port that posts a message on the current thread, to check its transfer list
    [ThreadStatic]
    private static PostingState? s_posting;

    private sealed class PostingState(MessagePort source, MessagePort? target)
    {
        public MessagePort Source { get; } = source;

        public MessagePort? Target { get; } = target;

        public bool Doomed { get; set; }
    }

    internal MessagePort()
    {
    }

    /// <summary>
    /// The object at which the message events are fired (https://html.spec.whatwg.org/multipage/web-messaging.html#message-event-target):
    /// the <see cref="Worker"/> object or the <see cref="DedicatedWorkerGlobalScope"/> of the implicit ports of a dedicated
    /// worker, else the port.
    /// </summary>
    internal EventTarget? MessageEventTarget { get; set; }

    /// <summary>Sets the relevant global object of a port created before its global object (the inside port of a worker).</summary>
    internal void SetRelevantGlobal(GlobalScope global)
    {
        SetRelevantGlobalCore(global);
        lock (StateLock)
        {
            QueueDeliveryLocked();
        }
    }

    internal static void Entangle(MessagePort port1, MessagePort port2)
    {
        lock (StateLock)
        {
            var entanglement = new Entanglement();
            entanglement.Holders[0] = port1;
            entanglement.Holders[1] = port2;
            port1._entanglement = entanglement;
            port1._side = 0;
            port2._entanglement = entanglement;
            port2._side = 1;
        }
    }

    /// <summary>
    /// Posts a message to the entangled port (https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-postmessage).
    /// </summary>
    /// <param name="message">The message: a value of the script engine, a .NET primitive value or a serializable platform object, or null for <c>undefined</c>.</param>
    /// <param name="transfer">The objects to transfer (the <c>ArrayBuffer</c> objects of the script engine, and the message ports).</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-postmessage"/>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-postmessage")]
    public void PostMessage(object? message, IReadOnlyList<object> transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        PostMessage(message, new StructuredSerializeOptions { Transfer = transfer });
    }

    /// <summary>
    /// Posts a message to the entangled port (https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-postmessage-options).
    /// </summary>
    /// <param name="message">The message: a value of the script engine, a .NET primitive value or a serializable platform object, or null for <c>undefined</c>.</param>
    /// <param name="options">The objects to transfer.</param>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-postmessage-options"/>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-postmessage-options")]
    public void PostMessage(object? message, StructuredSerializeOptions? options = null) => PostMessageCore(message, options?.Transfer ?? [], source: null);

    /// <summary>https://html.spec.whatwg.org/multipage/web-messaging.html#message-port-post-message-steps</summary>
    internal void PostMessageCore(object? message, IReadOnlyList<object> transfer, EventTarget? source)
    {
        MessagePort? targetPort;
        lock (StateLock)
        {
            targetPort = EntangledPortLocked;
        }

        var doomed = false;
        foreach (var item in transfer)
        {
            if (ReferenceEquals(item, this))
                throw new DomException("The source port cannot be transferred", "DataCloneError");

            if (targetPort is not null && ReferenceEquals(item, targetPort))
            {
                doomed = true;
            }
        }

        // The transfer list contains values of the script engine: the ports are checked by their transfer steps
        PortMessage serialized;
        var previousPosting = s_posting;
        s_posting = new PostingState(this, targetPort);
        try
        {
            serialized = PortMessage.Serialize(RelevantGlobal, message, transfer, source);
            doomed |= s_posting.Doomed;
        }
        finally
        {
            s_posting = previousPosting;
        }

        if (doomed)
            return;

        lock (StateLock)
        {
            // The message is buffered by the entanglement while the other port is being transferred
            if (_entanglement is { } entanglement)
            {
                var otherSide = 1 - _side;
                if (entanglement.Holders[otherSide] is { } otherPort)
                {
                    otherPort.EnqueueLocked(serialized);
                }
                else if (!entanglement.Closed)
                {
                    entanglement.Pending[otherSide].Add(serialized);
                }
            }
        }
    }

    /// <summary>Adds a message to the port message queue (or to the queue of the port it was transferred to).</summary>
    private void EnqueueLocked(PortMessage message)
    {
        if (_closed)
            return;

        _messages.Enqueue(message);
        QueueDeliveryLocked();
    }

    /// <summary>Queues a task to deliver the next message, when the port message queue is enabled.</summary>
    private void QueueDeliveryLocked()
    {
        if (!_enabled || _deliveryQueued || _detached || _messages.Count == 0 || RelevantGlobal is not { } global)
            return;

        _deliveryQueued = true;
        global.QueueGlobalTask(DeliverNextMessage);
    }

    private void DeliverNextMessage()
    {
        PortMessage? message;
        lock (StateLock)
        {
            _deliveryQueued = false;
            if (_detached || !_messages.TryDequeue(out message))
                return;

            QueueDeliveryLocked();
        }

        Deliver(message);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/web-messaging.html#message-port-post-message-steps (the task of step 7)</summary>
    private void Deliver(PortMessage message)
    {
        if (RelevantGlobal is not { } global || !global.CanRunScript)
            return;

        var target = MessageEventTarget ?? this;
        if (!message.TryDeserialize(global, out var data, out var ports))
        {
            target.FireEvent(new MessageEvent("messageerror"));
            return;
        }

        target.FireEvent(new MessageEvent("message", new MessageEventInit { Data = data, Ports = ports, Source = message.Source as MessagePort }));
    }

    /// <summary>Enables the port message queue: the pending messages are delivered (https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-start).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-start"/>
    [IdlMember("start", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-start")]
    public void Start()
    {
        lock (StateLock)
        {
            if (_enabled)
                return;

            _enabled = true;
            QueueDeliveryLocked();
        }
    }

    /// <summary>Closes the port: it is disentangled, and a close event is fired at the other port (https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-close).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-close"/>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-messageport-close")]
    public void Close()
    {
        lock (StateLock)
        {
            _closed = true;
            _detached = true;
        }

        Disentangle();
    }

    /// <summary>Discards the messages of the port message queue and the next ones (e.g. the outside port of a terminated worker).</summary>
    internal void DiscardMessages()
    {
        lock (StateLock)
        {
            _detached = true;
            _messages.Clear();
        }
    }

    /// <summary>
    /// Disentangles the port (https://html.spec.whatwg.org/multipage/web-messaging.html#disentangle): a close event is fired at
    /// the other port (e.g. when the port is closed, or when its worker ends).
    /// </summary>
    internal void Disentangle()
    {
        MessagePort? otherPort;
        lock (StateLock)
        {
            _detached = true;
            if (_entanglement is not { } entanglement)
                return;

            otherPort = EntangledPortLocked;
            entanglement.Closed = true;
            entanglement.Holders[0] = null;
            entanglement.Holders[1] = null;
            _entanglement = null;
            if (otherPort is not null)
            {
                otherPort._entanglement = null;
            }
        }

        otherPort?.RelevantGlobal?.QueueGlobalTask(() => otherPort.FireEvent(new Event("close")));
    }

    /// <summary>Gets or sets the handler of the message event; setting it enables the port message queue.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageport-onmessage"/>
    [IdlMember("onmessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageport-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>Gets or sets the event handler of the <c>messageerror</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageport-onmessageerror"/>
    [IdlMember("onmessageerror", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageport-onmessageerror")]
    public Action<Event>? OnMessageError
    {
        get => GetEventHandler("messageerror");
        set => SetEventHandler("messageerror", value);
    }

    /// <summary>Gets or sets the event handler of the <c>close</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageport-onclose"/>
    [IdlMember("onclose", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageport-onclose")]
    public Action<Event>? OnClose
    {
        get => GetEventHandler("close");
        set => SetEventHandler("close", value);
    }

    // The first time onmessage is set, the port message queue is enabled (https://html.spec.whatwg.org/multipage/web-messaging.html#handler-messageport-onmessage)
    private protected override void OnEventHandlerSet(string type)
    {
        if (type == "message")
        {
            Start();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Transfer (https://html.spec.whatwg.org/multipage/web-messaging.html#message-ports, the transfer steps)
    // ----------------------------------------------------------------------------------------------------------------

    void IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        // https://html.spec.whatwg.org/multipage/web-messaging.html#message-port-post-message-steps (steps 2–3), before the
        // serialization
        if (!serialized && s_posting is { } posting)
        {
            if (ReferenceEquals(posting.Source, this))
                throw new DomException("The source port cannot be transferred", "DataCloneError");

            if (ReferenceEquals(posting.Target, this))
            {
                posting.Doomed = true;
            }
        }

        // https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer (step 5), after the
        // serialization: the port may have been transferred by the getters of the message
        if (!serialized)
            return;

        lock (StateLock)
        {
            if (_detached)
                throw new DomException("A detached MessagePort could not be transferred", "DataCloneError");
        }
    }

    ITransferredData IStructuredTransferable.Transfer()
    {
        lock (StateLock)
        {
            if (_detached)
                throw new DomException("A detached MessagePort could not be transferred", "DataCloneError");

            // The port message queue moves to the new port; the messages sent meanwhile are buffered by the entanglement
            var data = new MessagePortTransferData(_entanglement, _side, [.. _messages]);
            if (_entanglement is { } entanglement)
            {
                entanglement.Holders[_side] = null;
            }

            _messages.Clear();
            _entanglement = null;
            _detached = true;
            return data;
        }
    }

    /// <summary>The two sides of a channel: the ports that hold them, and the messages sent to a side while its port is transferred.</summary>
    private sealed class Entanglement
    {
        public MessagePort?[] Holders { get; } = new MessagePort?[2];

        public List<PortMessage>[] Pending { get; } = [[], []];

        public bool Closed { get; set; }
    }

    /// <summary>The data of a transferred port: its side of the channel and its port message queue.</summary>
    private sealed class MessagePortTransferData(Entanglement? entanglement, int side, List<PortMessage> messages) : ITransferredData
    {
        private MessagePort? _receiver;

        /// <summary>The transfer-receiving steps: a new port in the realm of the target, which holds the side of the channel.</summary>
        public object Receive(GlobalScope? target)
        {
            lock (StateLock)
            {
                if (_receiver is not null)
                    return _receiver;
            }

            var port = new MessagePort();
            if (target is not null)
            {
                port.SetRelevantGlobalCore(target);
            }

            lock (StateLock)
            {
                if (_receiver is not null)
                    return _receiver;

                _receiver = port;
                foreach (var message in messages)
                {
                    port._messages.Enqueue(message);
                }

                messages.Clear();
                if (entanglement is { Closed: false })
                {
                    port._entanglement = entanglement;
                    port._side = side;
                    entanglement.Holders[side] = port;
                    foreach (var message in entanglement.Pending[side])
                    {
                        port._messages.Enqueue(message);
                    }

                    entanglement.Pending[side].Clear();
                }
            }

            return port;
        }
    }
}

/// <summary>A message of a port message queue: the serialized message (for any agent) and the data of the transferred ports.</summary>
internal sealed class PortMessage
{
    private PortMessage(object? serialized, IReadOnlyList<ITransferredData> transferred, EventTarget? source)
    {
        Serialized = serialized;
        Transferred = transferred;
        Source = source;
    }

    public object? Serialized { get; }

    public IReadOnlyList<ITransferredData> Transferred { get; }

    /// <summary>The source of the message event (e.g. the port of a shared worker connection), or null.</summary>
    public EventTarget? Source { get; }

    /// <summary>
    /// StructuredSerializeWithTransfer (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer)
    /// in the realm of <paramref name="global"/>, for another agent, or for the realms of the same script engine when
    /// <paramref name="sameAgent"/> is true (e.g. a message posted to a window).
    /// </summary>
    public static PortMessage Serialize(GlobalScope? global, object? message, IReadOnlyList<object> transfer, EventTarget? source = null, bool sameAgent = false)
    {
        using var collector = StructuredClone.CollectTransfers();
        object? serialized;
        if (global?.ScriptEngine is { } engine)
        {
            serialized = sameAgent ? engine.StructuredSerialize(global, message, transfer) : engine.StructuredSerializeForAgent(global, message, transfer);
        }
        else
        {
            // Without script engine, only the message ports of the transfer list can be transferred
            foreach (var item in transfer)
            {
                if (item is not IStructuredTransferable transferable)
                    throw new DomException("Transferring objects is not supported", "DataCloneError");

                transferable.ValidateTransfer(serialized: false);
            }

            serialized = StructuredClone.Serialize(message);
            foreach (var item in transfer)
            {
                ((IStructuredTransferable)item).ValidateTransfer(serialized: true);
            }

            foreach (var item in transfer)
            {
                StructuredClone.Transfer((IStructuredTransferable)item);
            }
        }

        return new PortMessage(serialized, [.. collector.Transferred], source);
    }

    /// <summary>
    /// StructuredDeserializeWithTransfer (https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserializewithtransfer)
    /// in the realm of <paramref name="global"/>: returns false when the message cannot be deserialized (a <c>messageerror</c> event).
    /// </summary>
    public bool TryDeserialize(GlobalScope global, out object? data, out IReadOnlyList<MessagePort> ports)
    {
        using var scope = StructuredClone.EnterDeserialization(global);
        try
        {
            data = global.ScriptEngine is { } engine ? engine.StructuredDeserialize(global, Serialized) : StructuredClone.Deserialize(Serialized);
        }
#pragma warning disable CA1031 // A deserialization error fires a messageerror event
        catch (Exception)
#pragma warning restore CA1031
        {
            data = null;
            ports = [];
            return false;
        }

        // The transferred ports that are not referenced by the message are also received (MessageEvent.ports)
        var result = new List<MessagePort>(Transferred.Count);
        foreach (var transferred in Transferred)
        {
            if (transferred.Receive(global) is MessagePort port)
            {
                result.Add(port);
            }
        }

        ports = result;
        return true;
    }
}
