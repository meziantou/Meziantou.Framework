using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

internal static class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateJobObject(ref SECURITY_ATTRIBUTES lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern JobObject OpenJobObject(JobObjectAccessRights desiredAccess, bool inheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetInformationJobObject(JobObject hJob, JobObjectInfoClass jobObjectInfoClass, ref JOBOBJECT_INFO lpJobObjectInfo, int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetInformationJobObject(JobObject hJob, JobObjectInfoClass jobObjectInfoClass, ref JOBOBJECT_BASIC_UI_RESTRICTIONS lpJobObjectInfo, int cbJobObjectInfoLength);

    [DllImport("kernel32.dll")]
    internal static extern bool TerminateJobObject(JobObject hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AssignProcessToJobObject(JobObject job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool IsProcessInJob(IntPtr process, JobObject hJob, out bool result);
}
