using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION32
    {
        [FieldOffset(0)]
        public JOBOBJECT_BASIC_LIMIT_INFORMATION32 BasicLimits;
        [FieldOffset(48)]
        public IO_COUNTERS IoInfo;
        [FieldOffset(96)]
        public uint ProcessMemoryLimit;
        [FieldOffset(100)]
        public uint JobMemoryLimit;
        [FieldOffset(104)]
        public uint PeakProcessMemoryUsed;
        [FieldOffset(108)]
        public uint PeakJobMemoryUsed;
    }

}
