using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct QuicListenerEvent
{
    public const int NewConnection = 0;
    public const int StopComplete = 1;

    [FieldOffset(0)]
    public unsafe int Type;

    [FieldOffset(8)]
    public unsafe QuicNewConnectionInfo* NewConnectionInfo;

    [FieldOffset(16)]
    public unsafe nint NewConnectionConnection;
}
