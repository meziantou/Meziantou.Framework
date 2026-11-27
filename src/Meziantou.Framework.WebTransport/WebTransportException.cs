namespace Meziantou.Framework.WebTransport;

/// <summary>The exception that WebTransport sessions and streams throw.</summary>
public sealed class WebTransportException : IOException
{
    /// <summary>Initializes a new instance of the <see cref="WebTransportException"/> class.</summary>
    public WebTransportException()
        : this(WebTransportError.ProtocolError, "A WebTransport error occurred.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WebTransportException"/> class.</summary>
    public WebTransportException(string message)
        : this(WebTransportError.ProtocolError, message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WebTransportException"/> class.</summary>
    public WebTransportException(string message, Exception? innerException)
        : this(WebTransportError.ProtocolError, message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WebTransportException"/> class.</summary>
    public WebTransportException(WebTransportError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>Gets the kind of error.</summary>
    public WebTransportError Error { get; }

    /// <summary>Gets the HTTP status code of a rejected session.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets the application error code (0 to 2^32-1) of a stream the peer aborted. It is <see langword="null"/> when the peer used an HTTP/3 error code that does not map to a WebTransport code.</summary>
    public uint? StreamErrorCode { get; init; }
}
