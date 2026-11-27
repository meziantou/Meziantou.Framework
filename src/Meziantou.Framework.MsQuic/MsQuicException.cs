namespace Meziantou.Framework.MsQuic;

/// <summary>The exception that MsQuic operations throw when the connection, a stream or a datagram fails.</summary>
public sealed class MsQuicException : IOException
{
    /// <summary>Initializes a new instance of the <see cref="MsQuicException"/> class.</summary>
    public MsQuicException()
        : this(MsQuicError.InternalError, applicationErrorCode: null, transportErrorCode: null, "A QUIC error occurred.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MsQuicException"/> class.</summary>
    public MsQuicException(string message)
        : this(MsQuicError.InternalError, applicationErrorCode: null, transportErrorCode: null, message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MsQuicException"/> class.</summary>
    public MsQuicException(string message, Exception? innerException)
        : this(MsQuicError.InternalError, applicationErrorCode: null, transportErrorCode: null, message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MsQuicException"/> class.</summary>
    public MsQuicException(MsQuicError error, long? applicationErrorCode, long? transportErrorCode, string message)
        : this(error, applicationErrorCode, transportErrorCode, message, innerException: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MsQuicException"/> class.</summary>
    public MsQuicException(MsQuicError error, long? applicationErrorCode, long? transportErrorCode, string message, Exception? innerException)
        : base(message, innerException)
    {
        Error = error;
        ApplicationErrorCode = applicationErrorCode;
        TransportErrorCode = transportErrorCode;
    }

    /// <summary>Gets the kind of error.</summary>
    public MsQuicError Error { get; }

    /// <summary>Gets the application error code the peer sent when it closed the connection or aborted the stream.</summary>
    public long? ApplicationErrorCode { get; }

    /// <summary>Gets the QUIC transport error code (RFC 9000, section 20.1) of a connection closed by the transport.</summary>
    public long? TransportErrorCode { get; }

    /// <summary>Gets the QUIC_STATUS value MsQuic returned, if any. It depends on the operating system.</summary>
    public int? NativeErrorCode { get; init; }
}
