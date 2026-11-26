namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The exception a <see cref="WebTransportConnectionStream"/> throws when the server aborts the stream: it reset it
/// (RESET_STREAM, for a read) or asked to stop sending (STOP_SENDING, for a write).
/// </summary>
public sealed class WebTransportStreamAbortedException : IOException
{
    /// <summary>Initializes a new instance of the <see cref="WebTransportStreamAbortedException"/> class.</summary>
    public WebTransportStreamAbortedException()
        : this(errorCode: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WebTransportStreamAbortedException"/> class.</summary>
    public WebTransportStreamAbortedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WebTransportStreamAbortedException"/> class.</summary>
    public WebTransportStreamAbortedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WebTransportStreamAbortedException"/> class.</summary>
    /// <param name="errorCode">The WebTransport application error code of the server, or null when its HTTP/3 error code does not map to one.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public WebTransportStreamAbortedException(uint? errorCode, Exception? innerException = null)
        : base(errorCode is { } code ? $"The server aborted the stream with the error code {code}." : "The server aborted the stream.", innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets the WebTransport application error code of the server, or null.</summary>
    public uint? ErrorCode { get; }
}
