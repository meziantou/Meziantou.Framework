using System;

namespace Meziantou.Framework.Win32.Natives
{
    [Flags]
    internal enum LimitFlags
    {
        WorkingSet = 0x00000001,
        ProcessTime = 0x00000002,
        JobTime = 0x00000004,
        ActiveProcess = 0x00000008,
        Affinity = 0x00000010,
        PriorityClass = 0x00000020,
        PreserveJobTime = 0x00000040,
        SchedulingClass = 0x00000080,
        ProcessMemory = 0x00000100,
        JobMemory = 0x00000200,
        DieOnUnhandledException = 0x00000400,
        BreakawayOk = 0x00000800,
        SilentBreakawayOk = 0x00001000,
        KillOnJobClose = 0x00002000,
        SubsetAffinity = 0x00004000,
        JobMemoryLow = 0x00008000,
        JobReadBytes = 0x00010000,
        JobWriteBytes = 0x00020000,
        RateControl = 0x00040000,
        CpuRateControl = 0x00040000,
        IoRateControl = 0x00080000,
        NetRateControl = 0x00100000,
    }
}
