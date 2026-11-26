namespace Meziantou.Framework.WebTransport.Http3;

// A violation of HTTP/3, QPACK or WebTransport by the peer, with the error code that closes the connection or the stream.
// It never leaves the library: it closes the connection or aborts the stream, and the application sees a
// WebTransportException.
#pragma warning disable CA1064 // Exceptions should be public
internal sealed class Http3ProtocolException : Exception
{
    public Http3ProtocolException()
    {
    }

    public Http3ProtocolException(string message)
        : base(message)
    {
    }

    public Http3ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Http3ProtocolException(long errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public long ErrorCode { get; } = Http3ErrorCode.GeneralProtocolError;
}
#pragma warning restore CA1064
