namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The events of a <see cref="WebTransportHostSession"/>, raised from tasks of the event loop of its global object.</summary>
internal interface IWebTransportSessionEvents
{
    /// <summary>The session is established.</summary>
    void Ready(string protocol, IReadOnlyList<KeyValuePair<string, string>> responseHeaders);

    /// <summary>The session could not be established, or ended abruptly.</summary>
    void Failed(string message);

    /// <summary>The server closed the session cleanly.</summary>
    void Closed(uint closeCode, string reason);

    /// <summary>The server asks to end the session.</summary>
    void Draining();

    /// <summary>The server opened a stream.</summary>
    void IncomingStream(WebTransportHostStream stream);

    /// <summary>The server sent a datagram.</summary>
    void Datagram(ReadOnlyMemory<byte> datagram);
}
