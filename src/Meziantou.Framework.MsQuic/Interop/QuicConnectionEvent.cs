using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct QuicConnectionEvent
{
    public const int Connected = 0;
    public const int ShutdownInitiatedByTransport = 1;
    public const int ShutdownInitiatedByPeer = 2;
    public const int ShutdownComplete = 3;
    public const int LocalAddressChanged = 4;
    public const int PeerAddressChanged = 5;
    public const int PeerStreamStarted = 6;
    public const int StreamsAvailable = 7;
    public const int PeerNeedsStreams = 8;
    public const int IdealProcessorChanged = 9;
    public const int DatagramStateChanged = 10;
    public const int DatagramReceived = 11;
    public const int DatagramSendStateChanged = 12;
    public const int Resumed = 13;
    public const int ResumptionTicketReceived = 14;
    public const int PeerCertificateReceived = 15;

    [FieldOffset(0)]
    public unsafe int Type;

    // CONNECTED
    [FieldOffset(9)]
    public unsafe byte ConnectedNegotiatedAlpnLength;

    [FieldOffset(16)]
    public unsafe byte* ConnectedNegotiatedAlpn;

    // SHUTDOWN_INITIATED_BY_TRANSPORT
    [FieldOffset(8)]
    public unsafe int ShutdownByTransportStatus;

    [FieldOffset(16)]
    public unsafe ulong ShutdownByTransportErrorCode;

    // SHUTDOWN_INITIATED_BY_PEER
    [FieldOffset(8)]
    public unsafe ulong ShutdownByPeerErrorCode;

    // SHUTDOWN_COMPLETE: HandshakeCompleted: 1, PeerAcknowledgedShutdown: 1, AppCloseInProgress: 1
    [FieldOffset(8)]
    public unsafe byte ShutdownCompleteFlags;

    // PEER_STREAM_STARTED
    [FieldOffset(8)]
    public unsafe nint PeerStreamStartedStream;

    [FieldOffset(16)]
    public unsafe int PeerStreamStartedFlags;

    // DATAGRAM_STATE_CHANGED
    [FieldOffset(8)]
    public unsafe byte DatagramStateChangedSendEnabled;

    [FieldOffset(10)]
    public unsafe ushort DatagramStateChangedMaxSendLength;

    // DATAGRAM_RECEIVED
    [FieldOffset(8)]
    public unsafe QuicBuffer* DatagramReceivedBuffer;

    // DATAGRAM_SEND_STATE_CHANGED
    [FieldOffset(8)]
    public unsafe void* DatagramSendStateChangedClientContext;

    [FieldOffset(16)]
    public unsafe int DatagramSendStateChangedState;

    // PEER_CERTIFICATE_RECEIVED: with USE_PORTABLE_CERTIFICATES, QUIC_BUFFER* of the DER certificate and of the
    // PKCS #7 chain; with Schannel, CERT_CONTEXT*.
    [FieldOffset(8)]
    public unsafe void* PeerCertificateReceivedCertificate;

    [FieldOffset(24)]
    public unsafe void* PeerCertificateReceivedChain;
}
