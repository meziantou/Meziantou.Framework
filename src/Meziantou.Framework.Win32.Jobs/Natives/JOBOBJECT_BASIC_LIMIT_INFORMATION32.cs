using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION32
    {
        [FieldOffset(0)]
        public long PerProcessUserTimeLimit;
        [FieldOffset(8)]
        public long PerJobUserTimeLimit;
        [FieldOffset(16)]
        public LimitFlags LimitFlags;
        [FieldOffset(20)]
        public uint MinimumWorkingSetSize;
        [FieldOffset(24)]
        public uint MaximumWorkingSetSize;
        [FieldOffset(28)]
        public uint ActiveProcessLimit;
        [FieldOffset(32)]
        public IntPtr Affinity;
        [FieldOffset(36)]
        public uint PriorityClass;
        [FieldOffset(40)]
        public uint SchedulingClass;
    }

}
