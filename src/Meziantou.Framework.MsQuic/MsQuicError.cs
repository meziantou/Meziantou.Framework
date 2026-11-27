namespace Meziantou.Framework.MsQuic;

/// <summary>The kind of error a <see cref="MsQuicException"/> reports.</summary>
public enum MsQuicError
{
    /// <summary>An unexpected error of MsQuic.</summary>
    InternalError,

    /// <summary>The peer closed the connection, with the application error code in <see cref="MsQuicException.ApplicationErrorCode"/>.</summary>
    ConnectionAborted,

    /// <summary>The peer aborted the stream, with the application error code in <see cref="MsQuicException.ApplicationErrorCode"/>.</summary>
    StreamAborted,

    /// <summary>The local address is already in use.</summary>
    AddressInUse,

    /// <summary>The address is not valid or not available.</summary>
    InvalidAddress,

    /// <summary>The handshake did not complete in time.</summary>
    ConnectionTimeout,

    /// <summary>The remote host is not reachable.</summary>
    HostUnreachable,

    /// <summary>The peer refused the connection.</summary>
    ConnectionRefused,

    /// <summary>The peers do not support a common QUIC version.</summary>
    VersionNegotiationError,

    /// <summary>The connection was idle for longer than the idle timeout.</summary>
    ConnectionIdle,

    /// <summary>The peer violated the QUIC protocol.</summary>
    ProtocolError,

    /// <summary>The operation was aborted, because the stream or the connection was aborted or closed locally.</summary>
    OperationAborted,

    /// <summary>Another listener of the process already uses the application protocol on this port.</summary>
    AlpnInUse,

    /// <summary>The TLS handshake failed: certificate rejected, no common application protocol, TLS alert...</summary>
    HandshakeFailure,

    /// <summary>The peer does not allow more streams of this type yet.</summary>
    StreamLimitReached,

    /// <summary>Datagrams are not enabled on the connection: either side did not enable them, or the connection is not established.</summary>
    DatagramsNotEnabled,

    /// <summary>The datagram is larger than <see cref="MsQuicConnection.MaxDatagramSendSize"/>.</summary>
    DatagramTooLarge,
}
