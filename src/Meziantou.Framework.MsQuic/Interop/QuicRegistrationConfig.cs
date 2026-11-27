using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct QuicRegistrationConfig
{
    [FieldOffset(0)]
    public safe byte* AppName;

    [FieldOffset(8)]
    public safe int ExecutionProfile;
}
