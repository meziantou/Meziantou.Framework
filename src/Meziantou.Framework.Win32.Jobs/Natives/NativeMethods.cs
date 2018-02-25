using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives
{
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateJobObject(ref SECURITY_ATTRIBUTES lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetInformationJobObject(SafeHandle hJob, JobObjectInfoClass JobObjectInfoClass, ref JOBOBJECT_INFO lpJobObjectInfo, int cbJobObjectInfoLength);

        [DllImport("kernel32.dll")]
        internal static extern bool TerminateJobObject(SafeHandle hJob, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool AssignProcessToJobObject(SafeHandle job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
