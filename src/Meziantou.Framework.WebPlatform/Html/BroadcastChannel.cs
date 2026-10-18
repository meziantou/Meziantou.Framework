namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A channel of messages between the windows and the workers of the same origin (https://html.spec.whatwg.org/multipage/web-messaging.html#broadcasting-to-other-browsing-contexts).
/// The messages are delivered to the channels with the same name of the browsing context group of the global object (the
/// windows of the top-level browsing context and of its nested browsing contexts, and their workers), through their event loops.
/// </summary>
/// <remarks>
/// The relevant global object of a channel is the current global object when it is created (e.g. the window or the worker
/// of the script that creates it); a channel created without current global object neither sends nor receives messages.
/// The messages are serialized by the script engine of the global object (structured clone); without script engine, only
/// the .NET primitive values and the serializable platform objects can be sent.
/// </remarks>
[IdlInterface("BroadcastChannel", "https://html.spec.whatwg.org/multipage/web-messaging.html#broadcastchannel")]
public sealed class BroadcastChannel : EventTarget
{
    private bool _closed;

    /// <summary>https://html.spec.whatwg.org/multipage/web-messaging.html#dom-broadcastchannel</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-broadcastchannel")]
    public BroadcastChannel(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        if (GetChannels() is { } channels)
        {
            lock (channels)
            {
                channels.Add(this);
            }
        }
    }

    /// <summary>Gets the channel name.</summary>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-broadcastchannel-name")]
    public string Name { get; }

    /// <summary>
    /// Sends a message to the other channels with the same name of the windows and the workers of the same origin
    /// (https://html.spec.whatwg.org/multipage/web-messaging.html#dom-broadcastchannel-postmessage).
    /// </summary>
    /// <param name="message">The message: a value of the script engine, a .NET primitive value or a serializable platform object, or null for <c>undefined</c>.</param>
    [IdlMember("postMessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-broadcastchannel-postmessage")]
    public void PostMessage(object? message)
    {
        if (_closed)
            throw new DomException("The channel is closed", "InvalidStateError");

        var global = RelevantGlobal;
        var serialized = PortMessage.Serialize(global, message, []);

        // https://wicg.github.io/nav-speculation/prerendering.html#delay-while-prerendering: the message of a prerendering
        // document is posted when the document is activated
        if (global is Window { Document: { Prerendering: true } prerenderingDocument })
        {
            prerenderingDocument.RunAfterPrerendering(() => PostSerializedMessage(global, serialized));
            return;
        }

        PostSerializedMessage(global, serialized);
    }

    private void PostSerializedMessage(GlobalScope? global, PortMessage serialized)
    {
        if (_closed)
            return;

        // The messages of a worker that is closing are not sent
        if (global is null or WorkerGlobalScope { Agent.IsClosing: true } || GetChannels() is not { } channels)
            return;

        var sourceOrigin = global.OriginRecord;

        // The destinations, in creation order: the other open channels of the same name whose global object can run
        // scripts (a fully active window, a worker that is not closing) and is same origin
        BroadcastChannel[] destinations;
        lock (channels)
        {
            destinations = [.. channels];
        }

        foreach (var destination in destinations)
        {
            if (ReferenceEquals(destination, this) || destination._closed || destination.Name != Name)
                continue;

            if (destination.RelevantGlobal is not { } destinationGlobal || !destinationGlobal.CanRunScript || !OriginRecord.IsSameOrigin(destinationGlobal.OriginRecord, sourceOrigin))
                continue;

            destinationGlobal.QueueGlobalTask(() => destination.Deliver(destinationGlobal, serialized, sourceOrigin.Serialize()));
        }
    }

    private void Deliver(GlobalScope global, PortMessage serialized, string origin)
    {
        if (_closed)
            return;

        if (!serialized.TryDeserialize(global, out var data, out _))
        {
            FireEvent(new MessageEvent("messageerror", new MessageEventInit { Origin = origin }));
            return;
        }

        FireEvent(new MessageEvent("message", new MessageEventInit { Data = data, Origin = origin }));
    }

    /// <summary>Closes the channel: it no longer sends nor receives messages (https://html.spec.whatwg.org/multipage/web-messaging.html#dom-broadcastchannel-close).</summary>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/web-messaging.html#dom-broadcastchannel-close")]
    public void Close()
    {
        _closed = true;
        if (GetChannels() is { } channels)
        {
            lock (channels)
            {
                channels.Remove(this);
            }
        }
    }

    /// <summary>Gets or sets the event handler of the <c>message</c> event.</summary>
    [IdlMember("onmessage", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-broadcastchannel-onmessage")]
    public Action<Event>? OnMessage
    {
        get => GetEventHandler("message");
        set => SetEventHandler("message", value);
    }

    /// <summary>Gets or sets the event handler of the <c>messageerror</c> event.</summary>
    [IdlMember("onmessageerror", "https://html.spec.whatwg.org/multipage/web-messaging.html#handler-broadcastchannel-onmessageerror")]
    public Action<Event>? OnMessageError
    {
        get => GetEventHandler("messageerror");
        set => SetEventHandler("messageerror", value);
    }

    /// <summary>The channels of the browsing context group of the relevant global object (shared by its workers), or null.</summary>
    private List<BroadcastChannel>? GetChannels() => RelevantGlobal is { } global ? GetChannels(global) : null;

    private static List<BroadcastChannel>? GetChannels(GlobalScope global) => global switch
    {
        Window window => window.BrowsingContext?.BroadcastChannels,
        WorkerGlobalScope worker => worker.Agent.Owner is { } owner ? GetChannels(owner) : worker.Agent.GroupBrowsingContext?.BroadcastChannels,
        _ => null,
    };

    /// <summary>Removes the channels of a global object (its document is destroyed, or its worker ends): they can no longer receive messages.</summary>
    internal static void RemoveChannels(GlobalScope global)
    {
        if (GetChannels(global) is not { } channels)
            return;

        lock (channels)
        {
            channels.RemoveAll(channel => ReferenceEquals(channel.RelevantGlobal, global));
        }
    }
}
