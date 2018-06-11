using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct JOBOBJECT_INFO
    {
        [FieldOffset(0)]
        public JOBOBJECT_EXTENDED_LIMIT_INFORMATION32 ExtendedLimits32;

        [FieldOffset(0)]
        public JOBOBJECT_EXTENDED_LIMIT_INFORMATION64 ExtendedLimits64;

        public static JOBOBJECT_INFO From(JobObjectLimits limits)
        {
            var info = new JOBOBJECT_INFO();
            if (Environment.Is64BitProcess)
            {
                info.ExtendedLimits64.BasicLimits.ActiveProcessLimit = limits.ActiveProcessLimit;
                info.ExtendedLimits64.BasicLimits.Affinity = limits.Affinity;
                info.ExtendedLimits64.BasicLimits.MaximumWorkingSetSize = (uint)limits.MaximumWorkingSetSize;
                info.ExtendedLimits64.BasicLimits.MinimumWorkingSetSize = (uint)limits.MinimumWorkingSetSize;
                info.ExtendedLimits64.BasicLimits.PerJobUserTimeLimit = limits.PerJobUserTimeLimit;
                info.ExtendedLimits64.BasicLimits.PerProcessUserTimeLimit = limits.PerProcessUserTimeLimit;
                info.ExtendedLimits64.BasicLimits.PriorityClass = limits.PriorityClass;
                info.ExtendedLimits64.BasicLimits.SchedulingClass = limits.SchedulingClass;
                info.ExtendedLimits64.ProcessMemoryLimit = limits.ProcessMemoryLimit;
                info.ExtendedLimits64.JobMemoryLimit = limits.JobMemoryLimit;

                info.ExtendedLimits64.BasicLimits.LimitFlags = limits.InternalFlags;
            }
            else
            {
                info.ExtendedLimits32.BasicLimits.ActiveProcessLimit = limits.ActiveProcessLimit;
                info.ExtendedLimits32.BasicLimits.Affinity = limits.Affinity;
                info.ExtendedLimits32.BasicLimits.MaximumWorkingSetSize = (uint)limits.MaximumWorkingSetSize;
                info.ExtendedLimits32.BasicLimits.MinimumWorkingSetSize = (uint)limits.MinimumWorkingSetSize;
                info.ExtendedLimits32.BasicLimits.PerJobUserTimeLimit = limits.PerJobUserTimeLimit;
                info.ExtendedLimits32.BasicLimits.PerProcessUserTimeLimit = limits.PerProcessUserTimeLimit;
                info.ExtendedLimits32.BasicLimits.PriorityClass = limits.PriorityClass;
                info.ExtendedLimits32.BasicLimits.SchedulingClass = limits.SchedulingClass;
                info.ExtendedLimits32.ProcessMemoryLimit = (uint)limits.ProcessMemoryLimit;
                info.ExtendedLimits32.JobMemoryLimit = (uint)limits.JobMemoryLimit;

                info.ExtendedLimits32.BasicLimits.LimitFlags = limits.InternalFlags;
            }
            return info;
        }
    }
}
