using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct IO_COUNTERS
    {
        [FieldOffset(0)]
        public ulong ReadOperationCount;
        [FieldOffset(8)]
        public ulong WriteOperationCount;
        [FieldOffset(16)]
        public ulong OtherOperationCount;
        [FieldOffset(24)]
        public ulong ReadTransferCount;
        [FieldOffset(32)]
        public ulong WriteTransferCount;
        [FieldOffset(40)]
        public ulong OtherTransferCount;
    }
}
