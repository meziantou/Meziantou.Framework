using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

// QUIC_SETTINGS: only the fields the library sets. Each field has a bit in IsSetFlags that tells MsQuic to read it.
[StructLayout(LayoutKind.Explicit, Size = 144)]
internal struct QuicSettings
{
    public const ulong IsSetHandshakeIdleTimeoutMs = 0x2;
    public const ulong IsSetIdleTimeoutMs = 0x4;
    public const ulong IsSetKeepAliveIntervalMs = 0x10000;
    public const ulong IsSetCongestionControlAlgorithm = 0x20000;
    public const ulong IsSetPeerBidiStreamCount = 0x40000;
    public const ulong IsSetPeerUnidiStreamCount = 0x80000;
    public const ulong IsSetDatagramReceiveEnabled = 0x8000000;

    [FieldOffset(0)]
    public safe ulong IsSetFlags;

    [FieldOffset(16)]
    public safe ulong HandshakeIdleTimeoutMs;

    [FieldOffset(24)]
    public safe ulong IdleTimeoutMs;

    [FieldOffset(88)]
    public safe uint KeepAliveIntervalMs;

    [FieldOffset(92)]
    public safe ushort CongestionControlAlgorithm;

    [FieldOffset(94)]
    public safe ushort PeerBidiStreamCount;

    [FieldOffset(96)]
    public safe ushort PeerUnidiStreamCount;

    // SendBufferingEnabled: 1, PacingEnabled: 1, MigrationEnabled: 1, DatagramReceiveEnabled: 1, ...
    [FieldOffset(106)]
    public safe byte Flags;

    public const byte DatagramReceiveEnabledBit = 0x08;
}
