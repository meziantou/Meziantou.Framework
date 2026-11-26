namespace Meziantou.Framework.WebTransport;

/// <summary>The kind of error a <see cref="WebTransportException"/> reports.</summary>
public enum WebTransportError
{
    /// <summary>The QUIC connection could not be established or was lost: see the inner exception.</summary>
    ConnectionFailed,

    /// <summary>The server does not support WebTransport over HTTP/3: its SETTINGS do not enable extended CONNECT, HTTP datagrams and WebTransport.</summary>
    NotSupported,

    /// <summary>The server rejected the session, with the HTTP status code in <see cref="WebTransportException.StatusCode"/>.</summary>
    SessionRejected,

    /// <summary>The session is closed: the operation cannot run anymore.</summary>
    SessionClosed,

    /// <summary>The session ended abruptly: the CONNECT stream was reset or the connection closed without a CLOSE_WEBTRANSPORT_SESSION capsule.</summary>
    SessionReset,

    /// <summary>The peer aborted the stream, with the error code in <see cref="WebTransportException.StreamErrorCode"/>.</summary>
    StreamAborted,

    /// <summary>The stream or the session was aborted locally.</summary>
    OperationAborted,

    /// <summary>The peer violated HTTP/3 or WebTransport.</summary>
    ProtocolError,

    /// <summary>The datagram is larger than <see cref="WebTransportSession.MaxDatagramSize"/>, or datagrams are not available.</summary>
    DatagramNotSent,
}
