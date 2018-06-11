using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION64
    {
        [FieldOffset(0)]
        public JOBOBJECT_BASIC_LIMIT_INFORMATION64 BasicLimits;

        [FieldOffset(64)]
        public IO_COUNTERS IoInfo;

        [FieldOffset(112)]
        public ulong ProcessMemoryLimit;

        [FieldOffset(120)]
        public ulong JobMemoryLimit;

        [FieldOffset(128)]
        public ulong PeakProcessMemoryUsed;

        [FieldOffset(136)]
        public ulong PeakJobMemoryUsed;
    }
}
