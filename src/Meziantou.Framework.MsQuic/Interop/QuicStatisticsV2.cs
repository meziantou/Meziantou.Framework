using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

// QUIC_STATISTICS_V2. MsQuic fills the fields that fit in the size the caller passes, so the structure can grow.
[StructLayout(LayoutKind.Explicit, Size = 208)]
internal struct QuicStatisticsV2
{
    [FieldOffset(12)]
    public safe uint Rtt;

    [FieldOffset(16)]
    public safe uint MinRtt;

    [FieldOffset(20)]
    public safe uint MaxRtt;

    [FieldOffset(60)]
    public safe ushort SendPathMtu;

    [FieldOffset(64)]
    public safe ulong SendTotalPackets;

    [FieldOffset(80)]
    public safe ulong SendSuspectedLostPackets;

    [FieldOffset(88)]
    public safe ulong SendSpuriousLostPackets;

    [FieldOffset(96)]
    public safe ulong SendTotalBytes;

    [FieldOffset(104)]
    public safe ulong SendTotalStreamBytes;

    [FieldOffset(112)]
    public safe uint SendCongestionCount;

    [FieldOffset(120)]
    public safe ulong RecvTotalPackets;

    [FieldOffset(136)]
    public safe ulong RecvDroppedPackets;

    [FieldOffset(152)]
    public safe ulong RecvTotalBytes;

    [FieldOffset(160)]
    public safe ulong RecvTotalStreamBytes;

    [FieldOffset(188)]
    public safe uint SendCongestionWindow;

    [FieldOffset(204)]
    public safe uint RttVariance;
}
