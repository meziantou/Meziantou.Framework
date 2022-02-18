using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION64
    {
        [FieldOffset(0)]
        public long PerProcessUserTimeLimit;

        [FieldOffset(8)]
        public long PerJobUserTimeLimit;

        [FieldOffset(16)]
        public LimitFlags LimitFlags;

        [FieldOffset(24)]
        public ulong MinimumWorkingSetSize;

        [FieldOffset(32)]
        public ulong MaximumWorkingSetSize;

        [FieldOffset(40)]
        public uint ActiveProcessLimit;

        [FieldOffset(48)]
        public IntPtr Affinity;

        [FieldOffset(56)]
        public uint PriorityClass;

        [FieldOffset(60)]
        public uint SchedulingClass;
    }
}
