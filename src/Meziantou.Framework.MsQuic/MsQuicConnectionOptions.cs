namespace Meziantou.Framework.MsQuic;

/// <summary>The options of a QUIC connection, common to the client and the server.</summary>
public abstract class MsQuicConnectionOptions
{
    private protected MsQuicConnectionOptions()
    {
    }

    /// <summary>Gets or sets the number of bidirectional streams the peer can have open at the same time. The default is 100.</summary>
    public int MaxInboundBidirectionalStreams { get; set; } = 100;

    /// <summary>Gets or sets the number of unidirectional streams the peer can have open at the same time. The default is 100.</summary>
    public int MaxInboundUnidirectionalStreams { get; set; } = 100;

    /// <summary>Gets or sets the time without activity after which the connection is closed. <see cref="TimeSpan.Zero"/> keeps the default of MsQuic (30 seconds).</summary>
    public TimeSpan IdleTimeout { get; set; }

    /// <summary>Gets or sets the time the handshake can take. <see cref="TimeSpan.Zero"/> keeps the default of MsQuic (10 seconds).</summary>
    public TimeSpan HandshakeTimeout { get; set; }

    /// <summary>Gets or sets the interval of the keep-alive packets. <see cref="TimeSpan.Zero"/> disables them.</summary>
    public TimeSpan KeepAliveInterval { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the connection accepts QUIC datagrams (RFC 9221). The peer must enable
    /// them too before either side can send one.
    /// </summary>
    public bool EnableDatagrams { get; set; }

    /// <summary>
    /// Gets or sets the number of received datagrams kept until <see cref="MsQuicConnection.ReceiveDatagramAsync"/>
    /// reads them. When the queue is full, the oldest datagram is dropped. The default is 1024.
    /// </summary>
    public int ReceivedDatagramQueueCapacity { get; set; } = 1024;

    /// <summary>Gets or sets the congestion control algorithm.</summary>
    public MsQuicCongestionControlAlgorithm CongestionControlAlgorithm { get; set; }

    /// <summary>Gets or sets the error code sent to the peer when a stream is disposed without being completed, or when an operation is canceled.</summary>
    public long DefaultStreamErrorCode { get; set; }

    /// <summary>Gets or sets the error code sent to the peer when the connection is disposed without being closed.</summary>
    public long DefaultCloseErrorCode { get; set; }

    internal static void Validate(MsQuicConnectionOptions options)
    {
        if (options.MaxInboundBidirectionalStreams is < 0 or > ushort.MaxValue)
            throw new ArgumentException($"{nameof(MaxInboundBidirectionalStreams)} must be between 0 and 65535.", nameof(options));

        if (options.MaxInboundUnidirectionalStreams is < 0 or > ushort.MaxValue)
            throw new ArgumentException($"{nameof(MaxInboundUnidirectionalStreams)} must be between 0 and 65535.", nameof(options));

        if (options.ReceivedDatagramQueueCapacity < 1)
            throw new ArgumentException($"{nameof(ReceivedDatagramQueueCapacity)} must be at least 1.", nameof(options));

        if (options.IdleTimeout < TimeSpan.Zero || options.HandshakeTimeout < TimeSpan.Zero || options.KeepAliveInterval < TimeSpan.Zero)
            throw new ArgumentException("The timeouts must not be negative.", nameof(options));

        if (!IsValidErrorCode(options.DefaultStreamErrorCode) || !IsValidErrorCode(options.DefaultCloseErrorCode))
            throw new ArgumentException("The error codes must be between 0 and 2^62-1.", nameof(options));
    }

    internal static bool IsValidErrorCode(long errorCode) => errorCode is >= 0 and <= (1L << 62) - 1;

    internal static void ValidateErrorCode(long errorCode, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(errorCode))] string? paramName = null)
    {
        // QUIC error codes are variable-length integers: 62 bits
        if (!IsValidErrorCode(errorCode))
            throw new ArgumentOutOfRangeException(paramName, errorCode, "The error code must be between 0 and 2^62-1.");
    }
}
