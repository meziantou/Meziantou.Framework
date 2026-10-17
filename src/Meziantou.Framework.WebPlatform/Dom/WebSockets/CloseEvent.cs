namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The initialization dictionary of a <see cref="CloseEvent"/> (https://websockets.spec.whatwg.org/#dictdef-closeeventinit).</summary>
[IdlInterface("CloseEventInit", "https://websockets.spec.whatwg.org/#dictdef-closeeventinit")]
public sealed class CloseEventInit : EventInit
{
    /// <summary>Gets whether the connection closed cleanly.</summary>
    [IdlMember("wasClean", "https://websockets.spec.whatwg.org/#dom-closeeventinit-wasclean")]
    public bool WasClean { get; init; }

    /// <summary>Gets the WebSocket connection close code.</summary>
    [IdlMember("code", "https://websockets.spec.whatwg.org/#dom-closeeventinit-code")]
    public ushort Code { get; init; }

    /// <summary>Gets the WebSocket connection close reason.</summary>
    [IdlMember("reason", "https://websockets.spec.whatwg.org/#dom-closeeventinit-reason")]
    public string Reason { get; init; } = "";
}

/// <summary>The event fired when a <see cref="WebSocket"/> is closed (https://websockets.spec.whatwg.org/#the-closeevent-interface).</summary>
[IdlInterface("CloseEvent", "https://websockets.spec.whatwg.org/#closeevent")]
public sealed class CloseEvent : Event
{
    /// <summary>Creates a close event with the given type and initialization dictionary.</summary>
    [IdlMember("constructor", "https://websockets.spec.whatwg.org/#dom-closeevent-closeevent")]
    public CloseEvent(string type, CloseEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        WasClean = eventInitDict?.WasClean ?? false;
        Code = eventInitDict?.Code ?? 0;
        Reason = eventInitDict?.Reason ?? "";
    }

    /// <summary>Gets whether the connection closed cleanly: the closing handshake completed.</summary>
    [IdlMember("wasClean", "https://websockets.spec.whatwg.org/#dom-closeevent-wasclean")]
    public bool WasClean { get; }

    /// <summary>
    /// Gets the WebSocket connection close code (https://www.rfc-editor.org/rfc/rfc6455#section-7.1.5): the status code of
    /// the Close frame received from the server, <c>1005</c> when it has no status code, or <c>1006</c> when no Close frame
    /// was received (e.g. the connection failed).
    /// </summary>
    [IdlMember("code", "https://websockets.spec.whatwg.org/#dom-closeevent-code")]
    public ushort Code { get; }

    /// <summary>Gets the WebSocket connection close reason: the reason of the Close frame received from the server, or the empty string.</summary>
    [IdlMember("reason", "https://websockets.spec.whatwg.org/#dom-closeevent-reason")]
    public string Reason { get; }
}
