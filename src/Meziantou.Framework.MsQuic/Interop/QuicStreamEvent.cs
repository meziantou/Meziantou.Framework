using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct QuicStreamEvent
{
    public const int StartComplete = 0;
    public const int Receive = 1;
    public const int SendComplete = 2;
    public const int PeerSendShutdown = 3;
    public const int PeerSendAborted = 4;
    public const int PeerReceiveAborted = 5;
    public const int SendShutdownComplete = 6;
    public const int ShutdownComplete = 7;
    public const int IdealSendBufferSize = 8;
    public const int PeerAccepted = 9;
    public const int CancelOnLoss = 10;

    [FieldOffset(0)]
    public unsafe int Type;

    // START_COMPLETE
    [FieldOffset(8)]
    public unsafe int StartCompleteStatus;

    [FieldOffset(16)]
    public unsafe ulong StartCompleteId;

    // PeerAccepted: 1
    [FieldOffset(24)]
    public unsafe byte StartCompleteFlags;

    // RECEIVE
    [FieldOffset(16)]
    public unsafe ulong ReceiveTotalBufferLength;

    [FieldOffset(24)]
    public unsafe QuicBuffer* ReceiveBuffers;

    [FieldOffset(32)]
    public unsafe uint ReceiveBufferCount;

    [FieldOffset(36)]
    public unsafe int ReceiveFlags;

    // SEND_COMPLETE
    [FieldOffset(8)]
    public unsafe byte SendCompleteCanceled;

    [FieldOffset(16)]
    public unsafe void* SendCompleteClientContext;

    // PEER_SEND_ABORTED, PEER_RECEIVE_ABORTED
    [FieldOffset(8)]
    public unsafe ulong AbortedErrorCode;

    // SEND_SHUTDOWN_COMPLETE
    [FieldOffset(8)]
    public unsafe byte SendShutdownCompleteGraceful;

    // SHUTDOWN_COMPLETE
    [FieldOffset(8)]
    public unsafe byte ShutdownCompleteConnectionShutdown;

    // AppCloseInProgress: 1, ConnectionShutdownByApp: 1, ConnectionClosedRemotely: 1
    [FieldOffset(9)]
    public unsafe byte ShutdownCompleteFlags;

    [FieldOffset(16)]
    public unsafe ulong ShutdownCompleteConnectionErrorCode;

    [FieldOffset(24)]
    public unsafe int ShutdownCompleteConnectionCloseStatus;
}
