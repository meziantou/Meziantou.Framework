using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

[StructLayout(LayoutKind.Explicit, Size = 72)]
internal struct QuicNewConnectionInfo
{
    [FieldOffset(0)]
    public safe uint QuicVersion;

    [FieldOffset(8)]
    public safe byte* LocalAddress;

    [FieldOffset(16)]
    public safe byte* RemoteAddress;

    [FieldOffset(28)]
    public safe ushort ClientAlpnListLength;

    [FieldOffset(30)]
    public safe ushort ServerNameLength;

    [FieldOffset(32)]
    public safe byte NegotiatedAlpnLength;

    [FieldOffset(48)]
    public safe byte* ClientAlpnList;

    [FieldOffset(56)]
    public safe byte* NegotiatedAlpn;

    [FieldOffset(64)]
    public safe byte* ServerName;
}
