using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct QuicBuffer
{
    public uint Length;
    public byte* Buffer;
}
